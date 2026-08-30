// Taj's COI Mods | TransportCapacityAdapters.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core.Buildings.Cargo;
using Mafi.Core.Buildings.Cargo.Modules;
using Mafi.Core.Buildings.Cargo.Ships;
using Mafi.Core.Prototypes;
using Mafi.Core.Trains;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles.Trucks;
using TajsCOI.Common.Tuning;
using TajsCOI.Tweaks.Features.Tuning;

namespace TajsCOI.Tweaks.Features.TransportCapacity
{
    /// <summary>
    /// Decision made when a capacity reduction meets cargo that is already in flight or stored.
    /// Adapters never remove cargo. Callers may defer the change or explicitly accept a temporary
    /// over-capacity state, depending on the native entity's documented behavior.
    /// </summary>
    internal enum CapacityReductionDecision
    {
        Allowed,
        Deferred,
        OverCapacity,
        Invalid,
    }

    internal static class CapacityReductionPolicy
    {
        internal static CapacityReductionDecision Evaluate(
            double requestedCapacity,
            double containedQuantity,
            bool allowOverCapacity)
        {
            if (double.IsNaN(requestedCapacity) || double.IsInfinity(requestedCapacity) || requestedCapacity < 0d ||
                double.IsNaN(containedQuantity) || double.IsInfinity(containedQuantity) || containedQuantity < 0d)
            {
                return CapacityReductionDecision.Invalid;
            }

            if (containedQuantity <= requestedCapacity)
            {
                return CapacityReductionDecision.Allowed;
            }

            return allowOverCapacity
                ? CapacityReductionDecision.OverCapacity
                : CapacityReductionDecision.Deferred;
        }

        /// <summary>
        /// Reports a safe effective cap while existing cargo drains. This is intentionally a
        /// reporting helper only; it does not mutate or truncate the contained quantity.
        /// </summary>
        internal static double EffectiveCapacity(double requestedCapacity, double containedQuantity)
        {
            bool requestedValid = !double.IsNaN(requestedCapacity) && !double.IsInfinity(requestedCapacity) && requestedCapacity >= 0d;
            bool containedValid = !double.IsNaN(containedQuantity) && !double.IsInfinity(containedQuantity) && containedQuantity >= 0d;
            if (!requestedValid)
            {
                return containedValid ? containedQuantity : 0d;
            }

            if (!containedValid)
            {
                return requestedCapacity;
            }

            return Math.Max(requestedCapacity, containedQuantity);
        }
    }

    internal readonly struct CapacitySetResult
    {
        internal CapacitySetResult(bool applied, CapacityReductionDecision decision, double effectiveCapacity)
        {
            Applied = applied;
            Decision = decision;
            EffectiveCapacity = effectiveCapacity;
        }

        internal bool Applied { get; }
        internal CapacityReductionDecision Decision { get; }
        internal double EffectiveCapacity { get; }
    }

    /// <summary>
    /// Shared adapter mechanics. Every concrete adapter still owns its member names and native
    /// semantics; this class only centralizes bounded, immutable-base registration.
    /// </summary>
    internal abstract class TransportCapacityAdapter
    {
        protected readonly TransportCapacityFeature m_owner;
        private readonly TypedBaseValueOverrideRegistry m_values;

        protected TransportCapacityAdapter(
            TransportCapacityFeature owner,
            TypedBaseValueOverrideRegistry values,
            string keyPrefix)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            m_values = values ?? throw new ArgumentNullException(nameof(values));
            KeyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        }

        internal string KeyPrefix { get; }

        internal TypedBaseValueOverrideRegistry Values => m_values;

        internal bool IsAvailable => m_owner.IsAvailable(KeyPrefix);

        internal abstract int RegisterVariants(ProtosDb protosDb);

