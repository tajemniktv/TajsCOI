// Taj's COI Mods | RenderingLoadSheddingFeature.cs

using System;
using System.Runtime.CompilerServices;
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
        private static readonly ConditionalWeakTable<ParticleSystem, ParticleState> s_particles = new();
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
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
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

            // Scene reloads can replace RenderSettings while the process-scoped feature remains
            // installed. Restore the previous scene's global override before capturing the new
            // scene's baseline, otherwise disabling the feature could restore stale settings.
            if (s_originalSceneHandle >= 0)
            {
                RestoreQuality();
            }
            s_originalSceneHandle = activeSceneHandle;
            s_originalFogCaptured = false;
            s_originalQualityCaptured = false;
            CaptureQualityState();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!s_installed)
            {
                return;
            }

            try
            {
                EnsureQualityStateForActiveScene();
                Apply();
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Live rendering scene refresh failed open.");
            }
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
            // is deliberately conservative and documented as version-sensitive; replace this
            // classifier with native category IDs if the game exposes them in a future version.
            foreach (ParticleSystem particle in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                string name = particle.gameObject.name.ToLowerInvariant();
                bool match = RenderingLoadSheddingRuntime.DisableWeather && (name.Contains("rain") || name.Contains("snow") || name.Contains("weather")) ||
                    RenderingLoadSheddingRuntime.DisableClouds && name.Contains("cloud") ||
                    RenderingLoadSheddingRuntime.DisableSmoke && name.Contains("smoke") ||
                    RenderingLoadSheddingRuntime.DisableDust && (name.Contains("dust") || name.Contains("exhaust"));
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
}
