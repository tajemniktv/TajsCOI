// Taj's COI Mods | GameLoopTimingDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using Mafi;
using Mafi.Core;
using Mafi.Core.Console;
using Mafi.Core.GameLoop;
using Mafi.Core.Simulation;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Profiler.Core;

namespace TajsCOI.Profiler.Probes.Runtime
{
    /// <summary>
    /// Reads Captain of Industry's existing GameLoopTimings rings once per render frame and
    /// retains a bounded primitive history for hitch diagnosis. The reader never wraps game
    /// callbacks or changes loop behavior; unsupported private surfaces degrade fail-open.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class GameLoopTimingDiagnosticsService
    {
        private const int HistoryCapacity = 4096;
        private const double MinimumSeconds = 1.0;
        private const double MaximumSeconds = 300.0;
        private const int MinimumSpikeCount = 1;
        private const int MaximumSpikeCount = 32;

        private readonly GameLoopTimingsAccess? m_timings;
        private readonly GameRunnerTimingAccess? m_runner;
        private readonly SimLoopEvents m_simLoop;
        private readonly RuntimeFrameHistory m_history = new RuntimeFrameHistory(HistoryCapacity);
        private ITajsLogger? m_log;
        private int m_callbackFailureLogged;

        public GameLoopTimingDiagnosticsService(
            DependencyResolver resolver,
            IGameLoopEvents gameLoop,
            SimLoopEvents simLoop,
            ITajsRuntime runtime)
        {
            m_simLoop = simLoop;
            m_log = runtime.GetLogger("TajsProfiler", "GameLoopTimings");

            GameLoopTimingsAccess.TryCreate(out m_timings, out string timingReason);
            Type? runnerType = typeof(IGameLoopEvents).Assembly.GetType("Mafi.Core.GameLoop.GameRunner", false);
            object? runner = runnerType is null ? null : resolver.TryResolve(runnerType).ValueOrNull;
            m_runner = GameRunnerTimingAccess.TryCreate(runner, out string runnerReason);

            gameLoop.InputUpdate.AddNonSaveable(this, CaptureFrame);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);

            CompatibilityState state = m_timings is null
                ? m_runner is null || !m_runner.IsAvailable ? CompatibilityState.Disabled : CompatibilityState.Degraded
                : m_runner is null || !m_runner.IsAvailable ? CompatibilityState.Degraded :
                CompatibilityState.Compatible;
            string details = m_timings is null ? "GameLoopTimings unavailable: " + timingReason :
                m_runner is null ? "GameRunner unavailable." :
                string.IsNullOrWhiteSpace(runnerReason) ? "All Phase A timing surfaces are available." :
                "GameRunner degraded: " + runnerReason;
            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsProfiler",
                "GameLoopTimings",
                state,
                "0.8.7b GameLoopTimings and GameRunner flight-recorder surfaces",
                details,
                state == CompatibilityState.Compatible
                    ? "Rolling frame, render, wait-for-simulation, and simulation summaries are available."
                    : state == CompatibilityState.Degraded
                        ? "Available timing surfaces remain active; unsupported detail is reported as unavailable."
                        : "No compatible timing surface was found; the probe remains inactive."));
        }

        [ConsoleCommand(
            documentation: "Shows the current low-overhead runtime flight-recorder status.",
            customCommandName: "tajs_profiler_status")]
        public string Status()
        {
            RuntimeFrameSample[] latest = m_history.SnapshotRecent(1);
            var builder = new StringBuilder(512)
                .Append("TajsProfiler runtime flight recorder: frames=")
                .Append(m_history.Count)
                .Append('/')
                .Append(m_history.Capacity)
                .Append(", GameLoopTimings=")
                .Append(m_timings is null ? "unavailable" : "available")
                .Append(", GameRunner=")
                .Append(m_runner is null ? "unavailable" : m_runner.IsAvailable ? "available" : "unavailable");

            if (m_timings is null)
            {
                builder.Append(" (reader disabled; private game surface did not validate)");
            }
            if (m_runner is not null && !string.IsNullOrWhiteSpace(m_runner.UnavailableProperties))
            {
                builder.Append("; unavailable GameRunner properties=").Append(m_runner.UnavailableProperties);
            }
            if (latest.Length == 1)
            {
                RuntimeFrameSample sample = latest[0];
                builder.Append("; latest sequence=").Append(sample.Sequence)
                    .Append(", classification=").Append(FormatClassification(sample.Classification))
                    .Append(", frame=").Append(FormatMilliseconds(sample.FrameTicks))
                    .Append(", wait-for-sim=").Append(FormatMilliseconds(sample.Timings.WaitForSimTicks))
                    .Append(", sim=").Append(FormatMilliseconds(sample.SimTicks));
            }
            else
            {
                builder.Append("; latest=none");
            }
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Shows rolling runtime p50/p95/p99/max timing summaries for the requested recent interval.",
            customCommandName: "tajs_profiler_runtime")]
        public string Runtime(float seconds = 10.0f)
        {
            if (!IsValidSeconds(seconds))
            {
                return "Runtime profiler rejected: seconds must be finite and between 1 and 300.";
            }

            RuntimeFrameSummary summary = m_history.SummarizeRecent(seconds);
            if (summary.Count == 0)
            {
                return "Runtime profiler summary: no complete GameLoopTimings samples captured yet.";
            }

            var builder = new StringBuilder(1024)
                .Append("Runtime stutter summary (last ")
                .Append(seconds.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" s, samples=")
                .Append(summary.Count)
                .Append(")")
                .Append("\nframe/update       ").Append(FormatMetric(summary.Frame))
                .Append("\nrender             ").Append(FormatMetric(summary.Render))
                .Append("\nwait-for-sim       ").Append(FormatMetric(summary.WaitForSim))
                .Append("\nsim-update         ").Append(FormatMetric(summary.Sim))
                .Append("\nclassification     ")
                .Append(FormatClassificationCounts(summary))
                .Append("\nlatest             sequence=").Append(summary.Latest.Sequence)
                .Append(", speed=").Append(summary.Latest.SimSpeedMult)
                .Append("x, paused=").Append(summary.Latest.SimPaused)
                .Append(", steps=").Append(summary.Latest.SimStepsPerUpdate)
                .Append('/').Append(summary.Latest.BudgetedSimSteps)
                .Append(", overtime=").Append(summary.Latest.Runner.WasOvertime)
                .Append(".");
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Lists the largest recent runtime frame samples retained by the flight recorder.",
            customCommandName: "tajs_profiler_spikes")]
        public string Spikes(int count = 5)
        {
            if (count < MinimumSpikeCount || count > MaximumSpikeCount)
            {
                return "Runtime profiler rejected: spike count must be between 1 and 32.";
            }

            RuntimeFrameSample[] spikes = m_history.FindSpikes(MaximumSeconds, count);
            if (spikes.Length == 0)
            {
                return "Runtime profiler spikes: none stored.";
            }

            var builder = new StringBuilder(1024).Append("Runtime profiler spikes (top ").Append(spikes.Length).Append("):");
            foreach (RuntimeFrameSample sample in spikes)
            {
                builder.Append("\n  #").Append(sample.Sequence)
                    .Append(" ").Append(FormatClassification(sample.Classification))
                    .Append(" frame=").Append(FormatMilliseconds(sample.FrameTicks))
                    .Append(", render=").Append(FormatMilliseconds(sample.RenderTicks))
                    .Append(", wait-for-sim=").Append(FormatMilliseconds(sample.Timings.WaitForSimTicks))
                    .Append(", sim=").Append(FormatMilliseconds(sample.SimTicks))
                    .Append(", speed=").Append(sample.SimSpeedMult).Append('x')
                    .Append(", paused=").Append(sample.SimPaused)
                    .Append(", steps=").Append(sample.SimStepsPerUpdate).Append('/').Append(sample.BudgetedSimSteps)
                    .Append(", overtime=").Append(sample.Runner.WasOvertime);
            }
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Shows the raw 20-phase timing values from the most recent flight-recorder samples.",
            customCommandName: "tajs_profiler_runtime_raw")]
        public string Raw(int count = 1)
        {
            if (count < 1 || count > 16)
            {
                return "Runtime profiler rejected: raw sample count must be between 1 and 16.";
            }

            RuntimeFrameSample[] samples = m_history.SnapshotRecent(count);
            if (samples.Length == 0)
            {
                return "Runtime profiler raw samples: none stored.";
            }

            var builder = new StringBuilder(2048).Append("Runtime profiler raw samples:");
            foreach (RuntimeFrameSample sample in samples)
            {
                builder.Append("\n  #").Append(sample.Sequence).Append(" ")
                    .Append(FormatRawTiming(sample.Timings));
            }
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Clears the bounded runtime flight-recorder history without changing game behavior.",
            customCommandName: "tajs_profiler_runtime_clear")]
        public string Clear()
        {
            int count = m_history.Count;
            m_history.Clear();
            return $"Runtime profiler history cleared ({count} frame sample(s)).";
        }

        private void CaptureFrame(GameTime _)
        {
            try
            {
                GameLoopTimingSnapshot timings = m_timings is null
                    ? default
                    : m_timings.ReadLatest();
                GameRunnerTimingSnapshot runner = m_runner is null
                    ? default
                    : m_runner.Read();
                m_history.Record(
                    Stopwatch.GetTimestamp(),
                    timings,
                    runner,
                    m_simLoop.SimSpeedMult,
                    m_simLoop.SimStepsPerUpdate,
                    m_simLoop.BudgetedSimSteps,
                    m_simLoop.IsSimPaused);
            }
            catch (Exception exception)
            {
                if (Interlocked.Exchange(ref m_callbackFailureLogged, 1) == 0)
                {
                    m_log?.Exception(exception, "GameLoopTimings read failed; runtime flight recorder remains fail-open.");
                }
            }
        }

        private void OnTerminate()
        {
            // The history is resolver-scoped and intentionally retained until the service is
            // released so the final gameplay interval can still be inspected by a console caller.
        }

        private static bool IsValidSeconds(float seconds) =>
            !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds >= MinimumSeconds && seconds <= MaximumSeconds;

        private static string FormatMetric(RuntimeMetricSummary metric) =>
            "p50 " + FormatMilliseconds(metric.P50Ticks) +
            "  p95 " + FormatMilliseconds(metric.P95Ticks) +
            "  p99 " + FormatMilliseconds(metric.P99Ticks) +
            "  max " + FormatMilliseconds(metric.MaxTicks) +
            "  total " + FormatMilliseconds(metric.TotalTicks);

        private static string FormatClassificationCounts(RuntimeFrameSummary summary) =>
            "main/render=" + summary.MainRenderBoundCount.ToString(CultureInfo.InvariantCulture) +
            ", sim=" + summary.SimulationBoundCount.ToString(CultureInfo.InvariantCulture) +
            ", wait=" + summary.WaitingForSimulationCount.ToString(CultureInfo.InvariantCulture) +
            ", mixed=" + summary.MixedCount.ToString(CultureInfo.InvariantCulture) +
            ", unknown=" + summary.UnknownCount.ToString(CultureInfo.InvariantCulture);

        private static string FormatRawTiming(GameLoopTimingSnapshot timing)
        {
            var builder = new StringBuilder(768)
                .Append("input=").Append(FormatMilliseconds(timing.InputTicks))
                .Append(", input-end=").Append(FormatMilliseconds(timing.InputEndTicks))
                .Append(", sync-start=").Append(FormatMilliseconds(timing.SyncStartTicks))
                .Append(", sync=").Append(FormatMilliseconds(timing.SyncTicks))
                .Append(", sync-end=").Append(FormatMilliseconds(timing.SyncEndTicks))
                .Append(", render-after-sync=").Append(FormatMilliseconds(timing.RenderAfterSyncTicks))
                .Append(", render=").Append(FormatMilliseconds(timing.RenderTicks))
                .Append(", render-end=").Append(FormatMilliseconds(timing.RenderEndTicks))
                .Append(", wait-for-sim=").Append(FormatMilliseconds(timing.WaitForSimTicks))
                .Append(", sim-cmd=").Append(FormatMilliseconds(timing.SimCmdTicks))
                .Append(", sim-cmd-extra=").Append(FormatMilliseconds(timing.SimCmdExtraTicks))
                .Append(", sim-after-sync=").Append(FormatMilliseconds(timing.SimAfterSyncTicks))
                .Append(", sim-start=").Append(FormatMilliseconds(timing.SimStartTicks))
                .Append(", sim-parallel-start=").Append(FormatMilliseconds(timing.SimParallelStartTicks))
                .Append(", sim-update=").Append(FormatMilliseconds(timing.SimUpdateTicks))
                .Append(", sim-parallel-end=").Append(FormatMilliseconds(timing.SimParallelEndTicks))
                .Append(", sim-end=").Append(FormatMilliseconds(timing.SimEndTicks))
                .Append(", sim-read-state=").Append(FormatMilliseconds(timing.SimReadStateTicks))
                .Append(", sim-end-for-ui=").Append(FormatMilliseconds(timing.SimEndForUiTicks))
                .Append(", sim-paused-ui=").Append(FormatMilliseconds(timing.SimPausedUiTicks));
            return builder.ToString();
        }

        private static string FormatClassification(RuntimeFrameClassification classification) =>
            classification switch
            {
                RuntimeFrameClassification.MainRenderBound => "main/render bound",
                RuntimeFrameClassification.SimulationBound => "simulation bound",
                RuntimeFrameClassification.WaitingForSimulation => "waiting for simulation",
                RuntimeFrameClassification.Mixed => "mixed",
                _ => "unknown",
            };

        private static string FormatMilliseconds(long stopwatchTicks) =>
            stopwatchTicks < 0
                ? "unavailable"
                : (stopwatchTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms";
    }
}
