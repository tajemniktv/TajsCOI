// Taj's COI Mods | InfrastructureTuningFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles.Trucks;
using TajsCOI.Common.Tuning;
using TajsCOI.Tweaks.Features.Tuning;

namespace TajsCOI.Tweaks.Features.InfrastructureTuning
{
    /// <summary>
    /// Owns the narrow prototype seams from issue #106.  Prototype values are captured once and
    /// all later changes derive from that capture, so changing a setting never compounds a
    /// previous override.  Missing or readonly members simply leave the native value untouched.
    /// </summary>
    internal sealed class InfrastructureTuningFeature : IDisposable
    {
        internal const string HarmonyId = "TajsCOI.Tweaks.InfrastructureTuning";
        internal const string ShipyardCargoKey = "TajsTweaks.Tuning.ShipyardCargoCapacity";
        internal const string TruckLoadDurationKey = "TajsTweaks.Tuning.TruckCargoPickupDuration";
        internal const string OreSorterInputBufferKey = "TajsTweaks.Tuning.OreSorterInputBuffer";
        internal const string OreSorterOutputBufferKey = "TajsTweaks.Tuning.OreSorterOutputBuffers";
        internal const string OreSorterThroughputKey = "TajsTweaks.Tuning.OreSorterThroughput";
        internal const string ShaftThroughputKey = "TajsTweaks.Tuning.ShaftThroughput";
        internal const string ThermalStorageCapacityKey = "TajsTweaks.Tuning.ThermalStorageCapacity";

        private readonly TypedBaseValueOverrideRegistry m_values;
        private readonly HashSet<string> m_ownedKeys = new(StringComparer.Ordinal);
        private static bool s_thermalInstalled;
        internal TypedBaseValueOverrideRegistry Values => m_values;

        internal InfrastructureTuningFeature(TypedBaseValueOverrideRegistry values)
        {
            m_values = values ?? throw new ArgumentNullException(nameof(values));
        }

        internal bool IsAvailable(string key) => m_values.HasRegistration(key);
        internal static bool ThermalPatchAvailable => s_thermalInstalled;

        internal void Reset()
        {
            foreach (string key in m_ownedKeys.ToArray())
            {
                m_values.TryUnregister(key);
            }
            m_ownedKeys.Clear();
        }

        public void Dispose() => Reset();

        private sealed class ThermalState
        {
            internal int Stored;
        }

        internal static void InstallThermalCapacity(Harmony harmony)
        {
            if (s_thermalInstalled)
            {
                return;
            }
            Type? thermalType = Type.GetType("Mafi.Base.Prototypes.Buildings.ThermalStorages.ThermalStorage, Mafi.Base", throwOnError: false);
            if (thermalType is null)
            {
                throw new TypeLoadException("Mafi.Base ThermalStorage is unavailable.");
            }

            MethodInfo? assign = AccessTools.Method(thermalType, "AssignProduct");
            MethodInfo? receive = thermalType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name.IndexOf("ReceiveAsMuchAsFromPort", StringComparison.Ordinal) >= 0);
            if (assign is null || receive is null)
            {
                throw new MissingMethodException(thermalType.FullName, "AssignProduct/ReceiveAsMuchAsFromPort");
            }

            harmony.Patch(
                assign,
                prefix: new HarmonyMethod(typeof(InfrastructureTuningFeature), nameof(CaptureThermalStatePrefix)),
                postfix: new HarmonyMethod(typeof(InfrastructureTuningFeature), nameof(RestoreThermalStatePostfix)));
            harmony.Patch(receive, prefix: new HarmonyMethod(typeof(InfrastructureTuningFeature), nameof(ThermalReceivePrefix)));
            s_thermalInstalled = true;
        }

        private static void CaptureThermalStatePrefix(object __instance, out ThermalState __state)
        {
            __state = new ThermalState { Stored = ReadInt(__instance, "HeatStored") };
        }

        private static void RestoreThermalStatePostfix(object __instance, ThermalState __state)
        {
            if (TajsTweaksRuntimeState.TuningThermalStorageCapacityMultiplier >= 1d ||
                __state.Stored <= 0 || ReadInt(__instance, "HeatCapacity") >= __state.Stored)
            {
                return;
            }

            // Prototype reduction cannot evict heat. Restore the old stored amount as temporary
            // over-capacity; the receive prefix rejects new charging until discharge catches up.
            // Keep HeatCapacity at the newly derived (reduced) target so the over-capacity state
            // naturally clears as heat is discharged.
            WriteInt(__instance, "HeatStored", __state.Stored);
        }

