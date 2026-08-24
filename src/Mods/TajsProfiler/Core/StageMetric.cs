// Taj's COI Mods | StageMetric.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;

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
            Difference(right, left, right.MaxTicks);

        internal static StageMetric Difference(StageMetric right, StageMetric left, long intervalMaxTicks) =>
            new(
                Math.Max(0, right.Count - left.Count),
                Math.Max(0, right.TotalTicks - left.TotalTicks),
                Math.Max(0, intervalMaxTicks),
                right.ManagedBytesDelta - left.ManagedBytesDelta,
                Math.Max(0, right.Gen0Collections - left.Gen0Collections),
                Math.Max(0, right.Gen1Collections - left.Gen1Collections),
                Math.Max(0, right.Gen2Collections - left.Gen2Collections));
    }

    internal sealed class StageAccumulator
    {
        private readonly object m_gate = new();
        private long m_count;
        private long m_totalTicks;
        private long m_intervalMaxTicks;
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

            lock (m_gate)
            {
                m_count++;
                m_totalTicks += elapsedTicks;
                m_managedBytesDelta += managedBytesDelta;
                m_gen0Collections += gen0Collections;
                m_gen1Collections += gen1Collections;
                m_gen2Collections += gen2Collections;
                m_intervalMaxTicks = Math.Max(m_intervalMaxTicks, elapsedTicks);
            }
        }

        internal StageMetric Snapshot()
        {
            lock (m_gate)
            {
                return CreateSnapshot();
            }
        }

        internal StageMetric SnapshotAndResetIntervalMax()
        {
            lock (m_gate)
            {
                StageMetric snapshot = CreateSnapshot();
                m_intervalMaxTicks = 0;
                return snapshot;
            }
        }

        internal void Reset()
        {
            lock (m_gate)
            {
                m_count = 0;
                m_totalTicks = 0;
                m_intervalMaxTicks = 0;
                m_managedBytesDelta = 0;
                m_gen0Collections = 0;
                m_gen1Collections = 0;
                m_gen2Collections = 0;
            }
        }

        private StageMetric CreateSnapshot() =>
            new(
                m_count,
                m_totalTicks,
                m_intervalMaxTicks,
                m_managedBytesDelta,
                m_gen0Collections,
                m_gen1Collections,
                m_gen2Collections);
    }
}
