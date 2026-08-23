// Taj's COI Mods | StageMetric.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Threading;

namespace TajsCOI.Profiler.Core
{
    internal readonly struct StageMetric
    {
        internal StageMetric(
            long count,
            long totalTicks,
            long maxTicks,
            long managedBytesDelta,
            long gen0Collections = 0,
            long gen1Collections = 0,
            long gen2Collections = 0)
        {
            Count = count;
            TotalTicks = totalTicks;
            MaxTicks = maxTicks;
            ManagedBytesDelta = managedBytesDelta;
            Gen0Collections = gen0Collections;
            Gen1Collections = gen1Collections;
            Gen2Collections = gen2Collections;
        }

        internal long Count { get; }
        internal long TotalTicks { get; }
        internal long MaxTicks { get; }
        internal long ManagedBytesDelta { get; }
        internal long Gen0Collections { get; }
        internal long Gen1Collections { get; }
        internal long Gen2Collections { get; }
        internal double TotalMilliseconds => TotalTicks * 1000.0 / Stopwatch.Frequency;
        internal double MaxMilliseconds => MaxTicks * 1000.0 / Stopwatch.Frequency;

        public static StageMetric operator -(StageMetric right, StageMetric left) =>
            new(
                Math.Max(0, right.Count - left.Count),
                Math.Max(0, right.TotalTicks - left.TotalTicks),
                Math.Max(0, right.MaxTicks),
                right.ManagedBytesDelta - left.ManagedBytesDelta,
                Math.Max(0, right.Gen0Collections - left.Gen0Collections),
                Math.Max(0, right.Gen1Collections - left.Gen1Collections),
                Math.Max(0, right.Gen2Collections - left.Gen2Collections));
    }

    internal sealed class StageAccumulator
    {
        private long m_count;
        private long m_totalTicks;
        private long m_maxTicks;
        private long m_managedBytesDelta;
        private long m_gen0Collections;
        private long m_gen1Collections;
        private long m_gen2Collections;

        internal void Record(
            long elapsedTicks,
            long managedBytesDelta,
            long gen0Collections = 0,
            long gen1Collections = 0,
            long gen2Collections = 0)
        {
            if (elapsedTicks < 0)
            {
                return;
            }

            Interlocked.Increment(ref m_count);
            Interlocked.Add(ref m_totalTicks, elapsedTicks);
            Interlocked.Add(ref m_managedBytesDelta, managedBytesDelta);
            Interlocked.Add(ref m_gen0Collections, gen0Collections);
            Interlocked.Add(ref m_gen1Collections, gen1Collections);
            Interlocked.Add(ref m_gen2Collections, gen2Collections);

            long observed = Volatile.Read(ref m_maxTicks);
            while (elapsedTicks > observed)
            {
                long exchanged = Interlocked.CompareExchange(ref m_maxTicks, elapsedTicks, observed);
                if (exchanged == observed)
                {
                    break;
                }
                observed = exchanged;
            }
        }

        internal StageMetric Snapshot() =>
            new(
                Interlocked.Read(ref m_count),
                Interlocked.Read(ref m_totalTicks),
                Interlocked.Read(ref m_maxTicks),
                Interlocked.Read(ref m_managedBytesDelta),
                Interlocked.Read(ref m_gen0Collections),
                Interlocked.Read(ref m_gen1Collections),
                Interlocked.Read(ref m_gen2Collections));

        internal void Reset()
        {
            Interlocked.Exchange(ref m_count, 0);
            Interlocked.Exchange(ref m_totalTicks, 0);
            Interlocked.Exchange(ref m_maxTicks, 0);
            Interlocked.Exchange(ref m_managedBytesDelta, 0);
            Interlocked.Exchange(ref m_gen0Collections, 0);
            Interlocked.Exchange(ref m_gen1Collections, 0);
            Interlocked.Exchange(ref m_gen2Collections, 0);
        }
    }
}
