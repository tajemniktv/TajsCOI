// Taj's COI Mods | LightingBackend.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Unity.Camera;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace TajsCOI.Visuals.Features.Lighting
{
    /// <summary>
    ///     The single owner of visual light policy for TajsVisuals. It captures one pristine
    ///     renderer snapshot per scene/light instance and always derives writes from that
    ///     snapshot. The renderer objects are weakly held because they are scene-owned.
    /// </summary>
    internal sealed class LightingBackend
    {
        private sealed class Snapshot
        {
            internal LightController.State ControllerState;
            internal Vector3 EulerAngles;
            internal ShadowQuality Shadows;
            internal float ShadowDistance;
            internal Color AmbientLight;
            internal float AmbientIntensity;
            internal float DirectIntensity;
            internal float DirectShadowStrength;
            internal Color DirectColor;
            internal bool HasEulerAngles;
            internal bool HasQualityShadowValues;
            internal bool HasAmbientValues;
            internal bool HasDirectLightValues;
        }

        private static readonly FieldInfo? s_lightField = typeof(LightController).GetField(
            "m_light",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ITajsRuntime m_runtime;
        private readonly ITajsLogger m_log;
        private readonly HashSet<string> m_reportedProperties = new(StringComparer.Ordinal);
        private WeakReference<LightController>? m_lightController;
        private WeakReference<Light>? m_light;
        private Snapshot? m_snapshot;
        private int m_sceneHandle = -1;
        private int m_lightInstanceId;
        private LightingPolicy m_basePolicy = LightingPolicy.Identity;
        private LightingPolicy? m_phasePolicy;

        public LightingBackend(ITajsRuntime runtime, ITajsLogger log)
        {
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal bool IsInitialized => m_snapshot is not null;

        internal int SceneHandle => m_sceneHandle;

        internal bool IsTargetAlive => TryGetLiveLight(out _);

        internal bool IsControllerAlive => TryGetLiveController(out _);

        internal LightingPolicy EffectivePolicy => LightingPolicy.Combine(
            m_basePolicy,
            m_phasePolicy ?? LightingPolicy.Identity);

        internal void SetBasePolicy(LightingPolicy policy) => m_basePolicy = policy.Sanitized();

        internal void SetPhasePolicy(LightingPolicy? policy) => m_phasePolicy = policy?.Sanitized();

        internal bool TryInitialize(DependencyResolver resolver, int sceneHandle)
        {
            if (resolver is null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            if (m_snapshot is not null && m_sceneHandle == sceneHandle &&
                TryGetLiveLight(out Light? currentLight) &&
                currentLight!.GetInstanceID() == m_lightInstanceId &&
                TryGetLiveController(out _))
            {
                return true;
            }

            if (!resolver.TryResolve(out LightController controller))
            {
                ReportProperty(
                    "AuthoritativeLight",
                    CompatibilityState.Disabled,
                    "Mafi.Unity.Camera.LightController with a directional Light",
                    "LightController was not available",
                    "Lighting controls remain inactive; vanilla rendering is unchanged.");
                return false;
            }

            Light? light = ResolveAuthoritativeLight(controller);
            if (light is null)
            {
                ReportProperty(
                    "AuthoritativeLight",
                    CompatibilityState.Disabled,
                    "A directional Light owned by LightController",
                    "No directional light was found",
                    "Lighting controls remain inactive; vanilla rendering is unchanged.");
                return false;
            }

            if (m_snapshot is not null)
            {
                Restore();
            }

            m_sceneHandle = sceneHandle;
            m_lightController = new WeakReference<LightController>(controller);
            m_light = new WeakReference<Light>(light);
            m_lightInstanceId = light.GetInstanceID();
            Capture(controller, light);
            return m_snapshot is not null;
        }

        internal void ResetForScene(int sceneHandle)
        {
            Restore();
            m_snapshot = null;
            m_lightController = null;
            m_light = null;
            m_lightInstanceId = 0;
            m_sceneHandle = sceneHandle;
            m_phasePolicy = null;
        }

        internal void Apply()
        {
            if (m_snapshot is null || !TryGetLiveLight(out Light? light) || light is null)
            {
                return;
            }

            LightingPolicy policy = EffectivePolicy;
            if (!TryGetLiveController(out LightController? controller) || controller is null)
            {
                ReportProperty(
                    "ControllerState",
                    CompatibilityState.Degraded,
                    "LightController.SetState(State)",
                    "The scene LightController was collected or unavailable",
                    "Only direct Unity light properties can be restored when possible.");
            }
            else
            {
                float intensity = m_snapshot.ControllerState.LightIntensity * policy.IntensityMultiplier;
                float shadowStrength = Clamp01(m_snapshot.ControllerState.ShadowsStrength * policy.ShadowStrengthMultiplier);
                TrySetControllerIntensity(controller, light, intensity, shadowStrength);
            }

            if (m_snapshot.HasEulerAngles)
            {
                try
                {
                    Vector3 angles = m_snapshot.EulerAngles;
                    light.transform.eulerAngles = new Vector3(
                        angles.x,
                        angles.y + policy.AngleOffsetDegrees,
                        angles.z);
                }
                catch (Exception exception)
                {
                    ReportProperty(
                        "AngleOffset",
                        CompatibilityState.Degraded,
                        "Directional Light transform.eulerAngles",
                        exception.GetType().Name,
                        "The angle policy was skipped; vanilla direction remains active.");
                }
            }
        }

        /// <summary>
        ///     Writes back every captured property exactly. Unsupported writes are reported per
        ///     property and never prevent the remaining safe restores.
        /// </summary>
        internal void Restore()
        {
            if (m_snapshot is null)
            {
                return;
            }

            TryGetLiveLight(out Light? light);
            if (TryGetLiveController(out LightController? controller) && controller is not null && light is not null)
            {
                TrySetControllerState(controller, light, m_snapshot.ControllerState);
            }
            else if (light is not null)
            {
                try
                {
                    light.intensity = m_snapshot.ControllerState.LightIntensity + m_snapshot.ControllerState.LightExtraIntensity;
                    light.shadowStrength = m_snapshot.ControllerState.ShadowsStrength;
                    light.color = m_snapshot.ControllerState.LightColor;
                }
                catch (Exception exception)
                {
                    ReportProperty("ControllerState", CompatibilityState.Degraded, "Original LightController state", exception.GetType().Name, "Vanilla state could not be fully restored.");
                }
            }

            if (light is not null && m_snapshot.HasDirectLightValues)
            {
                try
                {
                    // SetState is the authoritative path, but the direct values are also
                    // restored so a renderer with disabled weather (whose controller state can
                    // remain default-initialized) still returns to the exact captured light.
                    light.intensity = m_snapshot.DirectIntensity;
                    light.shadowStrength = m_snapshot.DirectShadowStrength;
                    light.color = m_snapshot.DirectColor;
                }
                catch (Exception exception)
                {
                    ReportProperty("DirectLightValues", CompatibilityState.Degraded, "UnityEngine.Light intensity, shadowStrength, and color", exception.GetType().Name, "The controller state was restored; one or more direct values remain unchanged.");
                }
            }

            if (light is not null && m_snapshot.HasEulerAngles)
            {
                try
                {
                    light.transform.eulerAngles = m_snapshot.EulerAngles;
                }
                catch (Exception exception)
                {
                    ReportProperty("AngleOffset", CompatibilityState.Degraded, "Directional Light transform.eulerAngles", exception.GetType().Name, "Vanilla direction could not be restored.");
                }
            }

            if (m_snapshot.HasAmbientValues)
            {
                try
                {
                    RenderSettings.ambientLight = m_snapshot.AmbientLight;
                    RenderSettings.ambientIntensity = m_snapshot.AmbientIntensity;
                }
                catch (Exception exception)
                {
                    ReportProperty("AmbientLighting", CompatibilityState.Degraded, "RenderSettings ambientLight and ambientIntensity", exception.GetType().Name, "Ambient lighting was left unchanged.");
                }
            }

            if (m_snapshot.HasQualityShadowValues)
            {
                try
                {
                    QualitySettings.shadows = m_snapshot.Shadows;
                    QualitySettings.shadowDistance = m_snapshot.ShadowDistance;
                }
                catch (Exception exception)
                {
                    ReportProperty("QualityShadowValues", CompatibilityState.Degraded, "QualitySettings shadows and shadowDistance", exception.GetType().Name, "Quality shadow values were left unchanged.");
                }
            }
        }

        private void Capture(LightController controller, Light light)
        {
            LightController.State controllerState;
            try
            {
                controllerState = controller.GetState();
            }
            catch (Exception exception)
            {
                m_snapshot = null;
                m_log.Exception(exception, "LightController state snapshot failed open.");
                ReportProperty("Intensity", CompatibilityState.Disabled, "LightController.GetState/SetLightIntensity", exception.GetType().Name, "Lighting controls remain inactive; vanilla rendering is unchanged.");
                return;
            }

            float directIntensity = 0f;
            float directShadowStrength = 0f;
            Color directColor = Color.black;
            bool directCaptureSucceeded = false;
            try
            {
                directIntensity = light.intensity;
                directShadowStrength = light.shadowStrength;
                directColor = light.color;
                directCaptureSucceeded = true;
                // A weather-disabled scene can leave LightController.State at its default while
                // the authoritative Unity light already has a valid intensity/color. Hydrate
                // only that unmistakable default case; a genuine zero-intensity weather state
                // retains the controller's value.
                if (controllerState.LightIntensity == 0f &&
                    controllerState.LightExtraIntensity == 0f &&
                    controllerState.LightColor == default(Color) &&
                    directIntensity != 0f)
                {
                    controllerState.LightIntensity = directIntensity;
                    controllerState.ShadowsStrength = directShadowStrength;
                    controllerState.LightColor = directColor;
                }
                ReportProperty("DirectLightValues", CompatibilityState.Compatible, "UnityEngine.Light intensity, shadowStrength, and color", "Captured for exact restore", "Direct values cover renderer scenes without initialized weather state.");
            }
            catch (Exception exception)
            {
                ReportProperty("DirectLightValues", CompatibilityState.Degraded, "UnityEngine.Light intensity, shadowStrength, and color", exception.GetType().Name, "Controller-state restore remains available.");
            }

            Vector3 eulerAngles = Vector3.zero;
            bool angleCaptureSucceeded = false;
            try
            {
                eulerAngles = light.transform.eulerAngles;
                angleCaptureSucceeded = true;
                ReportProperty("AngleOffset", CompatibilityState.Compatible, "Directional Light transform.eulerAngles", "LightController does not update angles from simulation time in 0.8.7b", "The offset is visual-only and simulation-independent.");
            }
            catch (Exception exception)
            {
                ReportProperty("AngleOffset", CompatibilityState.Disabled, "Directional Light transform.eulerAngles", exception.GetType().Name, "Angle policy is disabled; vanilla direction remains active.");
            }

            ShadowQuality shadows = ShadowQuality.All;
            float shadowDistance = 0f;
            bool qualityCaptureSucceeded = false;
            try
            {
                shadows = QualitySettings.shadows;
                shadowDistance = QualitySettings.shadowDistance;
                qualityCaptureSucceeded = true;
                ReportProperty("QualityShadowValues", CompatibilityState.Compatible, "QualitySettings shadows and shadowDistance", "Captured for exact restore", "Quality/shadow ownership remains with the vanilla renderer.");
            }
            catch (Exception exception)
            {
                ReportProperty("QualityShadowValues", CompatibilityState.Degraded, "QualitySettings shadows and shadowDistance", exception.GetType().Name, "Quality shadow values could not be captured for restore.");
            }

            Color ambientLight = Color.black;
            float ambientIntensity = 1f;
            bool ambientCaptureSucceeded = false;
            try
            {
                ambientLight = RenderSettings.ambientLight;
                ambientIntensity = RenderSettings.ambientIntensity;
                ambientCaptureSucceeded = true;
                ReportProperty("AmbientLighting", CompatibilityState.Compatible, "RenderSettings ambientLight and ambientIntensity", "Captured for exact restore", "Ambient ownership remains with the vanilla renderer.");
            }
            catch (Exception exception)
            {
                ReportProperty("AmbientLighting", CompatibilityState.Degraded, "RenderSettings ambientLight and ambientIntensity", exception.GetType().Name, "Ambient values could not be captured for restore.");
            }

            m_snapshot = new Snapshot
            {
                ControllerState = controllerState,
                EulerAngles = eulerAngles,
                Shadows = shadows,
                ShadowDistance = shadowDistance,
                AmbientLight = ambientLight,
                AmbientIntensity = ambientIntensity,
                DirectIntensity = directIntensity,
                DirectShadowStrength = directShadowStrength,
                DirectColor = directColor,
                HasEulerAngles = angleCaptureSucceeded,
                HasQualityShadowValues = qualityCaptureSucceeded,
                HasAmbientValues = ambientCaptureSucceeded,
                HasDirectLightValues = directCaptureSucceeded,
            };
            ReportProperty("Intensity", CompatibilityState.Compatible, "LightController.GetState/SetLightIntensity", "LightIntensity and LightExtraIntensity", "Base intensity is derived from the pristine scene snapshot; weather color/extra intensity remain renderer-owned.");
            ReportProperty("ShadowStrength", CompatibilityState.Compatible, "LightController.State.ShadowsStrength", "Captured and safely clamped", "Shadow policy never exceeds the vanilla snapshot.");
            ReportProperty("LightColor", CompatibilityState.Compatible, "LightController.State.LightColor", "Captured for exact restore", "Weather owns color transitions while the visuals policy is active.");
        }

        private Light? ResolveAuthoritativeLight(LightController controller)
        {
            if (s_lightField is null)
            {
                ReportProperty("AuthoritativeLight", CompatibilityState.Degraded, "LightController.m_light", "Private field was not found", "Falling back to a directional-light scene search.");
            }
            else
            {
                try
                {
                    if (s_lightField.GetValue(controller) is Light owned && owned && owned.type == LightType.Directional)
                    {
                        ReportProperty("AuthoritativeLight", CompatibilityState.Compatible, "LightController.m_light is directional", "Private authoritative light resolved", "The backend targets the renderer-owned sun.");
                        return owned;
                    }
                    ReportProperty("AuthoritativeLight", CompatibilityState.Degraded, "LightController.m_light is directional", "Field was absent, destroyed, or non-directional", "Falling back to a directional-light scene search.");
                }
                catch (Exception exception)
                {
                    ReportProperty("AuthoritativeLight", CompatibilityState.Degraded, "LightController.m_light", exception.GetType().Name, "Falling back to a directional-light scene search.");
                }
            }

            try
            {
                foreach (Light candidate in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (candidate && candidate.type == LightType.Directional)
                    {
                        ReportProperty("AuthoritativeLight", CompatibilityState.Degraded, "LightController.m_light is directional", "Directional light scene fallback", "The private ownership seam was unavailable; the fallback remains fail-open.");
                        return candidate;
                    }
                }
            }
            catch (Exception exception)
            {
                ReportProperty("AuthoritativeLight", CompatibilityState.Disabled, "Directional light scene search", exception.GetType().Name, "No light target is available.");
            }
            return null;
        }

        private bool TryGetLiveLight(out Light? light)
        {
            light = null;
            return m_light is not null && m_light.TryGetTarget(out light) && light is not null && light;
        }

        private bool TryGetLiveController(out LightController? controller)
        {
            controller = null;
            return m_lightController is not null && m_lightController.TryGetTarget(out controller) && controller is not null;
        }

        private void TrySetControllerState(LightController controller, Light light, LightController.State state)
        {
            try
            {
                controller.SetState(state);
            }
            catch (Exception exception)
            {
                ReportProperty("ControllerState", CompatibilityState.Degraded, "LightController.SetState(State)", exception.GetType().Name, "Falling back to direct Light writes without changing simulation state.");
                try
                {
                    light.intensity = state.LightIntensity + state.LightExtraIntensity;
                    light.shadowStrength = state.ShadowsStrength;
                    light.color = state.LightColor;
                }
                catch (Exception fallbackException)
                {
                    ReportProperty("ControllerState", CompatibilityState.Disabled, "Direct Light intensity, shadowStrength, and color", fallbackException.GetType().Name, "Vanilla light state remains active.");
                }
            }
        }

        private void TrySetControllerIntensity(LightController controller, Light light, float intensity, float shadowStrength)
        {
            try
            {
                // SetLightIntensity preserves the renderer's current weather color and any
                // transient lightning extra intensity while replacing only the policy-owned
                // base intensity/shadow values derived from the pristine snapshot.
                controller.SetLightIntensity(intensity, shadowStrength);
            }
            catch (Exception exception)
            {
                ReportProperty("ControllerState", CompatibilityState.Degraded, "LightController.SetLightIntensity(float, float)", exception.GetType().Name, "Falling back to direct Light writes without changing simulation state.");
                try
                {
                    light.intensity = intensity;
                    light.shadowStrength = shadowStrength;
                }
                catch (Exception fallbackException)
                {
                    ReportProperty("ControllerState", CompatibilityState.Disabled, "Direct Light intensity and shadowStrength", fallbackException.GetType().Name, "Vanilla light state remains active.");
                }
            }
        }

        private void ReportProperty(string property, CompatibilityState state, string expected, string observed, string reason)
        {
            if (!m_reportedProperties.Add(property + ":" + state + ":" + observed))
            {
                return;
            }

            m_runtime.ReportCompatibility(new CompatibilityReport(
                "TajsVisuals",
                "LightingBackend." + property,
                state,
                expected,
                observed,
                reason));
        }

        private static float Clamp01(float value) => Math.Min(1f, Math.Max(0f, value));
    }
}
