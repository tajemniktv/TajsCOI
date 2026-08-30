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
        internal const string FocusOwner = "TajsCOI.Tweaks.Sandbox.Focus";
        internal const string SolidWasteOwner = "TajsCOI.Tweaks.Sandbox.SolidWaste";
        internal const string BiowasteOwner = "TajsCOI.Tweaks.Sandbox.Biowaste";

        private sealed class WasteState
        {
            internal object? Landfill;
            internal object? Biowaste;
        }

        private static readonly FieldInfo? s_landfillField = typeof(Settlement).GetField("m_landfillInSettlementPartial", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_biowasteField = typeof(Settlement).GetField("m_bioWasteInSettlement", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void ApplySettlementNeeds(IPropertiesDb propertiesDb)
        {
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.SettlementConsumptionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableSettlementNeeds, SettlementNeedsOwner + ".Goods");
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.FoodConsumptionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableFoodNeed, FoodNeedOwner);
        }

        internal static void ApplyPollution(IPropertiesDb propertiesDb)
        {
            // Effects and vehicle/ship/train production are separate native properties. The
            // broad air/water production toggles remain fail-open because 0.8.7b has no global
            // production property and a broad virtual-output patch would affect unrelated flows.
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.DiseaseEffectsMultiplier),
                TajsTweaksRuntimeState.SandboxDisableDiseaseEffects, DiseaseEffectsOwner);
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.AirPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableAirPollutionEffects, AirEffectsOwner);
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.WaterPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableWaterPollutionEffects, WaterEffectsOwner);
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.ShipsPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableShipPollution, ShipPollutionOwner);
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.VehiclesPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableVehiclePollution, VehiclePollutionOwner);
            ApplyToggle(propertiesDb.GetProperty(IdsCore.PropertyIds.TrainsPollutionMultiplier),
                TajsTweaksRuntimeState.SandboxDisableTrainPollution, TrainPollutionOwner);
        }

        internal static void ApplyFocus(IPropertiesDb propertiesDb)
        {
            double multiplier = TajsTweaksRuntimeState.SandboxInfiniteFocus
                ? 1000d
                : Math.Min(1000d, Math.Max(0d, TajsTweaksRuntimeState.SandboxFocusMultiplier));
            IProperty<Percent> property = propertiesDb.GetProperty(IdsCore.PropertyIds.FocusPointsMultiplier);
            string owner = FocusOwner + ".Multiplier";
            if (Math.Abs(multiplier - 1d) < 0.0001d)
            {
                property.TryRemoveModifier(owner);
                return;
            }

            property.AddOrSetModifier(owner, ((multiplier - 1d) * 100d).Percent(), Property<Percent>.BASE_GROUP);
        }

        internal static void InstallSolidWaste(Harmony harmony)
        {
            MethodInfo? method = AccessTools.Method(typeof(Settlement), "TransformProductIntoWaste");
            if (method is null)
            {
                throw new MissingMethodException(typeof(Settlement).FullName, "TransformProductIntoWaste");
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(CaptureWasteStatePrefix)),
                postfix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(RestoreSolidWastePostfix)));
        }

        internal static void InstallBiowaste(Harmony harmony)
        {
            MethodInfo? method = AccessTools.Method(typeof(Settlement), "TransformProductIntoWaste");
            if (method is null)
            {
                throw new MissingMethodException(typeof(Settlement).FullName, "TransformProductIntoWaste");
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(CaptureWasteStatePrefix)),
                postfix: new HarmonyMethod(typeof(SandboxControlsFeature), nameof(RestoreBiowastePostfix)));
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
            if (TajsTweaksRuntimeState.SandboxDisableSolidWaste && s_landfillField is not null)
            {
                s_landfillField.SetValue(__instance, __state.Landfill);
            }
        }

        private static void RestoreBiowastePostfix(Settlement __instance, WasteState __state)
        {
            if (TajsTweaksRuntimeState.SandboxDisableBiowaste && s_biowasteField is not null)
            {
                s_biowasteField.SetValue(__instance, __state.Biowaste);
            }
        }

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
    }
}
