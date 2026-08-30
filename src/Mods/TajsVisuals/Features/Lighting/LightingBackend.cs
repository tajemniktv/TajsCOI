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

namespace TajsCOI.Visuals.Features.Lighting
{
    /// <summary>
    ///     The single owner of visual light policy for TajsVisuals. It captures one pristine
    ///     renderer snapshot per scene/light instance and always derives writes from that
    ///     snapshot. The renderer objects are weakly held because they are scene-owned.
    /// </summary>
    internal sealed class LightingBackend
    {
        private sealed class VanillaSnapshot
        {
            internal LightController.State ControllerState;
            internal Vector3 EulerAngles;
            internal bool HasEulerAngles;
        }

        private static readonly FieldInfo? s_lightField = typeof(LightController).GetField(
            "m_light",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ITajsRuntime m_runtime;
        private readonly ITajsLogger m_log;
        private readonly HashSet<string> m_reportedProperties = new(StringComparer.Ordinal);
        private WeakReference<LightController>? m_lightController;
        private WeakReference<Light>? m_light;
        private VanillaSnapshot? m_snapshot;
        private int m_sceneHandle = -1;
        private int m_lightInstanceId;
        private LightingPolicy m_basePolicy = LightingPolicy.Identity;
        private LightingPolicy? m_phasePolicy;
        private LightController.State m_lastVanillaState;
        private LightController.State m_lastAppliedState;
        private Vector3 m_lastAppliedEulerAngles;
        private bool m_hasAppliedState;
        private bool m_hasAppliedAngle;

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

        internal LightingPolicy BaseLightingPolicy => m_basePolicy;

        internal LightingPolicy? TimeOfDayPresentation => m_phasePolicy;

        internal LightingEffectiveState EffectiveState => new(
            m_basePolicy,
            m_phasePolicy,
            EffectivePolicy,
            IsInitialized);

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
            m_lastVanillaState = default;
            m_lastAppliedState = default;
            m_lastAppliedEulerAngles = default;
            m_hasAppliedState = false;
            m_hasAppliedAngle = false;
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
                LightController.State current;
                try
                {
                    current = controller.GetState();
                }
                catch (Exception exception)
                {
                    ReportProperty(
                        "ControllerState",
                        CompatibilityState.Degraded,
                        "LightController.GetState()",
                        exception.GetType().Name,
                        "The frame is skipped; vanilla lighting remains active.");
                    return;
                }
                LightController.State baseline = current;
                if (m_hasAppliedState)
                {
                    // Weather normally rewrites these fields before RenderUpdateEnd. When it
                    // does not, undo only the values written by the previous policy frame and
                    // retain the last observed vanilla baseline; this prevents compounding.
                    if (Approximately(current.LightIntensity, m_lastAppliedState.LightIntensity))
                    {
                        baseline.LightIntensity = m_lastVanillaState.LightIntensity;
                    }
                    if (Approximately(current.ShadowsStrength, m_lastAppliedState.ShadowsStrength))
                    {
                        baseline.ShadowsStrength = m_lastVanillaState.ShadowsStrength;
                    }
                }

                m_lastVanillaState = baseline;
                float intensity = baseline.LightIntensity * policy.IntensityMultiplier;
                float shadowStrength = Clamp01(baseline.ShadowsStrength * policy.ShadowStrengthMultiplier);
                if (TrySetControllerIntensity(controller, light, intensity, shadowStrength))
                {
                    m_lastAppliedState = current;
                    m_lastAppliedState.LightIntensity = intensity;
                    m_lastAppliedState.ShadowsStrength = shadowStrength;
                    m_hasAppliedState = true;
                }
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
                    m_lastAppliedEulerAngles = light.transform.eulerAngles;
                    m_hasAppliedAngle = true;
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
        ///     Restores only values previously written by this backend. Vanilla-owned weather,
        ///     ambient, and quality settings are left untouched; unsupported writes are reported
        ///     per property and never prevent the remaining safe restores.
        /// </summary>
        internal void Restore()
        {
            if (m_snapshot is null || (!m_hasAppliedState && !m_hasAppliedAngle))
            {
                return;
            }

            TryGetLiveLight(out Light? light);
            if (m_hasAppliedState && TryGetLiveController(out LightController? controller) && controller is not null && light is not null)
            {
                LightController.State current;
                bool canRestoreControllerState = true;
                try
                {
                    current = controller.GetState();
                }
                catch (Exception exception)
                {
                    ReportProperty(
                        "ControllerState",
                        CompatibilityState.Degraded,
                        "LightController.GetState()",
                        exception.GetType().Name,
                        "The controller state was left unchanged during restore.");
                    current = default;
                    canRestoreControllerState = false;
                }
                if (canRestoreControllerState)
                {
                    LightController.State restored = current;
                    if (Approximately(current.LightIntensity, m_lastAppliedState.LightIntensity))
                    {
                        restored.LightIntensity = m_lastVanillaState.LightIntensity;
                    }
                    if (Approximately(current.ShadowsStrength, m_lastAppliedState.ShadowsStrength))
                    {
                        restored.ShadowsStrength = m_lastVanillaState.ShadowsStrength;
                    }
                    TrySetControllerState(controller, light, restored);
                }
            }
            else if (m_hasAppliedState && light is not null)
            {
                try
                {
                    // Without the controller we cannot recover its extra-intensity component;
                    // restore only the policy-owned direct values and leave weather color alone.
                    light.intensity = m_lastVanillaState.LightIntensity;
                    light.shadowStrength = m_lastVanillaState.ShadowsStrength;
                }
                catch (Exception exception)
                {
                    ReportProperty(
                        "ControllerState",
                        CompatibilityState.Degraded,
                        "Original LightController state",
                        exception.GetType().Name,
                        "Vanilla state could not be fully restored.");
                }
            }

            if (m_hasAppliedAngle && light is not null && m_snapshot.HasEulerAngles)
            {
                try
                {
                    // Do not overwrite a direction changed by weather or another native
                    // renderer owner after our last policy write.
                    if (Approximately(light.transform.eulerAngles, m_lastAppliedEulerAngles))
                    {
                        light.transform.eulerAngles = m_snapshot.EulerAngles;
                    }
                }
                catch (Exception exception)
                {
                    ReportProperty(
                        "AngleOffset",
                        CompatibilityState.Degraded,
                        "Directional Light transform.eulerAngles",
                        exception.GetType().Name,
                        "Vanilla direction could not be restored.");
                }
            }

            m_hasAppliedState = false;
            m_hasAppliedAngle = false;

        }

        /// <summary>
        ///     Restores the captured vanilla snapshot through the backend's sole write path.
        /// </summary>
        internal void RestoreVanilla() => Restore();

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
                ReportProperty(
                    "Intensity",
                    CompatibilityState.Disabled,
                    "LightController.GetState/SetLightIntensity",
                    exception.GetType().Name,
                    "Lighting controls remain inactive; vanilla rendering is unchanged.");
                return;
            }