        private static bool ThermalReceivePrefix(object __instance, object __0, ref Quantity __result)
        {
            if (TajsTweaksRuntimeState.TuningThermalStorageCapacityMultiplier < 1d &&
                ReadInt(__instance, "HeatStored") >= ReadInt(__instance, "HeatCapacity"))
            {
                PropertyInfo? quantity = FindInstanceProperty(__0.GetType(), "Quantity");
                if (quantity?.GetValue(__0) is Quantity incoming)
                {
                    __result = incoming;
                    return false;
                }
            }
            return true;
        }

        private static int ReadInt(object instance, string propertyName)
        {
            try
            {
                object? value = FindInstanceProperty(instance.GetType(), propertyName)?.GetValue(instance);
                return value is int integer ? integer : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static void WriteInt(object instance, string propertyName, int value)
        {
            try
            {
                PropertyInfo? property = FindInstanceProperty(instance.GetType(), propertyName);
                MethodInfo? setter = property?.GetSetMethod(nonPublic: true);
                if (setter is not null)
                {
                    setter.Invoke(instance, new object[] { value });
                    return;
                }
                instance.GetType().GetField("<" + propertyName + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(instance, value);
            }
            catch
            {
                // Private compatibility seams fail open if the game changes shape.
            }
        }

        internal void ApplyFromPrototypes(ProtosDb protosDb, bool thermalSafe = true)
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
            if (!m_values.HasRegistration(OreSorterInputBufferKey) ||
                !m_values.HasRegistration(OreSorterOutputBufferKey) ||
                !m_values.HasRegistration(OreSorterThroughputKey))
            {
                RemoveCategory(OreSorterInputBufferKey);
                RemoveCategory(OreSorterOutputBufferKey);
                RemoveCategory(OreSorterThroughputKey);
            }

            // ThermalStorageProto lives in Mafi.Base. Resolve it by assembly-qualified name so
            // TajsTweaks keeps its normal Mafi.Unity compile boundary while still adapting the
            // exact 0.8.7b prototype when that assembly is present.
            Type? thermalType = Type.GetType("Mafi.Base.Prototypes.Buildings.ThermalStorages.ThermalStorageProto, Mafi.Base", throwOnError: false);
            if (thermalSafe && thermalType is not null)
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
                bool existed = HasExactRegistration(ShaftThroughputKey);
                bool registered = m_values.TryRegister(
                    ShaftThroughputKey,
                    shaftField.FieldType,
                    () => shaftField.GetValue(null),
                    value => shaftField.SetValue(null, value),
                    1d,
                    1000000d,
                    BaseValueApplyMode.ReloadRequired);
                if (registered)
                {
                    m_ownedKeys.Add(ShaftThroughputKey);
                }
                if (registered && !m_values.TrySetMultiplier(ShaftThroughputKey, TajsTweaksRuntimeState.TuningShaftThroughputMultiplier))
                {
                    if (!existed)
                    {
                        m_values.TryUnregister(ShaftThroughputKey);
                        m_ownedKeys.Remove(ShaftThroughputKey);
                    }
                }
            }
        }

        internal bool TrySetMultiplier(string key, double multiplier) =>
            m_values.TrySetMultiplier(key, multiplier);

        internal bool TryReset(string key) => m_values.TryReset(key);

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

        private void TryRegisterField(object target, string memberName, string key, double minimum, double maximum)
        {
            FieldInfo? field = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is null)
            {
                return;
            }

            string registrationKey = key + "." + GetPrototypeIdentity(target);
            bool existed = HasExactRegistration(registrationKey);
            if (!m_values.TryRegister(
                registrationKey,
                field.FieldType,
                () => field.GetValue(target),
                value =>
                {
                    field.SetValue(target, value);
                    VerifyNativeWrite(
                        value,
                        field.GetValue(target),
                        field.FieldType,
                        "The infrastructure member did not accept the reflected value.");
                },
                minimum,
                maximum,
                BaseValueApplyMode.ReloadRequired) ||
                !ApplyConfigured(registrationKey, key))
            {
                if (!existed)
                {
                    m_values.TryUnregister(registrationKey);
                }
            }
            else
            {
                m_ownedKeys.Add(registrationKey);
            }
        }

