// Taj's COI Mods | RuntimeSpikeHistory.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace TajsCOI.Profiler.Core
{
    internal readonly struct RuntimeSpikePolicy
    {
        internal RuntimeSpikePolicy(
            double frameMilliseconds = 50.0,
            double waitForSimMilliseconds = 20.0,
            double simulationMilliseconds = 50.0,
            double majorPhaseMilliseconds = 25.0,
            double relativeMultiplier = 3.0,
            double cooldownSeconds = 30.0,
            int maximumAutomaticCaptures = 8,
            double preWindowSeconds = 3.0,
            double postWindowSeconds = 4.0,
            bool automaticDeepCapture = false)
        {
            FrameThresholdTicks = MillisecondsToTicks(frameMilliseconds);
            WaitForSimThresholdTicks = MillisecondsToTicks(waitForSimMilliseconds);
            SimulationThresholdTicks = MillisecondsToTicks(simulationMilliseconds);
            MajorPhaseThresholdTicks = MillisecondsToTicks(majorPhaseMilliseconds);
            RelativeMultiplier = Math.Max(1.1, relativeMultiplier);
            CooldownTicks = SecondsToTicks(Math.Max(0, cooldownSeconds));
            MaximumAutomaticCaptures = Math.Max(1, Math.Min(64, maximumAutomaticCaptures));
            PreWindowTicks = SecondsToTicks(Math.Max(0, Math.Min(30, preWindowSeconds)));
            PostWindowTicks = SecondsToTicks(Math.Max(0, Math.Min(30, postWindowSeconds)));
            AutomaticDeepCapture = automaticDeepCapture;
        }

        internal long FrameThresholdTicks { get; }
        internal long WaitForSimThresholdTicks { get; }
        internal long SimulationThresholdTicks { get; }
        internal long MajorPhaseThresholdTicks { get; }
        internal double RelativeMultiplier { get; }
        internal long CooldownTicks { get; }
        internal int MaximumAutomaticCaptures { get; }
        internal long PreWindowTicks { get; }
        internal long PostWindowTicks { get; }
        internal bool AutomaticDeepCapture { get; }

        internal double FrameThresholdMilliseconds => TicksToMilliseconds(FrameThresholdTicks);
        internal double WaitForSimThresholdMilliseconds => TicksToMilliseconds(WaitForSimThresholdTicks);
        internal double SimulationThresholdMilliseconds => TicksToMilliseconds(SimulationThresholdTicks);
        internal double MajorPhaseThresholdMilliseconds => TicksToMilliseconds(MajorPhaseThresholdTicks);
        internal double CooldownSeconds => TicksToSeconds(CooldownTicks);
        internal double PreWindowSeconds => TicksToSeconds(PreWindowTicks);
        internal double PostWindowSeconds => TicksToSeconds(PostWindowTicks);

        internal string Format()
        {
            return "frame>" + Milliseconds(FrameThresholdTicks) +
                   ", wait>" + Milliseconds(WaitForSimThresholdTicks) +
                   ", sim>" + Milliseconds(SimulationThresholdTicks) +
                   ", phase>" + Milliseconds(MajorPhaseThresholdTicks) +
                   ", relative>" + RelativeMultiplier.ToString("F1", CultureInfo.InvariantCulture) + "x" +
                   ", cooldown=" + Seconds(CooldownTicks) +
                   ", captures=" + MaximumAutomaticCaptures +
                   ", window=" + Seconds(PreWindowTicks) + "/" + Seconds(PostWindowTicks) +
                   ", auto-deep=" + AutomaticDeepCapture;
        }

        internal static long MillisecondsToTicks(double milliseconds) =>
            double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds <= 0
                ? 0
                : (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000.0, MidpointRounding.AwayFromZero);

        internal static long SecondsToTicks(double seconds) =>
            double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0
                ? 0
                : (long)Math.Round(seconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);

        internal static string Milliseconds(long ticks) =>
            (ticks * 1000.0 / Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture) + " ms";

        internal static string Seconds(long ticks) =>
            (ticks / (double)Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture) + " s";

        private static double TicksToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static double TicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;
    }

    internal readonly struct RuntimeSpikeRecord
    {
        internal RuntimeSpikeRecord(
            long sequence,
            string reason,
            RuntimeFrameSample trigger,
            long startTimestamp,
            long endTimestamp,
            RuntimeFrameSample[] samples,
            bool automaticDeepCapture)
        {
            Sequence = sequence;
            Reason = reason;
            Trigger = trigger;
            StartTimestamp = startTimestamp;
            EndTimestamp = endTimestamp;
            Samples = samples;
            AutomaticDeepCapture = automaticDeepCapture;
        }

        internal long Sequence { get; }
        internal string Reason { get; }
        internal RuntimeFrameSample Trigger { get; }
        internal long StartTimestamp { get; }
        internal long EndTimestamp { get; }
        internal RuntimeFrameSample[] Samples { get; }
        internal bool AutomaticDeepCapture { get; }
        internal double DurationSeconds => (EndTimestamp - StartTimestamp) / (double)Stopwatch.Frequency;
    }

    /// <summary>
    ///     Keeps automatic broad captures bounded. The frame history owns the primitive samples;
    ///     this class only copies a before/after interval when a trigger actually fires.
    /// </summary>
    internal sealed class RuntimeSpikeHistory
    {
        private const int MaximumStoredCaptures = 16;

        private readonly RuntimeFrameHistory m_history;
        private readonly List<RuntimeSpikeRecord> m_captures = new(MaximumStoredCaptures);
        private ActiveCapture? m_active;
        private long m_nextSequence;
        private long m_lastAutomaticCaptureTimestamp = long.MinValue;
        private int m_automaticCaptures;

        internal RuntimeSpikeHistory(RuntimeFrameHistory history)
        {
            m_history = history;
        }

        internal int Count => m_captures.Count;
        internal RuntimeSpikeRecord? Latest => m_captures.Count == 0 ? null : m_captures[m_captures.Count - 1];
        internal bool IsCaptureActive => m_active is not null;
        internal bool CaptureStarted { get; private set; }
        internal string ActiveReason => m_active?.Reason ?? string.Empty;
        internal bool ActiveAutomaticDeepCapture => m_active?.AutomaticDeepCapture == true;

        internal RuntimeSpikeRecord? Observe(
            RuntimeFrameSample sample,
            long rollingMedianFrameTicks,
            RuntimeSpikePolicy policy)
        {
            CaptureStarted = false;
            RuntimeSpikeRecord? completed = null;
            if (m_active is not null && sample.CapturedTimestamp >= m_active.EndTimestamp)
            {
                completed = FinishActive(m_active, sample.CapturedTimestamp);
                m_active = null;
            }

            if (m_active is not null ||
                m_automaticCaptures >= policy.MaximumAutomaticCaptures ||
                (m_lastAutomaticCaptureTimestamp != long.MinValue &&
                 sample.CapturedTimestamp - m_lastAutomaticCaptureTimestamp < policy.CooldownTicks))
            {
                return completed;
            }

            if (!TryGetTrigger(sample, rollingMedianFrameTicks, policy, out string reason))
            {
                return completed;
            }

            long start = sample.CapturedTimestamp - policy.PreWindowTicks;
            long end = sample.CapturedTimestamp + policy.PostWindowTicks;
            m_active = new ActiveCapture(
                Interlocked.Increment(ref m_nextSequence),
                reason,
                sample,
                start,
                end,
                m_history.SnapshotBetween(start, sample.CapturedTimestamp),
                policy.AutomaticDeepCapture);
            m_lastAutomaticCaptureTimestamp = sample.CapturedTimestamp;
            m_automaticCaptures++;
            CaptureStarted = true;
            return completed;
        }

        internal RuntimeSpikeRecord[] Snapshot(int count)
        {
            if (count <= 0 || m_captures.Count == 0)
            {
                return Array.Empty<RuntimeSpikeRecord>();
            }
            int take = Math.Min(count, m_captures.Count);
            var result = new RuntimeSpikeRecord[take];
            for (int index = 0; index < take; index++)
            {
                result[index] = m_captures[m_captures.Count - 1 - index];
            }
            return result;
        }

        internal void Clear()
        {
            m_captures.Clear();
            m_active = null;
            CaptureStarted = false;
            m_automaticCaptures = 0;
            m_lastAutomaticCaptureTimestamp = long.MinValue;
        }

        internal static bool TryGetTrigger(
            RuntimeFrameSample sample,
            long rollingMedianFrameTicks,
            RuntimeSpikePolicy policy,
            out string reason)
        {
            if (sample.FrameTicks >= policy.FrameThresholdTicks && policy.FrameThresholdTicks > 0)
            {
                reason = "frame/update threshold";
                return true;
            }
            if (sample.Timings.WaitForSimTicks >= policy.WaitForSimThresholdTicks && policy.WaitForSimThresholdTicks > 0)
            {
                reason = "wait-for-simulation threshold";
                return true;
            }
            if (sample.SimTicks >= policy.SimulationThresholdTicks && policy.SimulationThresholdTicks > 0)
            {
                reason = "simulation worker threshold";
                return true;
            }
            long majorPhase = Math.Max(sample.Timings.SimUpdateTicks, Math.Max(sample.Timings.RenderTicks, sample.Timings.SyncTicks));
            if (majorPhase >= policy.MajorPhaseThresholdTicks && policy.MajorPhaseThresholdTicks > 0)
            {
                reason = "major phase threshold";
                return true;
            }
            if (sample.Runner.WasOvertime)
            {
                reason = "simulation overtime";
                return true;
            }
            if (rollingMedianFrameTicks > 0 && sample.FrameTicks >= Stopwatch.Frequency / 50 &&
                sample.FrameTicks >= rollingMedianFrameTicks * policy.RelativeMultiplier)
            {
                reason = "relative frame threshold";
                return true;
            }
            reason = string.Empty;
            return false;
        }

        private RuntimeSpikeRecord FinishActive(ActiveCapture active, long endTimestamp)
        {
            RuntimeFrameSample[] post = m_history.SnapshotBetween(active.Trigger.CapturedTimestamp + 1, endTimestamp);
            RuntimeFrameSample[] samples = new RuntimeFrameSample[active.PreSamples.Length + post.Length];
            Array.Copy(active.PreSamples, samples, active.PreSamples.Length);
            Array.Copy(post, 0, samples, active.PreSamples.Length, post.Length);
            var result = new RuntimeSpikeRecord(
                active.Sequence,
                active.Reason,
                active.Trigger,
                active.StartTimestamp,
                endTimestamp,
                samples,
                active.AutomaticDeepCapture);
            if (m_captures.Count == MaximumStoredCaptures)
            {
                m_captures.RemoveAt(0);
            }
            m_captures.Add(result);
            return result;
        }

        private sealed class ActiveCapture
        {
            internal ActiveCapture(
                long sequence,
                string reason,
                RuntimeFrameSample trigger,
                long startTimestamp,
                long endTimestamp,
                RuntimeFrameSample[] preSamples,
                bool automaticDeepCapture)
            {
                Sequence = sequence;
                Reason = reason;
                Trigger = trigger;
                StartTimestamp = startTimestamp;
                EndTimestamp = endTimestamp;
                PreSamples = preSamples;
                AutomaticDeepCapture = automaticDeepCapture;
            }

            internal long Sequence { get; }
            internal string Reason { get; }
            internal RuntimeFrameSample Trigger { get; }
            internal long StartTimestamp { get; }
            internal long EndTimestamp { get; }
            internal RuntimeFrameSample[] PreSamples { get; }
            internal bool AutomaticDeepCapture { get; }
        }
    }
}
