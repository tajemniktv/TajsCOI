// Taj's COI Mods | RuntimeFrameHistoryTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mafi.Logging;
using TajsCOI.Profiler.Core;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class RuntimeFrameHistoryTests
    {
        [Fact]
        public void HistoryWrapsWithoutGrowingAndPreservesChronologicalSamples()
        {
            var history = new RuntimeFrameHistory(3);
            for (int index = 1; index <= 5; index++)
            {
                history.Record(
                    index,
                    new GameLoopTimingSnapshot(renderTicks: index),
                    new GameRunnerTimingSnapshot(updateTicks: index),
                    simSpeedMult: 1,
                    simStepsPerUpdate: 1,
                    budgetedSimSteps: 1,
                    simPaused: false);
            }

            Assert.Equal(3, history.Count);
            Assert.Equal(new long[] { 3, 4, 5 }, history.SnapshotRecent(10).Select(x => x.Sequence));
        }

        [Fact]
        public void SummaryCalculatesPercentilesOnDemand()
        {
            var history = new RuntimeFrameHistory(8);
            for (int index = 1; index <= 8; index++)
            {
                history.Record(
                    index,
                    new GameLoopTimingSnapshot(renderTicks: index),
                    new GameRunnerTimingSnapshot(updateTicks: index),
                    simSpeedMult: 1,
                    simStepsPerUpdate: 1,
                    budgetedSimSteps: 1,
                    simPaused: false);
            }

            RuntimeFrameSummary summary = history.SummarizeRecent(100);

            Assert.Equal(8, summary.Count);
            Assert.Equal(4, summary.Frame.P50Ticks);
            Assert.Equal(8, summary.Frame.P95Ticks);
            Assert.Equal(8, summary.Frame.P99Ticks);
            Assert.Equal(36, summary.Frame.TotalTicks);
            Assert.Equal(8, summary.Latest.Sequence);
        }

        [Fact]
        public void SummaryExcludesPausedSamplesFromGameplayStatistics()
        {
            var history = new RuntimeFrameHistory(8);
            history.Record(
                1,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: 10),
                1,
                1,
                1,
                false);
            history.Record(
                2,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: 1434),
                1,
                1,
                1,
                true);
            history.Record(
                3,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: 20),
                1,
                1,
                1,
                false);

            RuntimeFrameSummary summary = history.SummarizeRecent(100);

            Assert.Equal(2, summary.Count);
            Assert.Equal(30, summary.Frame.TotalTicks);
            Assert.Equal(20, summary.Frame.MaxTicks);
            Assert.Equal(1, summary.PausedSampleCount);
            Assert.Equal(3, summary.Latest.Sequence);
        }

        [Fact]
        public void SpikeRankingExcludesPausedSamples()
        {
            var history = new RuntimeFrameHistory(8);
            history.Record(1, new GameLoopTimingSnapshot(), new GameRunnerTimingSnapshot(updateTicks: 10), 1, 1, 1, false);
            history.Record(2, new GameLoopTimingSnapshot(), new GameRunnerTimingSnapshot(updateTicks: 1434), 1, 1, 1, true);

            RuntimeFrameSample[] spikes = history.FindSpikes(100, 1);

            RuntimeFrameSample spike = Assert.Single(spikes);
            Assert.Equal(1, spike.Sequence);
            Assert.False(spike.SimPaused);
        }

        [Fact]
        public void SummaryRetainsGcCollectionsAcrossTheIntervalWhenLatestSampleIsZero()
        {
            var history = new RuntimeFrameHistory(8);
            history.RecordSample(
                1,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: System.Diagnostics.Stopwatch.Frequency / 10),
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
                    1,
                    0,
                    0,
                    0,
                    0,
                    0,
                    -1,
                    false,
                    0));
            history.RecordSample(
                2,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: 10),
                1,
                1,
                1,
                false,
                counters: RuntimeCounterSnapshot.Unavailable(2));

            RuntimeFrameSummary summary = history.SummarizeRecent(100);

            Assert.Equal(0, summary.Latest.Counters.TotalGcDelta);
            Assert.Equal(1, summary.GcDeltaTotal);
            Assert.Equal(1, summary.GcPeakDelta);
            Assert.Equal(1, summary.GcRelatedCount);
        }

        [Fact]
        public void ClassificationDistinguishesWaitingSimulationAndMainRender()
        {
            var waiting = new RuntimeFrameSample(
                1,
                1,
                new GameLoopTimingSnapshot(
                    renderTicks: 10,
                    waitForSimTicks: 60),
                new GameRunnerTimingSnapshot(updateTicks: 80, wasOvertime: true, overtimeTicks: 60),
                1,
                1,
                1,
                false);
            var main = new RuntimeFrameSample(
                2,
                2,
                new GameLoopTimingSnapshot(renderTicks: 60),
                new GameRunnerTimingSnapshot(updateTicks: 60),
                1,
                1,
                1,
                false);
            var sim = new RuntimeFrameSample(
                3,
                3,
                new GameLoopTimingSnapshot(simUpdateTicks: 60),
                new GameRunnerTimingSnapshot(updateTicks: 60, simTicks: 60),
                15,
                15,
                15,
                false);

            Assert.Equal(RuntimeFrameClassification.WaitingForSimulation, waiting.Classification);
            Assert.Equal(RuntimeFrameClassification.MainRenderBound, main.Classification);
            Assert.Equal(RuntimeFrameClassification.SimulationPressure, sim.Classification);
        }

        [Fact]
        public void DegradedRunnerUsesRingTimingsAndPreservesUnavailableSentinels()
        {
            var sample = new RuntimeFrameSample(
                1,
                1,
                new GameLoopTimingSnapshot(renderTicks: 10),
                GameRunnerTimingSnapshot.Unavailable,
                1,
                1,
                1,
                false);

            Assert.Equal(-1, sample.Runner.UpdateTicks);
            Assert.Equal(10, sample.FrameTicks);
            Assert.Equal(10, sample.RenderTicks);
        }

        [Fact]
        public void PartialRunnerFallsBackToAvailableComponentsWhenRingsAreUnavailable()
        {
            var sample = new RuntimeFrameSample(
                1,
                1,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(inputTicks: 1, syncTicks: 1, renderTicks: 3, simTicks: 2),
                1,
                1,
                1,
                false);

            Assert.Equal(7, sample.FrameTicks);
            Assert.Equal(3, sample.RenderTicks);
            Assert.Equal(2, sample.SimTicks);
        }

        [Fact]
        public void SummarySaturatesTotalTicksAtLongMaximum()
        {
            var history = new RuntimeFrameHistory(2);
            history.Record(
                1,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: long.MaxValue),
                1,
                1,
                1,
                false);
            history.Record(
                2,
                new GameLoopTimingSnapshot(),
                new GameRunnerTimingSnapshot(updateTicks: long.MaxValue),
                1,
                1,
                1,
                false);

            Assert.Equal(long.MaxValue, history.SummarizeRecent(10).Frame.TotalTicks);
        }

        [Fact]
        public void RingCursorConsumesEachCompletedWindowOnceAndReportsOverrun()
        {
            var cursor = new GameLoopTimingRingCursor(4);

            GameLoopTimingRingReadWindow first = cursor.Advance(1);
            Assert.Equal(1, first.Count);
            Assert.Equal(0, first.StartLogicalIndex);
            Assert.Equal(0, first.DroppedEntries);
            Assert.Equal(0, cursor.Advance(1).Count);

            GameLoopTimingRingReadWindow slowerProducer = cursor.Advance(4);
            Assert.Equal(3, slowerProducer.Count);
            Assert.Equal(1, slowerProducer.StartLogicalIndex);

            GameLoopTimingRingReadWindow fasterProducer = cursor.Advance(10);
            Assert.Equal(4, fasterProducer.Count);
            Assert.Equal(6, fasterProducer.StartLogicalIndex);
            Assert.Equal(2, fasterProducer.DroppedEntries);
            Assert.Equal(0, cursor.Advance(10).Count);
        }

        [Fact]
        public void RingCursorStartsAtThePreviousSafeEntryWhenReaderIsCreatedLate()
        {
            var cursor = new GameLoopTimingRingCursor(2048);

            GameLoopTimingRingReadWindow window = cursor.Advance(37);

            Assert.Equal(1, window.Count);
            Assert.Equal(36, window.StartLogicalIndex);
            Assert.Equal(0, window.DroppedEntries);
        }

        [Fact]
        public void RingCursorClampsProducerBurstsBeyondRetentionCapacity()
        {
            var cursor = new GameLoopTimingRingCursor(2048);
            cursor.Advance(1);

            GameLoopTimingRingReadWindow window = cursor.Advance(10000);

            Assert.Equal(2048, window.Count);
            Assert.Equal(7951, window.DroppedEntries);
            Assert.Equal(7952, window.StartLogicalIndex);
        }

        [Fact]
        public void RingCursorHandlesLogicalWriteIndexIntegerWraparound()
        {
            var cursor = new GameLoopTimingRingCursor(4);
            cursor.Advance(int.MaxValue - 1);

            GameLoopTimingRingReadWindow beforeWrap = cursor.Advance(int.MaxValue);
            GameLoopTimingRingReadWindow afterWrap = cursor.Advance(int.MinValue);

            Assert.Equal(1, beforeWrap.Count);
            Assert.Equal(int.MaxValue - 1, beforeWrap.StartLogicalIndex);
            Assert.Equal(1, afterWrap.Count);
            Assert.Equal(int.MaxValue, afterWrap.StartLogicalIndex);
            Assert.Equal(0, afterWrap.DroppedEntries);
        }

        [Fact]
        public async Task RingCursorCanPollAWriteIndexWhileProducerAdvances()
        {
            int writeIndex = 0;
            var cursor = new GameLoopTimingRingCursor(4);
            Task producer = Task.Run(() =>
            {
                for (int index = 1; index <= 10000; index++)
                {
                    // ReSharper disable once AccessToModifiedClosure
                    Volatile.Write(ref writeIndex, index);
                }
            });

            while (!producer.IsCompleted)
            {
                cursor.Advance(Volatile.Read(ref writeIndex));
            }
            await producer;
            GameLoopTimingRingReadWindow final = cursor.Advance(Volatile.Read(ref writeIndex));

            Assert.InRange(final.Count, 0, 4);
            Assert.True(final.DroppedEntries >= 0);
        }

        [Fact]
        public void RawSnapshotRetainsAllMajorPhasesWithoutResolverObjects()
        {
            var history = new RuntimeFrameHistory(2);
            history.Record(
                10,
                new GameLoopTimingSnapshot(
                    inputTicks: 1,
                    syncTicks: 2,
                    renderTicks: 3,
                    waitForSimTicks: 4,
                    simUpdateTicks: 5),
                new GameRunnerTimingSnapshot(updateTicks: 15),
                simSpeedMult: 20,
                simStepsPerUpdate: 20,
                budgetedSimSteps: 20,
                simPaused: false);

            RuntimeFrameSample sample = history.SnapshotRecent(1).Single();

            Assert.Equal(1, sample.Timings.InputTicks);
            Assert.Equal(2, sample.Timings.SyncTicks);
            Assert.Equal(3, sample.Timings.RenderTicks);
            Assert.Equal(4, sample.Timings.WaitForSimTicks);
            Assert.Equal(5, sample.Timings.SimUpdateTicks);
            Assert.Equal(20, sample.SimSpeedMult);
            Assert.False(sample.SimPaused);
        }

        [Fact]
        public void CurrentGameLoopTimingsShapeBuildsValidatedZeroAllocationReader()
        {
            bool available = GameLoopTimingsAccess.TryCreate(out GameLoopTimingsAccess? access, out string reason);

            Assert.True(available, reason);
            Assert.NotNull(access);
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            Assert.Equal(2048, access!.BufferSize);
            Assert.True(access.IsAvailable);

            Type timingsType = typeof(Mafi.Core.GameLoop.IGameLoopEvents).Assembly
                .GetType("Mafi.Core.GameLoop.GameLoopTimings", throwOnError: true)!;
            Type eventType = timingsType.GetNestedType("Event", BindingFlags.Public | BindingFlags.NonPublic)!;
            MethodInfo begin = timingsType.GetMethod("Begin", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            MethodInfo end = timingsType.GetMethod("End", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            object renderEvent = Enum.ToObject(eventType, 5);
            end.Invoke(null, new[] { begin.Invoke(null, new[] { renderEvent }) });
            end.Invoke(null, new[] { begin.Invoke(null, new[] { renderEvent }) });

            Assert.True(access.ReadLatest().RenderTicks > 0);
        }

        [Fact]
        public void RunnerTimingAccessorCompilesGettersOnceAndConvertsBroadDurations()
        {
            var fake = new FakeGameRunner
            {
                LatestUpdateDuration = TimeSpan.FromMilliseconds(12),
                LatestInputUpdateDuration = TimeSpan.FromMilliseconds(1),
                LatestSyncDuration = TimeSpan.FromMilliseconds(2),
                LatestRenderUpdateDuration = TimeSpan.FromMilliseconds(3),
                LatestSimUpdateDuration = TimeSpan.FromMilliseconds(4),
                LatestSimUpdateWasOvertime = true,
                LatestSimUpdateOvertimeDuration = TimeSpan.FromMilliseconds(5),
                RunSimulationInBackgroundThread = true,
                SimUpdateCount = 6,
                SimStepsSinceLoad = 7,
            };

            var access = GameRunnerTimingAccess.TryCreate(fake, out string reason);

            Assert.NotNull(access);
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            Assert.True(access!.IsAvailable, reason);
            GameRunnerTimingSnapshot snapshot = access.Read();
            Assert.True(snapshot.UpdateTicks > 0);
            Assert.True(snapshot.InputTicks > 0);
            Assert.True(snapshot.RenderTicks > 0);
            Assert.True(snapshot.SimTicks > 0);
            Assert.True(snapshot.WasOvertime);
            Assert.True(snapshot.OvertimeTicks > 0);
            Assert.True(snapshot.RunSimulationInBackgroundThread);
            Assert.Equal(6, snapshot.SimUpdateCount);
            Assert.Equal(7, snapshot.SimStepsSinceLoad);
        }

        [Fact]
        public void DeferredRunnerTimingAccessDoesNotDiscoverDuringConstruction()
        {
            var fake = new FakeGameRunner { LatestUpdateDuration = TimeSpan.FromMilliseconds(12) };
            var deferred = new DeferredGameRunnerTimingAccess(new Mafi.LazyResolve<IGameIdProvider>(fake));

            Assert.False(deferred.IsDiscoveryAttempted);

            GameRunnerTimingAccess? access = deferred.TryGet(out string reason);

            Assert.True(deferred.IsDiscoveryAttempted);
            Assert.NotNull(access);
            // ReSharper disable once RedundantSuppressNullableWarningExpression
            Assert.True(access!.IsAvailable, reason);
        }

        [Fact]
        public void DeferredRunnerTimingAccessFailsOpenWhenOptionalRunnerIsUnavailable()
        {
            var deferred = new DeferredGameRunnerTimingAccess(
                new Mafi.LazyResolve<IGameIdProvider>(Mafi.DependencyResolver.CreateEmpty()));

            GameRunnerTimingAccess? access = deferred.TryGet(out string reason);

            Assert.Null(access);
            Assert.Contains("GameRunner discovery failed", reason);
        }

        // Reflection/DynamicMethod discovery intentionally requires this complete property surface.
        // ReSharper disable UnusedAutoPropertyAccessor.Global
        // ReSharper disable UnusedAutoPropertyAccessor.Local
        private sealed class FakeGameRunner : IGameIdProvider
        {
            public long GameId { get; set; }
            public long SessionId { get; set; }
            public DateTime GameStartedAtUtc { get; set; }
            public string GameStartedAtVersion { get; set; } = "test";
            public TimeSpan LatestUpdateDuration { get; set; }
            public TimeSpan LatestInputUpdateDuration { get; set; }
            public TimeSpan LatestSyncDuration { get; set; }
            public TimeSpan LatestRenderUpdateDuration { get; set; }
            public TimeSpan LatestSimUpdateDuration { get; set; }
            public bool LatestSimUpdateWasOvertime { get; set; }
            public TimeSpan LatestSimUpdateOvertimeDuration { get; set; }
            public bool RunSimulationInBackgroundThread { get; set; }
            public int SimUpdateCount { get; set; }
            public int SimStepsSinceLoad { get; set; }
        }
        // ReSharper restore UnusedAutoPropertyAccessor.Local
        // ReSharper restore UnusedAutoPropertyAccessor.Global
    }
}
