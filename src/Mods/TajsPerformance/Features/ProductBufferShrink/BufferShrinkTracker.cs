// Taj's COI Mods | BufferShrinkTracker.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Performance.Features.ProductBufferShrink
{
    internal sealed class BufferShrinkTracker
    {
        private readonly int m_observationFrames;
        private readonly int m_cooldownFrames;
        private readonly int m_minimumCapacity;
        private int m_observedCapacity;
        private int m_underutilizedFrames;
        private int m_cooldownRemaining;

        internal BufferShrinkTracker(int observationFrames, int cooldownFrames = 3600, int minimumCapacity = 1024)
        {
            if (observationFrames <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(observationFrames));
            }
            if (cooldownFrames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cooldownFrames));
            }
            if (minimumCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumCapacity));
            }
            m_observationFrames = observationFrames;
            m_cooldownFrames = cooldownFrames;
            m_minimumCapacity = minimumCapacity;
        }

        internal bool Observe(int used, int capacity)
        {
            if (m_cooldownRemaining > 0)
            {
                m_cooldownRemaining--;
                ResetObservation(capacity);
                return false;
            }

            int vanillaTarget = NextPowerOfTwo(Math.Max(used, 256));
            bool worthShrinking = used >= 0 && capacity >= m_minimumCapacity &&
                vanillaTarget > 0 && vanillaTarget <= capacity / 4;
            if (!worthShrinking)
            {
                ResetObservation(capacity);
                return false;
            }

            if (m_observedCapacity != capacity)
            {
                m_observedCapacity = capacity;
                m_underutilizedFrames = 0;
            }

            m_underutilizedFrames++;
            if (m_underutilizedFrames < m_observationFrames)
            {
                return false;
            }

            m_underutilizedFrames = 0;
            m_cooldownRemaining = m_cooldownFrames;
            return true;
        }

        private void ResetObservation(int capacity)
        {
            m_observedCapacity = capacity;
            m_underutilizedFrames = 0;
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0 || value > 1 << 30)
            {
                return 0;
            }
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
}
