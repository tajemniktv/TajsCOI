// Taj's COI Mods | RuntimeProfilerPhaseTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
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
            Assert.Equal(10, ProfilerSettingsCatalog.All.Count);
            Assert.Equal(10, ProfilerSettingsCatalog.All.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(ProfilerSettingsCatalog.All, descriptor =>
            {
                Assert.Equal(ProfilerSettingsCatalog.ModId, descriptor.ModId);
                Assert.Equal("Profiler", descriptor.Category);
                Assert.Equal(SettingApplyMode.Immediate, descriptor.ApplyMode);
            });
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
            RuntimeSpikePolicy absolutePolicy = new RuntimeSpikePolicy(
                frameMilliseconds: 50,
                waitForSimMilliseconds: 20,
                simulationMilliseconds: 50,
                majorPhaseMilliseconds: 50,
                relativeMultiplier: 3);
            RuntimeFrameSample absolute = Sample(1, 1, StopwatchTicks(80), overtime: false);
            Assert.True(RuntimeSpikeHistory.TryGetTrigger(absolute, 0, absolutePolicy, out string absoluteReason));
            Assert.Equal("frame/update threshold", absoluteReason);

            RuntimeSpikePolicy relativePolicy = new RuntimeSpikePolicy(
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
        }

        [Fact]
        public void SpikeHistoryCapturesPreAndPostWindowAndHonorsMaximum()
        {
            var history = new RuntimeFrameHistory(16);
            var spikes = new RuntimeSpikeHistory(history);
            RuntimeSpikePolicy policy = new RuntimeSpikePolicy(
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
            RuntimeFrameSample gc = new RuntimeFrameSample(
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
            RuntimeFrameSample gpu = new RuntimeFrameSample(
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

        private static RuntimeFrameSample Sample(long sequence, long timestamp, long frameTicks, bool overtime)
        {
            return new RuntimeFrameSample(
                sequence,
                timestamp,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: frameTicks, wasOvertime: overtime),
                1,
                1,
                1,
                false);
        }

        private static long StopwatchTicks(double milliseconds) =>
            (long)Math.Round(milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000.0);
    }
}
