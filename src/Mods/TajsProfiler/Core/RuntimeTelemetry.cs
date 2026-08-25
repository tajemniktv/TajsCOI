// Taj's COI Mods | RuntimeTelemetry.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace TajsCOI.Profiler.Core
{
    internal enum RuntimeTelemetryUnit
    {
        Count,
        StopwatchTicks,
        Bytes,
    }

    /// <summary>
    ///     Stable numeric identity for a registered runtime counter. Registration is setup work;
    ///     publishing uses only the index and an atomic operation.
    /// </summary>
    internal readonly struct RuntimeTelemetryCounter
    {
        internal RuntimeTelemetryCounter(int index)
        {
            Index = index;
        }

        internal int Index { get; }
        internal bool IsValid => Index >= 0;
    }

    internal readonly struct RuntimeTelemetryEvent
    {
        internal RuntimeTelemetryEvent(int index)
        {
            Index = index;
        }

        internal int Index { get; }
        internal bool IsValid => Index >= 0;
    }

    /// <summary>
    ///     Bounded value-only per-frame counter data. The fixed fields are intentional: copying a
    ///     frame snapshot must not allocate an array or enumerate a collection in the render loop.
    /// </summary>
    internal readonly struct RuntimeTelemetrySnapshot
    {
        internal const int MaximumCounters = 16;

        internal RuntimeTelemetrySnapshot(
            int count,
            long value0,
            long value1,
            long value2,
            long value3,
            long value4,
            long value5,
            long value6,
            long value7,
            long value8,
            long value9,
            long value10,
            long value11,
            long value12,
            long value13,
            long value14,
            long value15)
        {
            Count = Math.Max(0, Math.Min(MaximumCounters, count));
            m_value0 = value0;
            m_value1 = value1;
            m_value2 = value2;
            m_value3 = value3;
            m_value4 = value4;
            m_value5 = value5;
            m_value6 = value6;
            m_value7 = value7;
            m_value8 = value8;
            m_value9 = value9;
            m_value10 = value10;
            m_value11 = value11;
            m_value12 = value12;
            m_value13 = value13;
            m_value14 = value14;
            m_value15 = value15;
        }

        private readonly long m_value0;
        private readonly long m_value1;
        private readonly long m_value2;
        private readonly long m_value3;
        private readonly long m_value4;
        private readonly long m_value5;
        private readonly long m_value6;
        private readonly long m_value7;
        private readonly long m_value8;
        private readonly long m_value9;
        private readonly long m_value10;
        private readonly long m_value11;
        private readonly long m_value12;
        private readonly long m_value13;
        private readonly long m_value14;
        private readonly long m_value15;

        internal int Count { get; }

        internal long Get(RuntimeTelemetryCounter counter) => counter.IsValid ? Get(counter.Index) : 0;

        internal long Get(int index)
        {
            switch (index)
            {
                case 0: return m_value0;
                case 1: return m_value1;
                case 2: return m_value2;
                case 3: return m_value3;
                case 4: return m_value4;
                case 5: return m_value5;
                case 6: return m_value6;
                case 7: return m_value7;
                case 8: return m_value8;
                case 9: return m_value9;
                case 10: return m_value10;
                case 11: return m_value11;
                case 12: return m_value12;
                case 13: return m_value13;
                case 14: return m_value14;
                case 15: return m_value15;
                default: return 0;
            }
        }
    }

    internal readonly struct RuntimeTelemetryEventSnapshot
    {
        internal RuntimeTelemetryEventSnapshot(
            long sequence,
            string name,
            long timestamp,
            int threadId,
            int phaseId)
        {
            Sequence = sequence;
            Name = name;
            Timestamp = timestamp;
            ThreadId = threadId;
            PhaseId = phaseId;
        }

        internal long Sequence { get; }
        internal string Name { get; }
        internal long Timestamp { get; }
        internal int ThreadId { get; }
        internal int PhaseId { get; }
    }

    /// <summary>
    ///     Shared low-overhead publication bus for profiler probes. Counters are cumulative in the
    ///     store and copied as deltas at frame boundaries. Events use preallocated slots and are
    ///     intended for sparse state changes or threshold crossings, not per-operation logging.
    /// </summary>
    internal static class RuntimeTelemetry
    {
        private const int MaximumEvents = 128;

        private static readonly object s_registryGate = new object();
        private static readonly string[] s_counterNames = new string[RuntimeTelemetrySnapshot.MaximumCounters];
        private static readonly long[] s_counterValues = new long[RuntimeTelemetrySnapshot.MaximumCounters];
        private static readonly long[] s_previousCounterValues = new long[RuntimeTelemetrySnapshot.MaximumCounters];
        private static readonly RuntimeTelemetryUnit[] s_counterUnits = new RuntimeTelemetryUnit[RuntimeTelemetrySnapshot.MaximumCounters];
        private static readonly string[] s_eventNames = new string[MaximumEvents];
        private static readonly TelemetryEventSlot[] s_eventSlots = CreateEventSlots();
        private static int s_counterCount;
        private static int s_eventCount;
        private static long s_eventSequence;

        internal static int CounterCount => Volatile.Read(ref s_counterCount);

        internal static RuntimeTelemetryCounter RegisterCounter(
            string name,
            RuntimeTelemetryUnit unit = RuntimeTelemetryUnit.Count)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new RuntimeTelemetryCounter(-1);
            }

            lock (s_registryGate)
            {
                int count = s_counterCount;
                for (int index = 0; index < count; index++)
                {
                    if (string.Equals(s_counterNames[index], name, StringComparison.Ordinal))
                    {
                        return new RuntimeTelemetryCounter(index);
                    }
                }

                if (count >= RuntimeTelemetrySnapshot.MaximumCounters)
                {
                    return new RuntimeTelemetryCounter(-1);
                }

                s_counterNames[count] = name;
                s_counterUnits[count] = unit;
                Volatile.Write(ref s_counterCount, count + 1);
                return new RuntimeTelemetryCounter(count);
            }
        }

        internal static RuntimeTelemetryEvent RegisterEvent(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new RuntimeTelemetryEvent(-1);
            }

            lock (s_registryGate)
            {
                int count = s_eventCount;
                for (int index = 0; index < count; index++)
                {
                    if (string.Equals(s_eventNames[index], name, StringComparison.Ordinal))
                    {
                        return new RuntimeTelemetryEvent(index);
                    }
                }

                if (count >= MaximumEvents)
                {
                    return new RuntimeTelemetryEvent(-1);
                }

                s_eventNames[count] = name;
                Volatile.Write(ref s_eventCount, count + 1);
                return new RuntimeTelemetryEvent(count);
            }
        }

        internal static string CounterName(int index)
        {
            int count = CounterCount;
            return index >= 0 && index < count ? s_counterNames[index] : string.Empty;
        }

        internal static RuntimeTelemetryUnit CounterUnit(int index) =>
            index >= 0 && index < CounterCount ? s_counterUnits[index] : RuntimeTelemetryUnit.Count;

        internal static long Read(RuntimeTelemetryCounter counter) =>
            counter.IsValid ? Interlocked.Read(ref s_counterValues[counter.Index]) : 0;

        internal static void Increment(RuntimeTelemetryCounter counter)
        {
            if (counter.IsValid)
            {
                Interlocked.Increment(ref s_counterValues[counter.Index]);
            }
        }

        internal static void Add(RuntimeTelemetryCounter counter, long value)
        {
            if (counter.IsValid && value != 0)
            {
                Interlocked.Add(ref s_counterValues[counter.Index], value);
            }
        }

        internal static void Reset(RuntimeTelemetryCounter counter)
        {
            if (counter.IsValid)
            {
                Interlocked.Exchange(ref s_counterValues[counter.Index], 0);
                Interlocked.Exchange(ref s_previousCounterValues[counter.Index], 0);
            }
        }

        internal static RuntimeTelemetrySnapshot Capture()
        {
            int count = CounterCount;
            return new RuntimeTelemetrySnapshot(
                count,
                ReadDelta(0),
                ReadDelta(1),
                ReadDelta(2),
                ReadDelta(3),
                ReadDelta(4),
                ReadDelta(5),
                ReadDelta(6),
                ReadDelta(7),
                ReadDelta(8),
                ReadDelta(9),
                ReadDelta(10),
                ReadDelta(11),
                ReadDelta(12),
                ReadDelta(13),
                ReadDelta(14),
                ReadDelta(15));
        }

        internal static void Publish(RuntimeTelemetryEvent telemetryEvent, long timestamp, int phaseId)
        {
            if (!telemetryEvent.IsValid)
            {
                return;
            }

            long sequence = Interlocked.Increment(ref s_eventSequence);
            TelemetryEventSlot slot = s_eventSlots[(int)((sequence - 1) % MaximumEvents)];
            Volatile.Write(ref slot.Timestamp, timestamp > 0 ? timestamp : Stopwatch.GetTimestamp());
            Volatile.Write(ref slot.ThreadId, Thread.CurrentThread.ManagedThreadId);
            Volatile.Write(ref slot.PhaseId, phaseId);
            Volatile.Write(ref slot.EventIndex, telemetryEvent.Index);
            Volatile.Write(ref slot.Sequence, sequence);
        }

        internal static RuntimeTelemetryEventSnapshot[] SnapshotEvents(
            long startTimestamp = 0,
            long endTimestamp = 0)
        {
            long latest = Volatile.Read(ref s_eventSequence);
            long first = Math.Max(1, latest - MaximumEvents + 1);
            var result = new List<RuntimeTelemetryEventSnapshot>();
            for (long sequence = first; sequence <= latest; sequence++)
            {
                TelemetryEventSlot slot = s_eventSlots[(int)((sequence - 1) % MaximumEvents)];
                if (Volatile.Read(ref slot.Sequence) != sequence)
                {
                    continue;
                }

                int eventIndex = Volatile.Read(ref slot.EventIndex);
                int eventCount = Volatile.Read(ref s_eventCount);
                if (eventIndex < 0 || eventIndex >= eventCount)
                {
                    continue;
                }

                long timestamp = Volatile.Read(ref slot.Timestamp);
                if ((startTimestamp > 0 && timestamp < startTimestamp) ||
                    (endTimestamp > 0 && timestamp > endTimestamp))
                {
                    continue;
                }

                result.Add(new RuntimeTelemetryEventSnapshot(
                    sequence,
                    s_eventNames[eventIndex],
                    timestamp,
                    Volatile.Read(ref slot.ThreadId),
                    Volatile.Read(ref slot.PhaseId)));
            }

            return result.ToArray();
        }

        private static long ReadDelta(int index)
        {
            long current = Interlocked.Read(ref s_counterValues[index]);
            long previous = Interlocked.Exchange(ref s_previousCounterValues[index], current);
            return current >= previous ? current - previous : current;
        }

        private static TelemetryEventSlot[] CreateEventSlots()
        {
            var result = new TelemetryEventSlot[MaximumEvents];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new TelemetryEventSlot();
            }

            return result;
        }

        private sealed class TelemetryEventSlot
        {
            internal long Sequence;
            internal long Timestamp;
            internal int ThreadId;
            internal int PhaseId;
            internal int EventIndex;
        }
    }
}
