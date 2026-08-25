// Taj's COI Mods | GameLoopTimingDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Mafi;
using Mafi.Core;
using Mafi.Core.Console;
using Mafi.Core.GameLoop;
using Mafi.Core.Simulation;
using Mafi.Logging;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using TajsCOI.Profiler.Core;
using TajsCOI.Profiler.Probes.Dumping;

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
        private readonly DeferredGameRunnerTimingAccess m_runnerDiscovery;
        private readonly SimLoopEvents m_simLoop;
        private readonly RuntimeFrameHistory m_history = new RuntimeFrameHistory(HistoryCapacity);
        private readonly RuntimeSpikeHistory m_spikes;
        private readonly RuntimeCounterSampler m_counters;
        private readonly RuntimeRollingPercentile m_frameBaseline = new RuntimeRollingPercentile(128);
        private RuntimeSpikePolicy m_spikePolicy = new RuntimeSpikePolicy();
        private readonly ITajsSettings m_settings;
        private readonly ITajsRuntime m_runtime;
        private GameRunnerTimingAccess? m_runner;
        private string m_timingReason = string.Empty;
        private string m_runnerReason = DeferredGameRunnerTimingAccess.PendingReason;
        private readonly object m_runnerReportGate = new object();
        private bool m_runnerCompatibilityReported;
        private DeepTracingPatchSummary m_deepPatchSummary;
        private long m_rollingMedianFrameTicks;
        private int m_baselineSampleCounter;
        private long m_traceWindowStart;
        private long m_traceWindowEnd;
        private ITajsLogger? m_log;
        private int m_callbackFailureLogged;

        public GameLoopTimingDiagnosticsService(
            LazyResolve<IGameIdProvider> runner,
            IGameLoopEvents gameLoop,
            SimLoopEvents simLoop,
            ITajsRuntime runtime,
            ITajsSettings settings)
        {
            m_simLoop = simLoop;
            m_settings = settings;
            m_runtime = runtime;
            m_runnerDiscovery = new DeferredGameRunnerTimingAccess(runner);
            m_spikes = new RuntimeSpikeHistory(m_history);
            m_counters = new RuntimeCounterSampler();
            m_log = runtime.GetLogger("TajsProfiler", "GameLoopTimings");
            ProfilerSettingsCatalog.RegisterAll(settings);
            m_spikePolicy = ProfilerSettingsCatalog.ReadPolicy(settings);
            settings.Changed += OnSettingChanged;

            GameLoopTimingsAccess.TryCreate(out m_timings, out m_timingReason);

            try
            {
                m_deepPatchSummary = DeepCallbackRecorder.Initialize(gameLoop, simLoop);
            }
            catch (Exception exception)
            {
                m_deepPatchSummary = default;
                m_log?.Exception(exception, "Deep callback tracing setup failed; broad profiling remains active.");
            }

            gameLoop.InputUpdate.AddNonSaveable(this, CaptureFrame);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);

            ReportTimingCompatibility(discoveryPending: true);

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsProfiler",
                "DeepCallbacks",
                m_deepPatchSummary.IsAvailable ? CompatibilityState.Compatible : CompatibilityState.Degraded,
                "0.8.7b Event/EventNonSaveable callback invocation surfaces",
                "expected=" + m_deepPatchSummary.ExpectedMethods +
                ", patched=" + m_deepPatchSummary.PatchedMethods +
                ", callback replacements=" + m_deepPatchSummary.ReplacedInvocations +
                ", failures=" + m_deepPatchSummary.Failures,
                m_deepPatchSummary.IsAvailable
                    ? "Opt-in deep callback spans are available; they remain inactive until explicitly armed."
                    : "Deep callback spans are unavailable; broad timing and counter capture remain independent."));
        }

        private void ReportTimingCompatibility(bool discoveryPending)
        {
            bool runnerAvailable = m_runner is not null && m_runner.IsAvailable;
            CompatibilityState state = discoveryPending
                ? CompatibilityState.Degraded
                : m_timings is null
                    ? runnerAvailable ? CompatibilityState.Degraded : CompatibilityState.Disabled
                    : runnerAvailable ? CompatibilityState.Compatible : CompatibilityState.Degraded;
            string details;
            if (discoveryPending)
            {
                details = m_timings is null
                    ? "GameLoopTimings unavailable: " + m_timingReason + "; " + m_runnerReason
                    : m_runnerReason + " GameLoopTimings remains the primary timing source.";
            }
            else if (m_timings is null)
            {
                details = "GameLoopTimings unavailable: " + m_timingReason +
                    (m_runner is null ? "; GameRunner unavailable: " : "; GameRunner degraded: ") + m_runnerReason;
            }
            else if (m_runner is null)
            {
                details = "GameRunner unavailable: " + m_runnerReason;
            }
            else if (string.IsNullOrWhiteSpace(m_runnerReason))
            {
                details = "All Phase A timing surfaces are available.";
            }
            else
            {
                details = "GameRunner degraded: " + m_runnerReason;
            }

            m_runtime.ReportCompatibility(new CompatibilityReport(
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

        private void EnsureRunnerTimingAccess()
        {
            lock (m_runnerReportGate)
            {
                if (m_runnerCompatibilityReported)
                {
                    return;
                }

                m_runner = m_runnerDiscovery.TryGet(out m_runnerReason);
                m_runnerCompatibilityReported = true;
                ReportTimingCompatibility(discoveryPending: false);
            }
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
                .Append(!m_runnerDiscovery.IsDiscoveryAttempted
                    ? "pending"
                    : m_runner is null ? "unavailable" : m_runner.IsAvailable ? "available" : "unavailable")
                .Append(", automatic-spikes=").Append(m_spikes.Count)
                .Append(", deep=").Append(DeepCallbackRecorder.IsActive ? "active" : "idle")
                .Append(", deep-patches=").Append(m_deepPatchSummary.PatchedMethods)
                .Append('/').Append(m_deepPatchSummary.ExpectedMethods)
                .Append(", timing-ring-drops=").Append(m_timings?.DroppedEntries ?? 0)
                .Append(", counters=").Append(m_counters.SupportedUnityCounters == 0 ? "managed-only" : "available")
                .Append(", gpu-frame=").Append(m_counters.GpuTelemetryStatus)
                .Append("; gpu-memory=graphics-driver allocation (not dedicated VRAM)");

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
                    .Append(", sim=").Append(FormatMilliseconds(sample.SimTicks))
                    .Append(", graphics-driver-memory=")
                    .Append(RuntimeTraceText.OptionalBytes(sample.Counters.UnityGraphicsBytes))
                    .Append(", gc-latest-delta=").Append(sample.Counters.TotalGcDelta)
                    .Append(", dumping-calls=").Append(sample.SubsystemCounters.DumpingCalls);
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
                .Append(", paused-excluded=")
                .Append(summary.PausedSampleCount)
                .Append(")")
                .Append("\nframe/update       ").Append(FormatMetric(summary.Frame))
                .Append("\nrender             ").Append(FormatMetric(summary.Render))
                .Append("\nwait-for-sim       ").Append(FormatMetric(summary.WaitForSim))
                .Append("\nsim-update         ").Append(FormatMetric(summary.Sim))
                .Append("\nclassification     ")
                .Append(FormatClassificationCounts(summary))
                .Append("\nresources          managed-heap=")
                .Append(RuntimeTraceText.OptionalBytes(summary.Latest.Counters.ManagedHeapBytes))
                .Append(", mono-used=")
                .Append(RuntimeTraceText.OptionalBytes(summary.Latest.Counters.MonoUsedBytes))
                .Append(", mono-heap=")
                .Append(RuntimeTraceText.OptionalBytes(summary.Latest.Counters.MonoHeapBytes))
                .Append(", unity-allocated=")
                .Append(RuntimeTraceText.OptionalBytes(summary.Latest.Counters.UnityAllocatedBytes))
                .Append(", unity-reserved=")
                .Append(RuntimeTraceText.OptionalBytes(summary.Latest.Counters.UnityReservedBytes))
                .Append(", unity-unused-reserved=")
                .Append(RuntimeTraceText.OptionalBytes(summary.Latest.Counters.UnityUnusedReservedBytes))
                .Append(", graphics-driver-memory=")
                .Append(RuntimeTraceText.OptionalBytes(summary.Latest.Counters.UnityGraphicsBytes))
                .Append(", gc-latest-delta=")
                .Append(summary.Latest.Counters.TotalGcDelta)
                .Append(", gc-interval=")
                .Append(summary.GcDeltaTotal)
                .Append(", gc-peak=")
                .Append(summary.GcPeakDelta)
                .Append(", gpu-frame=")
                .Append(summary.Latest.Counters.HasGpuTelemetry ? RuntimeTraceText.Milliseconds(summary.Latest.Counters.GpuFrameTicks) : "unavailable")
                .Append("\nsubsystems          dumping-calls=")
                .Append(summary.Latest.SubsystemCounters.DumpingCalls)
                .Append(", dumping-time=")
                .Append(RuntimeTraceText.Milliseconds(summary.Latest.SubsystemCounters.DumpingElapsedTicks))
                .Append(", path-enqueues=")
                .Append(summary.Latest.SubsystemCounters.PathEnqueues)
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

            RuntimeSpikeRecord[] captures = m_spikes.Snapshot(count);
            RuntimeFrameSample[] spikes = m_history.FindSpikes(MaximumSeconds, count);
            if (captures.Length == 0 && spikes.Length == 0)
            {
                return "Runtime profiler spikes: none stored.";
            }

            var builder = new StringBuilder(2048).Append("Runtime profiler spikes:");
            foreach (RuntimeSpikeRecord capture in captures)
            {
                RuntimeFrameSample sample = capture.Trigger;
                builder.Append("\n  capture #").Append(capture.Sequence)
                    .Append(" reason=\"").Append(capture.Reason).Append('"')
                    .Append(", window=").Append(RuntimeSpikePolicy.Seconds((long)(capture.DurationSeconds * Stopwatch.Frequency)))
                    .Append(", samples=").Append(capture.Samples.Length)
                    .Append(", automatic-deep=").Append(capture.AutomaticDeepCapture)
                    .Append(", trigger=#").Append(sample.Sequence)
                    .Append(" ").Append(FormatClassification(sample.Classification))
                    .Append(" frame=").Append(FormatMilliseconds(sample.FrameTicks));
            }
            if (spikes.Length > 0)
            {
                builder.Append("\nLargest retained frames:");
            }
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
            m_spikes.Clear();
            m_frameBaseline.Clear();
            m_rollingMedianFrameTicks = 0;
            m_traceWindowStart = 0;
            m_traceWindowEnd = 0;
            return $"Runtime profiler history cleared ({count} frame sample(s)).";
        }

        [ConsoleCommand(
            documentation: "Arms opt-in deep callback tracing for a bounded number of seconds.",
            customCommandName: "tajs_profiler_arm")]
        public string Arm(float seconds = 10.0f) => StartDeepCapture(seconds, false);

        [ConsoleCommand(
            documentation: "Starts opt-in deep callback tracing for a bounded number of seconds.",
            customCommandName: "tajs_profiler_deep_start")]
        public string DeepStart(float seconds = 10.0f) => StartDeepCapture(seconds, false);

        [ConsoleCommand(
            documentation: "Stops the current opt-in deep callback capture.",
            customCommandName: "tajs_profiler_deep_stop")]
        public string DeepStop()
        {
            DeepCaptureWindow window = DeepCallbackRecorder.Stop();
            if (!window.WasActive)
            {
                return "Runtime profiler deep capture: no active capture.";
            }
            m_traceWindowStart = window.StartTimestamp;
            m_traceWindowEnd = window.EndTimestamp;
            return "Runtime profiler deep capture stopped: window=" +
                FormatSeconds(window.EndTimestamp - window.StartTimestamp) + ".";
        }

        [ConsoleCommand(
            documentation: "Shows top deep callbacks ranked by total time with phase, share, percentiles and spike context.",
            customCommandName: "tajs_profiler_deep_report")]
        public string DeepReport(int count = 10)
        {
            if (count < 1 || count > 64)
            {
                return "Runtime profiler rejected: callback count must be between 1 and 64.";
            }
            CallbackMetricSnapshot[] metrics = DeepCallbackRecorder.SnapshotCallbackMetrics(count);
            if (metrics.Length == 0)
            {
                return "Runtime profiler deep report: no callback spans captured.";
            }

            var builder = new StringBuilder(2048).Append("Runtime profiler deep callbacks (top ").Append(metrics.Length).Append("):");
            foreach (CallbackMetricSnapshot metric in metrics)
            {
                builder.Append("\n  ").Append(metric.Metadata.DisplayName)
                    .Append(" phase=").Append(RuntimeTracePhase.Name(metric.PhaseId))
                    .Append(" calls=").Append(metric.CallCount)
                    .Append(", total=").Append(RuntimeTraceText.Milliseconds(metric.TotalTicks))
                    .Append(", share=").Append(metric.SharePercent.ToString("F2", CultureInfo.InvariantCulture)).Append('%')
                    .Append(", avg=").Append(RuntimeTraceText.Milliseconds(metric.AverageTicks))
                    .Append(", p95=").Append(RuntimeTraceText.Milliseconds(metric.P95Ticks))
                    .Append(", p99=").Append(RuntimeTraceText.Milliseconds(metric.P99Ticks))
                    .Append(", max=").Append(RuntimeTraceText.Milliseconds(metric.MaxTicks))
                    .Append(", slow=").Append(metric.SlowCallCount)
                    .Append(", worst-ts=").Append(metric.WorstStartTimestamp > 0
                        ? metric.WorstStartTimestamp.ToString(CultureInfo.InvariantCulture)
                        : "unavailable");
            }
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Shows the largest individual deep callback executions, ranked by single-invocation duration.",
            customCommandName: "tajs_profiler_deep_worst")]
        public string DeepWorst(int count = 10)
        {
            if (count < 1 || count > 64)
            {
                return "Runtime profiler rejected: callback count must be between 1 and 64.";
            }

            CallbackInvocationSnapshot[] invocations = DeepCallbackRecorder.SnapshotWorstCallbackInvocations(count);
            if (invocations.Length == 0)
            {
                return "Runtime profiler worst callbacks: no callback spans captured.";
            }

            var builder = new StringBuilder(2048)
                .Append("Runtime profiler worst callback invocations (top ")
                .Append(invocations.Length)
                .Append("):");
            for (int index = 0; index < invocations.Length; index++)
            {
                CallbackInvocationSnapshot invocation = invocations[index];
                builder.Append("\n  ").Append(index + 1).Append(". ")
                    .Append(invocation.Metadata.DisplayName)
                    .Append(" phase=").Append(RuntimeTracePhase.Name(invocation.PhaseId))
                    .Append(", duration=").Append(RuntimeTraceText.Milliseconds(invocation.DurationTicks))
                    .Append(", timestamp=").Append(invocation.StartTimestamp)
                    .Append(", thread=").Append(invocation.ThreadId)
                    .Append(", sequence=").Append(invocation.Sequence);
            }
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Exports the bounded broad/deep profiler history as Chrome trace JSON.",
            customCommandName: "tajs_profiler_trace_export")]
        public string TraceExport(string name = "runtime")
        {
            string exportDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsCOI",
                "Profiler");
            string safeName = SanitizeFileName(name);
            string path = Path.Combine(
                exportDirectory,
                safeName + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json");
            RuntimeFrameSample[] frames = m_traceWindowStart > 0
                ? m_history.SnapshotBetween(
                    m_traceWindowStart,
                    m_traceWindowEnd > 0 && m_traceWindowEnd <= Stopwatch.GetTimestamp()
                        ? m_traceWindowEnd
                        : Stopwatch.GetTimestamp())
                : m_history.SnapshotRecent(600);
            RuntimeTraceExportResult result = RuntimeTraceExporter.Export(
                path,
                frames,
                DeepCallbackRecorder.SnapshotSpans(),
                DeepCallbackRecorder.SnapshotMetadata(),
                DeepCallbackRecorder.SnapshotMarkers());
            return "Runtime profiler trace exported: " + result.EventCount + " event(s) to " + result.Path;
        }

        [ConsoleCommand(
            documentation: "Adds a user marker to the active or most recent deep trace.",
            customCommandName: "tajs_profiler_mark")]
        public string Mark(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return "Runtime profiler rejected: marker label is empty.";
            }
            DeepCallbackRecorder.AddMarker(Stopwatch.GetTimestamp(), label);
            return "Runtime profiler marker added: " + label.Trim();
        }

        [ConsoleCommand(
            documentation: "Measures the validated GameLoopTimings reader over a bounded command-only loop.",
            customCommandName: "tajs_profiler_overhead_bench")]
        public string OverheadBench(int iterations = 10000)
        {
            if (iterations < 100 || iterations > 1000000)
            {
                return "Runtime profiler rejected: iterations must be between 100 and 1000000.";
            }
            if (m_timings is null)
            {
                return "Runtime profiler overhead: GameLoopTimings reader unavailable.";
            }
            long elapsed = DeepCallbackRecorder.MeasureReader(() => m_timings.ReadLatest(), iterations);
            return "Runtime profiler reader overhead: iterations=" + iterations +
                ", total=" + RuntimeTraceText.Milliseconds(elapsed) +
                ", avg=" + RuntimeTraceText.Milliseconds(elapsed / iterations) + ".";
        }

        [ConsoleCommand(
            documentation: "Shows or updates absolute, relative, cooldown and trigger-window spike policy.",
            customCommandName: "tajs_profiler_spike_policy")]
        public string SpikePolicy(
            float frameMilliseconds = -1.0f,
            float waitForSimMilliseconds = -1.0f,
            float simulationMilliseconds = -1.0f,
            float majorPhaseMilliseconds = -1.0f,
            float relativeMultiplier = -1.0f,
            float cooldownSeconds = -1.0f,
            int maximumCaptures = -1,
            float preWindowSeconds = -1.0f,
            float postWindowSeconds = -1.0f)
        {
            if (!IsOptionalPositive(frameMilliseconds) ||
                !IsOptionalPositive(waitForSimMilliseconds) ||
                !IsOptionalPositive(simulationMilliseconds) ||
                !IsOptionalPositive(majorPhaseMilliseconds) ||
                !IsOptionalPositive(relativeMultiplier) ||
                !IsOptionalPositive(cooldownSeconds) ||
                !IsOptionalPositive(maximumCaptures) ||
                !IsOptionalPositive(preWindowSeconds) ||
                !IsOptionalPositive(postWindowSeconds))
            {
                return "Runtime profiler rejected: policy values must be positive or omitted.";
            }

            m_spikePolicy = new RuntimeSpikePolicy(
                frameMilliseconds > 0 ? frameMilliseconds : m_spikePolicy.FrameThresholdMilliseconds,
                waitForSimMilliseconds > 0 ? waitForSimMilliseconds : m_spikePolicy.WaitForSimThresholdMilliseconds,
                simulationMilliseconds > 0 ? simulationMilliseconds : m_spikePolicy.SimulationThresholdMilliseconds,
                majorPhaseMilliseconds > 0 ? majorPhaseMilliseconds : m_spikePolicy.MajorPhaseThresholdMilliseconds,
                relativeMultiplier > 0 ? relativeMultiplier : m_spikePolicy.RelativeMultiplier,
                cooldownSeconds > 0 ? cooldownSeconds : m_spikePolicy.CooldownSeconds,
                maximumCaptures > 0 ? maximumCaptures : m_spikePolicy.MaximumAutomaticCaptures,
                preWindowSeconds > 0 ? preWindowSeconds : m_spikePolicy.PreWindowSeconds,
                postWindowSeconds > 0 ? postWindowSeconds : m_spikePolicy.PostWindowSeconds,
                m_spikePolicy.AutomaticDeepCapture);
            return "Runtime profiler spike policy: " + m_spikePolicy.Format();
        }

        [ConsoleCommand(
            documentation: "Enables or disables automatic deep capture when a broad spike trigger fires.",
            customCommandName: "tajs_profiler_auto_deep")]
        public string AutoDeep(bool enabled = true)
        {
            m_spikePolicy = new RuntimeSpikePolicy(
                m_spikePolicy.FrameThresholdMilliseconds,
                m_spikePolicy.WaitForSimThresholdMilliseconds,
                m_spikePolicy.SimulationThresholdMilliseconds,
                m_spikePolicy.MajorPhaseThresholdMilliseconds,
                m_spikePolicy.RelativeMultiplier,
                m_spikePolicy.CooldownSeconds,
                m_spikePolicy.MaximumAutomaticCaptures,
                m_spikePolicy.PreWindowSeconds,
                m_spikePolicy.PostWindowSeconds,
                enabled);
            return "Runtime profiler automatic deep capture: " + enabled + "; policy=" + m_spikePolicy.Format();
        }

        private void CaptureFrame(GameTime _)
        {
            try
            {
                EnsureRunnerTimingAccess();
                long capturedTimestamp = Stopwatch.GetTimestamp();
                GameLoopTimingRanges ranges = default;
                GameLoopTimingSnapshot timings = new GameLoopTimingSnapshot();
                bool hasTimingSample = m_timings is not null && m_timings.ReadCompleted(out timings, out ranges);
                if (!hasTimingSample)
                {
                    timings = new GameLoopTimingSnapshot();
                    ranges = default;
                }
                GameRunnerTimingSnapshot runner = m_runner is null
                    ? GameRunnerTimingSnapshot.Unavailable
                    : m_runner.Read();
                if (!hasTimingSample && (m_runner is null || !runner.IsAvailable))
                {
                    return;
                }
                RuntimeFrameSample sample = m_history.RecordSample(
                    capturedTimestamp,
                    timings,
                    runner,
                    m_simLoop.SimSpeedMult,
                    m_simLoop.SimStepsPerUpdate,
                    m_simLoop.BudgetedSimSteps,
                    m_simLoop.IsSimPaused,
                    ranges,
                    m_counters.Read(capturedTimestamp, DeepCallbackRecorder.IsActive),
                    DumpSearchDiagnosticsService.ReadTimelineCounters());
                if (!sample.SimPaused)
                {
                    m_frameBaseline.Add(sample.FrameTicks);
                    if ((++m_baselineSampleCounter & 15) == 0)
                    {
                        m_rollingMedianFrameTicks = m_frameBaseline.Get(0.5);
                    }
                }
                RuntimeSpikeRecord? completed = m_spikes.Observe(sample, m_rollingMedianFrameTicks, m_spikePolicy);
                if (m_spikes.CaptureStarted && m_spikes.ActiveAutomaticDeepCapture &&
                    m_deepPatchSummary.IsAvailable && !DeepCallbackRecorder.IsActive)
                {
                    DeepCaptureWindow automatic = DeepCallbackRecorder.Start(
                        Math.Max(1.0, m_spikePolicy.PostWindowSeconds),
                        true,
                        capturedTimestamp);
                    if (automatic.WasActive)
                    {
                        m_traceWindowStart = automatic.StartTimestamp;
                        m_traceWindowEnd = automatic.EndTimestamp;
                    }
                }
                if (completed.HasValue)
                {
                    RuntimeSpikeRecord capture = completed.Value;
                    m_traceWindowStart = capture.StartTimestamp;
                    m_traceWindowEnd = capture.EndTimestamp;
                    DeepCallbackRecorder.AddMarker(
                        capturedTimestamp,
                        "spike #" + capture.Sequence.ToString(CultureInfo.InvariantCulture) + ": " + capture.Reason);
                }
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
            m_settings.Changed -= OnSettingChanged;
            DeepCaptureWindow window = DeepCallbackRecorder.Stop();
            if (window.WasActive)
            {
                m_traceWindowStart = window.StartTimestamp;
                m_traceWindowEnd = window.EndTimestamp;
            }
            // The history is resolver-scoped and intentionally retained until the service is
            // released so the final gameplay interval can still be inspected by a console caller.
        }

        private void OnSettingChanged(object? sender, SettingChangedEventArgs args)
        {
            if (string.Equals(args.Descriptor.ModId, ProfilerSettingsCatalog.ModId, StringComparison.Ordinal))
            {
                try
                {
                    m_spikePolicy = ProfilerSettingsCatalog.ReadPolicy(m_settings);
                }
                catch (Exception exception)
                {
                    m_log?.Exception(exception, "Profiler setting change was rejected; previous policy remains active.");
                }
            }
        }

        private static bool IsValidSeconds(float seconds) =>
            !float.IsNaN(seconds) && !float.IsInfinity(seconds) && seconds >= MinimumSeconds && seconds <= MaximumSeconds;

        private string StartDeepCapture(float seconds, bool automatic)
        {
            if (!IsValidSeconds(seconds) || seconds > 30.0f)
            {
                return "Runtime profiler rejected: deep capture seconds must be finite and between 1 and 30.";
            }
            if (!m_deepPatchSummary.IsAvailable)
            {
                return "Runtime profiler deep capture unavailable: validated callback patch surface is inactive.";
            }
            DeepCaptureWindow window = DeepCallbackRecorder.Start(seconds, automatic);
            if (!window.WasActive)
            {
                return "Runtime profiler deep capture unavailable: callback patch surface is inactive.";
            }
            m_traceWindowStart = window.StartTimestamp;
            m_traceWindowEnd = window.EndTimestamp;
            return "Runtime profiler deep capture started: window=" +
                FormatSeconds(window.EndTimestamp - window.StartTimestamp) +
                ", automatic=" + automatic + ".";
        }

        private static bool IsOptionalPositive(float value) =>
            value < 0 || (!float.IsNaN(value) && !float.IsInfinity(value) && value > 0);

        private static string FormatSeconds(long stopwatchTicks) =>
            (stopwatchTicks / (double)Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " s";

        private static string SanitizeFileName(string name)
        {
            string value = string.IsNullOrWhiteSpace(name) ? "runtime" : name.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                builder.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i]);
            }
            return builder.Length == 0 ? "runtime" : builder.ToString();
        }

        private static string FormatMetric(RuntimeMetricSummary metric) =>
            "p50 " + FormatMilliseconds(metric.P50Ticks) +
            "  p95 " + FormatMilliseconds(metric.P95Ticks) +
            "  p99 " + FormatMilliseconds(metric.P99Ticks) +
            "  max " + FormatMilliseconds(metric.MaxTicks) +
            "  total " + FormatMilliseconds(metric.TotalTicks);

        private static string FormatClassificationCounts(RuntimeFrameSummary summary) =>
            "main/render=" + summary.MainRenderBoundCount.ToString(CultureInfo.InvariantCulture) +
            ", sim-bound=" + summary.SimulationBoundCount.ToString(CultureInfo.InvariantCulture) +
            ", sim-pressure=" + summary.SimulationPressureCount.ToString(CultureInfo.InvariantCulture) +
            ", wait=" + summary.WaitingForSimulationCount.ToString(CultureInfo.InvariantCulture) +
            ", gc=" + summary.GcRelatedCount.ToString(CultureInfo.InvariantCulture) +
            ", gpu=" + summary.LikelyGpuBoundCount.ToString(CultureInfo.InvariantCulture) +
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
                RuntimeFrameClassification.SimulationPressure => "simulation pressure",
                RuntimeFrameClassification.WaitingForSimulation => "waiting for simulation",
                RuntimeFrameClassification.GcRelated => "GC-related",
                RuntimeFrameClassification.LikelyGpuBound => "likely GPU bound",
                RuntimeFrameClassification.Mixed => "mixed",
                _ => "unknown",
            };

        private static string FormatMilliseconds(long stopwatchTicks) =>
            stopwatchTicks < 0
                ? "unavailable"
                : (stopwatchTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms";
    }
}
