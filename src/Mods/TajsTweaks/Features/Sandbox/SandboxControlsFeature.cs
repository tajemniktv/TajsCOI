// Taj's COI Mods | SandboxControlsFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.PropertiesDb;
using Mafi.Core.Products;

namespace TajsCOI.Tweaks.Features.Sandbox
{
    internal enum WasteOutput
    {
        Solid,
        Biowaste,
    }

    /// <summary>
    /// Native-property and narrow source-branch sandbox controls for settlement needs,
    /// pollution, disease effects, focus and settlement waste. Every modifier has a dedicated
    /// owner so disabling one option removes only that option's contribution.
    /// </summary>
    internal static class SandboxControlsFeature
    {
        internal const string SettlementNeedsOwner = "TajsCOI.Tweaks.Sandbox.SettlementNeeds";
        internal const string FoodNeedOwner = "TajsCOI.Tweaks.Sandbox.FoodNeed";
        internal const string DiseaseEffectsOwner = "TajsCOI.Tweaks.Sandbox.DiseaseEffects";
        internal const string AirEffectsOwner = "TajsCOI.Tweaks.Sandbox.AirEffects";
        internal const string WaterEffectsOwner = "TajsCOI.Tweaks.Sandbox.WaterEffects";
        internal const string ShipPollutionOwner = "TajsCOI.Tweaks.Sandbox.ShipPollution";
        internal const string VehiclePollutionOwner = "TajsCOI.Tweaks.Sandbox.VehiclePollution";
        internal const string TrainPollutionOwner = "TajsCOI.Tweaks.Sandbox.TrainPollution";
        internal const string FocusInfiniteOwner = "TajsCOI.Tweaks.Sandbox.Focus.Infinite";
        internal const string FocusMultiplierOwner = "TajsCOI.Tweaks.Sandbox.Focus.Multiplier";
        internal const string SolidWasteOwner = "TajsCOI.Tweaks.Sandbox.SolidWaste";
        internal const string BiowasteOwner = "TajsCOI.Tweaks.Sandbox.Biowaste";

        private static bool s_settlementNeedsAvailable;
        private static bool s_foodNeedAvailable;
        private static bool s_diseaseEffectsAvailable;
        private static bool s_airEffectsAvailable;
        private static bool s_waterEffectsAvailable;
        private static bool s_shipPollutionAvailable;
        private static bool s_vehiclePollutionAvailable;
        private static bool s_trainPollutionAvailable;
        private static bool s_focusAvailable;
        private static bool s_solidWasteAvailable;
        private static bool s_biowasteAvailable;

        internal static bool SettlementNeedsAvailable => s_settlementNeedsAvailable;
        internal static bool FoodNeedAvailable => s_foodNeedAvailable;
        internal static bool DiseaseEffectsAvailable => s_diseaseEffectsAvailable;
        internal static bool AirEffectsAvailable => s_airEffectsAvailable;
        internal static bool WaterEffectsAvailable => s_waterEffectsAvailable;
        internal static bool ShipPollutionAvailable => s_shipPollutionAvailable;
        internal static bool VehiclePollutionAvailable => s_vehiclePollutionAvailable;
        internal static bool TrainPollutionAvailable => s_trainPollutionAvailable;
        internal static bool FocusAvailable => s_focusAvailable;
        internal static bool SolidWasteAvailable => s_solidWasteAvailable;
        internal static bool BiowasteAvailable => s_biowasteAvailable;

        private sealed class WasteState
        {
            internal object? Landfill;
            internal object? Biowaste;
        }

        private static readonly FieldInfo? s_landfillField = typeof(Settlement).GetField("m_landfillInSettlementPartial", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_biowasteField = typeof(Settlement).GetField("m_bioWasteInSettlement", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void ApplySettlementNeeds(IPropertiesDb propertiesDb)
        {
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.SettlementConsumptionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableSettlementNeeds,
                SettlementNeedsOwner,
                ref s_settlementNeedsAvailable);
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.FoodConsumptionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableFoodNeed,
                FoodNeedOwner,
                ref s_foodNeedAvailable);
        }