        internal CapacitySetResult TrySetMultiplier(
            string registrationKey,
            double multiplier,
            Func<double>? containedQuantity = null,
            bool allowOverCapacity = false)
        {
            if (!m_values.TryGetEffectiveValue(registrationKey, out double requestedCapacity))
            {
                return new CapacitySetResult(false, CapacityReductionDecision.Invalid, 0d);
            }

            // The effective value before applying a new multiplier is not the immutable base.
            // Read the registration so overflow is rejected before the common setter is called.
            if (!m_values.TryGet(registrationKey, out IBaseValueOverride<double>? registration) || registration is null ||
                !m_values.TryGetBounds(registrationKey, out double minimum, out double maximum) ||
                !TryScale(registration.BaseValue, multiplier, minimum, maximum, out requestedCapacity))
            {
                return new CapacitySetResult(false, CapacityReductionDecision.Invalid, 0d);
            }

            double contained = 0d;
            if (containedQuantity is not null)
            {
                try
                {
                    contained = containedQuantity();
                }
                catch
                {
                    return new CapacitySetResult(false, CapacityReductionDecision.Invalid, requestedCapacity);
                }

                CapacityReductionDecision decision = CapacityReductionPolicy.Evaluate(
                    requestedCapacity,
                    contained,
                    allowOverCapacity);
                if (decision is CapacityReductionDecision.Invalid or CapacityReductionDecision.Deferred)
                {
                    return new CapacitySetResult(false, decision, CapacityReductionPolicy.EffectiveCapacity(requestedCapacity, contained));
                }

                if (!m_values.TrySetMultiplier(registrationKey, multiplier))
                {
                    return new CapacitySetResult(false, decision, CapacityReductionPolicy.EffectiveCapacity(requestedCapacity, contained));
                }

                return new CapacitySetResult(true, decision, CapacityReductionPolicy.EffectiveCapacity(requestedCapacity, contained));
            }

            if (!m_values.TrySetMultiplier(registrationKey, multiplier))
            {
                return new CapacitySetResult(false, CapacityReductionDecision.Invalid, requestedCapacity);
            }

            return new CapacitySetResult(true, CapacityReductionDecision.Allowed, requestedCapacity);
        }

        internal bool TryReset(string registrationKey) => m_values.TryReset(registrationKey);

        protected bool RegisterField(object prototype, string memberName, string variantIdentity)
        {
            string registrationKey = KeyPrefix + "." + variantIdentity;
            FieldInfo? field = prototype.GetType().GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is null)
            {
                m_owner.MarkUnavailable(registrationKey);
                return false;
            }

