// Taj's COI Mods | ProfilerMetricTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi.Core.SaveGame;
using TajsCOI.Profiler.Core;
using TajsCOI.Profiler.Probes.Runtime;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class ProfilerMetricTests
    {
        [Fact]
        public void ResourceVisualizationTelemetryUsesStableBoundedCounters()
        {
            Assert.Equal(
                "resource-visualization.eager-build-skipped",
                RuntimeTelemetry.CounterName(RuntimePerformanceDiagnosticsService.ResourceVisualizationSkippedEagerBuildCounter.Index));
            Assert.Equal(
                "resource-visualization.first-activation-duration",
                RuntimeTelemetry.CounterName(RuntimePerformanceDiagnosticsService.ResourceVisualizationFirstActivationDurationCounter.Index));
            Assert.Equal(
                RuntimeTelemetryUnit.StopwatchTicks,
                RuntimeTelemetry.CounterUnit(RuntimePerformanceDiagnosticsService.ResourceVisualizationFirstActivationDurationCounter.Index));
            Assert.Equal(
                "resource-visualization.initialization-fallback",
                RuntimeTelemetry.CounterName(RuntimePerformanceDiagnosticsService.ResourceVisualizationInitializationFallbackCounter.Index));
        }

        [Fact]
        public void StageAccumulatorTracksCountTotalsWorstAndMemory()
        {
            var accumulator = new StageAccumulator();
            accumulator.Record(10, 100, 1, 0, 0);
            accumulator.Record(30, -20, 2, 1, 1);

            StageMetric metric = accumulator.Snapshot();
            Assert.Equal(2, metric.Count);
            Assert.Equal(40, metric.TotalTicks);
            Assert.Equal(30, metric.MaxTicks);
            Assert.Equal(80, metric.ManagedBytesDelta);
            Assert.Equal(3, metric.Gen0Collections);
            Assert.Equal(1, metric.Gen1Collections);
            Assert.Equal(1, metric.Gen2Collections);
        }

        [Fact]
        public void StageMetricDifferenceRequiresExplicitIntervalMaximum()
        {
            var first = new StageMetric(2, 100, 70, 50, 1, 1, 0);
            var second = new StageMetric(5, 260, 90, -10, 4, 2, 1);

            StageMetric delta = StageMetric.Difference(second, first, intervalMaxTicks: 40);
            Assert.Equal(3, delta.Count);
            Assert.Equal(160, delta.TotalTicks);
            Assert.Equal(40, delta.MaxTicks);
            Assert.Equal(-60, delta.ManagedBytesDelta);
            Assert.Equal(3, delta.Gen0Collections);
            Assert.Equal(1, delta.Gen1Collections);
            Assert.Equal(1, delta.Gen2Collections);
        }

        [Fact]
        public void StageAccumulatorCheckpointsResetOnlyTheIntervalMaximum()
        {
            var accumulator = new StageAccumulator();
            accumulator.Record(90, 10);
            StageMetric first = accumulator.SnapshotAndResetIntervalMax();
            accumulator.Record(20, 5);
            StageMetric second = accumulator.SnapshotAndResetIntervalMax();

            Assert.Equal(90, first.MaxTicks);
            Assert.Equal(20, second.MaxTicks);
            StageMetric delta = StageMetric.Difference(second, first, second.MaxTicks);
            Assert.Equal(1, delta.Count);
            Assert.Equal(20, delta.TotalTicks);
            Assert.Equal(20, delta.MaxTicks);
        }

        [Fact]
        public void RequiredRuntimeProbeBindingsResolveAgainstConfiguredGameAssemblies()
        {
            MethodInfo install = typeof(RuntimePerformanceDiagnosticsService).GetMethod(
                "InstallPatches",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            StageAccumulator? checksumStage = null;
            try
            {
                object summary = install.Invoke(null, null)!;
                int expected = (int)summary.GetType().GetProperty("RequiredExpected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(summary)!;
                int installed = (int)summary.GetType().GetProperty("RequiredInstalled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(summary)!;
                Assert.Equal(expected, installed);

                SaveLoadFileUtils.ValidateChecksum(
                    "this-file-deliberately-does-not-exist.save",
                    out _,
                    out Mafi.Option<Exception> _);

                FieldInfo stagesField = typeof(RuntimePerformanceDiagnosticsService).GetField(
                    "s_stages",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                var stages = (Dictionary<string, StageAccumulator>)stagesField.GetValue(null)!;
                checksumStage = stages[RuntimePerformanceDiagnosticsService.FileChecksumValidation];
                Assert.True(checksumStage.Snapshot().Count >= 1);
            }
            finally
            {
                checksumStage?.Reset();
                new Harmony("TajsCOI.Profiler.RuntimePerformance").UnpatchAll("TajsCOI.Profiler.RuntimePerformance");
            }
        }

        [Fact]
        public void SceneGcTranspilerReplacesOnlyTheTwoFrameworkCalls()
        {
            MethodInfo collect = typeof(GC).GetMethod(
                nameof(GC.Collect),
                new[] { typeof(int), typeof(GCCollectionMode), typeof(bool), typeof(bool) })!;
            MethodInfo wait = typeof(GC).GetMethod(nameof(GC.WaitForPendingFinalizers), Type.EmptyTypes)!;
            MethodInfo transpiler = typeof(RuntimePerformanceDiagnosticsService).GetMethod(
                "InstrumentGcPasses",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var input = new List<CodeInstruction>
            {
                new(System.Reflection.Emit.OpCodes.Nop), new(System.Reflection.Emit.OpCodes.Call, collect), new(System.Reflection.Emit.OpCodes.Call, wait),
            };

            List<CodeInstruction> output = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { input })!).ToList();

            Assert.Equal(3, output.Count);
            Assert.Equal("CollectGarbageMeasured", ((MethodInfo)output[1].operand).Name);
            Assert.Equal("WaitForPendingFinalizersMeasured", ((MethodInfo)output[2].operand).Name);
        }

        [Fact]
        public void ProductSlotTelemetrySeparatesLiveHighWaterAndCapacity()
        {
            var metric = new ProductRendererMetric(
                true,
                instances: 0,
                gpuInstances: 0,
                liveSlots: 600,
                highWaterSlots: 1_000,
                capacitySlots: 2_048,
                fragmentedSlots: 400,
                freeRangeCount: 3,
                largestFreeRange: 250,
                liveBufferUsed: 120,
                liveBufferCapacity: 256,
                reserveBufferUsed: 60,
                reserveBufferCapacity: 128,
                instancesBytes: 3_072,
                staticOwnersBytes: 1_024,
                dynamicOwnersBytes: 2_048,
                slotsBytes: 65_536,
                texturesBytes: 1_048_576,
                reason: "test");

            Assert.Equal(1_448, metric.TotalFreeSlots);
            Assert.Equal(1_048, metric.UnusedCapacitySlots);
            Assert.Equal(600 * 100.0 / 2_048, metric.Utilization, 8);
            Assert.Equal(1_120_256, metric.GpuBytes);
            string graphics = RuntimePerformanceDiagnosticsService.FormatUnityGraphicsBytes(0, metric);
            Assert.StartsWith("unavailable (driver counter", graphics);
            Assert.Contains("ProductsRenderer estimate=1.07 MiB", graphics);
        }

        [Fact]
        public void RuntimeCounterSnapshotKeepsProfilerRecorderContextDistinctFromGraphicsMemory()
        {
            var snapshot = new RuntimeCounterSnapshot(
                true,
                1,
                100,
                200,
                300,
                50,
                -1,
                120,
                140,
                0,
                0,
                0,
                10,
                20,
                0,
                500,
                true,
                0,
                mainThreadTicks: 600,
                renderThreadTicks: 700,
                drawCalls: 80,
                batches: 90,
                triangles: 1000,
                vertices: 2000,
                gcAllocatedBytes: 4096);

            Assert.Equal(-1, snapshot.UnityGraphicsBytes);
            Assert.True(snapshot.HasGpuTelemetry);
            Assert.Equal(600, snapshot.MainThreadTicks);
            Assert.Equal(700, snapshot.RenderThreadTicks);
            Assert.Equal(80, snapshot.DrawCalls);
            Assert.Equal(4096, snapshot.GcAllocatedBytes);
        }

        [Fact]
        public void RuntimeCounterSamplerReportsCounterSourcesAndDoesNotInventGpuTiming()
        {
            using var sampler = new RuntimeCounterSampler(intervalSeconds: 0.05);

            RuntimeCounterSnapshot snapshot = sampler.Read(Stopwatch.GetTimestamp(), force: true);

            Assert.Contains("unity:", sampler.SupportSummary);
            Assert.Contains("profiler:", sampler.SupportSummary);
            Assert.Contains("frame-timing-gpu=", sampler.SupportSummary);
            if (sampler.GpuTelemetryStatus.IndexOf("unavailable", StringComparison.Ordinal) >= 0)
            {
                Assert.False(snapshot.HasGpuTelemetry);
                Assert.Equal(-1, snapshot.GpuFrameTicks);
            }
            else
            {
                Assert.True(snapshot.GpuFrameTicks >= -1);
            }
        }

        [Fact]
        public void FrameTimingGpuConversionUsesStopwatchTicksAndRejectsUnavailableValues()
        {
            Assert.Equal(-1, RuntimeCounterSampler.FrameTimingGpuSampler.MillisecondsToStopwatchTicks(0));
            Assert.Equal(-1, RuntimeCounterSampler.FrameTimingGpuSampler.MillisecondsToStopwatchTicks(double.NaN));

            long expected = Stopwatch.Frequency / 60;
            long actual = RuntimeCounterSampler.FrameTimingGpuSampler.MillisecondsToStopwatchTicks(1000.0 / 60.0);
            Assert.InRange(actual, expected - 1, expected + 1);
        }

        [Fact]
        public void RuntimeCounterSamplerUpdatesBoundedSamplingIntervalWithoutAHotPathLock()
        {
            using var sampler = new RuntimeCounterSampler(intervalSeconds: 0.05);
            Assert.Equal(50.0, sampler.IntervalMilliseconds, 1);

            sampler.UpdateIntervalSeconds(10.0);
            Assert.Equal(2_000.0, sampler.IntervalMilliseconds, 1);

            sampler.UpdateIntervalSeconds(0.25);
            Assert.Equal(250.0, sampler.IntervalMilliseconds, 1);
        }

        [Fact]
        public void RuntimeStageLabelsDescribeChecksumAndIteratorScope()
        {
            Assert.Equal("file.checksum-validation", RuntimePerformanceDiagnosticsService.FileChecksumValidation);
            Assert.EndsWith("-slice", RuntimePerformanceDiagnosticsService.LoadFinalize);
            Assert.EndsWith("-slice", RuntimePerformanceDiagnosticsService.LoadResolverFinalization);
        }

        [Theory]
        [InlineData(-1, -1)]
        [InlineData(0, -1)]
        [InlineData(42, 42)]
        public void ProcessMemoryMetricsTreatNonPositiveValuesAsUnavailable(long value, long expected) => Assert.Equal(
            expected,
            RuntimePerformanceDiagnosticsService.NormalizeProcessMemoryMetric(value));

        [Fact]
        public void HarmonyAuditMethodFormattingIncludesParameterSignatures()
        {
            MethodInfo method = typeof(ProfilerMetricTests).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(x => x.Name == nameof(HarmonyAuditOverload) &&
                             x.GetParameters().Single().ParameterType == typeof(int));

            Assert.EndsWith("HarmonyAuditOverload(System.Int32)", RuntimePerformanceDiagnosticsService.FormatHarmonyMethod(method));
        }

        [Fact]
        public void GcPassComparisonSelectsOnlyCaptureInterval()
        {
            var passes = new List<GcPassMetric>
            {
                new(4, 10, 100, 90, 0, 0, 1), new(5, 20, 100, 80, 0, 0, 1), new(6, 30, 100, 70, 0, 0, 1), new(7, 40, 100, 60, 0, 0, 1),
            };

            IReadOnlyList<GcPassMetric> interval = RuntimePerformanceDiagnosticsService
                .SelectGcPassInterval(passes, afterSequence: 4, throughSequence: 6);

            Assert.Equal(new long[] { 5, 6 }, interval.Select(x => x.Sequence));
        }

        [Fact]
        public void GcPassMetricRetainsTeardownOrdinalAndFinalizerDrainDuration()
        {
            var pass = new GcPassMetric(
                sequence: 12,
                passNumber: 3,
                elapsedTicks: 1_000,
                finalizerDrainElapsedTicks: 250,
                beforeBytes: 10_000,
                afterBytes: 7_500,
                gen0: 2,
                gen1: 1,
                gen2: 1);

            Assert.Equal(3, pass.PassNumber);
            Assert.Equal(1_000, pass.ElapsedTicks);
            Assert.Equal(250, pass.FinalizerDrainElapsedTicks);
            Assert.Equal(2_500, pass.ReclaimedBytes);
            Assert.Equal(250 * 1000.0 / Stopwatch.Frequency, pass.FinalizerDrainElapsedMilliseconds);
            Assert.Equal(2, pass.Gen0Collections);
            Assert.Equal(1, pass.Gen1Collections);
            Assert.Equal(1, pass.Gen2Collections);
        }

        // ReSharper disable once UnusedParameter.Local
        private static void HarmonyAuditOverload(int value)
        {
        }

        // ReSharper disable once UnusedParameter.Local
        private static void HarmonyAuditOverload(string value)
        {
        }
    }
}