            float directIntensity = 0f;
            float directShadowStrength = 0f;
            Color directColor = Color.black;
            try
            {
                directIntensity = light.intensity;
                directShadowStrength = light.shadowStrength;
                directColor = light.color;
                // A weather-disabled scene can leave LightController.State at its default while
                // the authoritative Unity light already has a valid intensity/color. Hydrate
                // only that unmistakable default case; a genuine zero-intensity weather state
                // retains the controller's value.
                if (controllerState.LightIntensity == 0f &&
                    controllerState.LightExtraIntensity == 0f &&
                    controllerState.LightColor == default &&
                    directIntensity != 0f)
                {
                    controllerState.LightIntensity = directIntensity;
                    controllerState.ShadowsStrength = directShadowStrength;
                    controllerState.LightColor = directColor;
                }
                ReportProperty(
                    "DirectLightValues",
                    CompatibilityState.Compatible,
                    "UnityEngine.Light intensity, shadowStrength, and color",
                    "Captured for exact restore",
                    "Direct values cover renderer scenes without initialized weather state.");
            }
            catch (Exception exception)
            {
                ReportProperty(
                    "DirectLightValues",
                    CompatibilityState.Degraded,
                    "UnityEngine.Light intensity, shadowStrength, and color",
                    exception.GetType().Name,
                    "Controller-state restore remains available.");
            }

            Vector3 eulerAngles = Vector3.zero;
            bool angleCaptureSucceeded = false;
            try
            {
                eulerAngles = light.transform.eulerAngles;
                angleCaptureSucceeded = true;
                ReportProperty(
                    "AngleOffset",
                    CompatibilityState.Compatible,
                    "Directional Light transform.eulerAngles",
                    "LightController does not update angles from simulation time in 0.8.7b",
                    "The offset is visual-only and simulation-independent.");
            }
            catch (Exception exception)
            {
                ReportProperty(
                    "AngleOffset",
                    CompatibilityState.Disabled,
                    "Directional Light transform.eulerAngles",
                    exception.GetType().Name,
                    "Angle policy is disabled; vanilla direction remains active.");
            }

