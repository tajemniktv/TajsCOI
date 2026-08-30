// Taj's COI Mods | TypedBaseValueOverrideRegistry.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mafi;
using TajsCOI.Common.Tuning;

namespace TajsCOI.Tweaks.Features.Tuning
{
    /// <summary>
    /// Scene/prototype-scoped owner for value-only base overrides. Native member conversion stays
    /// here so feature owners can keep their exact private-member knowledge while sharing one
    /// immutable-base, bounded, rollback-safe lifecycle.
    /// </summary>
    internal sealed class TypedBaseValueOverrideRegistry : IDisposable
    {
        private sealed class Entry
        {
            internal Entry(
                Type nativeType,
                double minimum,
                double maximum,
                IBaseValueOverride<double> value)
            {
                NativeType = nativeType;
                Minimum = minimum;
                Maximum = maximum;
                Value = value;
            }

            internal Type NativeType { get; }
            internal double Minimum { get; }
            internal double Maximum { get; }
            internal IBaseValueOverride<double> Value { get; }
        }

        private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_unavailable = new(StringComparer.Ordinal);
        private bool m_disposed;

        internal IReadOnlyDictionary<string, IBaseValueOverride<double>> Values =>
            m_entries.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

        internal IEnumerable<string> Keys => m_entries.Keys;

        internal bool HasRegistration(string keyPrefix) =>
            !string.IsNullOrWhiteSpace(keyPrefix) &&
            m_entries.Keys.Any(key => key.StartsWith(keyPrefix, StringComparison.Ordinal));

        internal bool HasAvailablePrefix(string keyPrefix) =>
            !string.IsNullOrWhiteSpace(keyPrefix) &&
            m_entries.Keys.Any(key => key.StartsWith(keyPrefix, StringComparison.Ordinal) && IsAvailable(key));

        internal bool IsAvailable(string key) =>
            !m_disposed && !m_unavailable.Contains(key) && m_entries.ContainsKey(key);

        internal void MarkUnavailable(string key)
        {
            if (!m_disposed && !string.IsNullOrWhiteSpace(key))
            {
                m_unavailable.Add(key);
            }
        }

        internal void MarkAvailable(string key)
        {
            if (!m_disposed)
            {
                m_unavailable.Remove(key);
            }
        }

        internal bool TryGet(string key, out IBaseValueOverride<double>? value)
        {
            if (IsAvailable(key) && m_entries.TryGetValue(key, out Entry? entry))
            {
                value = entry.Value;
                return true;
            }

            value = null;
            return false;
        }

        internal bool TryGetBounds(string key, out double minimum, out double maximum)
        {
            if (IsAvailable(key) && m_entries.TryGetValue(key, out Entry? entry))
            {
                minimum = entry.Minimum;
                maximum = entry.Maximum;
                return true;
            }

            minimum = 0d;
            maximum = 0d;
            return false;
        }

        internal bool TryGetEffectiveValue(string key, out double value)
        {
            if (TryGet(key, out IBaseValueOverride<double>? registration) && registration is not null)
            {
                value = registration.EffectiveValue;
                return true;
            }

            value = 0d;
            return false;
        }

        internal bool TryRegister(
            string key,
            Type nativeType,
            Func<object?> getter,
            Action<object?> setter,
            double minimum,
            double maximum,
            BaseValueApplyMode applyMode)
        {
            if (m_disposed || string.IsNullOrWhiteSpace(key) || nativeType is null || getter is null ||
                setter is null || !IsFinite(minimum) || !IsFinite(maximum) || minimum > maximum)
            {
                return false;
            }

            if (m_entries.TryGetValue(key, out Entry? existing))
            {
                return existing.NativeType == nativeType && existing.Minimum == minimum &&
                       existing.Maximum == maximum && existing.Value.ApplyMode == applyMode;
            }

            double baseValue;
            try
            {
                baseValue = ReadScalar(getter());
            }
            catch
            {
                return false;
            }

            if (!IsFinite(baseValue))
            {
                return false;
            }

            var value = new BaseValueOverride<double>(
                key,
                baseValue,
                effective => setter(ConvertScalar(effective, nativeType)),
                applyMode,
                effective => IsFinite(effective) && effective >= minimum && effective <= maximum);
            m_entries.Add(key, new Entry(nativeType, minimum, maximum, value));
            return true;
        }

        internal bool TrySetMultiplier(string key, double multiplier)
        {
            if (!TryGet(key, out IBaseValueOverride<double>? value) ||
                !m_entries.TryGetValue(key, out Entry? entry) || !IsFinite(multiplier) || multiplier < 0d)
            {
                return false;
            }

            if (value is null || entry is null)
            {
                return false;
            }

            double raw = value.BaseValue * multiplier;
            if (!IsFinite(raw))
            {
                return false;
            }

            double effective = Math.Min(entry.Maximum, Math.Max(entry.Minimum, raw));
            double previous = value.EffectiveValue;
            if (!value.TrySetEffective(effective))
            {
                return false;
            }
            if (value.Apply())
            {
                return true;
            }

            value.TrySetEffective(previous);
            value.Apply();
            return false;
        }

        internal bool TryReset(string key)
        {
            if (!TryGet(key, out IBaseValueOverride<double>? value) || value is null)
            {
                return false;
            }

            return value.TrySetEffective(value.BaseValue) && value.Apply();
        }

        internal bool TryUnregister(string key)
        {
            if (!m_entries.TryGetValue(key, out Entry? entry))
            {
                return false;
            }

            entry.Value.Dispose();
            m_entries.Remove(key);
            m_unavailable.Remove(key);
            return true;
        }

        internal void Reset()
        {
            if (m_disposed)
            {
                return;
            }

            foreach (Entry entry in m_entries.Values.ToArray())
            {
                entry.Value.Dispose();
            }
            m_entries.Clear();
            m_unavailable.Clear();
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            Reset();
            m_disposed = true;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static double ReadScalar(object? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("The native value is null.");
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

        private static object ConvertScalar(double value, Type type)
        {
            if (!IsFinite(value))
            {
                throw new OverflowException("The native value is not finite.");
            }

            if (type == typeof(Quantity))
            {
                return new Quantity(checked((int)Math.Round(value, MidpointRounding.AwayFromZero)));
            }
            if (type == typeof(Duration))
            {
                return Duration.FromTicks(checked((int)Math.Round(value, MidpointRounding.AwayFromZero)));
            }
            if (type == typeof(Fix32))
            {
                return Fix32.FromRaw(checked((int)Math.Round(value * Fix32.FRACTION_RANGE, MidpointRounding.AwayFromZero)));
            }
            if (type == typeof(PartialQuantity))
            {
                return new PartialQuantity(Fix32.FromRaw(checked((int)Math.Round(value * Fix32.FRACTION_RANGE, MidpointRounding.AwayFromZero))));
            }
            if (type == typeof(Upoints))
            {
                return new Upoints(Fix32.FromRaw(checked((int)Math.Round(value * Fix32.FRACTION_RANGE, MidpointRounding.AwayFromZero))));
            }
            if (type == typeof(Percent))
            {
                return Percent.FromRaw(checked((int)Math.Round(value * 100000d, MidpointRounding.AwayFromZero)));
            }

            if (type == typeof(int))
            {
                return checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
            }
            if (type == typeof(long))
            {
                return checked((long)Math.Round(value, MidpointRounding.AwayFromZero));
            }
            if (type == typeof(float))
            {
                return checked((float)value);
            }
            if (type == typeof(double))
            {
                return value;
            }

            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture)!;
        }
    }
}
