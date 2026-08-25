// Taj's COI Mods | RenderingLoadSheddingFeature.cs

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TajsCOI.Performance.Features.RenderingLoadShedding
{
    /// <summary>
    ///     Live and reversible renderer controls. It touches Unity objects only when a setting
    ///     changes or a scene becomes active, never from a per-frame Harmony callback. The one-time
    ///     scene scan can hitch on very large saves; this is intentional and bounded to transitions.
    /// </summary>
    internal sealed class RenderingLoadSheddingFeature : IPerformanceFeature
    {
        private sealed class ParticleState
        {
            internal bool EmissionEnabled;
        }

        private const string ModId = "TajsPerformance";
        private static ConditionalWeakTable<ParticleSystem, ParticleState> s_particles = new();
        private static readonly string[] s_weatherParticleTokens = { "rain", "snow", "weather" };
        private static readonly string[] s_cloudParticleTokens = { "cloud", "clouds" };
        private static readonly string[] s_smokeParticleTokens = { "smoke" };
        private static readonly string[] s_dustParticleTokens = { "dust", "exhaust" };
        private static int s_particleStatesTracked;
        private static bool s_installed;
        private static bool s_originalFog;
        private static bool s_originalFogCaptured;
        private static ShadowQuality s_originalShadows;
        private static float s_originalShadowDistance;
        private static bool s_originalQualityCaptured;
        private static int s_originalSceneHandle = -1;
        private static ITajsLogger? s_log;

        public string Id => "RenderingLoadShedding";
        public string ConfigKey => RenderingLoadSheddingSettings.EnableConfigKey;

        public bool IsProcessPatchInstalled() => s_installed;

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
#pragma warning disable S2696
            s_log = log;
#pragma warning restore S2696
            s_installed = true;
            try
            {
                SubscribeToSceneLifecycle();
                EnsureQualityStateForActiveScene();
                Apply();
                runtime.ReportCompatibility(new CompatibilityReport(
                    ModId,
                    Id,
                    CompatibilityState.Compatible,
                    "Unity QualitySettings and opt-in scene particle controls",
                    "Live renderer control installed",
                    "Controls are disabled by default, reversible, and intended for profiler A/B comparisons."));
            }
            catch
            {
                // An install failure must not leave static Unity callbacks attached to a
                // partially initialized feature. The host reports the original exception.
                Uninstall();
                throw;
            }
        }

        internal static void Uninstall()
        {
            if (!s_installed)
            {
                return;
            }

            try
            {
                // Terminate runs while the gameplay scene is still available. Restore both the
                // global quality values and any particle emission values before dropping all
                // scene references; the next resolver must start from its own baseline.
                RestoreQuality();
                RestoreParticles();
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Live rendering controls failed to restore during scene termination.");
            }
            finally
            {
                UnsubscribeFromSceneLifecycle();
                ResetParticleTracking();
                s_originalSceneHandle = -1;
                s_originalFogCaptured = false;
                s_originalQualityCaptured = false;
                s_installed = false;
                s_log = null;
            }
        }

        internal static void OnSettingChanged(SettingChangedEventArgs change, ITajsSettings settings)
        {
            if (!s_installed || change.Descriptor.ModId != ModId)
            {
                return;
            }
            try
            {
                Apply();
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Live rendering control failed open.");
            }
        }

        private static void Apply()
        {
            // The catalog is registered before this feature is installed. The settings object is
            // passed through the event handler only; the feature's static values are refreshed by
            // the host before calling Apply below.
            EnsureQualityStateForActiveScene();
            if (!RenderingLoadSheddingRuntime.Enabled)
            {
                RestoreQuality();
                RestoreParticles();
                return;
            }
            if (RenderingLoadSheddingRuntime.DisableFog)
            {
                RenderSettings.fog = false;
            }
            else if (s_originalFogCaptured)
            {
                RenderSettings.fog = s_originalFog;
            }
            QualitySettings.shadows = RenderingLoadSheddingRuntime.DisableShadows ? ShadowQuality.Disable : s_originalShadows;
            if (RenderingLoadSheddingRuntime.ShadowDistance > 0)
            {
                QualitySettings.shadowDistance = RenderingLoadSheddingRuntime.ShadowDistance;
            }
            else if (s_originalQualityCaptured)
            {
                QualitySettings.shadowDistance = s_originalShadowDistance;
            }
            ApplyParticles();
        }

        internal static void RefreshFromSettings(ITajsSettings settings)
        {
            RenderingLoadSheddingRuntime.Enabled = settings.Get<bool>(ModId, RenderingLoadSheddingSettings.EnableConfigKey);
            RenderingLoadSheddingRuntime.DisableSmoke = settings.Get<bool>(ModId, RenderingLoadSheddingSettings.DisableSmokeConfigKey);
            RenderingLoadSheddingRuntime.DisableDust = settings.Get<bool>(ModId, RenderingLoadSheddingSettings.DisableDustConfigKey);
            RenderingLoadSheddingRuntime.DisableWeather = settings.Get<bool>(ModId, RenderingLoadSheddingSettings.DisableWeatherConfigKey);
            RenderingLoadSheddingRuntime.DisableClouds = settings.Get<bool>(ModId, RenderingLoadSheddingSettings.DisableCloudsConfigKey);
            RenderingLoadSheddingRuntime.DisableFog = settings.Get<bool>(ModId, RenderingLoadSheddingSettings.DisableFogConfigKey);
            RenderingLoadSheddingRuntime.DisableShadows = settings.Get<bool>(ModId, RenderingLoadSheddingSettings.DisableShadowsConfigKey);
            RenderingLoadSheddingRuntime.ShadowDistance = settings.Get<int>(ModId, RenderingLoadSheddingSettings.ShadowDistanceConfigKey);
        }

        private static void CaptureQualityState()
        {
            if (!s_originalFogCaptured)
            {
                s_originalFog = RenderSettings.fog;
                s_originalFogCaptured = true;
            }
            if (!s_originalQualityCaptured)
            {
                s_originalShadows = QualitySettings.shadows;
                s_originalShadowDistance = QualitySettings.shadowDistance;
                s_originalQualityCaptured = true;
            }
        }

        private static void EnsureQualityStateForActiveScene()
        {
            int activeSceneHandle = SceneManager.GetActiveScene().handle;
            if (s_originalSceneHandle == activeSceneHandle &&
                s_originalFogCaptured &&
                s_originalQualityCaptured)
            {
                return;
            }

            ResetForScene(activeSceneHandle);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!s_installed)
            {
                return;
            }

            try
            {
                // sceneLoaded is the authoritative boundary for a recreated gameplay scene.
                // An additive scene can load without becoming active; do not replace the
                // active scene's baseline until Unity reports or the next Apply observes it.
                Scene activeScene = SceneManager.GetActiveScene();
                if (mode == LoadSceneMode.Additive && activeScene.handle != scene.handle)
                {
                    return;
                }

                // activeSceneChanged normally handles a newly activated additive scene. Avoid
                // doing the same scan twice when Unity delivers sceneLoaded immediately after it.
                if (activeScene.handle == s_originalSceneHandle &&
                    s_originalFogCaptured &&
                    s_originalQualityCaptured)
                {
                    return;
                }

                // Force a reset even if Unity reused the same scene handle during a reload.
                ResetForScene(activeScene.handle);
                Apply();
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Live rendering scene refresh failed open.");
            }
        }

        private static void OnActiveSceneChanged(Scene previousScene, Scene newScene)
        {
            if (!s_installed)
            {
                return;
            }

            try
            {
                // SetActiveScene does not necessarily produce sceneLoaded. Keep the baseline
                // tied to the active scene without polling from Update or a Harmony frame hook.
                ResetForScene(newScene.handle);
                Apply();
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Live rendering active-scene refresh failed open.");
            }
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            if (!s_installed || scene.handle != s_originalSceneHandle)
            {
                return;
            }

            try
            {
                // Restore while the old scene objects can still be found. sceneLoaded also calls
                // ResetForScene, so this remains safe when Unity destroys objects before the
                // unload callback reaches us.
                RestoreQuality();
                RestoreParticles();
                ResetParticleTracking();
                s_originalSceneHandle = -1;
                s_originalFogCaptured = false;
                s_originalQualityCaptured = false;
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Live rendering scene unload restore failed open.");
            }
        }

        private static void SubscribeToSceneLifecycle()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private static void UnsubscribeFromSceneLifecycle()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private static void ResetForScene(int sceneHandle)
        {
            if (s_originalSceneHandle >= 0)
            {
                RestoreQuality();
                RestoreParticles();
            }

            // ConditionalWeakTable has no Clear operation. Replacing it is intentional: particle
            // components are scene-owned Unity objects and must never inherit another scene's
            // emission baseline. This happens only at scene boundaries, never per frame.
            ResetParticleTracking();
            s_originalSceneHandle = sceneHandle;
            s_originalFogCaptured = false;
            s_originalQualityCaptured = false;
            CaptureQualityState();
        }

        private static void ResetParticleTracking()
        {
            s_particles = new ConditionalWeakTable<ParticleSystem, ParticleState>();
            Volatile.Write(ref s_particleStatesTracked, 0);
        }

        private static void RestoreQuality()
        {
            if (s_originalFogCaptured) RenderSettings.fog = s_originalFog;
            if (s_originalQualityCaptured)
            {
                QualitySettings.shadows = s_originalShadows;
                QualitySettings.shadowDistance = s_originalShadowDistance;
            }
        }

        private static void ApplyParticles()
        {
            // Unity does not expose a stable COI rendering-category contract here. Name matching
            // is deliberately conservative and documented as version-sensitive. Normalize
            // separators and camel-case names, then match whole tokens to avoid classifying an
            // unrelated object merely because it contains a short substring.
            foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                string normalizedName = ParticleNameMatcher.Normalize(particle.gameObject.name);
                bool match = RenderingLoadSheddingRuntime.DisableWeather && ParticleNameMatcher.MatchesAnyToken(normalizedName, s_weatherParticleTokens) ||
                    RenderingLoadSheddingRuntime.DisableClouds && ParticleNameMatcher.MatchesAnyToken(normalizedName, s_cloudParticleTokens) ||
                    RenderingLoadSheddingRuntime.DisableSmoke && ParticleNameMatcher.MatchesAnyToken(normalizedName, s_smokeParticleTokens) ||
                    RenderingLoadSheddingRuntime.DisableDust && ParticleNameMatcher.MatchesAnyToken(normalizedName, s_dustParticleTokens);
                ParticleSystem.EmissionModule emission = particle.emission;
                ParticleState state = s_particles.GetValue(particle, CreateParticleState);
                emission.enabled = match ? false : state.EmissionEnabled;
            }
        }

        private static void RestoreParticles()
        {
            // Keep the default-disabled and never-used path allocation-free and scan-free. Once
            // the feature has captured a particle baseline, a scan is required to restore any
            // currently loaded objects that were changed by an earlier opt-in interval.
            if (Volatile.Read(ref s_particleStatesTracked) == 0)
            {
                return;
            }

            foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (s_particles.TryGetValue(particle, out ParticleState? state))
                {
                    ParticleSystem.EmissionModule emission = particle.emission;
                    emission.enabled = state.EmissionEnabled;
                }
            }
        }

        private static ParticleState CreateParticleState(ParticleSystem particle)
        {
            Interlocked.Increment(ref s_particleStatesTracked);
            return new ParticleState { EmissionEnabled = particle.emission.enabled };
        }

        private static class RenderingLoadSheddingRuntime
        {
            internal static bool Enabled;
            internal static bool DisableSmoke;
            internal static bool DisableDust;
            internal static bool DisableWeather;
            internal static bool DisableClouds;
            internal static bool DisableFog;
            internal static bool DisableShadows;
            internal static int ShadowDistance;
        }
    }

    // Kept outside the Unity-bound feature type so static normalization tests can run in the
    // ordinary .NET test host without loading UnityEngine.CoreModule.
    internal static class ParticleNameMatcher
    {
        internal static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(name.Length + 8);
            char previous = '\0';
            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                    {
                        builder.Append(' ');
                    }
                    previous = ' ';
                    continue;
                }

                if (char.IsUpper(character) && char.IsLower(previous) && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(char.ToLowerInvariant(character));
                previous = character;
            }
            return builder.ToString().Trim();
        }

        internal static bool MatchesAnyToken(string normalizedName, IReadOnlyList<string> tokens)
        {
            foreach (string token in tokens)
            {
                int start = 0;
                while (start < normalizedName.Length)
                {
                    int index = normalizedName.IndexOf(token, start, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    bool leftBoundary = index == 0 || normalizedName[index - 1] == ' ';
                    int end = index + token.Length;
                    bool rightBoundary = end == normalizedName.Length || normalizedName[end] == ' ';
                    if (leftBoundary && rightBoundary)
                    {
                        return true;
                    }
                    start = end;
                }
            }
            return false;
        }
    }
}