            m_snapshot = new VanillaSnapshot
            {
                ControllerState = controllerState,
                EulerAngles = eulerAngles,
                HasEulerAngles = angleCaptureSucceeded,
            };
            ReportProperty(
                "Intensity",
                CompatibilityState.Compatible,
                "LightController.GetState/SetLightIntensity",
                "LightIntensity and LightExtraIntensity",
                "Base intensity is derived from the latest vanilla weather state; weather color/extra intensity remain renderer-owned.");
            ReportProperty(
                "ShadowStrength",
                CompatibilityState.Compatible,
                "LightController.State.ShadowsStrength",
                "Captured and safely clamped",
                "Shadow policy never exceeds the vanilla snapshot.");
            ReportProperty(
                "LightColor",
                CompatibilityState.Compatible,
                "LightController.State.LightColor",
                "Captured for exact restore",
                "Weather owns color transitions while the visuals policy is active.");
        }

        private Light? ResolveAuthoritativeLight(LightController controller)
        {
            if (s_lightField is null)
            {
                ReportProperty(
                    "AuthoritativeLight",
                    CompatibilityState.Disabled,
                    "LightController.m_light",
                    "Private field was not found",
                    "The backend will not guess a scene light; vanilla rendering remains unchanged.");
                return null;
            }

            try
            {
                if (s_lightField.GetValue(controller) is Light owned && owned && owned.type == LightType.Directional)
                {
                    ReportProperty(
                        "AuthoritativeLight",
                        CompatibilityState.Compatible,
                        "LightController.m_light is directional",
                        "Private authoritative light resolved",
                        "The backend targets the renderer-owned sun.");
                    return owned;
                }
                ReportProperty(
                    "AuthoritativeLight",
                    CompatibilityState.Disabled,
                    "LightController.m_light is directional",
                    "Field was absent, destroyed, or non-directional",
                    "The backend will not guess a scene light; vanilla rendering remains unchanged.");
            }
            catch (Exception exception)
            {
                ReportProperty(
                    "AuthoritativeLight",
                    CompatibilityState.Disabled,
                    "LightController.m_light",
                    exception.GetType().Name,
                    "The backend will not guess a scene light; vanilla rendering remains unchanged.");
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
                ReportProperty(
                    "ControllerState",
                    CompatibilityState.Degraded,
                    "LightController.SetState(State)",
                    exception.GetType().Name,
                    "Falling back to direct Light writes without changing simulation state.");
                try
                {
                    light.intensity = state.LightIntensity + state.LightExtraIntensity;
                    light.shadowStrength = state.ShadowsStrength;
                    light.color = state.LightColor;
                }
                catch (Exception fallbackException)
                {
                    ReportProperty(
                        "ControllerState",
                        CompatibilityState.Disabled,
                        "Direct Light intensity, shadowStrength, and color",
                        fallbackException.GetType().Name,
                        "Vanilla light state remains active.");
                }
            }
        }

        private bool TrySetControllerIntensity(LightController controller, Light light, float intensity, float shadowStrength)
        {
            try
            {
                // SetLightIntensity preserves the renderer's current weather color and any
                // transient lightning extra intensity while replacing only the policy-owned
                // base intensity/shadow values derived from the latest vanilla state.
                controller.SetLightIntensity(intensity, shadowStrength);
                return true;
            }
            catch (Exception exception)
            {
                ReportProperty(
                    "ControllerState",
                    CompatibilityState.Degraded,
                    "LightController.SetLightIntensity(float, float)",
                    exception.GetType().Name,
                    "Falling back to direct Light writes without changing simulation state.");
                try
                {
                    light.intensity = intensity;
                    light.shadowStrength = shadowStrength;
                    return true;
                }
                catch (Exception fallbackException)
                {
                    ReportProperty(
                        "ControllerState",
                        CompatibilityState.Disabled,
                        "Direct Light intensity and shadowStrength",
                        fallbackException.GetType().Name,
                        "Vanilla light state remains active.");
                }
            }

            return false;
        }

        private void ReportProperty(string property, CompatibilityState state, string expected, string observed, string reason)
        {
            if (!m_reportedProperties.Add(property + ":" + state + ":" + observed))
            {
                return;
            }

            m_runtime.ReportCompatibility(
                new CompatibilityReport(
                    "TajsVisuals",
                    "LightingBackend." + property,
                    state,
                    expected,
                    observed,
                    reason));
        }

        private static bool Approximately(float left, float right) => Math.Abs(left - right) <= 0.0001f;

        private static bool Approximately(Vector3 left, Vector3 right) =>
            Approximately(left.x, right.x) && Approximately(left.y, right.y) && Approximately(left.z, right.z);

        private static float Clamp01(float value) => Math.Min(1f, Math.Max(0f, value));
    }
}
