// Taj's COI Mods | InfrastructureTuningFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Globalization;
using System.Reflection;
using Mafi;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles.Trucks;
using TajsCOI.Common.Tuning;

namespace TajsCOI.Tweaks.Features.InfrastructureTuning
{
    /// <summary>
    /// Owns the narrow prototype seams from issue #106.  Prototype values are captured once and
    /// all later changes derive from that capture, so changing a setting never compounds a
    /// previous override.  Missing or readonly members simply leave the native value untouched.
    /// </summary>
    internal static class InfrastructureTuningFeature
    {
        internal const string ShipyardCargoKey = "TajsTweaks.Tuning.ShipyardCargoCapacity";
        internal const string TruckLoadDurationKey = "TajsTweaks.Tuning.TruckCargoPickupDuration";
        internal const string OreSorterInputBufferKey = "TajsTweaks.Tuning.OreSorterInputBuffer";
        internal const string OreSorterOutputBufferKey = "TajsTweaks.Tuning.OreSorterOutputBuffers";
        internal const string OreSorterThroughputKey = "TajsTweaks.Tuning.OreSorterThroughput";
        internal const string ShaftThroughputKey = "TajsTweaks.Tuning.ShaftThroughput";
        internal const string ThermalStorageCapacityKey = "TajsTweaks.Tuning.ThermalStorageCapacity";

        private static readonly BaseValueOverrideService s_values = new();
        internal static BaseValueOverrideService Values => s_values;

        internal static bool IsAvailable(string key) => s_values.HasRegistration(key);

        internal static void Reset() => s_values.Clear();

        internal static void ApplyFromPrototypes(ProtosDb protosDb)
        {
            if (protosDb is null)
            {
                return;
            }

            // These fields are immutable prototype registrations in 0.8.7b.  Reflection is
            // deliberately resolved once, and a failed member leaves the native path intact.
            foreach (ShipyardProto proto in protosDb.All<ShipyardProto>())
            {
                TryRegisterField(proto, nameof(ShipyardProto.CargoCapacity), ShipyardCargoKey, 1d, 1000000d);
            }

            foreach (TruckProto proto in protosDb.All<TruckProto>())
            {
                TryRegisterField(proto, nameof(TruckProto.CargoPickupDuration), TruckLoadDurationKey, 1d, 1000000d);
            }

            foreach (OreSortingPlantProto proto in protosDb.All<OreSortingPlantProto>())
            {
                TryRegisterField(proto, nameof(OreSortingPlantProto.InputBufferCapacity), OreSorterInputBufferKey, 1d, 1000000d);
                TryRegisterField(proto, nameof(OreSortingPlantProto.OutputBuffersCapacity), OreSorterOutputBufferKey, 1d, 1000000d);
                TryRegisterMember(proto, nameof(OreSortingPlantProto.QuantityPerDuration), OreSorterThroughputKey, 1d, 1000000d);
            }

            // ThermalStorageProto lives in Mafi.Base. Resolve it by assembly-qualified name so
            // TajsTweaks keeps its normal Mafi.Unity compile boundary while still adapting the
            // exact 0.8.7b prototype when that assembly is present.
            Type? thermalType = Type.GetType("Mafi.Base.Prototypes.Buildings.ThermalStorages.ThermalStorageProto, Mafi.Base", throwOnError: false);
            if (thermalType is not null)
            {
                foreach (object proto in protosDb.All(thermalType))
                {
                    TryRegisterMember(proto, "Capacity", ThermalStorageCapacityKey, 1d, 100000000d);
                }
            }

            Type? shaftManagerType = Type.GetType("Mafi.Core.Factory.MechanicalPower.ShaftManager, Mafi.Core", throwOnError: false);
            FieldInfo? shaftField = shaftManagerType?.GetField("MAX_SHAFT_THROUGHPUT", BindingFlags.Public | BindingFlags.Static);
            if (shaftField is not null)
            {
                s_values.TryRegister(
                    ShaftThroughputKey,
                    shaftField.FieldType,
                    () => shaftField.GetValue(null),
                    value => shaftField.SetValue(null, value),
                    1d,
                    1000000d,
                    BaseValueApplyMode.ReloadRequired);
                s_values.TrySetMultiplier(ShaftThroughputKey, TajsTweaksRuntimeState.TuningShaftThroughputMultiplier);
            }
        }

