// Taj's COI Mods | IBaseValueOverride.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using TajsCOI.Common.Tuning;

namespace TajsCOI.Tweaks.Features.Tuning
{
    /// <summary>
    ///     Lifecycle-owned contract for a value whose vanilla base is captured once. Owners keep
    ///     instances scoped to the prototype/scene lifecycle; no process-static registry or game
    ///     object retention is implied by this internal Tweaks primitive.
    /// </summary>
    internal interface IBaseValueOverride<T> : IDisposable
    {
        string StableKey { get; }
        T BaseValue { get; }
        T EffectiveValue { get; }
        BaseValueApplyMode ApplyMode { get; }
        bool Validate();
        bool TrySetEffective(T value);
        bool Apply();
        void Reset();
    }

    /// <summary>Typed implementation with immutable base, validation, apply, and reset semantics.</summary>
    internal sealed class BaseValueOverride<T> : IBaseValueOverride<T>, IDisposable
    {
        private readonly string m_stableKey;
        private readonly T m_baseValue;
        private readonly Action<T>? m_setter;
        private readonly Func<T, bool>? m_validator;
        private T m_effectiveValue;
        private T m_lastAppliedValue;
        private bool m_disposed;

        internal BaseValueOverride(
            string stableKey,
            T baseValue,
            Action<T> setter,
            BaseValueApplyMode applyMode = BaseValueApplyMode.ReloadRequired,
            Func<T, bool>? validator = null)
        {
            m_stableKey = stableKey?.Trim() ?? string.Empty;
            m_baseValue = baseValue;
            m_effectiveValue = baseValue;
            m_setter = setter;
            m_validator = validator;
            ApplyMode = applyMode;
            m_lastAppliedValue = baseValue;
        }

        public string StableKey => m_stableKey;
        public T BaseValue => m_baseValue;
        public T EffectiveValue => m_effectiveValue;
        public BaseValueApplyMode ApplyMode { get; }

        public bool Validate()
        {
            if (m_disposed || m_stableKey.Length == 0 || m_setter is null)
            {
                return false;
            }

            try
            {
                return m_validator?.Invoke(m_effectiveValue) ?? true;
            }
            catch
            {
                return false;
            }
        }

        public bool Apply()
        {
            if (!Validate())
            {
                return false;
            }

            try
            {
                m_setter!(m_effectiveValue);
                m_lastAppliedValue = m_effectiveValue;
                return true;
            }
            catch
            {
                // A compatibility setter may mutate before reporting failure. Restore the
                // last value known to have applied so callers never retain an uncommitted
                // effective value after a failed write.
                m_effectiveValue = m_lastAppliedValue;
                try
                {
                    m_setter!(m_lastAppliedValue);
                }
                catch
                {
                    // The seam is unavailable; remain fail-open and let the owner report it.
                }
                return false;
            }
        }

        public bool TrySetEffective(T value)
        {
            if (m_disposed)
            {
                return false;
            }

            T previous = m_effectiveValue;
            m_effectiveValue = value;
            if (Validate())
            {
                return true;
            }

            m_effectiveValue = previous;
            return false;
        }

        public void Reset()
        {
            if (m_disposed)
            {
                return;
            }

            m_effectiveValue = m_baseValue;
            try
            {
                m_setter?.Invoke(m_baseValue);
                m_lastAppliedValue = m_baseValue;
            }
            catch
            {
                // Teardown is fail-open; stale scene setters are never retried indefinitely.
            }
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
    }
}
