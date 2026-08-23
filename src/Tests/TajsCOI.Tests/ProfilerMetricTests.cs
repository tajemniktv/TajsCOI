// Taj's COI Mods | ProfilerMetricTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

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
        public void StageMetricDifferenceUsesIntervalCountsAndTotals()
        {
            var first = new StageMetric(2, 100, 70, 50, 1, 1, 0);
            var second = new StageMetric(5, 260, 90, -10, 4, 2, 1);

            StageMetric delta = second - first;
            Assert.Equal(3, delta.Count);
            Assert.Equal(160, delta.TotalTicks);
            Assert.Equal(90, delta.MaxTicks);
            Assert.Equal(-60, delta.ManagedBytesDelta);
            Assert.Equal(3, delta.Gen0Collections);
            Assert.Equal(1, delta.Gen1Collections);
            Assert.Equal(1, delta.Gen2Collections);
        }

        [Fact]
        public void RequiredRuntimeProbeBindingsResolveAgainstConfiguredGameAssemblies()
        {
            MethodInfo install = typeof(RuntimePerformanceDiagnosticsService).GetMethod(
                "InstallPatches",
                BindingFlags.Static | BindingFlags.NonPublic)!;

            object summary = install.Invoke(null, null)!;
            int expected = (int)summary.GetType().GetProperty("RequiredExpected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(summary)!;
            int installed = (int)summary.GetType().GetProperty("RequiredInstalled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(summary)!;

            try
            {
                Assert.Equal(expected, installed);

                SaveLoadFileUtils.ValidateChecksum(
                    "this-file-deliberately-does-not-exist.save",
                    out _,
                    out Mafi.Option<System.Exception> _);

                FieldInfo stagesField = typeof(RuntimePerformanceDiagnosticsService).GetField(
                    "s_stages",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                var stages = (System.Collections.Generic.Dictionary<string, StageAccumulator>)stagesField.GetValue(null)!;
                Assert.True(stages[RuntimePerformanceDiagnosticsService.ChecksumValidation].Snapshot().Count >= 1);

            }
            finally
            {
                new Harmony("TajsCOI.Profiler.RuntimePerformance").UnpatchAll("TajsCOI.Profiler.RuntimePerformance");
            }
        }
    }
}