        internal static bool TrySetMultiplier(string key, double multiplier) =>
            s_values.TrySetMultiplier(key, multiplier);

        internal static bool TryReset(string key) => s_values.TryReset(key);

        /// <summary>
        /// Returns a capacity that cannot strand already stored thermal energy.  A reduction is
        /// represented as temporary over-capacity; callers reject new charging until discharge
        /// brings the stored amount below the requested capacity.
        /// </summary>
        internal static double EffectiveThermalCapacity(double requestedCapacity, double heatStored)
        {
            if (double.IsNaN(requestedCapacity) || double.IsInfinity(requestedCapacity) || requestedCapacity < 0d)
            {
                return heatStored;
            }

            if (double.IsNaN(heatStored) || double.IsInfinity(heatStored) || heatStored < 0d)
            {
                heatStored = 0d;
            }

            return Math.Max(requestedCapacity, heatStored);
        }

        internal static bool CanChargeThermalStorage(double requestedCapacity, double heatStored) =>
            heatStored < requestedCapacity;

        private static void TryRegisterField(object target, string memberName, string key, double minimum, double maximum)
        {
            FieldInfo? field = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is null)
            {
                return;
            }

            string registrationKey = key + "." + GetPrototypeIdentity(target);
            s_values.TryRegister(
                registrationKey,
                field.FieldType,
                () => field.GetValue(target),
                value => field.SetValue(target, value),
                minimum,
                maximum,
                BaseValueApplyMode.ReloadRequired);

            ApplyConfigured(registrationKey, key);
        }

        private static void TryRegisterMember(object target, string memberName, string key, double minimum, double maximum)
        {
            Type type = target.GetType();
            PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo? field = property is null
                ? type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : null;
            Type? valueType = property?.PropertyType ?? field?.FieldType;
            if (valueType is null)
            {
                // Auto-properties in game prototypes are backed by a private readonly field.
                field = type.GetField("<" + memberName + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                valueType = field?.FieldType;
            }

            if (valueType is null || (property is not null && !property.CanWrite && field is null))
            {
                return;
            }

            string registrationKey = key + "." + GetPrototypeIdentity(target);
            Func<object?> getter = property is not null ? () => property.GetValue(target) : () => field!.GetValue(target);
            Action<object?> setter = property?.CanWrite == true
                ? value => property.SetValue(target, value)
                : value => field!.SetValue(target, value);
            if (s_values.TryRegister(registrationKey, valueType, getter, setter, minimum, maximum, BaseValueApplyMode.ReloadRequired))
            {
                ApplyConfigured(registrationKey, key);
            }
        }

        private static void ApplyConfigured(string registrationKey, string category)
        {
            double multiplier = category switch
            {
                ShipyardCargoKey => TajsTweaksRuntimeState.TuningShipyardCargoMultiplier,
                TruckLoadDurationKey => TajsTweaksRuntimeState.TuningTruckLoadDurationMultiplier,
                OreSorterInputBufferKey => TajsTweaksRuntimeState.TuningOreSorterBufferMultiplier,
                OreSorterOutputBufferKey => TajsTweaksRuntimeState.TuningOreSorterBufferMultiplier,
                OreSorterThroughputKey => Math.Max(
                    TajsTweaksRuntimeState.TuningOreSorterThroughputMultiplier,
                    TajsTweaksRuntimeState.SandboxFastOreSorting ? 2d : 1d),
                ThermalStorageCapacityKey => TajsTweaksRuntimeState.TuningThermalStorageCapacityMultiplier,
                _ => 1d,
            };
            s_values.TrySetMultiplier(registrationKey, multiplier);
        }

        private static string GetPrototypeIdentity(object prototype)
        {
            object? id = prototype.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(prototype);
            return Convert.ToString(id, CultureInfo.InvariantCulture) ?? prototype.GetType().Name;
        }
    }
}