            return Register(
                prototype,
                field.FieldType,
                variantIdentity,
                () => field.GetValue(prototype),
                value =>
                {
                    field.SetValue(prototype, value);
                    if (!Equals(field.GetValue(prototype), value))
                    {
                        throw new InvalidOperationException("The capacity member did not accept the reflected value.");
                    }
                });
        }

        protected bool RegisterProperty(object prototype, string memberName, string variantIdentity)
        {
            string registrationKey = KeyPrefix + "." + variantIdentity;
            Type type = prototype.GetType();
            PropertyInfo? property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo? backingField = null;
            Type? valueType = property?.PropertyType;
            if (property is not null && !property.CanWrite)
            {
                backingField = type.GetField(
                    "<" + memberName + ">k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                valueType = backingField?.FieldType;
            }

            if (valueType is null || (property is null && backingField is null))
            {
                m_owner.MarkUnavailable(registrationKey);
                return false;
            }

            Func<object?> getter = property is not null
                ? () => property.GetValue(prototype)
                : () => backingField!.GetValue(prototype);
            Action<object?> setter = property?.CanWrite == true
                ? value =>
                {
                    property.SetValue(prototype, value);
                    if (!Equals(property.GetValue(prototype), value))
                    {
                        throw new InvalidOperationException("The capacity property did not accept the reflected value.");
                    }
                }
                : value =>
                {
                    backingField!.SetValue(prototype, value);
                    if (!Equals(backingField.GetValue(prototype), value))
                    {
                        throw new InvalidOperationException("The capacity backing field did not accept the reflected value.");
                    }
                };
            return Register(prototype, valueType, variantIdentity, getter, setter);
        }

        protected bool Register(
            object prototype,
            Type valueType,
            string variantIdentity,
            Func<object?> getter,
            Action<object?> setter)
        {
            string key = KeyPrefix + "." + variantIdentity;
            bool registered = m_values.TryRegister(
                key,
                valueType,
                getter,
                setter,
                1d,
                TransportCapacityFeature.MaximumCapacity,
                BaseValueApplyMode.ReloadRequired);
            if (!registered)
            {
                m_owner.MarkUnavailable(key);
                return false;
            }

            m_owner.Track(key);

            bool applied = m_owner.ApplyConfigured(key, KeyPrefix);
            if (applied)
            {
                m_owner.MarkAvailable(key);
            }
            else
            {
                m_owner.MarkUnavailable(key);
            }
            return applied;
        }

        protected static string Identity(object prototype)
        {
            try
            {
                object? id = FindInstanceProperty(prototype.GetType(), "Id")?.GetValue(prototype);
                return Convert.ToString(id, CultureInfo.InvariantCulture) ?? prototype.GetType().Name;
            }
            catch
            {
                return prototype.GetType().Name;
            }
        }

        private static PropertyInfo? FindInstanceProperty(Type type, string name)
        {
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

        private static bool TryScale(
            double baseValue,
            double multiplier,
            double minimum,
            double maximum,
            out double scaled)
        {
            scaled = 0d;
            if (double.IsNaN(baseValue) || double.IsInfinity(baseValue) || baseValue < 0d ||
                double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier < 0d)
            {
                return false;
            }

            double raw = baseValue * multiplier;
            if (double.IsNaN(raw) || double.IsInfinity(raw))
            {
                return false;
            }

            // The common service clamps finite values to the descriptor bounds. Keep that
            // behavior while rejecting only arithmetic overflow and conversion-invalid values.
            scaled = Math.Min(maximum, Math.Max(minimum, raw));
            return !double.IsNaN(scaled) && !double.IsInfinity(scaled);
        }
    }

    internal sealed class TruckCapacityAdapter : TransportCapacityAdapter
    {
        internal TruckCapacityAdapter(TransportCapacityFeature owner, TypedBaseValueOverrideRegistry values)
            : base(owner, values, TransportCapacityFeature.TruckCapacityKey)
        {
        }

        internal override int RegisterVariants(ProtosDb protosDb)
        {
            int count = 0;
            foreach (TruckProto proto in protosDb.All<TruckProto>())
            {
                if (RegisterField(proto, nameof(TruckProto.CapacityBase), Identity(proto)))
                {
                    count++;
                }
            }
            return count;
        }
    }

    internal sealed class ExcavatorCapacityAdapter : TransportCapacityAdapter
    {
        internal ExcavatorCapacityAdapter(TransportCapacityFeature owner, TypedBaseValueOverrideRegistry values)
            : base(owner, values, TransportCapacityFeature.ExcavatorCapacityKey)
        {
        }

        internal override int RegisterVariants(ProtosDb protosDb)
        {
            int count = 0;
            foreach (ExcavatorProto proto in protosDb.All<ExcavatorProto>())
            {
                if (RegisterField(proto, nameof(ExcavatorProto.Capacity), Identity(proto)))
                {
                    count++;
                }
            }
            return count;
        }
    }

    internal sealed class TrainWagonCapacityAdapter : TransportCapacityAdapter
    {
        internal TrainWagonCapacityAdapter(TransportCapacityFeature owner, TypedBaseValueOverrideRegistry values)
            : base(owner, values, TransportCapacityFeature.TrainWagonCapacityKey)
        {
        }

        internal override int RegisterVariants(ProtosDb protosDb)
        {
            int count = 0;
            foreach (CargoWagonProto proto in protosDb.All<CargoWagonProto>())
            {
                if (TryRegisterBaseCapacity(proto))
                {
                    count++;
                }
            }
            return count;
        }

        private bool TryRegisterBaseCapacity(CargoWagonProto proto)
        {
            FieldInfo? baseField = typeof(CargoWagonProto).GetField(
                "m_baseCapacity",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo? capacityProperty = typeof(CargoWagonProto).GetProperty(
                nameof(CargoWagonProto.Capacity),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo? subCapacityProperty = typeof(CargoWagonProto).GetProperty(
                nameof(CargoWagonProto.SubCarCapacity),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (baseField is null || capacityProperty is null || subCapacityProperty is null)
            {
                m_owner.MarkUnavailable(KeyPrefix + "." + Identity(proto));
                return false;
            }

            // CargoWagonProto already derives Capacity from m_baseCapacity and the native train
            // property. Replace the native base, then preserve the currently active native ratio
            // when updating the cached display values. Future native property changes continue to
            // derive from this adjusted base, rather than losing the Tajs multiplier.
            string key = KeyPrefix + "." + Identity(proto);
            Func<object?> getter = () => baseField.GetValue(proto);
            Action<object?> setter = value =>
            {
                object? oldBase = baseField.GetValue(proto);
                object? oldCapacity = capacityProperty.GetValue(proto);
                double oldBaseValue = ReadQuantity(oldBase);
                double oldCapacityValue = ReadQuantity(oldCapacity);
                double ratio = oldBaseValue > 0d ? oldCapacityValue / oldBaseValue : 1d;
                baseField.SetValue(proto, value);
                if (!Equals(baseField.GetValue(proto), value))
                {
                    throw new InvalidOperationException("Cargo wagon base capacity did not accept the reflected value.");
                }
                double effectiveBase = ReadQuantity(value);
                object effective = ConvertQuantity(effectiveBase * ratio, value!.GetType());
                capacityProperty.SetValue(proto, effective);
                double subCarCount = Math.Max(1d, proto.SubCarCount);
                subCapacityProperty.SetValue(proto, ConvertQuantity(effectiveBase * ratio / subCarCount, value.GetType()));
            };
            if (!Values.TryRegister(
                    key,
                    baseField.FieldType,
                    getter,
                    setter,
                    1d,
                    TransportCapacityFeature.MaximumCapacity,
                    BaseValueApplyMode.ReloadRequired))
            {
                m_owner.MarkUnavailable(key);
                return false;
            }

            m_owner.Track(key);

            bool applied = m_owner.ApplyConfigured(key, KeyPrefix);
            if (applied)
            {
                m_owner.MarkAvailable(key);
            }
            else
            {
                m_owner.MarkUnavailable(key);
            }
            return applied;
        }

        private static double ReadQuantity(object? value)
        {
            if (value is null)
            {
                return 0d;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                PropertyInfo? property = value.GetType().GetProperty(
                    "Value",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return Convert.ToDouble(property?.GetValue(value), CultureInfo.InvariantCulture);
            }
        }

        private static object ConvertQuantity(double value, Type quantityType)
        {
            int rounded = checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
            ConstructorInfo? constructor = quantityType.GetConstructor(new[] { typeof(int) });
            if (constructor is null)
            {
                throw new InvalidOperationException("Cargo wagon quantity constructor is unavailable.");
            }
            return constructor.Invoke(new object[] { rounded });
        }
    }

    internal sealed class CargoShipCapacityAdapter : TransportCapacityAdapter
    {
        internal CargoShipCapacityAdapter(TransportCapacityFeature owner, TypedBaseValueOverrideRegistry values)
            : base(owner, values, TransportCapacityFeature.CargoShipCapacityKey)
        {
        }

        internal override int RegisterVariants(ProtosDb protosDb)
        {
            int count = 0;
            foreach (CargoShipProto proto in protosDb.All<CargoShipProto>())
            {
                if (RegisterField(proto, nameof(CargoShipProto.CapacityMultiplier), Identity(proto)))
                {
                    count++;
                }
            }
            return count;
        }
    }

    internal sealed class CargoDepotCapacityAdapter : TransportCapacityAdapter
    {
        internal CargoDepotCapacityAdapter(TransportCapacityFeature owner, TypedBaseValueOverrideRegistry values)
            : base(owner, values, TransportCapacityFeature.CargoDepotCapacityKey)
        {
        }

        internal override int RegisterVariants(ProtosDb protosDb)
        {
            int count = 0;
            foreach (CargoDepotModuleProto proto in protosDb.All<CargoDepotModuleProto>())
            {
                if (RegisterField(proto, nameof(CargoDepotModuleProto.Capacity), Identity(proto)))
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Registry and lifecycle owner for the five independent #130 adapters. Supporting
    /// infrastructure is intentionally not inferred from a vehicle setting: callers must opt in
    /// and select the relevant adapter/category explicitly.
    /// </summary>
    internal sealed class TransportCapacityFeature : IDisposable
    {
        internal const string TruckCapacityKey = "TajsTweaks.Tuning.TransportTruckCapacity";
        internal const string ExcavatorCapacityKey = "TajsTweaks.Tuning.TransportExcavatorCapacity";
        internal const string TrainWagonCapacityKey = "TajsTweaks.Tuning.TransportTrainWagonCapacity";
        internal const string CargoShipCapacityKey = "TajsTweaks.Tuning.TransportCargoShipCapacity";
        internal const string CargoDepotCapacityKey = "TajsTweaks.Tuning.TransportCargoDepotCapacity";
        internal const double MaximumCapacity = int.MaxValue;

        private readonly TypedBaseValueOverrideRegistry m_values;
        private readonly HashSet<string> m_ownedKeys = new(StringComparer.Ordinal);
        private readonly IReadOnlyList<TransportCapacityAdapter> m_adapters;

        internal TransportCapacityFeature(TypedBaseValueOverrideRegistry values)
        {
            m_values = values ?? throw new ArgumentNullException(nameof(values));
            m_adapters = new TransportCapacityAdapter[]
            {
                new TruckCapacityAdapter(this, m_values),
                new ExcavatorCapacityAdapter(this, m_values),
                new TrainWagonCapacityAdapter(this, m_values),
                new CargoShipCapacityAdapter(this, m_values),
                new CargoDepotCapacityAdapter(this, m_values),
            };
        }

        internal TypedBaseValueOverrideRegistry Values => m_values;

        internal IReadOnlyList<TransportCapacityAdapter> Adapters => m_adapters;

        internal int ApplyFromPrototypes(ProtosDb protosDb)
        {
            if (protosDb is null)
            {
                return 0;
            }

            int count = 0;
            foreach (TransportCapacityAdapter adapter in Adapters)
            {
                try
                {
                    count += adapter.RegisterVariants(protosDb);
                }
                catch
                {
                    // A changed private/member seam disables only this adapter. Other transport
                    // families and the vanilla paths remain available.
                }
            }
            return count;
        }

        internal bool IsAvailable(string keyPrefix) =>
            !string.IsNullOrWhiteSpace(keyPrefix) &&
            m_values.Keys.Any(key => key.StartsWith(keyPrefix, StringComparison.Ordinal) && m_values.IsAvailable(key));

        internal void MarkUnavailable(string key) => m_values.MarkUnavailable(key);

        internal void MarkAvailable(string key) => m_values.MarkAvailable(key);

        internal void Track(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                m_ownedKeys.Add(key);
            }
        }

        internal bool TrySetMultiplier(string keyPrefix, string variantIdentity, double multiplier) =>
            m_values.IsAvailable(keyPrefix + "." + variantIdentity) &&
            (Adapters.FirstOrDefault(adapter => string.Equals(adapter.KeyPrefix, keyPrefix, StringComparison.Ordinal))?.TrySetMultiplier(
                 keyPrefix + "." + variantIdentity,
                 multiplier).Applied ?? false);

        internal CapacitySetResult TrySetMultiplier(
            string keyPrefix,
            string variantIdentity,
            double multiplier,
            Func<double>? containedQuantity,
            bool allowOverCapacity) =>
            m_values.IsAvailable(keyPrefix + "." + variantIdentity)
                ? Adapters.FirstOrDefault(adapter => string.Equals(adapter.KeyPrefix, keyPrefix, StringComparison.Ordinal))?.TrySetMultiplier(
                      keyPrefix + "." + variantIdentity,
                      multiplier,
                      containedQuantity,
                      allowOverCapacity) ?? new CapacitySetResult(false, CapacityReductionDecision.Invalid, 0d)
                : new CapacitySetResult(false, CapacityReductionDecision.Invalid, 0d);

        internal bool TryReset(string keyPrefix, string variantIdentity) =>
            m_values.IsAvailable(keyPrefix + "." + variantIdentity) &&
            m_values.TryReset(keyPrefix + "." + variantIdentity);

        internal void Reset()
        {
            foreach (string key in m_ownedKeys.ToArray())
            {
                m_values.TryUnregister(key);
            }
            m_ownedKeys.Clear();
        }

        internal bool ApplyConfigured(string registrationKey, string category)
        {
            double multiplier = category switch
            {
                TruckCapacityKey => TajsTweaksRuntimeState.TuningTransportTruckCapacityMultiplier,
                ExcavatorCapacityKey => TajsTweaksRuntimeState.TuningTransportExcavatorCapacityMultiplier,
                TrainWagonCapacityKey => TajsTweaksRuntimeState.TuningTransportTrainWagonCapacityMultiplier,
                CargoShipCapacityKey => TajsTweaksRuntimeState.TuningTransportCargoShipCapacityMultiplier,
                CargoDepotCapacityKey => TajsTweaksRuntimeState.TuningTransportCargoDepotCapacityMultiplier,
                _ => 1d,
            };

            // 1.0x is a semantic reset: the typed owner writes the immutable captured native base
            // rather than leaving a stale override in the prototype.
            return multiplier.Equals(1d)
                ? m_values.TryReset(registrationKey)
                : m_values.TrySetMultiplier(registrationKey, multiplier);
        }

        // The host owns the shared registry. This feature only releases registrations it created.
        public void Dispose() => Reset();
    }
}
