// Taj's COI Mods | TajsVisualsFeatureHost.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi;
using Mafi.Core;
using Mafi.Core.GameLoop;
using Mafi.Core.Simulation;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Diagnostics;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using TajsCOI.Visuals.Features.Lighting;
using UnityEngine.SceneManagement;

namespace TajsCOI.Visuals
{
    /// <summary>
    ///     Owns scene lifetime and the presentation clock. It deliberately does not resolve or
    ///     mutate weather/fog services; those remain authoritative in the vanilla renderer.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class TajsVisualsFeatureHost
    {
        private const string ComponentId = "LightingBackend";
        private readonly DependencyResolver m_resolver;
        private readonly ITajsRuntime m_runtime;
        private readonly ITajsSettings m_settings;
        private readonly ITajsLogger m_log;
        private readonly LightingBackend m_lighting;
        private ICalendar? m_calendar;
        private float m_lastPresentationClock;
        private float? m_fixedClock;
        private bool m_lastFixedLighting;
        private bool m_terminated;

        public TajsVisualsFeatureHost(
            DependencyResolver resolver,
            IGameLoopEvents gameLoop,
            ITajsRuntime runtime,
            ITajsSettings settings)
        {
            m_resolver = resolver;
            m_runtime = runtime;
            m_settings = settings;
            m_log = runtime.GetLogger(TajsVisualsSettingsCatalog.ModId, ComponentId);
            m_lighting = new LightingBackend(runtime, m_log);

            TajsVisualsSettingsCatalog.RegisterAll(settings);
            settings.Changed += OnSettingChanged;
            gameLoop.RenderUpdateEnd.AddNonSaveable(this, OnRenderUpdateEnd);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            m_runtime.RegisterComponent(
                new RuntimeComponentDescriptor(
                    TajsVisualsSettingsCatalog.ModId,
                    ComponentId,
                    RuntimeComponentLifetime.GameplayScene,
                    "Mafi.Unity.Camera.LightController and directional UnityEngine.Light",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()));
            m_runtime.ReportCompatibility(
                new CompatibilityReport(
                    TajsVisualsSettingsCatalog.ModId,
                    "FeatureHost",
                    CompatibilityState.Compatible,
                    "Scene-owned snapshot backend with presentation-only phase policy",
                    TajsVisualsSettingsCatalog.All.Count + " settings registered",
                    "Lighting is opt-in; weather, fog, and simulation date/time remain vanilla-owned."));
        }

        private void InitializeScene()
        {
            if (m_terminated)
            {
                return;
            }

            try
            {
                Scene active = SceneManager.GetActiveScene();
                if (!m_resolver.TryResolve(out m_calendar))
                {
                    m_runtime.ReportCompatibility(
                        new CompatibilityReport(
                            TajsVisualsSettingsCatalog.ModId,
                            "PresentationClock",
                            CompatibilityState.Degraded,
                            "Authoritative ICalendar.CurrentDate",
                            "ICalendar was unavailable",
                            "The cycle falls back to smooth simulation steps; no simulation state is changed."));
                }
                if (m_lighting.TryInitialize(m_resolver, active.handle))
                {
                    m_runtime.RegisterCapability(
                        new RuntimeCapabilityDescriptor(
                            "TajsVisuals.LightingBackend",
                            TajsVisualsSettingsCatalog.ModId,
                            ComponentId,
                            RuntimeCapabilityState.Available,
                            "0.8.7b",
                            "Snapshot-based directional-light policy and exact restore",
                            string.Empty,
                            RuntimeComponentLifetime.GameplayScene));
                }
                else
                {
                    m_runtime.RegisterCapability(
                        new RuntimeCapabilityDescriptor(
                            "TajsVisuals.LightingBackend",
                            TajsVisualsSettingsCatalog.ModId,
                            ComponentId,
                            RuntimeCapabilityState.Unavailable,
                            "0.8.7b",
                            "Directional-light target unavailable",
                            "Vanilla rendering remains active.",
                            RuntimeComponentLifetime.GameplayScene));
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Visual lighting initialization failed open.");
                m_runtime.ReportCompatibility(
                    new CompatibilityReport(
                        TajsVisualsSettingsCatalog.ModId,
                        ComponentId,
                        CompatibilityState.Disabled,
                        "Scene-owned directional-light target",
                        exception.GetType().Name,
                        "Vanilla rendering remains active."));
            }
        }

        private void OnRenderUpdateEnd(GameTime time)
        {
            if (m_terminated)
            {
                return;
            }

            try
            {
                if (!m_lighting.IsInitialized || !m_lighting.IsTargetAlive || !m_lighting.IsControllerAlive)
                {
                    InitializeScene();
                }

                m_lastPresentationClock = m_calendar is null
                    ? PresentationClock.FromSimulationSteps(
                        time.TotalElapsedSimStepsSmooth.ToDouble(),
                        Calendar.SIM_STEPS_PER_DAY)
                    : PresentationClock.FromSimulationDate(
                        m_calendar.CurrentDate,
                        time.TotalElapsedSimStepsSmooth.ToDouble(),
                        Calendar.SIM_STEPS_PER_DAY);
                RefreshFixedState();
                ApplyCurrentPolicy();
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Visual lighting render update failed open.");
            }
        }

        private void OnSettingChanged(object? sender, SettingChangedEventArgs change)
        {
            if (m_terminated || !string.Equals(change.Descriptor.ModId, TajsVisualsSettingsCatalog.ModId, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                RefreshFixedState();
                ApplyCurrentPolicy();
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Visual lighting setting change failed open.");
            }
        }

        private void ApplyCurrentPolicy()
        {
            bool lightingEnabled = m_settings.Get<bool>(TajsVisualsSettingsCatalog.ModId, LightingSettings.EnableLighting);
            bool cycleEnabled = m_settings.Get<bool>(TajsVisualsSettingsCatalog.ModId, LightingSettings.EnableCycle);
            LightingPolicy basePolicy = lightingEnabled
                ? new LightingPolicy(
                    (float)m_settings.Get<double>(TajsVisualsSettingsCatalog.ModId, LightingSettings.IntensityMultiplier),
                    (float)m_settings.Get<double>(TajsVisualsSettingsCatalog.ModId, LightingSettings.AngleOffset),
                    (float)m_settings.Get<double>(TajsVisualsSettingsCatalog.ModId, LightingSettings.ShadowStrength))
                : LightingPolicy.Identity;
            m_lighting.SetBasePolicy(basePolicy);

            if (cycleEnabled)
            {
                VisualPhaseConfiguration configuration = ReadPhaseConfiguration();
                float phaseClock = m_fixedClock ?? m_lastPresentationClock;
                m_lighting.SetPhasePolicy(configuration.Evaluate(phaseClock));
            }
            else
            {
                // Reset deliberately removes only the phase override; the #64/base policy stays
                // available to the backend when the cycle is turned off.
                m_lighting.SetPhasePolicy(null);
            }

            if (lightingEnabled || cycleEnabled)
            {
                m_lighting.Apply();
            }
            else
            {
                m_lighting.Restore();
            }
        }

        private void RefreshFixedState()
        {
            bool cycleEnabled = m_settings.Get<bool>(TajsVisualsSettingsCatalog.ModId, LightingSettings.EnableCycle);
            bool fixedLighting = cycleEnabled && m_settings.Get<bool>(TajsVisualsSettingsCatalog.ModId, LightingSettings.FixedLighting);
            if (!fixedLighting)
            {
                m_fixedClock = null;
            }
            else if (!m_lastFixedLighting)
            {
                // Capture the current presentation clock only on the false->true transition;
                // subsequent render frames never advance it.
                m_fixedClock = m_lastPresentationClock;
            }
            m_lastFixedLighting = fixedLighting;
        }

        private VisualPhaseConfiguration ReadPhaseConfiguration()
        {
            return new VisualPhaseConfiguration(
                Read(LightingSettings.DawnStart),
                Read(LightingSettings.DayStart),
                Read(LightingSettings.DuskStart),
                Read(LightingSettings.NightStart),
                Phase(LightingSettings.DawnIntensity, LightingSettings.DawnAngle, LightingSettings.DawnShadow),
                Phase(LightingSettings.DayIntensity, LightingSettings.DayAngle, LightingSettings.DayShadow),
                Phase(LightingSettings.DuskIntensity, LightingSettings.DuskAngle, LightingSettings.DuskShadow),
                Phase(LightingSettings.NightIntensity, LightingSettings.NightAngle, LightingSettings.NightShadow));

            float Read(string key) => (float)m_settings.Get<double>(TajsVisualsSettingsCatalog.ModId, key);

            LightingPolicy Phase(string intensityKey, string angleKey, string shadowKey) => new(
                Read(intensityKey),
                Read(angleKey),
                Read(shadowKey));
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (m_terminated)
            {
                return;
            }

            Scene active = SceneManager.GetActiveScene();
            if (mode == LoadSceneMode.Additive && active.handle != scene.handle)
            {
                return;
            }
            try
            {
                ResetPresentationState();
                m_lighting.ResetForScene(active.handle);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Visual lighting scene-load refresh failed open.");
            }
        }

        private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
        {
            if (m_terminated)
            {
                return;
            }
            try
            {
                ResetPresentationState();
                m_lighting.ResetForScene(newScene.handle);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Visual lighting active-scene refresh failed open.");
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (m_terminated)
            {
                return;
            }
            if (scene.handle != m_lighting.SceneHandle)
            {
                return;
            }
            try
            {
                ResetPresentationState();
                m_lighting.ResetForScene(-1);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Visual lighting scene-unload restore failed open.");
            }
        }

        private void OnTerminate()
        {
            if (m_terminated)
            {
                return;
            }
            m_terminated = true;
            try
            {
                ResetPresentationState();
                m_lighting.ResetForScene(-1);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Visual lighting termination restore failed open.");
            }
            finally
            {
                m_settings.Changed -= OnSettingChanged;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            }
        }

        private void ResetPresentationState()
        {
            m_lastPresentationClock = 0f;
            m_fixedClock = null;
            m_lastFixedLighting = false;
        }
    }
}
