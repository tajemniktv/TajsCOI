// Taj's COI Mods | RuntimeTraceModels.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace TajsCOI.Profiler.Core
{
    internal static class RuntimeTraceMath
    {
        internal static long SaturatingAdd(long left, long right)
        {
            if (left <= 0)
            {
                return Math.Max(0, right);
            }
            if (right <= 0)
            {
                return left;
            }
            return left > long.MaxValue - right ? long.MaxValue : left + right;
        }
    }

    internal readonly struct GameLoopTimingRange
    {
        internal GameLoopTimingRange(long startTimestamp, long endTimestamp)
        {
            StartTimestamp = startTimestamp;
            EndTimestamp = endTimestamp;
        }

        internal long StartTimestamp { get; }
        internal long EndTimestamp { get; }
        internal bool IsValid => StartTimestamp > 0 && EndTimestamp >= StartTimestamp;
        internal long DurationTicks => IsValid ? EndTimestamp - StartTimestamp : 0;
    }

    /// <summary>
    ///     A value-only copy of the twenty vanilla timing ranges. Keeping this as fields rather
    ///     than an array is deliberate: Tier 0 sampling must not allocate.
    /// </summary>
    internal readonly struct GameLoopTimingRanges
    {
        internal GameLoopTimingRanges(
            GameLoopTimingRange input,
            GameLoopTimingRange syncStart,
            GameLoopTimingRange sync,
            GameLoopTimingRange syncEnd,
            GameLoopTimingRange renderAfterSync,
            GameLoopTimingRange render,
            GameLoopTimingRange renderEnd,
            GameLoopTimingRange waitForSim,
            GameLoopTimingRange inputEnd,
            GameLoopTimingRange simCmd,
            GameLoopTimingRange simStart,
            GameLoopTimingRange simUpdate,
            GameLoopTimingRange simEnd,
            GameLoopTimingRange simEndForUi,
            GameLoopTimingRange simAfterSync,
            GameLoopTimingRange simParallelStart,
            GameLoopTimingRange simParallelEnd,
            GameLoopTimingRange simReadState,
            GameLoopTimingRange simPausedUi,
            GameLoopTimingRange simCmdExtra)
        {
            Input = input;
            SyncStart = syncStart;
            Sync = sync;
            SyncEnd = syncEnd;
            RenderAfterSync = renderAfterSync;
            Render = render;
            RenderEnd = renderEnd;
            WaitForSim = waitForSim;
            InputEnd = inputEnd;
            SimCmd = simCmd;
            SimStart = simStart;
            SimUpdate = simUpdate;
            SimEnd = simEnd;
            SimEndForUi = simEndForUi;
            SimAfterSync = simAfterSync;
            SimParallelStart = simParallelStart;
            SimParallelEnd = simParallelEnd;
            SimReadState = simReadState;
            SimPausedUi = simPausedUi;
            SimCmdExtra = simCmdExtra;
        }

        internal GameLoopTimingRange Input { get; }
        internal GameLoopTimingRange SyncStart { get; }
        internal GameLoopTimingRange Sync { get; }
        internal GameLoopTimingRange SyncEnd { get; }
        internal GameLoopTimingRange RenderAfterSync { get; }
        internal GameLoopTimingRange Render { get; }
        internal GameLoopTimingRange RenderEnd { get; }
        internal GameLoopTimingRange WaitForSim { get; }
        internal GameLoopTimingRange InputEnd { get; }
        internal GameLoopTimingRange SimCmd { get; }
        internal GameLoopTimingRange SimStart { get; }
        internal GameLoopTimingRange SimUpdate { get; }
        internal GameLoopTimingRange SimEnd { get; }
        internal GameLoopTimingRange SimEndForUi { get; }
        internal GameLoopTimingRange SimAfterSync { get; }
        internal GameLoopTimingRange SimParallelStart { get; }
        internal GameLoopTimingRange SimParallelEnd { get; }
        internal GameLoopTimingRange SimReadState { get; }
        internal GameLoopTimingRange SimPausedUi { get; }
        internal GameLoopTimingRange SimCmdExtra { get; }

        internal GameLoopTimingRange Get(GameLoopTimingEvent eventId)
        {
            switch (eventId)
            {
                case GameLoopTimingEvent.Input: return Input;
                case GameLoopTimingEvent.SyncStart: return SyncStart;
                case GameLoopTimingEvent.Sync: return Sync;
                case GameLoopTimingEvent.SyncEnd: return SyncEnd;
                case GameLoopTimingEvent.RenderAfterSync: return RenderAfterSync;
                case GameLoopTimingEvent.Render: return Render;
                case GameLoopTimingEvent.RenderEnd: return RenderEnd;
                case GameLoopTimingEvent.WaitForSim: return WaitForSim;
                case GameLoopTimingEvent.InputEnd: return InputEnd;
                case GameLoopTimingEvent.SimCmd: return SimCmd;
                case GameLoopTimingEvent.SimStart: return SimStart;
                case GameLoopTimingEvent.SimUpdate: return SimUpdate;
                case GameLoopTimingEvent.SimEnd: return SimEnd;
                case GameLoopTimingEvent.SimEndForUi: return SimEndForUi;
                case GameLoopTimingEvent.SimAfterSync: return SimAfterSync;
                case GameLoopTimingEvent.SimParallelStart: return SimParallelStart;
                case GameLoopTimingEvent.SimParallelEnd: return SimParallelEnd;
                case GameLoopTimingEvent.SimReadState: return SimReadState;
                case GameLoopTimingEvent.SimPausedUi: return SimPausedUi;
                case GameLoopTimingEvent.SimCmdExtra: return SimCmdExtra;
                default: return default;
            }
        }
    }

    internal readonly struct RuntimeCounterSnapshot
    {
        internal RuntimeCounterSnapshot(
            bool available,
            long capturedTimestamp,
            long managedHeapBytes,
            long unityAllocatedBytes,
            long unityReservedBytes,
            long unityUnusedReservedBytes,
            long unityGraphicsBytes,
            long monoUsedBytes,
            long monoHeapBytes,
            int gen0Delta,
            int gen1Delta,
            int gen2Delta,
            long managedHeapDeltaBytes,
            long unityAllocatedDeltaBytes,
            long unityGraphicsDeltaBytes,
            long gpuFrameTicks,
            bool gpuFrameTrusted,
            int supportedUnityCounters)
        {
            Available = available;
            CapturedTimestamp = capturedTimestamp;
            ManagedHeapBytes = managedHeapBytes;
            UnityAllocatedBytes = unityAllocatedBytes;
            UnityReservedBytes = unityReservedBytes;
            UnityUnusedReservedBytes = unityUnusedReservedBytes;
            UnityGraphicsBytes = unityGraphicsBytes;
            MonoUsedBytes = monoUsedBytes;
            MonoHeapBytes = monoHeapBytes;
            Gen0Delta = gen0Delta;
            Gen1Delta = gen1Delta;
            Gen2Delta = gen2Delta;
            ManagedHeapDeltaBytes = managedHeapDeltaBytes;
            UnityAllocatedDeltaBytes = unityAllocatedDeltaBytes;
            UnityGraphicsDeltaBytes = unityGraphicsDeltaBytes;
            GpuFrameTicks = gpuFrameTicks;
            GpuFrameTrusted = gpuFrameTrusted;
            SupportedUnityCounters = supportedUnityCounters;
        }

        internal bool Available { get; }
        internal long CapturedTimestamp { get; }
        internal long ManagedHeapBytes { get; }
        internal long UnityAllocatedBytes { get; }
        internal long UnityReservedBytes { get; }
        internal long UnityUnusedReservedBytes { get; }
        internal long UnityGraphicsBytes { get; }
        internal long MonoUsedBytes { get; }
        internal long MonoHeapBytes { get; }
        internal int Gen0Delta { get; }
        internal int Gen1Delta { get; }
        internal int Gen2Delta { get; }
        internal long ManagedHeapDeltaBytes { get; }
        internal long UnityAllocatedDeltaBytes { get; }
        internal long UnityGraphicsDeltaBytes { get; }
        internal long GpuFrameTicks { get; }
        internal bool GpuFrameTrusted { get; }
        internal int SupportedUnityCounters { get; }
        internal int TotalGcDelta => Math.Max(0, Gen0Delta) + Math.Max(0, Gen1Delta) + Math.Max(0, Gen2Delta);
        internal bool HasGpuTelemetry => GpuFrameTrusted && GpuFrameTicks >= 0;

        internal static RuntimeCounterSnapshot Unavailable(long timestamp = 0) =>
            new(false, timestamp, -1, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, -1, false, 0);
    }

    /// <summary>
    ///     Cumulative values from an existing subsystem probe. This deliberately contains no
    ///     subsystem objects or names so it can be copied into a frame sample and a trace ring.
    /// </summary>
    internal readonly struct RuntimeSubsystemCounterSnapshot
    {
        internal RuntimeSubsystemCounterSnapshot(
            long dumpingCalls,
            long dumpingTrueResults,
            long dumpingFalseResults,
            long dumpingElapsedTicks,
            long pathEnqueues,
            long pathSearchElapsedTicks)
        {
            DumpingCalls = dumpingCalls;
            DumpingTrueResults = dumpingTrueResults;
            DumpingFalseResults = dumpingFalseResults;
            DumpingElapsedTicks = dumpingElapsedTicks;
            PathEnqueues = pathEnqueues;
            PathSearchElapsedTicks = pathSearchElapsedTicks;
        }

        internal long DumpingCalls { get; }
        internal long DumpingTrueResults { get; }
        internal long DumpingFalseResults { get; }
        internal long DumpingElapsedTicks { get; }
        internal long PathEnqueues { get; }
        internal long PathSearchElapsedTicks { get; }
    }

    internal readonly struct RuntimeTraceSpan
    {
        internal RuntimeTraceSpan(
            long startTimestamp,
            long endTimestamp,
            int callbackId,
            int phaseId,
            int threadId,
            long sequence,
            ushort flags)
        {
            StartTimestamp = startTimestamp;
            EndTimestamp = endTimestamp;
            CallbackId = callbackId;
            PhaseId = phaseId;
            ThreadId = threadId;
            Sequence = sequence;
            Flags = flags;
        }

        internal long StartTimestamp { get; }
        internal long EndTimestamp { get; }
        internal int CallbackId { get; }
        internal int PhaseId { get; }
        internal int ThreadId { get; }
        internal long Sequence { get; }
        internal ushort Flags { get; }
        internal long DurationTicks => EndTimestamp >= StartTimestamp ? EndTimestamp - StartTimestamp : 0;
    }

    internal readonly struct RuntimeTraceMarker
    {
        internal RuntimeTraceMarker(long timestamp, string label, int threadId)
        {
            Timestamp = timestamp;
            Label = label;
            ThreadId = threadId;
        }

        internal long Timestamp { get; }
        internal string Label { get; }
        internal int ThreadId { get; }
    }

    internal readonly struct CallbackMetadataSnapshot
    {
        internal CallbackMetadataSnapshot(int id, string ownerType, string methodName, string assemblyName)
        {
            Id = id;
            OwnerType = ownerType;
            MethodName = methodName;
            AssemblyName = assemblyName;
        }

        internal int Id { get; }
        internal string OwnerType { get; }
        internal string MethodName { get; }
        internal string AssemblyName { get; }
        internal string DisplayName => OwnerType + "." + MethodName;
    }

    internal readonly struct CallbackMetricSnapshot
    {
        internal CallbackMetricSnapshot(
            CallbackMetadataSnapshot metadata,
            int phaseId,
            long callCount,
            long totalTicks,
            long p95Ticks,
            long p99Ticks,
            long maxTicks,
            long slowCallCount)
        {
            Metadata = metadata;
            PhaseId = phaseId;
            CallCount = callCount;
            TotalTicks = totalTicks;
            P95Ticks = p95Ticks;
            P99Ticks = p99Ticks;
            MaxTicks = maxTicks;
            SlowCallCount = slowCallCount;
        }

        internal CallbackMetadataSnapshot Metadata { get; }
        internal int PhaseId { get; }
        internal long CallCount { get; }
        internal long TotalTicks { get; }
        internal long P95Ticks { get; }
        internal long P99Ticks { get; }
        internal long MaxTicks { get; }
        internal long SlowCallCount { get; }
        internal long AverageTicks => CallCount == 0 ? 0 : TotalTicks / CallCount;
    }

    internal sealed class RuntimeRollingPercentile
    {
        private readonly long[] m_values;
        private readonly long[] m_sorted;
        private int m_count;
        private int m_next;

        internal RuntimeRollingPercentile(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            m_values = new long[capacity];
            m_sorted = new long[capacity];
        }

        internal int Count => m_count;

        internal void Add(long value)
        {
            if (value <= 0)
            {
                return;
            }
            m_values[m_next] = value;
            m_next = (m_next + 1) % m_values.Length;
            m_count = Math.Min(m_values.Length, m_count + 1);
        }

        internal void Clear()
        {
            Array.Clear(m_values, 0, m_values.Length);
            m_count = 0;
            m_next = 0;
        }

        internal long Get(double percentile)
        {
            if (m_count == 0)
            {
                return 0;
            }
            Array.Copy(m_values, m_sorted, m_count);
            Array.Sort(m_sorted, 0, m_count);
            int index = (int)Math.Ceiling(m_count * percentile) - 1;
            index = Math.Max(0, Math.Min(m_count - 1, index));
            return m_sorted[index];
        }
    }

    internal static class RuntimeTraceText
    {
        internal static string Milliseconds(long ticks) =>
            ticks < 0
                ? "unavailable"
                : (ticks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms";

        internal static string OptionalBytes(long bytes) =>
            bytes < 0 ? "unavailable" : FormatBytes(bytes);

        internal static string FormatBytes(long bytes)
        {
            double absolute = Math.Abs((double)bytes);
            if (absolute >= 1024 * 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " GiB";
            }
            if (absolute >= 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " MiB";
            }
            if (absolute >= 1024)
            {
                return (bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " KiB";
            }
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        internal static string AssemblyName(MethodInfo method)
        {
            try
            {
                return method.Module.Assembly.GetName().Name ?? "<unknown>";
            }
            catch
            {
                return "<unknown>";
            }
        }

        internal static string AssemblyName(Type type)
        {
            try
            {
                return type.Assembly.GetName().Name ?? "<unknown>";
            }
            catch
            {
                return "<unknown>";
            }
        }
    }
}
