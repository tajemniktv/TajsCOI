// Taj's COI Mods | RuntimeProfilerPhaseTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using TajsCOI.Common.Settings;
using TajsCOI.Profiler;
using TajsCOI.Profiler.Core;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class RuntimeProfilerPhaseTests
    {
        [Fact]
        public void ProfilerSettingsCatalogExposesImmediateBoundedCaptureControls()
        {
            Assert.Equal(11, ProfilerSettingsCatalog.All.Count);
            Assert.Equal(11, ProfilerSettingsCatalog.All.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(
                ProfilerSettingsCatalog.All,
                descriptor =>
                {
                    Assert.Equal(ProfilerSettingsCatalog.ModId, descriptor.ModId);
                    Assert.Equal("Profiler", descriptor.Category);
                    Assert.Equal(SettingApplyMode.Immediate, descriptor.ApplyMode);
                });
        }

        [Fact]
        public void SharedTelemetryPublishesAtomicDeltasAndSparseEvents()
        {
            RuntimeTelemetryCounter counter = RuntimeTelemetry.RegisterCounter(
                "tests.telemetry." + Guid.NewGuid().ToString("N"),
                RuntimeTelemetryUnit.Count,
                "TajsCOI.Tests.Telemetry");
            RuntimeTelemetryEvent telemetryEvent = RuntimeTelemetry.RegisterEvent(
                "tests.event." + Guid.NewGuid().ToString("N"));
            RuntimeTelemetry.Reset(counter);
            RuntimeTelemetry.Capture();

            Parallel.For(0, 4, _ => RuntimeTelemetry.Add(counter, 25));
            RuntimeTelemetrySnapshot snapshot = RuntimeTelemetry.Capture();

            Assert.Equal(100, snapshot.Get(counter));
            Assert.Equal(0, RuntimeTelemetry.Capture().Get(counter));
            Assert.Equal("TajsCOI.Tests.Telemetry", RuntimeTelemetry.CounterOwner(counter.Index));

            RuntimeTelemetry.Publish(telemetryEvent, 1234, RuntimeTracePhase.SimUpdate);
            RuntimeTelemetryEventSnapshot[] events = RuntimeTelemetry.SnapshotEvents();
            RuntimeTelemetryEventSnapshot observed = events.Last(x => x.Name.StartsWith("tests.event.", StringComparison.Ordinal));
            Assert.Equal(1234, observed.Timestamp);
            Assert.Equal(RuntimeTracePhase.SimUpdate, observed.PhaseId);
        }

        [Fact]
        public void SharedTelemetryIsRepresentedInTheUnifiedTrace()
        {
            string counterName = "tests.trace.counter." + Guid.NewGuid().ToString("N");
            string eventName = "tests.trace.event." + Guid.NewGuid().ToString("N");
            RuntimeTelemetryCounter counter = RuntimeTelemetry.RegisterCounter(counterName);
            RuntimeTelemetryEvent telemetryEvent = RuntimeTelemetry.RegisterEvent(eventName);
            RuntimeTelemetry.Reset(counter);
            RuntimeTelemetry.Capture();
            RuntimeTelemetry.Add(counter, 7);
            RuntimeTelemetrySnapshot telemetry = RuntimeTelemetry.Capture();
            RuntimeTelemetry.Publish(telemetryEvent, 1500, RuntimeTracePhase.Render);

            string path = Path.Combine(Path.GetTempPath(), "tajs-telemetry-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var frame = new RuntimeFrameSample(
                    1,
                    1000,
                    new GameLoopTimingSnapshot(),
                    new GameRunnerTimingSnapshot(updateTicks: 1000),
                    1,
                    1,
                    1,
                    false,
                    counters: new RuntimeCounterSnapshot(
                        true,
                        1000,
                        1,
                        -1,
                        -1,
                        -1,
                        -1,
                        -1,
                        -1,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        -1,
                        false,
                        0,
                        mainThreadTicks: 2,
                        renderThreadTicks: 3,
                        drawCalls: 4,
                        batches: 5,
                        triangles: 6,
                        vertices: 7,
                        gcAllocatedBytes: 8),
                    telemetry: telemetry);
                RuntimeTraceExporter.Export(
                    path,
                    new[] { frame },
                    Array.Empty<RuntimeTraceSpan>(),
                    Array.Empty<CallbackMetadataSnapshot>(),
                    Array.Empty<RuntimeTraceMarker>(),
                    RuntimeTelemetry.SnapshotEvents());

                string json = File.ReadAllText(path);
                Assert.Contains(counterName, json);
                Assert.Contains("TajsProfiler", json);
                Assert.Contains(eventName, json);
                Assert.Contains("coi.telemetry", json);
                Assert.Contains("mainThreadMs", json);
                Assert.Contains("drawCalls", json);
                Assert.Contains("gcAllocatedBytes", json);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void CsvExportIncludesBroadTimingColumnsAndLeavesUnavailableCountersBlank()
        {
            var frame = new RuntimeFrameSample(
                1,
                1000,
                new GameLoopTimingSnapshot(renderTicks: 20),
                new GameRunnerTimingSnapshot(updateTicks: 30),
                1,
                1,
                1,
                false);
            string path = Path.Combine(Path.GetTempPath(), "tajs-runtime-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                RuntimeTraceExportResult result = RuntimeTraceExporter.ExportCsv(path, new[] { frame });

                Assert.Equal(1, result.EventCount);
                string[] lines = File.ReadAllText(path).TrimEnd('\r', '\n').Split('\n');
                Assert.Equal(2, lines.Length);
                string[] header = lines[0].Split(',');
                string[] row = lines[1].Split(',');
                Assert.Equal(header.Length, row.Length);
                Assert.Contains("phase_RENDER_ms", header);
                Assert.Contains("classification", header);
                Assert.Contains("MainRenderBound", row);
                Assert.Equal(string.Empty, row[Array.IndexOf(header, "managedHeapBytes")]);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void RollingPercentileRetainsBoundedValuesAndRanksTail()
        {
            var percentile = new RuntimeRollingPercentile(3);
            percentile.Add(10);
            percentile.Add(30);
            percentile.Add(20);
            percentile.Add(40);

            Assert.Equal(3, percentile.Count);
            Assert.Equal(30, percentile.Get(0.5));
            Assert.Equal(40, percentile.Get(0.95));
        }

        [Fact]
        public void SpikePolicySupportsAbsoluteRelativeAndOvertimeTriggers()
        {
            var absolutePolicy = new RuntimeSpikePolicy(
                frameMilliseconds: 50,
                waitForSimMilliseconds: 20,
                simulationMilliseconds: 50,
                majorPhaseMilliseconds: 50,
                relativeMultiplier: 3);
            RuntimeFrameSample absolute = Sample(1, 1, StopwatchTicks(80), overtime: false);
            Assert.True(RuntimeSpikeHistory.TryGetTrigger(absolute, 0, absolutePolicy, out string absoluteReason));
            Assert.Equal("frame/update threshold", absoluteReason);

            var relativePolicy = new RuntimeSpikePolicy(
                frameMilliseconds: 1000,
                waitForSimMilliseconds: 1000,
                simulationMilliseconds: 1000,
                majorPhaseMilliseconds: 1000,
                relativeMultiplier: 3);
            RuntimeFrameSample relative = Sample(2, 2, StopwatchTicks(40), overtime: false);
            Assert.True(RuntimeSpikeHistory.TryGetTrigger(relative, StopwatchTicks(10), relativePolicy, out string relativeReason));
            Assert.Equal("relative frame threshold", relativeReason);

            RuntimeFrameSample overtime = Sample(3, 3, StopwatchTicks(5), overtime: true);
            Assert.True(RuntimeSpikeHistory.TryGetTrigger(overtime, 0, relativePolicy, out string overtimeReason));
            Assert.Equal("simulation overtime", overtimeReason);

            RuntimeFrameSample paused = Sample(4, 4, StopwatchTicks(500), overtime: false, simPaused: true);
            Assert.False(RuntimeSpikeHistory.TryGetTrigger(paused, 0, absolutePolicy, out string pausedReason));
            Assert.Equal(string.Empty, pausedReason);
        }

        [Fact]
        public async Task PhaseContextKeepsOverlappingDispatchesOnTheirOwningThreads()
        {
            object simulationEvent = new();
            object renderEvent = new();
            RuntimeTracePhaseContext.RegisterEvent(simulationEvent, RuntimeTracePhase.SimUpdate);
            RuntimeTracePhaseContext.RegisterEvent(renderEvent, RuntimeTracePhase.Render);

            using var start = new ManualResetEventSlim(false);
            int simulationPhase = RuntimeTracePhase.Unknown;
            int renderPhase = RuntimeTracePhase.Unknown;
            int simulationRestored = RuntimeTracePhase.Unknown;
            int renderRestored = RuntimeTracePhase.Unknown;

            Task simulation = Task.Run(() =>
            {
                start.Wait();
                RuntimeTracePhaseContext.PhaseScope scope =
                    RuntimeTracePhaseContext.Enter(simulationEvent, RuntimeTracePhase.Unknown);
                try
                {
                    Action callback = () =>
                    {
                        simulationPhase = RuntimeTracePhaseContext.CurrentPhase;
                        Thread.Sleep(10);
                    };
                    callback();
                }
                finally
                {
                    scope.Dispose();
                    simulationRestored = RuntimeTracePhaseContext.CurrentPhase;
                }
            });
            Task render = Task.Run(() =>
            {
                start.Wait();
                RuntimeTracePhaseContext.PhaseScope scope =
                    RuntimeTracePhaseContext.Enter(renderEvent, RuntimeTracePhase.Unknown);
                try
                {
                    Action callback = () =>
                    {
                        renderPhase = RuntimeTracePhaseContext.CurrentPhase;
                        Thread.Sleep(10);
                    };
                    callback();
                }
                finally
                {
                    scope.Dispose();
                    renderRestored = RuntimeTracePhaseContext.CurrentPhase;
                }
            });

            start.Set();
            await Task.WhenAll(simulation, render);

            Assert.Equal(RuntimeTracePhase.SimUpdate, simulationPhase);
            Assert.Equal(RuntimeTracePhase.Render, renderPhase);
            Assert.Equal(RuntimeTracePhase.Unknown, simulationRestored);
            Assert.Equal(RuntimeTracePhase.Unknown, renderRestored);
        }

        [Fact]
        public void ConflictingEventRegistrationIsReportedOnceAndRemainsUnknown()
        {
            object eventSource = new();
            int conflictsBefore = RuntimeTracePhaseContext.PhaseConflictCount;

            RuntimeTracePhaseContext.RegisterEvent(eventSource, RuntimeTracePhase.SimUpdate);
            RuntimeTracePhaseContext.RegisterEvent(eventSource, RuntimeTracePhase.Render);
            RuntimeTracePhaseContext.RegisterEvent(eventSource, RuntimeTracePhase.Render);
            RuntimeTracePhaseContext.RegisterEvent(eventSource, RuntimeTracePhase.SimUpdate);

            Assert.Equal(conflictsBefore + 1, RuntimeTracePhaseContext.PhaseConflictCount);
            Assert.Equal(
                RuntimeTracePhase.Unknown,
                RuntimeTracePhaseContext.Resolve(eventSource, RuntimeTracePhase.Unknown));
        }

        [Fact]
        public void DeepOverheadBenchmarkComparesCallbackModesWithoutLeavingCaptureActive()
        {
            DeepCallbackOverheadBenchmark benchmark = DeepCallbackRecorder.MeasureOverhead(1000);

            Assert.True(benchmark.Available);
            Assert.Equal(1000, benchmark.Iterations);
            Assert.True(benchmark.BaselineTicks > 0);
            Assert.True(benchmark.DisabledTicks > 0);
            Assert.True(benchmark.EnabledTicks > 0);
            Assert.True(benchmark.MetadataAndSpanTicks > 0);
            Assert.False(DeepCallbackRecorder.IsActive);
        }

        [Fact]
        public void DeepCallbackStatisticsReportShareTailAndWorstInvocation()
        {
            var spans = new List<RuntimeTraceSpan>();
            for (int index = 1; index <= 100; index++)
            {
                long start = 1000 + index * 10;
                spans.Add(
                    new RuntimeTraceSpan(
                        start,
                        start + index,
                        1,
                        RuntimeTracePhase.SimUpdate,
                        11,
                        index,
                        0));
            }

            long slowTicks = System.Diagnostics.Stopwatch.Frequency * 2 / 1000;
            spans.Add(new RuntimeTraceSpan(3000, 3000 + slowTicks, 2, RuntimeTracePhase.Render, 12, 101, 0));
            CallbackMetadataSnapshot[] metadata = { new(1, "SimulationOwner", "Update", "Tests"), new(2, "RenderOwner", "Render", "Tests") };

            CallbackMetricSnapshot[] metrics = DeepCallbackRecorder.AggregateCallbackMetrics(spans, metadata, 64);
            CallbackMetricSnapshot simulation = Assert.Single(metrics, x => x.Metadata.Id == 1);
            CallbackMetricSnapshot render = Assert.Single(metrics, x => x.Metadata.Id == 2);

            Assert.Equal(100, simulation.CallCount);
            Assert.Equal(5050, simulation.TotalTicks);
            Assert.Equal(95, simulation.P95Ticks);
            Assert.Equal(99, simulation.P99Ticks);
            Assert.Equal(100, simulation.MaxTicks);
            Assert.Equal(2000, simulation.WorstStartTimestamp);
            Assert.Equal(0, simulation.SlowCallCount);
            Assert.Equal(5050 * 100.0 / (5050 + slowTicks), simulation.SharePercent, 6);
            Assert.Equal(1, render.SlowCallCount);

            CallbackInvocationSnapshot worst = Assert.Single(
                DeepCallbackRecorder.RankWorstCallbackInvocations(spans, metadata, 1));
            Assert.Equal(2, worst.Metadata.Id);
            Assert.Equal(RuntimeTracePhase.Render, worst.PhaseId);
            Assert.Equal(slowTicks, worst.DurationTicks);
            Assert.Equal(3000, worst.StartTimestamp);
        }

        [Fact]
        public void SpikeHistoryCapturesPreAndPostWindowAndHonorsMaximum()
        {
            var history = new RuntimeFrameHistory(16);
            var spikes = new RuntimeSpikeHistory(history);
            var policy = new RuntimeSpikePolicy(
                frameMilliseconds: 50,
                waitForSimMilliseconds: 1000,
                simulationMilliseconds: 1000,
                majorPhaseMilliseconds: 1000,
                cooldownSeconds: 0,
                maximumAutomaticCaptures: 1,
                preWindowSeconds: 2,
                postWindowSeconds: 1);
            long frequency = System.Diagnostics.Stopwatch.Frequency;
            history.RecordSample(0, new GameLoopTimingSnapshot(), new GameRunnerTimingSnapshot(updateTicks: StopwatchTicks(10)), 1, 1, 1, false);
            history.RecordSample(frequency, new GameLoopTimingSnapshot(), new GameRunnerTimingSnapshot(updateTicks: StopwatchTicks(10)), 1, 1, 1, false);
            RuntimeFrameSample trigger = history.RecordSample(
                frequency * 2,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: StopwatchTicks(80)),
                1,
                1,
                1,
                false);

            spikes.Observe(trigger, 0, policy);
            Assert.True(spikes.CaptureStarted);
            Assert.True(spikes.IsCaptureActive);

            RuntimeFrameSample after = history.RecordSample(
                frequency * 3 + 1,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: StopwatchTicks(10)),
                1,
                1,
                1,
                false);
            RuntimeSpikeRecord? completed = spikes.Observe(after, 0, policy);

            Assert.True(completed.HasValue);
            Assert.Equal(4, completed!.Value.Samples.Length);
            Assert.Equal(1, spikes.Count);

            RuntimeFrameSample secondTrigger = history.RecordSample(
                frequency * 4,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: StopwatchTicks(80)),
                1,
                1,
                1,
                false);
            spikes.Observe(secondTrigger, 0, policy);
            Assert.False(spikes.IsCaptureActive);
            Assert.Equal(1, spikes.Count);
        }

        [Fact]
        public void ClassificationUsesTrustedGpuAndGcSignalsWithoutInventingUnsupportedTelemetry()
        {
            var gc = new RuntimeFrameSample(
                1,
                1,
                new GameLoopTimingSnapshot(renderTicks: StopwatchTicks(60)),
                new GameRunnerTimingSnapshot(updateTicks: StopwatchTicks(60)),
                1,
                1,
                1,
                false,
                counters: new RuntimeCounterSnapshot(
                    true,
                    1,
                    100,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    0,
                    0,
                    1,
                    0,
                    0,
                    0,
                    -1,
                    false,
                    0));
            var gpu = new RuntimeFrameSample(
                2,
                2,
                new GameLoopTimingSnapshot(renderTicks: StopwatchTicks(20)),
                new GameRunnerTimingSnapshot(updateTicks: StopwatchTicks(20)),
                1,
                1,
                1,
                false,
                counters: new RuntimeCounterSnapshot(
                    true,
                    2,
                    100,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    StopwatchTicks(60),
                    true,
                    0));
            RuntimeCounterSnapshot unavailable = RuntimeCounterSnapshot.Unavailable(3);

            Assert.Equal(RuntimeFrameClassification.GcRelated, gc.Classification);
            Assert.Equal(RuntimeFrameClassification.LikelyGpuBound, gpu.Classification);
            Assert.False(unavailable.Available);
            Assert.False(unavailable.HasGpuTelemetry);
            Assert.Equal(-1, unavailable.ManagedHeapBytes);
        }

        [Fact]
        public void ChromeTraceExporterWritesValidEscapedJsonAndUnavailableValues()
        {
            string path = Path.Combine(Path.GetTempPath(), "tajs-profiler-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var ranges = new GameLoopTimingRanges(
                    new GameLoopTimingRange(1000, 2000),
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default,
                    default);
                var frame = new RuntimeFrameSample(
                    1,
                    1000,
                    new GameLoopTimingSnapshot(),
                    new GameRunnerTimingSnapshot(updateTicks: 1000),
                    1,
                    1,
                    1,
                    false,
                    ranges);
                var metadata = new CallbackMetadataSnapshot(1, "Owner", "Callback", "Assembly");
                var span = new RuntimeTraceSpan(1000, 1500, 1, RuntimeTracePhase.Render, 1, 1, 0);
                var marker = new RuntimeTraceMarker(1500, "quote \" slash \\ line\n", 1);

                RuntimeTraceExportResult result = RuntimeTraceExporter.Export(
                    path,
                    new[] { frame },
                    new[] { span },
                    new[] { metadata },
                    new[] { marker });
                string json = File.ReadAllText(result.Path);
                object parsed = new JavaScriptSerializer().DeserializeObject(json)!;

                Assert.NotNull(parsed);
                Assert.True(result.EventCount >= 5);
                Assert.Contains("unavailable", json);
                Assert.Contains("quote \\\" slash", json);
                Assert.Contains("monoUsedBytes", json);
                Assert.Contains("unityUnusedReservedBytes", json);
                Assert.Contains("unityGraphicsBytes", json);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void ManualDeepStopUsesActualStopTimestamp()
        {
            DeepCaptureWindow started = DeepCallbackRecorder.Start(1, automatic: false, timestamp: 100);
            Assert.True(started.WasActive);

            DeepCaptureWindow stopped = DeepCallbackRecorder.Stop(timestamp: 110);

            Assert.True(stopped.WasActive);
            Assert.Equal(100, stopped.StartTimestamp);
            Assert.Equal(110, stopped.EndTimestamp);
        }

        private static RuntimeFrameSample Sample(
            long sequence,
            long timestamp,
            long frameTicks,
            bool overtime,
            bool simPaused = false)
        {
            return new RuntimeFrameSample(
                sequence,
                timestamp,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: frameTicks, wasOvertime: overtime),
                1,
                1,
                1,
                simPaused);
        }

        private static long StopwatchTicks(double milliseconds) =>
            (long)Math.Round(milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000.0);
    }
}
