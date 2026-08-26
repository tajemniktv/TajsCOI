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
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Maintenance;
using Mafi.Core.Population;

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

            PatchSimUpdate(harmony, typeof(Machine), nameof(MachineSimUpdatePostfix), required: true);
            PatchSimUpdate(harmony, typeof(OfficeBuilding), nameof(ExtraWorkSimUpdatePostfix), required: false);
            PatchSimUpdate(harmony, typeof(WasteSortingPlant), nameof(ExtraWorkSimUpdatePostfix), required: false);

            MethodInfo? animationStart = AccessTools.Method(typeof(AnimationWithPauseState), nameof(AnimationWithPauseState.Start));
            if (animationStart is not null && s_animationParamsField is not null)
            {
                harmony.Patch(animationStart,
                    prefix: new HarmonyMethod(typeof(OverclockingPatches), nameof(AnimationWithPauseStartPrefix)));
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
                __result = new Electricity(Math.Max(0, (int)Math.Round(__result.Value *
                    OverclockingMath.CostMultiplier(percent, TajsTweaksRuntimeState.OverclockPowerCurve))));
            }
        }

        internal static void ComputingPostfix(object __instance, ref Computing __result)
        {
            int percent = TajsOverclockingFeature.GetPercentFor(__instance);
            if (percent != 100)
            {
                __result = new Computing(Math.Max(0, (int)Math.Round(__result.Value *
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
                int scale = Math.Max(1, (int)Math.Round(OverclockingMath.CostMultiplier(
                    percent, TajsTweaksRuntimeState.OverclockMaintenanceCurve) * 100));
                __result = new MaintenanceCosts(
                    __result.Product,
                    __result.MaintenancePerMonth.ScaledBy(scale.Percent()),
                    __result.MaxMaintenancePerMonth,
                    __result.ExtraBufferDuration,
                    __result.InitialMaintenanceBoost);
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
        /// COI's AnimationWithPauseState has a fixed animation timeline, while Machine gives it
        /// the speed-scaled recipe duration. At an overclock the latter can become shorter than
        /// the former. Adjust only the state-local duration passed to this animation state; the
        /// machine's RecipeResult remains the authoritative production timer.
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

        internal static void ExtraWorkSimUpdatePostfix(object __instance)
        {
            if (TajsTweaksRuntimeState.Overclocking)
            {
                TajsOverclockingFeature.Current?.AdvanceExtraCycles(__instance);
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

            int? percent = data.GetInt(ConfigKey);
            bool? auto = data.GetBool(AutoConfigKey);
            int? minimum = data.GetInt(MinConfigKey);
            int? maximum = data.GetInt(MaxConfigKey);
            if (percent.HasValue)
            {
                feature.ApplyManual(__instance.Id, percent.Value, out _);
            }

            if (auto.HasValue || minimum.HasValue || maximum.HasValue)
            {
                feature.SetAuto(__instance.Id, auto == true, minimum, maximum, out _);
            }
        }

        internal static void TransportAddToConfigPostfix(Transport __instance, EntityConfigData data)
        {
            if (!TajsTweaksRuntimeState.Overclocking || TajsOverclockingFeature.Current is not TajsOverclockingFeature feature)
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

            int? percent = data.GetInt(ConfigKey);
            bool? auto = data.GetBool(AutoConfigKey);
            int? minimum = data.GetInt(MinConfigKey);
            int? maximum = data.GetInt(MaxConfigKey);
            if (percent.HasValue)
            {
                feature.ApplyManual(__instance.Id, percent.Value, out _);
            }

            if (auto.HasValue || minimum.HasValue || maximum.HasValue)
            {
                feature.SetAuto(__instance.Id, auto == true, minimum, maximum, out _);
            }
        }

        private static void PatchOptionalCostInterfaces(Harmony harmony, Type entityType)
        {
            PatchInterface(harmony, entityType, typeof(IElectricityConsumingEntity), "get_PowerRequired", nameof(PowerPostfix), required: false);
            PatchInterface(harmony, entityType, typeof(IComputingConsumingEntity), "get_ComputingRequired", nameof(ComputingPostfix), required: false);
            PatchInterface(harmony, entityType, typeof(IEntityWithWorkers), "get_WorkersNeeded", nameof(WorkersPostfix), required: false);
            PatchInterface(harmony, entityType, typeof(IMaintainedEntity), "get_MaintenanceCosts", nameof(MaintenancePostfix), required: false);
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
