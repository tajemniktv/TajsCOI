// Taj's COI Mods | OverclockingPatches.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Offices;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Waste;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Animations;
using Mafi.Core.Factory.ComputingPower;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Maintenance;
using Mafi.Core.Population;
using TajsCOI.Tweaks.Configuration;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    internal static class OverclockingPatches
    {
        private const string ConfigKey = "TajsTweaks_OverclockPercent";
        private const string AutoConfigKey = "TajsTweaks_OverclockAuto";
        private const string MinConfigKey = "TajsTweaks_OverclockMin";
        private const string MaxConfigKey = "TajsTweaks_OverclockMax";
        private static readonly Dictionary<Type, MethodInfo?> s_simUpdateMethods = new();
        private static readonly FieldInfo? s_animationParamsField = AccessTools.Field(typeof(AnimationWithPauseState), "m_params");
        private static readonly FieldInfo? s_repeatAnimationParamsField = AccessTools.Field(typeof(RepeatAnimationState), "m_params");

        internal static void Install(Harmony harmony, TajsOverclockingFeature feature)
        {
            PatchInterface(harmony, typeof(Machine), typeof(IElectricityConsumingEntity), "get_PowerRequired", nameof(PowerPostfix), required: true);
            PatchInterface(harmony, typeof(Machine), typeof(IComputingConsumingEntity), "get_ComputingRequired", nameof(ComputingPostfix), required: true);
            PatchInterface(harmony, typeof(Machine), typeof(IEntityWithWorkers), "get_WorkersNeeded", nameof(WorkersPostfix), required: true);
            PatchInterface(harmony, typeof(Machine), typeof(IMaintainedEntity), "get_MaintenanceCosts", nameof(MaintenancePostfix), required: true);

            PatchOptionalCostInterfaces(harmony, typeof(OreSortingPlant));
            PatchOptionalCostInterfaces(harmony, typeof(OfficeBuilding));
            PatchOptionalCostInterfaces(harmony, typeof(WasteSortingPlant));
            PatchOptionalCostInterfaces(harmony, typeof(Transport));

            PatchOfficeFocusPoints(harmony);

            PatchSimUpdate(harmony, typeof(Machine), nameof(MachineSimUpdatePostfix), required: true);
            PatchSimUpdate(harmony, typeof(OfficeBuilding), nameof(ExtraWorkSimUpdatePostfix), required: false);
            PatchSimUpdate(harmony, typeof(WasteSortingPlant), nameof(ExtraWorkSimUpdatePostfix), required: false);

            MethodInfo? animationStart = AccessTools.Method(typeof(AnimationWithPauseState), nameof(AnimationWithPauseState.Start));
            if (animationStart is not null && s_animationParamsField is not null)
            {
                harmony.Patch(
                    animationStart,
                    prefix: new HarmonyMethod(typeof(OverclockingPatches), nameof(AnimationWithPauseStartPrefix)));
            }

            MethodInfo? repeatAnimationStart = AccessTools.Method(typeof(RepeatAnimationState), nameof(RepeatAnimationState.Start));
            if (repeatAnimationStart is not null && s_repeatAnimationParamsField is not null)
            {
                harmony.Patch(
                    repeatAnimationStart,
                    prefix: new HarmonyMethod(typeof(OverclockingPatches), nameof(RepeatAnimationStartPrefix)));
            }

            MethodInfo addToConfig = AccessTools.Method(typeof(Machine), "AddToConfig")
                                     ?? throw new MissingMethodException(typeof(Machine).FullName, "AddToConfig");
            MethodInfo applyConfig = AccessTools.Method(typeof(Machine), "ApplyConfig")
                                     ?? throw new MissingMethodException(typeof(Machine).FullName, "ApplyConfig");
            harmony.Patch(addToConfig, postfix: new HarmonyMethod(typeof(OverclockingPatches), nameof(AddToConfigPostfix)));
            harmony.Patch(applyConfig, postfix: new HarmonyMethod(typeof(OverclockingPatches), nameof(ApplyConfigPostfix)));

            MethodInfo? transportAddToConfig = AccessTools.Method(typeof(Transport), "AddToConfig");
            MethodInfo? transportApplyConfig = AccessTools.Method(typeof(Transport), "ApplyConfig");
            if (transportAddToConfig is not null && transportApplyConfig is not null)
            {
                harmony.Patch(transportAddToConfig, postfix: new HarmonyMethod(typeof(OverclockingPatches), nameof(TransportAddToConfigPostfix)));
                harmony.Patch(transportApplyConfig, postfix: new HarmonyMethod(typeof(OverclockingPatches), nameof(TransportApplyConfigPostfix)));
            }
        }

        internal static void PowerPostfix(object __instance, ref Electricity __result)
        {
            int percent = TajsOverclockingFeature.GetPercentFor(__instance);
            if (percent != 100)
            {
                __result = new Electricity(
                    Math.Max(
                        0,
                        (int)Math.Round(
                            __result.Value *
                            OverclockingMath.CostMultiplier(percent, TajsTweaksRuntimeState.OverclockPowerCurve))));
            }
        }

        internal static void ComputingPostfix(object __instance, ref Computing __result)
        {
            int percent = TajsOverclockingFeature.GetPercentFor(__instance);
            if (percent != 100)
            {
                __result = new Computing(
                    Math.Max(
                        0,
                        (int)Math.Round(
                            __result.Value *
                            OverclockingMath.CostMultiplier(percent, TajsTweaksRuntimeState.OverclockComputingCurve))));
            }
        }

        internal static void WorkersPostfix(object __instance, ref int __result)
        {
            int percent = TajsOverclockingFeature.GetPercentFor(__instance);
            if (percent != 100)
            {
                __result = OverclockingMath.WorkersAt(__result, percent, TajsTweaksRuntimeState.OverclockWorkerCurve);
            }
        }

        internal static void MaintenancePostfix(object __instance, ref MaintenanceCosts __result)
        {
            int percent = TajsOverclockingFeature.GetPercentFor(__instance);
            if (percent != 100 && __result.MaintenancePerMonth.IsPositive)
            {
                int scale = Math.Max(
                    1,
                    (int)Math.Round(
                        OverclockingMath.CostMultiplier(
                            percent,
                            TajsTweaksRuntimeState.OverclockMaintenanceCurve) * 100));
                __result = new MaintenanceCosts(
                    __result.Product,
                    __result.MaintenancePerMonth.ScaledBy(scale.Percent()),
                    __result.MaxMaintenancePerMonth,
                    __result.ExtraBufferDuration,
                    __result.InitialMaintenanceBoost);
            }
        }

        internal static void OfficeFocusPointsPostfix(OfficeBuilding __instance, ref int __result)
        {
            int percent = TajsOverclockingFeature.GetPercentFor(__instance);
            if (percent != 100)
            {
                __result = OverclockingMath.ScaleRate(__result, percent);
            }
        }

        internal static void MachineSimUpdatePostfix(Machine __instance)
        {
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                return;
            }

            TajsOverclockingFeature.ReapplyMachine(__instance);
        }

        /// <summary>
        ///     COI's AnimationWithPauseState has a fixed animation timeline, while Machine gives it
        ///     the speed-scaled recipe duration. At an overclock the latter can become shorter than
        ///     the former. Adjust only the state-local duration passed to this animation state; the
        ///     machine's RecipeResult remains the authoritative production timer.
        /// </summary>
        internal static void AnimationWithPauseStartPrefix(
            AnimationWithPauseState __instance,
            IEntity entity,
            ref Duration currentProcessDuration)
        {
            try
            {
                if (!TajsTweaksRuntimeState.Overclocking || entity is not Machine machine)
                {
                    return;
                }

                int overclockPercent = TajsOverclockingFeature.GetPercentFor(machine);
                if (overclockPercent == 100 || s_animationParamsField is null)
                {
                    return;
                }

                if (s_animationParamsField.GetValue(__instance) is not AnimationWithPauseParams animationParams ||
                    animationParams.FillMode != AnimationWithPauseParams.Mode.ExtendPauseToFit ||
                    !animationParams.BaseSpeed.IsPositive || currentProcessDuration.IsNotPositive)
                {
                    return;
                }

                Duration effectiveAnimationDuration = animationParams.TotalDuration.ScaledBy(
                    Percent.Hundred / animationParams.BaseSpeed);
                int adjustedTicks = OverclockingMath.EnsureAnimationProcessFits(
                    currentProcessDuration.Ticks,
                    effectiveAnimationDuration.Ticks,
                    overclockPercent);
                if (adjustedTicks != currentProcessDuration.Ticks)
                {
                    currentProcessDuration = Duration.FromTicks(adjustedTicks);
                }
            }
            catch
            {
                // Animation compatibility is optional. Preserve vanilla behavior if a private
                // field or an unexpected animation parameter shape changes.
            }
        }

        /// <summary>
        ///     COI's RepeatAutoTimes state rounds the number of repeats from the available process
        ///     duration. A sufficiently high overclock can make that duration shorter than one
        ///     repeat, which produces the native "RepeatCount is not positive" diagnostic. Extend
        ///     only the animation-local duration; the machine's production timer remains
        ///     authoritative.
        /// </summary>
        internal static void RepeatAnimationStartPrefix(
            RepeatAnimationState __instance,
            IEntity entity,
            ref Duration currentProcessDuration)
        {
            try
            {
                if (!TajsTweaksRuntimeState.Overclocking || entity is not Machine machine ||
                    currentProcessDuration.IsNotPositive || s_repeatAnimationParamsField is null)
                {
                    return;
                }

                int overclockPercent = TajsOverclockingFeature.GetPercentFor(machine);
                if (overclockPercent == 100)
                {
                    return;
                }

                if (s_repeatAnimationParamsField.GetValue(__instance) is not RepeatableAnimationParams animationParams ||
                    animationParams.RepeatCount.HasValue || !animationParams.TotalDuration.IsPositive)
                {
                    return;
                }

                Duration repeatDuration = animationParams.TotalDuration;
                if (animationParams.CustomSpeed.HasValue)
                {
                    if (animationParams.CustomSpeed.Value.IsNotPositive)
                    {
                        return;
                    }

                    repeatDuration = repeatDuration.ScaleByAnimationSpeed(animationParams.CustomSpeed.Value);
                }

                int delayedStartTicks = Math.Max(0, animationParams.DelayedStartAt.Ticks);
                int requiredTicks = repeatDuration.Ticks >= int.MaxValue - delayedStartTicks
                    ? int.MaxValue
                    : repeatDuration.Ticks + delayedStartTicks;
                int adjustedTicks = OverclockingMath.EnsureAnimationProcessFits(
                    currentProcessDuration.Ticks,
                    requiredTicks,
                    overclockPercent);
                if (adjustedTicks != currentProcessDuration.Ticks)
                {
                    currentProcessDuration = Duration.FromTicks(adjustedTicks);
                }
            }
            catch
            {
                // Animation compatibility is optional. Preserve vanilla behavior if a private
                // field or an unexpected animation parameter shape changes.
            }
        }

        internal static void ExtraWorkSimUpdatePostfix(object __instance)
        {
            if (TajsTweaksRuntimeState.Overclocking)
            {
                TajsOverclockingFeature.Current?.AdvanceExtraCycles(__instance);
            }
        }

        internal static IReadOnlyDictionary<string, object> ReadBlueprintValues(object runtimeEntity)
        {
            TajsOverclockingFeature? feature = TajsOverclockingFeature.Current;
            if (!TajsTweaksRuntimeState.Overclocking || feature is null || runtimeEntity is not IEntity entity ||
                !feature.CanControl(entity.Id))
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }

            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            if (feature.TryGetEntityPolicy(entity.Id, out OverclockEntityPolicy? policy) && policy is not null)
            {
                if (policy.HasManualOverride)
                {
                    values[ConfigKey] = policy.ManualPercent;
                }
                if (policy.HasAutoOverride)
                {
                    values[AutoConfigKey] = policy.Auto;
                }
                if (policy.HasBoundsOverride)
                {
                    values[MinConfigKey] = policy.MinPercent;
                    values[MaxConfigKey] = policy.MaxPercent;
                }
            }
            else
            {
                int percent = feature.GetPercent(entity.Id);
                if (percent != 100)
                {
                    values[ConfigKey] = percent;
                }
            }

            return values;
        }

        internal static bool ApplyBlueprintValues(object runtimeEntity, IReadOnlyDictionary<string, object> values)
        {
            TajsOverclockingFeature? feature = TajsOverclockingFeature.Current;
            if (!TajsTweaksRuntimeState.Overclocking || feature is null || runtimeEntity is not IEntity entity || values is null ||
                !feature.CanControl(entity.Id))
            {
                return false;
            }

            int? percent = null;
            bool? auto = null;
            int? minimum = null;
            int? maximum = null;
            foreach (KeyValuePair<string, object> pair in values)
            {
                if (pair.Key == ConfigKey && TryReadInt(pair.Value, out int parsedPercent))
                {
                    percent = parsedPercent;
                }
                else if (pair.Key == AutoConfigKey && pair.Value is bool parsedAuto)
                {
                    auto = parsedAuto;
                }
                else if (pair.Key == MinConfigKey && TryReadInt(pair.Value, out int parsedMinimum))
                {
                    minimum = parsedMinimum;
                }
                else if (pair.Key == MaxConfigKey && TryReadInt(pair.Value, out int parsedMaximum))
                {
                    maximum = parsedMaximum;
                }
                else if (pair.Key == ConfigKey || pair.Key == AutoConfigKey || pair.Key == MinConfigKey || pair.Key == MaxConfigKey)
                {
                    return false;
                }
            }

            if (!percent.HasValue && !auto.HasValue && !minimum.HasValue && !maximum.HasValue)
            {
                return false;
            }

            if (percent.HasValue && !feature.ExecuteSetManual(entity.Id, percent.Value, out _))
            {
                return false;
            }

            if (auto.HasValue || minimum.HasValue || maximum.HasValue)
            {
                bool enabled = auto ?? feature.IsAuto(entity.Id);
                if (!feature.ExecuteSetAuto(entity.Id, enabled, minimum, maximum, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadInt(object value, out int result)
        {
            try
            {
                result = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException)
            {
                result = default;
                return false;
            }
        }

        internal static void AddToConfigPostfix(Machine __instance, EntityConfigData data)
        {
            if (!TajsTweaksRuntimeState.Overclocking)
            {
                return;
            }

            TajsOverclockingFeature? feature = TajsOverclockingFeature.Current;
            if (feature is null)
            {
                return;
            }

            if (TajsConfigurationPipeline.TryCapture(__instance, data))
            {
                return;
            }

            if (feature.TryGetEntityPolicy(__instance.Id, out OverclockEntityPolicy? policy) && policy is not null)
            {
                if (policy.HasManualOverride)
                {
                    data.SetInt(ConfigKey, policy.ManualPercent);
                }

                if (policy.HasAutoOverride && policy.Auto)
                {
                    data.SetBool(AutoConfigKey, true);
                }

                if (policy.HasBoundsOverride)
                {
                    data.SetInt(MinConfigKey, policy.MinPercent);
                    data.SetInt(MaxConfigKey, policy.MaxPercent);
                }
                return;
            }

            int percent = feature.GetPercent(__instance.Id);
            if (percent != 100)
            {
                data.SetInt(ConfigKey, percent);
            }
        }

        internal static void ApplyConfigPostfix(Machine __instance, EntityConfigData data)
        {
            if (!TajsTweaksRuntimeState.Overclocking || TajsOverclockingFeature.Current is not TajsOverclockingFeature feature)
            {
                return;
            }

            if (TajsConfigurationPipeline.TryApply(__instance, data))
            {
                return;
            }

            int? percent = data.GetInt(ConfigKey);
            bool? auto = data.GetBool(AutoConfigKey);
            int? minimum = data.GetInt(MinConfigKey);
            int? maximum = data.GetInt(MaxConfigKey);
            if (percent.HasValue)
            {
                feature.ExecuteSetManual(__instance.Id, percent.Value, out _);
            }

            if (auto.HasValue || minimum.HasValue || maximum.HasValue)
            {
                feature.ExecuteSetAuto(__instance.Id, auto == true, minimum, maximum, out _);
            }
        }

        internal static void TransportAddToConfigPostfix(Transport __instance, EntityConfigData data)
        {
            if (!TajsTweaksRuntimeState.Overclocking || TajsOverclockingFeature.Current is not TajsOverclockingFeature feature)
            {
                return;
            }

            if (TajsConfigurationPipeline.TryCapture(__instance, data))
            {
                return;
            }

            if (feature.TryGetEntityPolicy(__instance.Id, out OverclockEntityPolicy? policy) && policy is not null)
            {
                if (policy.HasManualOverride)
                {
                    data.SetInt(ConfigKey, policy.ManualPercent);
                }

                if (policy.HasAutoOverride && policy.Auto)
                {
                    data.SetBool(AutoConfigKey, true);
                }

                if (policy.HasBoundsOverride)
                {
                    data.SetInt(MinConfigKey, policy.MinPercent);
                    data.SetInt(MaxConfigKey, policy.MaxPercent);
                }

                return;
            }

            int percent = feature.GetPercent(__instance.Id);
            if (percent != 100)
            {
                data.SetInt(ConfigKey, percent);
            }
        }

        internal static void TransportApplyConfigPostfix(Transport __instance, EntityConfigData data)
        {
            if (!TajsTweaksRuntimeState.Overclocking || TajsOverclockingFeature.Current is not TajsOverclockingFeature feature)
            {
                return;
            }

            if (TajsConfigurationPipeline.TryApply(__instance, data))
            {
                return;
            }

            int? percent = data.GetInt(ConfigKey);
            bool? auto = data.GetBool(AutoConfigKey);
            int? minimum = data.GetInt(MinConfigKey);
            int? maximum = data.GetInt(MaxConfigKey);
            if (percent.HasValue)
            {
                feature.ExecuteSetManual(__instance.Id, percent.Value, out _);
            }

            if (auto.HasValue || minimum.HasValue || maximum.HasValue)
            {
                feature.ExecuteSetAuto(__instance.Id, auto == true, minimum, maximum, out _);
            }
        }

        private static void PatchOptionalCostInterfaces(Harmony harmony, Type entityType)
        {
            PatchInterface(harmony, entityType, typeof(IElectricityConsumingEntity), "get_PowerRequired", nameof(PowerPostfix), required: false);
            PatchInterface(harmony, entityType, typeof(IComputingConsumingEntity), "get_ComputingRequired", nameof(ComputingPostfix), required: false);
            PatchInterface(harmony, entityType, typeof(IEntityWithWorkers), "get_WorkersNeeded", nameof(WorkersPostfix), required: false);
            PatchInterface(harmony, entityType, typeof(IMaintainedEntity), "get_MaintenanceCosts", nameof(MaintenancePostfix), required: false);
        }

        private static void PatchOfficeFocusPoints(Harmony harmony)
        {
            PatchOfficeFocusGetter(harmony, nameof(OfficeBuilding.FocusPointsLastTick));
            PatchOfficeFocusGetter(harmony, nameof(OfficeBuilding.FocusPointsMaxAvailable));
        }

        private static void PatchOfficeFocusGetter(Harmony harmony, string propertyName)
        {
            try
            {
                MethodInfo? getter = AccessTools.PropertyGetter(typeof(OfficeBuilding), propertyName);
                if (getter is not null)
                {
                    harmony.Patch(
                        getter,
                        postfix: new HarmonyMethod(typeof(OverclockingPatches), nameof(OfficeFocusPointsPostfix)));
                }
            }
            catch
            {
                // Office focus is an optional compatibility seam; a changed getter leaves
                // office focus vanilla without disabling the rest of overclocking.
            }
        }

        private static void PatchInterface(Harmony harmony, Type entityType, Type interfaceType, string methodName, string postfixName, bool required)
        {
            try
            {
                if (!interfaceType.IsAssignableFrom(entityType))
                {
                    if (required)
                    {
                        throw new MissingMethodException(entityType.FullName, methodName);
                    }

                    return;
                }

                InterfaceMapping map = entityType.GetInterfaceMap(interfaceType);
                for (int index = 0; index < map.InterfaceMethods.Length; index++)
                {
                    if (map.InterfaceMethods[index].Name == methodName)
                    {
                        harmony.Patch(map.TargetMethods[index], postfix: new HarmonyMethod(typeof(OverclockingPatches), postfixName));
                        return;
                    }
                }

                if (required)
                {
                    throw new MissingMethodException(entityType.FullName, methodName);
                }
            }
            catch when (!required)
            {
                // Non-standard entities are opt-in by exact seam discovery. A missing optional
                // interface leaves that entity vanilla without disabling machine overclocking.
            }
        }

        private static void PatchSimUpdate(Harmony harmony, Type entityType, string postfixName, bool required)
        {
            try
            {
                if (!typeof(IEntityWithSimUpdate).IsAssignableFrom(entityType))
                {
                    if (required)
                    {
                        throw new MissingMethodException(entityType.FullName, "IEntityWithSimUpdate.SimUpdate");
                    }

                    return;
                }

                if (!s_simUpdateMethods.TryGetValue(entityType, out MethodInfo? target))
                {
                    InterfaceMapping map = entityType.GetInterfaceMap(typeof(IEntityWithSimUpdate));
                    int index = Array.FindIndex(map.InterfaceMethods, method => method.Name == "SimUpdate");
                    target = index < 0 ? null : map.TargetMethods[index];
                    s_simUpdateMethods[entityType] = target;
                }

                if (target is null)
                {
                    if (required)
                    {
                        throw new MissingMethodException(entityType.FullName, "IEntityWithSimUpdate.SimUpdate");
                    }

                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(typeof(OverclockingPatches), postfixName));
            }
            catch when (!required)
            {
            }
        }
    }
}