        internal static void ApplyPollution(IPropertiesDb propertiesDb)
        {
            // Effects and vehicle/ship/train production are separate native properties. The
            // broad air/water production toggles remain fail-open because 0.8.7b has no global
            // production property and a broad virtual-output patch would affect unrelated flows.
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.DiseaseEffectsMultiplier),
                TajsTweaksRuntimeState.SandboxDisableDiseaseEffects,
                DiseaseEffectsOwner,
                ref s_diseaseEffectsAvailable);
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.AirPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableAirPollutionEffects,
                AirEffectsOwner,
                ref s_airEffectsAvailable);
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.WaterPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableWaterPollutionEffects,
                WaterEffectsOwner,
                ref s_waterEffectsAvailable);
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.ShipsPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableShipPollution,
                ShipPollutionOwner,
                ref s_shipPollutionAvailable);
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.VehiclesPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableVehiclePollution,
                VehiclePollutionOwner,
                ref s_vehiclePollutionAvailable);
            TryApplyToggle(
                () => propertiesDb.GetProperty(IdsCore.PropertyIds.TrainsPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableTrainPollution,
                TrainPollutionOwner,
                ref s_trainPollutionAvailable);
        }

        internal static void ApplyFocus(IPropertiesDb propertiesDb)
        {
            double configuredMultiplier = TajsTweaksRuntimeState.SandboxFocusMultiplier;
            if (double.IsNaN(configuredMultiplier) || double.IsInfinity(configuredMultiplier))
            {
                configuredMultiplier = 1d;
            }
            double multiplier = TajsTweaksRuntimeState.SandboxInfiniteFocus
                ? 1000d
                : Math.Min(1000d, Math.Max(0d, configuredMultiplier));
            try
            {
                IProperty<Percent> property = propertiesDb.GetProperty(IdsCore.PropertyIds.FocusPointsMultiplier);
                property.TryRemoveModifier(FocusInfiniteOwner);
                property.TryRemoveModifier(FocusMultiplierOwner);
                string owner = TajsTweaksRuntimeState.SandboxInfiniteFocus
                    ? FocusInfiniteOwner
                    : FocusMultiplierOwner;
                if (Math.Abs(multiplier - 1d) >= 0.0001d)
                {
                    property.AddOrSetModifier(owner, ((multiplier - 1d) * 100d).Percent(), Property<Percent>.BASE_GROUP);
                }
                s_focusAvailable = true;
            }
            catch
            {
                s_focusAvailable = false;
            }
        }

        internal static void InstallSolidWaste(Harmony harmony)
        {
            s_solidWasteAvailable = false;
            MethodInfo? method = AccessTools.Method(typeof(Settlement), "TransformProductIntoWaste");
            if (method is null || s_landfillField is null)
            {
                throw new MissingMethodException(typeof(Settlement).FullName, "TransformProductIntoWaste/m_landfillInSettlementPartial");
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(CaptureWasteStatePrefix)),
                postfix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(RestoreSolidWastePostfix)));
            s_solidWasteAvailable = true;
        }

        internal static void InstallBiowaste(Harmony harmony)
        {
            s_biowasteAvailable = false;
            MethodInfo? method = AccessTools.Method(typeof(Settlement), "TransformProductIntoWaste");
            if (method is null || s_biowasteField is null)
            {
                throw new MissingMethodException(typeof(Settlement).FullName, "TransformProductIntoWaste/m_bioWasteInSettlement");
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(CaptureWasteStatePrefix)),
                postfix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(RestoreBiowastePostfix)));
            s_biowasteAvailable = true;
        }

        internal static void InstallWaste(Harmony harmony)
        {
            InstallSolidWaste(harmony);
            InstallBiowaste(harmony);
        }

        private static void CaptureWasteStatePrefix(Settlement __instance, out WasteState __state)
        {
            __state = new WasteState
            {
                Landfill = s_landfillField?.GetValue(__instance),
                Biowaste = s_biowasteField?.GetValue(__instance),
            };
        }

        private static void RestoreSolidWastePostfix(Settlement __instance, WasteState __state)
        {
            if (ShouldSuppressWasteOutput(
                    WasteOutput.Solid,
                    TajsTweaksRuntimeState.SandboxDisableSolidWaste,
                    TajsTweaksRuntimeState.SandboxDisableBiowaste) &&
                s_landfillField is not null)
            {
                s_landfillField.SetValue(__instance, __state.Landfill);
            }
        }

        private static void RestoreBiowastePostfix(Settlement __instance, WasteState __state)
        {
            if (ShouldSuppressWasteOutput(
                    WasteOutput.Biowaste,
                    TajsTweaksRuntimeState.SandboxDisableSolidWaste,
                    TajsTweaksRuntimeState.SandboxDisableBiowaste) &&
                s_biowasteField is not null)
            {
                s_biowasteField.SetValue(__instance, __state.Biowaste);
            }
        }

        internal static bool ShouldSuppressWasteOutput(
            WasteOutput output,
            bool disableSolidWaste,
            bool disableBiowaste) =>
            output == WasteOutput.Solid ? disableSolidWaste : disableBiowaste;

        private static void ApplyToggle(IProperty<Percent> property, bool disabled, string owner)
        {
            if (disabled)
            {
                property.AddOrSetModifier(owner, (-100).Percent(), Property<Percent>.BASE_GROUP);
            }
            else
            {
                property.TryRemoveModifier(owner);
            }
        }

        private static bool TryApplyToggle(
            Func<IProperty<Percent>> propertyFactory,
            bool disabled,
            string owner,
            ref bool available)
        {
            try
            {
                ApplyToggle(propertyFactory(), disabled, owner);
                available = true;
                return true;
            }
            catch
            {
                available = false;
                return false;
            }
        }
    }
}
