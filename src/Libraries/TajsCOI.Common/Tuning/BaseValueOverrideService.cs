// Taj's COI Mods | BaseValueOverrideService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace TajsCOI.Common.Tuning
{
    public enum BaseValueApplyMode
    {
        Immediate,
        ReloadRequired,
    }

    /// <summary>
    /// Captures an immutable prototype value once and derives every subsequent value from that
    /// capture. Adapters can therefore be re-registered or reset without compounding a previous
    /// override. The callbacks are deliberately object-based so the service can be shared by
    /// mods without taking a dependency on MaFi types.
    /// </summary>
    public sealed class BaseValueOverrideService
    {
        public sealed class Registration
        {
            internal Registration(
                string key,
                Type valueType,
                Func<object?> getter,
                Action<object?> setter,
                double baseValue,
                double minimum,
                double maximum,
                BaseValueApplyMode applyMode)
            {
                Key = key;
                ValueType = valueType;
                Getter = getter;
                Setter = setter;
                BaseValue = baseValue;
                Minimum = minimum;
                Maximum = maximum;
                ApplyMode = applyMode;
                Multiplier = 1d;
            }

            public string Key { get; }
            public Type ValueType { get; }
            public double BaseValue { get; }
            public double Minimum { get; }
            public double Maximum { get; }
            public BaseValueApplyMode ApplyMode { get; }
            public double Multiplier { get; internal set; }
            public double? OverrideValue { get; internal set; }
            internal Func<object?> Getter { get; }
            internal Action<object?> Setter { get; }
            public double EffectiveValue => Clamp(OverrideValue ?? BaseValue * Multiplier, Minimum, Maximum);
        }

        private readonly Dictionary<string, Registration> m_registrations =
            new(StringComparer.Ordinal);

        public IReadOnlyCollection<Registration> Registrations => m_registrations.Values;

        public bool HasRegistration(string keyPrefix)
        {
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                return false;
            }
            foreach (string key in m_registrations.Keys)
            {
                if (key.StartsWith(keyPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public void Clear() => m_registrations.Clear();

        public bool TryRegister<T>(
            string key,
            Func<T> getter,
            Action<T> setter,
            T minimum,
            T maximum,
            BaseValueApplyMode applyMode = BaseValueApplyMode.ReloadRequired)
        {
            if (getter is null || setter is null)
            {
                return false;
            }

            object? current;
            try
            {
                current = getter();
            }
            catch
            {
                return false;
            }
            if (!TryToDouble(current, out double baseValue) ||
                !TryToDouble(minimum, out double minimumValue) ||
                !TryToDouble(maximum, out double maximumValue) ||
                !IsFinite(baseValue) || !IsFinite(minimumValue) || !IsFinite(maximumValue) ||
                minimumValue > maximumValue || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return RegisterCore(
                key,
                typeof(T),
                () => getter(),
                value => setter((T)value!),
                baseValue,
                minimumValue,
                maximumValue,
                applyMode);
        }

        public bool TryRegister(
            string key,
            Type valueType,
            Func<object?> getter,
            Action<object?> setter,
            double minimum,
            double maximum,
            BaseValueApplyMode applyMode = BaseValueApplyMode.ReloadRequired)
        {
            if (getter is null || setter is null || valueType is null || string.IsNullOrWhiteSpace(key) ||
                !IsFinite(minimum) || !IsFinite(maximum) || minimum > maximum)
            {
                return false;
            }

            object? current;
            try
            {
                current = getter();
            }
            catch
            {
                return false;
            }

            if (!TryToDouble(current, out double baseValue) || !IsFinite(baseValue))
            {
                return false;
            }

            if (m_registrations.TryGetValue(key, out Registration? existing))
            {
                // Duplicate registration is intentionally idempotent. The first capture remains
                // authoritative even if a caller now observes an already-modified value.
                return existing.ValueType == valueType &&
                       existing.Minimum == minimum &&
                       existing.Maximum == maximum &&
                       existing.ApplyMode == applyMode;
            }

            return RegisterCore(key, valueType, getter, setter, baseValue, minimum, maximum, applyMode);
        }

        private bool RegisterCore(
            string key,
            Type valueType,
            Func<object?> getter,
            Action<object?> setter,
            double baseValue,
            double minimum,
            double maximum,
            BaseValueApplyMode applyMode)
        {
            if (m_registrations.TryGetValue(key, out Registration? existing))
            {
                return existing.ValueType == valueType &&
                       existing.Minimum == minimum &&
                       existing.Maximum == maximum &&
                       existing.ApplyMode == applyMode;
            }

            m_registrations.Add(key, new Registration(key, valueType, getter, setter, baseValue, minimum, maximum, applyMode));
            return true;
        }

        public bool TrySetMultiplier(string key, double multiplier)
        {
            if (!m_registrations.TryGetValue(key, out Registration? registration) || !IsFinite(multiplier))
            {
                return false;
            }

            double previousMultiplier = registration.Multiplier;
            double? previousOverride = registration.OverrideValue;
            registration.Multiplier = multiplier;
            registration.OverrideValue = null;
            if (TryApply(registration))
            {
                return true;
            }

            registration.Multiplier = previousMultiplier;
            registration.OverrideValue = previousOverride;
            return false;
        }

        public bool TrySetOverride(string key, double? value)
        {
            if (!m_registrations.TryGetValue(key, out Registration? registration) ||
                (value.HasValue && (!IsFinite(value.Value) || value.Value < registration.Minimum || value.Value > registration.Maximum)))
            {
                return false;
            }

            double previousMultiplier = registration.Multiplier;
            double? previousOverride = registration.OverrideValue;
            registration.OverrideValue = value;
            if (TryApply(registration))
            {
                return true;
            }

            registration.Multiplier = previousMultiplier;
            registration.OverrideValue = previousOverride;
            return false;
        }

        public bool TryReset(string key)
        {
            if (!m_registrations.TryGetValue(key, out Registration? registration))
            {
                return false;
            }

            double previousMultiplier = registration.Multiplier;
            double? previousOverride = registration.OverrideValue;
            registration.Multiplier = 1d;
            registration.OverrideValue = null;
            try
            {
                registration.Setter(ConvertValue(registration.BaseValue, registration.ValueType));
                return true;
            }
            catch
            {
                registration.Multiplier = previousMultiplier;
                registration.OverrideValue = previousOverride;
                return false;
            }
        }

        public bool TryGetEffectiveValue(string key, out double value)
        {
            if (m_registrations.TryGetValue(key, out Registration? registration))
            {
                value = registration.EffectiveValue;
                return true;
            }

            value = 0d;
            return false;
        }

        private static bool TryToDouble(object? value, out double result)
        {
            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0d;
                return false;
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static double Clamp(double value, double minimum, double maximum) =>
            Math.Min(maximum, Math.Max(minimum, IsFinite(value) ? value : minimum));

        private static bool TryApply(Registration registration)
        {
            try
            {
                registration.Setter(ConvertValue(registration.EffectiveValue, registration.ValueType));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object ConvertValue(double value, Type type)
        {
            if (type == typeof(double)) return value;
            if (type == typeof(float)) return (float)value;
            if (type == typeof(decimal)) return (decimal)value;
            if (type == typeof(byte)) return checked((byte)Math.Round(value, MidpointRounding.AwayFromZero));
            if (type == typeof(sbyte)) return checked((sbyte)Math.Round(value, MidpointRounding.AwayFromZero));
            if (type == typeof(short)) return checked((short)Math.Round(value, MidpointRounding.AwayFromZero));
            if (type == typeof(ushort)) return checked((ushort)Math.Round(value, MidpointRounding.AwayFromZero));
            if (type == typeof(int)) return checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
            if (type == typeof(uint)) return checked((uint)Math.Round(value, MidpointRounding.AwayFromZero));
            if (type == typeof(long)) return checked((long)Math.Round(value, MidpointRounding.AwayFromZero));
            if (type == typeof(ulong)) return checked((ulong)Math.Round(value, MidpointRounding.AwayFromZero));

            // MaFi's immutable Quantity/Duration value objects expose a public integer
            // constructor rather than implementing IConvertible.  Keep Common independent of
            // those assemblies while still allowing an adapter to register them by type.
            ConstructorInfo? integerConstructor = type.GetConstructor(new[] { typeof(int) });
            if (integerConstructor is not null)
            {
                return integerConstructor.Invoke(new object[] { checked((int)Math.Round(value, MidpointRounding.AwayFromZero)) });
            }

            // MechPower/Electricity/Computing wrappers expose FromQuantity(...) instead of an
            // integer constructor. Resolve that shape generically so Common remains game-agnostic.
            Type? quantityType = type.Assembly.GetType("Mafi.Quantity", throwOnError: false);
            MethodInfo? fromQuantity = quantityType is null
                ? null
                : type.GetMethod("FromQuantity", BindingFlags.Public | BindingFlags.Static, null, new[] { quantityType }, null);
            if (quantityType is not null && fromQuantity is not null)
            {
                ConstructorInfo? quantityConstructor = quantityType.GetConstructor(new[] { typeof(int) });
                if (quantityConstructor is not null)
                {
                    object quantity = quantityConstructor.Invoke(new object[] { checked((int)Math.Round(value, MidpointRounding.AwayFromZero)) });
                    return fromQuantity.Invoke(null, new[] { quantity })!;
                }
            }

            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture)!;
        }
    }
}
