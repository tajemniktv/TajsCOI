// Taj's COI Mods | RuntimeFrameHistory.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TajsCOI.Profiler.Core
{
    internal enum RuntimeFrameClassification
    {
        Unknown,
        MainRenderBound,
        SimulationBound,
        WaitingForSimulation,
        GcRelated,
        LikelyGpuBound,
        Mixed,
    }

    internal readonly struct GameRunnerTimingSnapshot
    {
        internal GameRunnerTimingSnapshot(
            long updateTicks = -1,
            long inputTicks = -1,
            long syncTicks = -1,
            long renderTicks = -1,
            long simTicks = -1,
            bool wasOvertime = false,
            long overtimeTicks = -1,
            bool runSimulationInBackgroundThread = false,
            int simUpdateCount = -1,
            int simStepsSinceLoad = -1)
        {
            UpdateTicks = updateTicks;
            InputTicks = inputTicks;
            SyncTicks = syncTicks;
            RenderTicks = renderTicks;
            SimTicks = simTicks;
            WasOvertime = wasOvertime;
            OvertimeTicks = overtimeTicks;
            RunSimulationInBackgroundThread = runSimulationInBackgroundThread;
            SimUpdateCount = simUpdateCount;
            SimStepsSinceLoad = simStepsSinceLoad;
        }

        internal long UpdateTicks { get; }
        internal long InputTicks { get; }
        internal long SyncTicks { get; }
        internal long RenderTicks { get; }
        internal long SimTicks { get; }
        internal bool WasOvertime { get; }
        internal long OvertimeTicks { get; }
        internal bool RunSimulationInBackgroundThread { get; }
        internal int SimUpdateCount { get; }
        internal int SimStepsSinceLoad { get; }
        internal bool IsAvailable => UpdateTicks >= 0 || InputTicks >= 0 || SyncTicks >= 0 || RenderTicks >= 0 || SimTicks >= 0;
    }

    internal sealed class GameRunnerTimingAccess
    {
        private readonly object m_runner;
        private readonly Func<object, TimeSpan>? m_update;
        private readonly Func<object, TimeSpan>? m_input;
        private readonly Func<object, TimeSpan>? m_sync;
        private readonly Func<object, TimeSpan>? m_render;
        private readonly Func<object, TimeSpan>? m_sim;
        private readonly Func<object, bool>? m_wasOvertime;
        private readonly Func<object, TimeSpan>? m_overtime;
        private readonly Func<object, bool>? m_background;
        private readonly Func<object, int>? m_simUpdateCount;
        private readonly Func<object, int>? m_simStepsSinceLoad;

        private GameRunnerTimingAccess(
            object runner,
            Func<object, TimeSpan>? update,
            Func<object, TimeSpan>? input,
            Func<object, TimeSpan>? sync,
            Func<object, TimeSpan>? render,
            Func<object, TimeSpan>? sim,
            Func<object, bool>? wasOvertime,
            Func<object, TimeSpan>? overtime,
            Func<object, bool>? background,
            Func<object, int>? simUpdateCount,
            Func<object, int>? simStepsSinceLoad,
            string unavailableProperties)
        {
            m_runner = runner;
            m_update = update;
            m_input = input;
            m_sync = sync;
            m_render = render;
            m_sim = sim;
            m_wasOvertime = wasOvertime;
            m_overtime = overtime;
            m_background = background;
            m_simUpdateCount = simUpdateCount;
            m_simStepsSinceLoad = simStepsSinceLoad;
            UnavailableProperties = unavailableProperties;
        }

        internal string UnavailableProperties { get; }
        internal bool IsAvailable => m_update is not null || m_sim is not null || m_render is not null;

        internal static GameRunnerTimingAccess? TryCreate(object? runner, out string reason)
        {
            reason = string.Empty;
            if (runner is null)
            {
                reason = "Mafi.Core.GameLoop.GameRunner is not available in the active resolver.";
                return null;
            }

            Type type = runner.GetType();
            var unavailable = new List<string>();
            Func<object, TimeSpan>? update = CreateGetter<TimeSpan>(type, "LatestUpdateDuration", typeof(TimeSpan), unavailable);
            Func<object, TimeSpan>? input = CreateGetter<TimeSpan>(type, "LatestInputUpdateDuration", typeof(TimeSpan), unavailable);
            Func<object, TimeSpan>? sync = CreateGetter<TimeSpan>(type, "LatestSyncDuration", typeof(TimeSpan), unavailable);
            Func<object, TimeSpan>? render = CreateGetter<TimeSpan>(type, "LatestRenderUpdateDuration", typeof(TimeSpan), unavailable);
            Func<object, TimeSpan>? sim = CreateGetter<TimeSpan>(type, "LatestSimUpdateDuration", typeof(TimeSpan), unavailable);
            Func<object, bool>? wasOvertime = CreateGetter<bool>(type, "LatestSimUpdateWasOvertime", typeof(bool), unavailable);
            Func<object, TimeSpan>? overtime = CreateGetter<TimeSpan>(type, "LatestSimUpdateOvertimeDuration", typeof(TimeSpan), unavailable);
            Func<object, bool>? background = CreateGetter<bool>(type, "RunSimulationInBackgroundThread", typeof(bool), unavailable);
            Func<object, int>? simUpdateCount = CreateGetter<int>(type, "SimUpdateCount", typeof(int), unavailable);
            Func<object, int>? simStepsSinceLoad = CreateGetter<int>(type, "SimStepsSinceLoad", typeof(int), unavailable);

            GameRunnerTimingAccess access = new GameRunnerTimingAccess(
                runner,
                update,
                input,
                sync,
                render,
                sim,
                wasOvertime,
                overtime,
                background,
                simUpdateCount,
                simStepsSinceLoad,
                unavailable.Count == 0 ? string.Empty : string.Join(", ", unavailable));
            reason = access.UnavailableProperties;
            return access;
        }

        internal GameRunnerTimingSnapshot Read()
        {
            return new GameRunnerTimingSnapshot(
                ToStopwatchTicks(m_update is null ? TimeSpan.MinValue : m_update(m_runner)),
                ToStopwatchTicks(m_input is null ? TimeSpan.MinValue : m_input(m_runner)),
                ToStopwatchTicks(m_sync is null ? TimeSpan.MinValue : m_sync(m_runner)),
                ToStopwatchTicks(m_render is null ? TimeSpan.MinValue : m_render(m_runner)),
                ToStopwatchTicks(m_sim is null ? TimeSpan.MinValue : m_sim(m_runner)),
                m_wasOvertime is not null && m_wasOvertime(m_runner),
                ToStopwatchTicks(m_overtime is null ? TimeSpan.MinValue : m_overtime(m_runner)),
                m_background is not null && m_background(m_runner),
                m_simUpdateCount is null ? -1 : m_simUpdateCount(m_runner),
                m_simStepsSinceLoad is null ? -1 : m_simStepsSinceLoad(m_runner));
        }

        private static Func<object, T>? CreateGetter<T>(
            Type type,
            string propertyName,
            Type expectedType,
            ICollection<string> unavailable)
        {
            PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is null || property.PropertyType != expectedType || property.GetGetMethod(true) is null)
            {
                unavailable.Add(propertyName);
                return null;
            }

            try
            {
                MethodInfo getter = property.GetGetMethod(true)!;
                DynamicMethod method = new DynamicMethod(
                    "ReadGameRunner" + propertyName,
                    typeof(T),
                    new[] { typeof(object) },
                    typeof(GameRunnerTimingAccess).Module,
                    true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, type);
                il.Emit(getter.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getter);
                il.Emit(OpCodes.Ret);
                return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
            }
            catch
            {
                unavailable.Add(propertyName);
                return null;
            }
        }

        private static long ToStopwatchTicks(TimeSpan value)
        {
            if (value == TimeSpan.MinValue || value <= TimeSpan.Zero)
            {
                return value == TimeSpan.MinValue ? -1 : 0;
            }

            double ticks = value.TotalSeconds * Stopwatch.Frequency;
            return ticks >= long.MaxValue ? long.MaxValue : (long)Math.Round(ticks, MidpointRounding.AwayFromZero);
        }
    }

    internal readonly struct RuntimeFrameSample
    {
        internal RuntimeFrameSample(
            long sequence,
            long capturedTimestamp,
            GameLoopTimingSnapshot timings,
            GameRunnerTimingSnapshot runner,
            int simSpeedMult,
            int simStepsPerUpdate,
            int budgetedSimSteps,
            bool simPaused,
            GameLoopTimingRanges ranges = default,
            RuntimeCounterSnapshot counters = default,
            RuntimeSubsystemCounterSnapshot subsystemCounters = default)
        {
            Sequence = sequence;
            CapturedTimestamp = capturedTimestamp;
            Timings = timings;
            TimingRanges = ranges;
            Runner = runner;
            SimSpeedMult = simSpeedMult;
            SimStepsPerUpdate = simStepsPerUpdate;
            BudgetedSimSteps = budgetedSimSteps;
            SimPaused = simPaused;
            Counters = counters;
            SubsystemCounters = subsystemCounters;
        }

        internal long Sequence { get; }
        internal long CapturedTimestamp { get; }
        internal GameLoopTimingSnapshot Timings { get; }
        internal GameLoopTimingRanges TimingRanges { get; }
        internal GameRunnerTimingSnapshot Runner { get; }
        internal int SimSpeedMult { get; }
        internal int SimStepsPerUpdate { get; }
        internal int BudgetedSimSteps { get; }
        internal bool SimPaused { get; }
        internal RuntimeCounterSnapshot Counters { get; }
        internal RuntimeSubsystemCounterSnapshot SubsystemCounters { get; }

        internal long FrameTicks => Runner.UpdateTicks >= 0 ? Runner.UpdateTicks : Timings.MainPhaseTicks + Timings.WaitForSimTicks;
        internal long RenderTicks => Runner.RenderTicks >= 0 ? Runner.RenderTicks : Timings.RenderPhaseTicks;
        internal long SimTicks => Runner.SimTicks >= 0 ? Runner.SimTicks : Timings.SimulationPhaseTicks;
        internal RuntimeFrameClassification Classification => RuntimeFrameClassifier.Classify(this);
    }

    internal static class RuntimeFrameClassifier
    {
        internal static RuntimeFrameClassification Classify(RuntimeFrameSample sample)
        {
            long frameTicks = sample.FrameTicks;
            long waitTicks = sample.Timings.WaitForSimTicks;
            long mainTicks = sample.Timings.MainPhaseTicks;
            long simTicks = sample.SimTicks;
            if (frameTicks <= 0 && mainTicks <= 0 && simTicks <= 0 && waitTicks <= 0)
            {
                return RuntimeFrameClassification.Unknown;
            }

            long waitingThreshold = Math.Max(1, frameTicks / 4);
            if (waitTicks > 0 && waitTicks >= waitingThreshold && (sample.Runner.WasOvertime || sample.Runner.OvertimeTicks > 0))
            {
                return RuntimeFrameClassification.WaitingForSimulation;
            }

            if (sample.Counters.HasGpuTelemetry &&
                sample.Counters.GpuFrameTicks > Math.Max(frameTicks, Stopwatch.Frequency / 100))
            {
                return RuntimeFrameClassification.LikelyGpuBound;
            }

            if (sample.Counters.TotalGcDelta > 0 && frameTicks >= Stopwatch.Frequency / 30)
            {
                return RuntimeFrameClassification.GcRelated;
            }

            if (mainTicks <= 0 && simTicks <= 0)
            {
                return RuntimeFrameClassification.Unknown;
            }
            if (mainTicks <= 0)
            {
                return RuntimeFrameClassification.SimulationBound;
            }
            if (simTicks <= 0)
            {
                return RuntimeFrameClassification.MainRenderBound;
            }

            long difference = Math.Abs(mainTicks - simTicks);
            long mixedThreshold = Math.Max(1, Math.Max(mainTicks, simTicks) / 4);
            if (difference <= mixedThreshold)
            {
                return RuntimeFrameClassification.Mixed;
            }

            return simTicks > mainTicks
                ? RuntimeFrameClassification.SimulationBound
                : RuntimeFrameClassification.MainRenderBound;
        }
    }

    internal readonly struct RuntimeMetricSummary
    {
        internal RuntimeMetricSummary(int count, long totalTicks, long p50Ticks, long p95Ticks, long p99Ticks, long maxTicks)
        {
            Count = count;
            TotalTicks = totalTicks;
            P50Ticks = p50Ticks;
            P95Ticks = p95Ticks;
            P99Ticks = p99Ticks;
            MaxTicks = maxTicks;
        }

        internal int Count { get; }
        internal long TotalTicks { get; }
        internal long P50Ticks { get; }
        internal long P95Ticks { get; }
        internal long P99Ticks { get; }
        internal long MaxTicks { get; }
    }

    internal readonly struct RuntimeFrameSummary
    {
        internal RuntimeFrameSummary(
            RuntimeFrameSample latest,
            RuntimeMetricSummary frame,
            RuntimeMetricSummary render,
            RuntimeMetricSummary waitForSim,
            RuntimeMetricSummary sim,
            int unknownCount,
            int mainRenderBoundCount,
            int simulationBoundCount,
            int waitingForSimulationCount,
            int gcRelatedCount,
            int likelyGpuBoundCount,
            int mixedCount)
        {
            Latest = latest;
            Frame = frame;
            Render = render;
            WaitForSim = waitForSim;
            Sim = sim;
            UnknownCount = unknownCount;
            MainRenderBoundCount = mainRenderBoundCount;
            SimulationBoundCount = simulationBoundCount;
            WaitingForSimulationCount = waitingForSimulationCount;
            GcRelatedCount = gcRelatedCount;
            LikelyGpuBoundCount = likelyGpuBoundCount;
            MixedCount = mixedCount;
        }

        internal RuntimeFrameSample Latest { get; }
        internal RuntimeMetricSummary Frame { get; }
        internal RuntimeMetricSummary Render { get; }
        internal RuntimeMetricSummary WaitForSim { get; }
        internal RuntimeMetricSummary Sim { get; }
        internal int UnknownCount { get; }
        internal int MainRenderBoundCount { get; }
        internal int SimulationBoundCount { get; }
        internal int WaitingForSimulationCount { get; }
        internal int GcRelatedCount { get; }
        internal int LikelyGpuBoundCount { get; }
        internal int MixedCount { get; }
        internal int Count => Frame.Count;
    }

    internal sealed class RuntimeFrameHistory
    {
        private readonly object m_gate = new object();
        private readonly RuntimeFrameSample[] m_samples;
        private int m_count;
        private int m_next;
        private long m_sequence;

        internal RuntimeFrameHistory(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            m_samples = new RuntimeFrameSample[capacity];
        }

        internal int Count
        {
            get
            {
                lock (m_gate)
                {
                    return m_count;
                }
            }
        }

        internal int Capacity => m_samples.Length;

        internal long Record(
            long capturedTimestamp,
            GameLoopTimingSnapshot timings,
            GameRunnerTimingSnapshot runner,
            int simSpeedMult,
            int simStepsPerUpdate,
            int budgetedSimSteps,
            bool simPaused,
            GameLoopTimingRanges ranges = default,
            RuntimeCounterSnapshot counters = default,
            RuntimeSubsystemCounterSnapshot subsystemCounters = default)
        {
            return RecordSample(
                capturedTimestamp,
                timings,
                runner,
                simSpeedMult,
                simStepsPerUpdate,
                budgetedSimSteps,
                simPaused,
                ranges,
                counters,
                subsystemCounters).Sequence;
        }

        internal RuntimeFrameSample RecordSample(
            long capturedTimestamp,
            GameLoopTimingSnapshot timings,
            GameRunnerTimingSnapshot runner,
            int simSpeedMult,
            int simStepsPerUpdate,
            int budgetedSimSteps,
            bool simPaused,
            GameLoopTimingRanges ranges = default,
            RuntimeCounterSnapshot counters = default,
            RuntimeSubsystemCounterSnapshot subsystemCounters = default)
        {
            lock (m_gate)
            {
                long sequence = ++m_sequence;
                RuntimeFrameSample sample = new RuntimeFrameSample(
                    sequence,
                    capturedTimestamp,
                    timings,
                    runner,
                    simSpeedMult,
                    simStepsPerUpdate,
                    budgetedSimSteps,
                    simPaused,
                    ranges,
                    counters,
                    subsystemCounters);
                m_samples[m_next] = sample;
                m_next = (m_next + 1) % m_samples.Length;
                m_count = Math.Min(m_samples.Length, m_count + 1);
                return sample;
            }
        }

        internal RuntimeFrameSummary SummarizeRecent(double seconds)
        {
            RuntimeFrameSample[] samples = Snapshot();
            if (samples.Length == 0)
            {
                return default;
            }

            long cutoff = samples[samples.Length - 1].CapturedTimestamp -
                          (long)Math.Max(0, seconds * Stopwatch.Frequency);
            RuntimeFrameSample[] interval = samples
                .Where(x => x.CapturedTimestamp >= cutoff)
                .ToArray();
            return Summarize(interval);
        }

        internal RuntimeFrameSample[] SnapshotRecent(int count)
        {
            if (count <= 0)
            {
                return Array.Empty<RuntimeFrameSample>();
            }

            RuntimeFrameSample[] snapshot = Snapshot();
            if (snapshot.Length <= count)
            {
                return snapshot;
            }
            RuntimeFrameSample[] result = new RuntimeFrameSample[count];
            Array.Copy(snapshot, snapshot.Length - count, result, 0, count);
            return result;
        }

        internal RuntimeFrameSample[] SnapshotBetween(long startTimestamp, long endTimestamp)
        {
            RuntimeFrameSample[] snapshot = Snapshot();
            return snapshot
                .Where(x => x.CapturedTimestamp >= startTimestamp && x.CapturedTimestamp <= endTimestamp)
                .ToArray();
        }

        internal RuntimeFrameSample[] FindSpikes(double seconds, int count)
        {
            RuntimeFrameSample[] samples = Snapshot();
            if (samples.Length == 0 || count <= 0)
            {
                return Array.Empty<RuntimeFrameSample>();
            }

            long cutoff = samples[samples.Length - 1].CapturedTimestamp -
                          (long)Math.Max(0, seconds * Stopwatch.Frequency);
            return samples
                .Where(x => x.CapturedTimestamp >= cutoff)
                .OrderByDescending(x => x.FrameTicks)
                .ThenByDescending(x => x.Timings.WaitForSimTicks)
                .Take(count)
                .ToArray();
        }

        internal void Clear()
        {
            lock (m_gate)
            {
                m_count = 0;
                m_next = 0;
            }
        }

        private RuntimeFrameSample[] Snapshot()
        {
            lock (m_gate)
            {
                RuntimeFrameSample[] result = new RuntimeFrameSample[m_count];
                int start = (m_next - m_count + m_samples.Length) % m_samples.Length;
                for (int index = 0; index < m_count; index++)
                {
                    result[index] = m_samples[(start + index) % m_samples.Length];
                }
                return result;
            }
        }

        private static RuntimeFrameSummary Summarize(IReadOnlyList<RuntimeFrameSample> samples)
        {
            if (samples.Count == 0)
            {
                return default;
            }

            long[] frame = new long[samples.Count];
            long[] render = new long[samples.Count];
            long[] waitForSim = new long[samples.Count];
            long[] sim = new long[samples.Count];
            int unknown = 0;
            int mainRenderBound = 0;
            int simulationBound = 0;
            int waitingForSimulation = 0;
            int gcRelated = 0;
            int likelyGpuBound = 0;
            int mixed = 0;
            for (int index = 0; index < samples.Count; index++)
            {
                RuntimeFrameSample sample = samples[index];
                frame[index] = Math.Max(0, sample.FrameTicks);
                render[index] = Math.Max(0, sample.RenderTicks);
                waitForSim[index] = Math.Max(0, sample.Timings.WaitForSimTicks);
                sim[index] = Math.Max(0, sample.SimTicks);
                switch (sample.Classification)
                {
                    case RuntimeFrameClassification.Unknown: unknown++; break;
                    case RuntimeFrameClassification.MainRenderBound: mainRenderBound++; break;
                    case RuntimeFrameClassification.SimulationBound: simulationBound++; break;
                    case RuntimeFrameClassification.WaitingForSimulation: waitingForSimulation++; break;
                    case RuntimeFrameClassification.GcRelated: gcRelated++; break;
                    case RuntimeFrameClassification.LikelyGpuBound: likelyGpuBound++; break;
                    case RuntimeFrameClassification.Mixed: mixed++; break;
                }
            }

            return new RuntimeFrameSummary(
                samples[samples.Count - 1],
                SummarizeMetric(frame),
                SummarizeMetric(render),
                SummarizeMetric(waitForSim),
                SummarizeMetric(sim),
                unknown,
                mainRenderBound,
                simulationBound,
                waitingForSimulation,
                gcRelated,
                likelyGpuBound,
                mixed);
        }

        private static RuntimeMetricSummary SummarizeMetric(long[] values)
        {
            Array.Sort(values);
            long total = 0;
            for (int index = 0; index < values.Length; index++)
            {
                total += values[index];
            }
            return new RuntimeMetricSummary(
                values.Length,
                total,
                Percentile(values, 0.50),
                Percentile(values, 0.95),
                Percentile(values, 0.99),
                values[values.Length - 1]);
        }

        private static long Percentile(long[] sortedValues, double percentile)
        {
            int index = (int)Math.Ceiling(sortedValues.Length * percentile) - 1;
            index = Math.Max(0, Math.Min(sortedValues.Length - 1, index));
            return sortedValues[index];
        }
    }
}