        private void TryRegisterMember(object target, string memberName, string key, double minimum, double maximum)
        {
            Type type = target.GetType();
            PropertyInfo? property = FindInstanceProperty(type, memberName);
            FieldInfo? field = property is null
                ? type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : null;
            Type? valueType = property?.PropertyType ?? field?.FieldType;
            if (property is not null && !property.CanWrite && field is null)
            {
                // Auto-properties in game prototypes are backed by a private readonly field.
                field = type.GetField("<" + memberName + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                valueType = field?.FieldType;
            }

            if (valueType is null)
            {
                return;
            }

            string registrationKey = key + "." + GetPrototypeIdentity(target);
            bool existed = HasExactRegistration(registrationKey);
            Func<object?> getter = property is not null ? () => property.GetValue(target) : () => field!.GetValue(target);
            Action<object?> setter = property?.CanWrite == true
                ? value =>
                {
                    property.SetValue(target, value);
                    VerifyNativeWrite(
                        value,
                        property.GetValue(target),
                        valueType,
                        "The infrastructure property did not accept the reflected value.");
                }
                : value =>
                {
                    field!.SetValue(target, value);
                    VerifyNativeWrite(
                        value,
                        field.GetValue(target),
                        valueType,
                        "The infrastructure backing field did not accept the reflected value.");
                };
            if (m_values.TryRegister(registrationKey, valueType, getter, setter, minimum, maximum, BaseValueApplyMode.ReloadRequired) &&
                !ApplyConfigured(registrationKey, key))
            {
                if (!existed)
                {
                    m_values.TryUnregister(registrationKey);
                }
            }
            else if (m_values.IsAvailable(registrationKey))
            {
                m_ownedKeys.Add(registrationKey);
            }
        }

        private bool ApplyConfigured(string registrationKey, string category)
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
            return m_values.TrySetMultiplier(registrationKey, multiplier);
        }

        private static string GetPrototypeIdentity(object prototype)
        {
            object? id = FindInstanceProperty(prototype.GetType(), "Id")?.GetValue(prototype);
            return Convert.ToString(id, CultureInfo.InvariantCulture) ?? prototype.GetType().Name;
        }

        private static PropertyInfo? FindInstanceProperty(Type type, string name)
        {
            // GetProperty(name, flags) throws when a game type hides a property with the same
            // name on a base type. Walk from the concrete type instead, selecting one readable
            // non-indexed property at each level so a compatibility seam remains deterministic.
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                PropertyInfo? property = current.GetProperties(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
                                        candidate.GetIndexParameters().Length == 0 &&
                                        candidate.GetGetMethod(nonPublic: true) is not null)
                    .OrderBy(candidate => candidate.MetadataToken)
                    .FirstOrDefault();
                if (property is not null)
                {
                    return property;
                }
            }

            return null;
        }

        private static void VerifyNativeWrite(object? expected, object? actual, Type nativeType, string message)
        {
            double expectedValue = ReadScalar(expected);
            double actualValue = ReadScalar(actual);
            double tolerance = nativeType == typeof(Fix32) ||
                               nativeType == typeof(PartialQuantity) ||
                               nativeType == typeof(Upoints)
                ? 1d / Fix32.FRACTION_RANGE
                : nativeType == typeof(Percent) ? 1d / 100000d : 0d;
            if (Math.Abs(actualValue - expectedValue) > tolerance)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static double ReadScalar(object? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("The native infrastructure value is null.");
            }

            if (value is Quantity quantity)
            {
                return quantity.Value;
            }
            if (value is Duration duration)
            {
                return duration.Ticks;
            }
            if (value is PartialQuantity partial)
            {
                return partial.Value.RawValue / (double)Fix32.FRACTION_RANGE;
            }
            if (value is Fix32 fix32)
            {
                return fix32.RawValue / (double)Fix32.FRACTION_RANGE;
            }
            if (value is Upoints upoints)
            {
                return upoints.Value.RawValue / (double)Fix32.FRACTION_RANGE;
            }
            if (value is Percent percent)
            {
                return percent.RawValue / 100000d;
            }

            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private bool HasExactRegistration(string key) => m_values.Keys.Any(
            registrationKey => string.Equals(registrationKey, key, StringComparison.Ordinal));

        private void RemoveCategory(string keyPrefix)
        {
            foreach (string key in m_values.Keys
                         .Where(registrationKey => registrationKey.StartsWith(keyPrefix + ".", StringComparison.Ordinal))
                         .ToArray())
            {
                m_values.TryUnregister(key);
            }
        }
    }
}
