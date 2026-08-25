// Taj's COI Mods | DeepCallbackRecorder.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Mafi;
using Mafi.Core.GameLoop;
using Mafi.Core.Simulation;

namespace TajsCOI.Profiler.Core
{
    internal static class RuntimeTracePhase
    {
        internal const int Unknown = 0;
        internal const int SyncStart = 1;
        internal const int Sync = 2;
        internal const int SyncEnd = 3;
        internal const int Input = 4;
        internal const int InputEnd = 5;
        internal const int RenderAfterSync = 6;
        internal const int Render = 7;
        internal const int RenderEnd = 8;
        internal const int SimSync = 9;
        internal const int SimCommands = 10;
        internal const int SimAfterSync = 11;
        internal const int SimStart = 12;
        internal const int SimParallelStart = 13;
        internal const int SimUpdate = 14;
        internal const int SimParallelEnd = 15;
        internal const int SimEnd = 16;
        internal const int SimReadState = 17;
        internal const int SimEndForUi = 18;
        internal const int SimPausedUi = 19;
        internal const int SimIdle = 20;

        internal static string Name(int phaseId)
        {
            switch (phaseId)
            {
                case SyncStart: return "SYNC_START";
                case Sync: return "SYNC";
                case SyncEnd: return "SYNC_END";
                case Input: return "INPUT";
                case InputEnd: return "INPUT_END";
                case RenderAfterSync: return "RENDER_AFTER_SYNC";
                case Render: return "RENDER";
                case RenderEnd: return "RENDER_END";
                case SimSync: return "SIM_SYNC";
                case SimCommands: return "SIM_CMD";
                case SimAfterSync: return "SIM_AFTER_SYNC";
                case SimStart: return "SIM_START";
                case SimParallelStart: return "SIM_PARALLEL_START";
                case SimUpdate: return "SIM_UPDATE";
                case SimParallelEnd: return "SIM_PARALLEL_END";
                case SimEnd: return "SIM_END";
                case SimReadState: return "SIM_READ_STATE";
                case SimEndForUi: return "SIM_END_FOR_UI";
                case SimPausedUi: return "SIM_PAUSED_UI";
                case SimIdle: return "SIM_IDLE";
                default: return "UNKNOWN";
            }
        }

        internal static bool IsSimulation(int phaseId) => phaseId >= SimSync;
    }

    /// <summary>
    ///     Tracks the phase belonging to the exact event instance being dispatched. COI reuses
    ///     the same concrete Event type for several loop phases, so a type-level phase map is not
    ///     sufficient. The current phase is thread-local and scopes nest safely when a callback
    ///     dispatch invokes another dispatch on the same thread.
    /// </summary>
    internal static class RuntimeTracePhaseContext
    {
        private const int Unassigned = int.MinValue;

        private sealed class EventPhase
        {
            internal int PhaseId = Unassigned;
            internal int Conflict;
        }

        private static readonly ConditionalWeakTable<object, EventPhase> s_eventPhases = new();
        private static int s_phaseConflictCount;

        [ThreadStatic]
        private static int s_currentPhase;

        internal static int CurrentPhase => s_currentPhase;
        internal static int PhaseConflictCount => Volatile.Read(ref s_phaseConflictCount);

        internal static void RegisterEvent(object eventSource, int phaseId)
        {
            EventPhase phase = s_eventPhases.GetValue(eventSource, _ => new EventPhase());
            while (true)
            {
                int current = Volatile.Read(ref phase.PhaseId);
                if (current == Unassigned)
                {
                    if (Interlocked.CompareExchange(ref phase.PhaseId, phaseId, Unassigned) == Unassigned)
                    {
                        return;
                    }
                    continue;
                }
                if (current == phaseId)
                {
                    return;
                }

                // The same event instance should not represent multiple dispatch phases. Keep
                // the mapping degraded rather than silently attributing callbacks incorrectly,
                // and count the transition once so compatibility output explains UNKNOWN.
                if (current == RuntimeTracePhase.Unknown)
                {
                    if (Interlocked.Exchange(ref phase.Conflict, 1) == 0)
                    {
                        Interlocked.Increment(ref s_phaseConflictCount);
                    }
                    return;
                }
                if (Interlocked.CompareExchange(
                        ref phase.PhaseId,
                        RuntimeTracePhase.Unknown,
                        current) == current)
                {
                    if (Interlocked.Exchange(ref phase.Conflict, 1) == 0)
                    {
                        Interlocked.Increment(ref s_phaseConflictCount);
                    }
                    return;
                }
            }
        }

        internal static int Resolve(object? eventSource, int fallbackPhase)
        {
            if (eventSource is not null && s_eventPhases.TryGetValue(eventSource, out EventPhase? mapped))
            {
                int mappedPhase = Volatile.Read(ref mapped.PhaseId);
                if (mappedPhase != Unassigned && mappedPhase != RuntimeTracePhase.Unknown)
                {
                    return mappedPhase;
                }
            }

            return fallbackPhase != RuntimeTracePhase.Unknown ? fallbackPhase : s_currentPhase;
        }

        internal static PhaseScope Enter(object? eventSource, int fallbackPhase)
        {
            int previous = s_currentPhase;
            s_currentPhase = Resolve(eventSource, fallbackPhase);
            return new PhaseScope(previous);
        }

        internal readonly struct PhaseScope : IDisposable
        {
            private readonly int m_previousPhase;

            internal PhaseScope(int previousPhase)
            {
                m_previousPhase = previousPhase;
            }

            public void Dispose()
            {
                s_currentPhase = m_previousPhase;
            }
        }
    }

    internal readonly struct DeepTracingPatchSummary
    {
        internal DeepTracingPatchSummary(
            int expectedMethods,
            int patchedMethods,
            int replacedInvocations,
            int failures,
            int phaseConflicts = 0)
        {
            ExpectedMethods = expectedMethods;
            PatchedMethods = patchedMethods;
            ReplacedInvocations = replacedInvocations;
            Failures = failures;
            PhaseConflicts = phaseConflicts;
        }

        internal int ExpectedMethods { get; }
        internal int PatchedMethods { get; }
        internal int ReplacedInvocations { get; }
        internal int Failures { get; }
        internal int PhaseConflicts { get; }
        internal bool IsAvailable => PatchedMethods > 0 && ReplacedInvocations > 0;
        internal bool IsComplete => ExpectedMethods > 0 && PatchedMethods == ExpectedMethods &&
            Failures == 0 && PhaseConflicts == 0;
    }

    internal readonly struct DeepCaptureWindow
    {
        internal DeepCaptureWindow(bool active, long startTimestamp, long endTimestamp, bool automatic)
        {
            WasActive = active;
            StartTimestamp = startTimestamp;
            EndTimestamp = endTimestamp;
            Automatic = automatic;
        }

        internal bool WasActive { get; }
        internal long StartTimestamp { get; }
        internal long EndTimestamp { get; }
        internal bool Automatic { get; }
    }

    internal readonly struct DeepCallbackOverheadSnapshot
    {
        internal DeepCallbackOverheadSnapshot(long callbackCount, long overheadTicks, long frameCount)
        {
            CallbackCount = callbackCount;
            OverheadTicks = overheadTicks;
            FrameCount = frameCount;
        }

        internal long CallbackCount { get; }
        internal long OverheadTicks { get; }
        internal long FrameCount { get; }
        internal long AverageOverheadPerFrameTicks => FrameCount <= 0 ? 0 : OverheadTicks / FrameCount;
    }

    internal readonly struct DeepCallbackOverheadBenchmark
    {
        internal DeepCallbackOverheadBenchmark(
            int iterations,
            long baselineTicks,
            long disabledTicks,
            long enabledTicks)
        {
            Available = true;
            Iterations = iterations;
            BaselineTicks = baselineTicks;
            DisabledTicks = disabledTicks;
            EnabledTicks = enabledTicks;
        }

        internal bool Available { get; }
        internal int Iterations { get; }
        internal long BaselineTicks { get; }
        internal long DisabledTicks { get; }
        internal long EnabledTicks { get; }
        internal long EnabledOverheadTicks => Math.Max(0, EnabledTicks - DisabledTicks);
    }

    internal readonly struct CallbackToken
    {
        internal CallbackToken(
            long startTimestamp,
            int callbackId,
            int phaseId,
            int threadId,
            long sequence,
            int generation)
        {
            StartTimestamp = startTimestamp;
            CallbackId = callbackId;
            PhaseId = phaseId;
            ThreadId = threadId;
            Sequence = sequence;
            Generation = generation;
        }

        internal long StartTimestamp { get; }
        internal int CallbackId { get; }
        internal int PhaseId { get; }
        internal int ThreadId { get; }
        internal long Sequence { get; }
        internal int Generation { get; }
        internal bool IsActive => StartTimestamp > 0 && CallbackId > 0;
    }

    /// <summary>
    ///     Owns the opt-in callback recorder and its process-scoped Harmony patches. The only
    ///     process-scoped references to scene objects are weak loop-context references used to
    ///     name the current phase; callback owners are converted to strings/IDs immediately.
    /// </summary>
    internal static class DeepCallbackRecorder
    {
        private const string HarmonyId = "TajsCOI.Profiler.DeepTracing";
        private const int SpanCapacityPerThread = 65536;
        private const int MarkerCapacity = 128;
        private const int MetadataCapacity = 4096;
        private const int MaximumTraceThreads = 64;
        private static readonly long SlowCallbackTicks = Stopwatch.Frequency * 2 / 1000;

        private static readonly object s_patchGate = new object();
        private static readonly object s_metadataGate = new object();
        private static readonly object s_markerGate = new object();
        private static readonly ConcurrentDictionary<int, TraceSpanRing> s_rings = new();
        private static readonly Dictionary<CallbackMetadataKey, CallbackMetadataSnapshot> s_metadataByKey = new();
        private static readonly List<CallbackMetadataSnapshot> s_metadata = new();
        private static readonly List<RuntimeTraceMarker> s_markers = new(MarkerCapacity);
        private static readonly ConditionalWeakTable<object, OwnerMetadata> s_ownerMetadata = new();
        private static readonly ConditionalWeakTable<Delegate, CallbackMetadataCache> s_delegateMetadata = new();
        [ThreadStatic]
        private static TraceSpanRing? s_threadRing;
        [ThreadStatic]
        private static int s_threadRingId;
        private static int s_metadataCount;
        private static readonly MethodInfo s_invokeAction = typeof(DeepCallbackRecorder).GetMethod(
            nameof(InvokeWithOwner), BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(object), typeof(Action), typeof(int), typeof(object) }, null)!;
        private static readonly MethodInfo s_invokeActionWithoutOwner = typeof(DeepCallbackRecorder).GetMethod(
            nameof(InvokeWithoutOwner), BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(Action), typeof(int), typeof(object) }, null)!;
        private static readonly MethodInfo s_invokeAction1 = GetGenericWrapper(nameof(InvokeWithOwner), 1);
        private static readonly MethodInfo s_invokeAction1WithoutOwner = GetGenericWrapper(nameof(InvokeWithoutOwner), 1);
        private static readonly MethodInfo s_invokeAction2 = GetGenericWrapper(nameof(InvokeWithOwner), 2);
        private static readonly MethodInfo s_invokeAction2WithoutOwner = GetGenericWrapper(nameof(InvokeWithoutOwner), 2);

        private static int s_patchAttempted;
        private static DeepTracingPatchSummary s_patchSummary;
        private static int s_active;
        private static int s_benchmarkMode;
        private static readonly TraceSpanRing s_benchmarkRing = new(SpanCapacityPerThread);
        private static long s_captureStartTimestamp;
        private static long s_captureEndTimestamp;
        private static int s_captureGeneration;
        private static bool s_captureAutomatic;
        private static long s_spanSequence;
        private static long s_deepCallbackCount;
        private static long s_deepOverheadTicks;
        private static long s_deepFrameCount;

        internal static DeepTracingPatchSummary Initialize(IGameLoopEvents gameLoop, SimLoopEvents simLoop)
        {
            if (Volatile.Read(ref s_patchAttempted) != 0)
            {
                return s_patchSummary;
            }

            lock (s_patchGate)
            {
                if (s_patchAttempted != 0)
                {
                    return s_patchSummary;
                }

                var eventTypes = new HashSet<Type>();
                AddEventType(eventTypes, gameLoop.SyncUpdateStart, RuntimeTracePhase.SyncStart);
                AddEventType(eventTypes, gameLoop.SyncUpdate, RuntimeTracePhase.Sync);
                AddEventType(eventTypes, gameLoop.SyncUpdateEnd, RuntimeTracePhase.SyncEnd);
                AddEventType(eventTypes, gameLoop.InputUpdate, RuntimeTracePhase.Input);
                AddEventType(eventTypes, gameLoop.InputUpdateEnd, RuntimeTracePhase.InputEnd);
                AddEventType(eventTypes, gameLoop.RenderUpdateAfterSync, RuntimeTracePhase.RenderAfterSync);
                AddEventType(eventTypes, gameLoop.RenderUpdate, RuntimeTracePhase.Render);
                AddEventType(eventTypes, gameLoop.RenderUpdateEnd, RuntimeTracePhase.RenderEnd);
                AddEventType(eventTypes, gameLoop.Terminate, RuntimeTracePhase.Unknown);
                AddEventType(eventTypes, simLoop.Sync, RuntimeTracePhase.SimSync);
                AddEventType(eventTypes, simLoop.UpdateBeforeCmdProc, RuntimeTracePhase.SimCommands);
                AddEventType(eventTypes, simLoop.UpdateAfterCmdProc, RuntimeTracePhase.SimCommands);
                AddEventType(eventTypes, simLoop.UpdateAfterSync, RuntimeTracePhase.SimAfterSync);
                AddEventType(eventTypes, simLoop.UpdateStart, RuntimeTracePhase.SimStart);
                AddEventType(eventTypes, simLoop.ParallelUpdateStart, RuntimeTracePhase.SimParallelStart);
                AddEventType(eventTypes, simLoop.Update, RuntimeTracePhase.SimUpdate);
                AddEventType(eventTypes, simLoop.ParallelUpdateEnd, RuntimeTracePhase.SimParallelEnd);
                AddEventType(eventTypes, simLoop.UpdateEnd, RuntimeTracePhase.SimEnd);
                AddEventType(eventTypes, simLoop.ReadGameStateFrequent, RuntimeTracePhase.SimReadState);
                AddEventType(eventTypes, simLoop.UpdateEndForUi, RuntimeTracePhase.SimEndForUi);
                AddEventType(eventTypes, simLoop.UpdateEndForUiEnd, RuntimeTracePhase.SimPausedUi);
                AddEventType(eventTypes, simLoop.IdleUpdate, RuntimeTracePhase.SimIdle);

                if (eventTypes.Count == 0)
                {
                    s_patchSummary = default;
                    Volatile.Write(ref s_patchAttempted, 1);
                    return s_patchSummary;
                }

                int patched = 0;
                int replacements = 0;
                int failures = 0;
                var harmony = new Harmony(HarmonyId);
                foreach (Type eventType in eventTypes)
                {
                    MethodInfo[] methods = eventType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(x => x.Name == "Invoke" || x.Name == "InvokeTraced")
                        .ToArray();
                    foreach (MethodInfo method in methods)
                    {
                        try
                        {
                            harmony.Patch(
                                method,
                                transpiler: new HarmonyMethod(typeof(DeepCallbackRecorder), nameof(TranspileCallbackInvocations)));
                            patched++;
                        }
                        catch
                        {
                            failures++;
                        }
                    }
                }

                replacements = Volatile.Read(ref s_transpiledInvocationCount);
                s_patchSummary = new DeepTracingPatchSummary(
                    eventTypes.Sum(x => x.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Count(y => y.Name == "Invoke" || y.Name == "InvokeTraced")),
                    patched,
                    replacements,
                    failures,
                    RuntimeTracePhaseContext.PhaseConflictCount);
                Volatile.Write(ref s_patchAttempted, 1);
                return s_patchSummary;
            }
        }

        private static int s_transpiledInvocationCount;

        internal static bool IsActive
        {
            get
            {
                if (Volatile.Read(ref s_active) == 0)
                {
                    return false;
                }
                if (Stopwatch.GetTimestamp() < Volatile.Read(ref s_captureEndTimestamp))
                {
                    return true;
                }
                Volatile.Write(ref s_active, 0);
                return false;
            }
        }

        internal static DeepCaptureWindow Start(double seconds, bool automatic, long timestamp = 0)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 1.0 || seconds > 30.0)
            {
                return default;
            }

            long now = timestamp > 0 ? timestamp : Stopwatch.GetTimestamp();
            long duration = (long)Math.Round(seconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);
            lock (s_patchGate)
            {
                s_captureGeneration++;
                s_captureStartTimestamp = now;
                s_captureEndTimestamp = now + Math.Max(1, duration);
                s_captureAutomatic = automatic;
                Volatile.Write(ref s_deepCallbackCount, 0);
                Volatile.Write(ref s_deepOverheadTicks, 0);
                Volatile.Write(ref s_deepFrameCount, 0);
                Volatile.Write(ref s_active, 1);
            }
            lock (s_markerGate)
            {
                s_markers.Clear();
            }
            AddMarker(now, automatic ? "automatic deep capture started" : "deep capture started");
            return new DeepCaptureWindow(true, now, now + Math.Max(1, duration), automatic);
        }

        internal static DeepCaptureWindow Stop(long timestamp = 0)
        {
            long now = timestamp > 0 ? timestamp : Stopwatch.GetTimestamp();
            bool wasActive = Interlocked.Exchange(ref s_active, 0) != 0;
            long start = Volatile.Read(ref s_captureStartTimestamp);
            long end = now;
            if (wasActive)
            {
                AddMarker(now, "deep capture stopped");
            }
            return new DeepCaptureWindow(wasActive, start, end, s_captureAutomatic);
        }

        internal static DeepCallbackOverheadSnapshot SnapshotOverhead() =>
            new(
                Volatile.Read(ref s_deepCallbackCount),
                Volatile.Read(ref s_deepOverheadTicks),
                Volatile.Read(ref s_deepFrameCount));

        internal static void RecordFrame()
        {
            if (Volatile.Read(ref s_active) != 0 && Volatile.Read(ref s_benchmarkMode) == 0)
            {
                Interlocked.Increment(ref s_deepFrameCount);
            }
        }

        internal static DeepCallbackOverheadBenchmark MeasureOverhead(int iterations)
        {
            if (iterations <= 0 || IsActive || Interlocked.CompareExchange(ref s_benchmarkMode, 1, 0) != 0)
            {
                return default;
            }

            Action callback = NoOpCallback;
            object eventSource = new object();
            RuntimeTracePhaseContext.RegisterEvent(eventSource, RuntimeTracePhase.SimUpdate);
            const int WarmupIterations = 256;
            for (int index = 0; index < WarmupIterations; index++)
            {
                callback();
                InvokeWithOwner(null, callback, RuntimeTracePhase.Unknown, eventSource);
            }

            long baselineStart = Stopwatch.GetTimestamp();
            for (int index = 0; index < iterations; index++)
            {
                callback();
            }
            long baselineTicks = Stopwatch.GetTimestamp() - baselineStart;

            long disabledStart = Stopwatch.GetTimestamp();
            for (int index = 0; index < iterations; index++)
            {
                InvokeWithOwner(null, callback, RuntimeTracePhase.Unknown, eventSource);
            }
            long disabledTicks = Stopwatch.GetTimestamp() - disabledStart;

            int previousActive = Volatile.Read(ref s_active);
            long previousStart = Volatile.Read(ref s_captureStartTimestamp);
            long previousEnd = Volatile.Read(ref s_captureEndTimestamp);
            long previousCallbackCount = Volatile.Read(ref s_deepCallbackCount);
            long previousOverheadTicks = Volatile.Read(ref s_deepOverheadTicks);
            long previousFrameCount = Volatile.Read(ref s_deepFrameCount);
            try
            {
                s_benchmarkRing.Reset(Volatile.Read(ref s_captureGeneration));
                Volatile.Write(ref s_captureStartTimestamp, Stopwatch.GetTimestamp());
                Volatile.Write(ref s_captureEndTimestamp, long.MaxValue);
                Volatile.Write(ref s_active, 1);

                // Warm metadata/ring paths before measuring the steady-state enabled wrapper.
                for (int index = 0; index < WarmupIterations; index++)
                {
                    InvokeWithOwner(null, callback, RuntimeTracePhase.Unknown, eventSource);
                }
                long enabledStart = Stopwatch.GetTimestamp();
                for (int index = 0; index < iterations; index++)
                {
                    InvokeWithOwner(null, callback, RuntimeTracePhase.Unknown, eventSource);
                }
                long enabledTicks = Stopwatch.GetTimestamp() - enabledStart;
                return new DeepCallbackOverheadBenchmark(iterations, baselineTicks, disabledTicks, enabledTicks);
            }
            finally
            {
                Volatile.Write(ref s_active, previousActive);
                Volatile.Write(ref s_captureStartTimestamp, previousStart);
                Volatile.Write(ref s_captureEndTimestamp, previousEnd);
                Volatile.Write(ref s_deepCallbackCount, previousCallbackCount);
                Volatile.Write(ref s_deepOverheadTicks, previousOverheadTicks);
                Volatile.Write(ref s_deepFrameCount, previousFrameCount);
                Volatile.Write(ref s_benchmarkMode, 0);
            }
        }

        private static void NoOpCallback()
        {
        }

        internal static void AddMarker(long timestamp, string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }
            lock (s_markerGate)
            {
                if (s_markers.Count == MarkerCapacity && s_markers.Count > 0)
                {
                    s_markers.RemoveAt(0);
                }
                s_markers.Add(new RuntimeTraceMarker(timestamp, label.Trim(), Thread.CurrentThread.ManagedThreadId));
            }
        }

        internal static RuntimeTraceMarker[] SnapshotMarkers()
        {
            lock (s_markerGate)
            {
                return s_markers.ToArray();
            }
        }

        internal static RuntimeTraceSpan[] SnapshotSpans()
        {
            int generation = Volatile.Read(ref s_captureGeneration);
            var spans = new List<RuntimeTraceSpan>();
            foreach (TraceSpanRing ring in s_rings.Values)
            {
                spans.AddRange(ring.Snapshot(generation));
            }
            return spans
                .OrderBy(x => x.StartTimestamp)
                .ThenBy(x => x.Sequence)
                .ToArray();
        }

        internal static CallbackMetadataSnapshot[] SnapshotMetadata()
        {
            lock (s_metadataGate)
            {
                return s_metadata.ToArray();
            }
        }

        internal static CallbackMetricSnapshot[] SnapshotCallbackMetrics(int count)
        {
            return AggregateCallbackMetrics(SnapshotSpans(), SnapshotMetadata(), count);
        }

        internal static CallbackMetricSnapshot[] AggregateCallbackMetrics(
            IReadOnlyList<RuntimeTraceSpan> spans,
            IReadOnlyList<CallbackMetadataSnapshot> metadata,
            int count)
        {
            var metrics = new Dictionary<CallbackMetricKey, CallbackMetricBuilder>();
            long capturedCallbackTicks = 0;
            for (int index = 0; index < spans.Count; index++)
            {
                RuntimeTraceSpan span = spans[index];
                long duration = span.DurationTicks;
                if (duration <= 0)
                {
                    continue;
                }
                capturedCallbackTicks = RuntimeTraceMath.SaturatingAdd(capturedCallbackTicks, duration);
                var key = new CallbackMetricKey(span.CallbackId, span.PhaseId);
                if (!metrics.TryGetValue(key, out CallbackMetricBuilder? builder))
                {
                    builder = new CallbackMetricBuilder();
                    metrics.Add(key, builder);
                }
                builder.Add(duration, span.StartTimestamp);
            }

            var byId = metadata.ToDictionary(x => x.Id);
            return metrics
                .Where(x => byId.ContainsKey(x.Key.CallbackId))
                .Select(x => x.Value.ToSnapshot(byId[x.Key.CallbackId], x.Key.PhaseId, capturedCallbackTicks))
                .OrderByDescending(x => x.TotalTicks)
                .ThenByDescending(x => x.MaxTicks)
                .Take(Math.Max(1, Math.Min(64, count)))
                .ToArray();
        }

        internal static CallbackInvocationSnapshot[] SnapshotWorstCallbackInvocations(int count)
        {
            return RankWorstCallbackInvocations(SnapshotSpans(), SnapshotMetadata(), count);
        }

        internal static CallbackInvocationSnapshot[] RankWorstCallbackInvocations(
            IReadOnlyList<RuntimeTraceSpan> spans,
            IReadOnlyList<CallbackMetadataSnapshot> metadata,
            int count)
        {
            var byId = metadata.ToDictionary(x => x.Id);
            var result = new List<CallbackInvocationSnapshot>();
            foreach (RuntimeTraceSpan span in spans
                .Where(x => x.DurationTicks > 0 && byId.ContainsKey(x.CallbackId))
                .OrderByDescending(x => x.DurationTicks)
                .ThenBy(x => x.StartTimestamp)
                .ThenBy(x => x.Sequence)
                .Take(Math.Max(1, Math.Min(64, count))))
            {
                result.Add(new CallbackInvocationSnapshot(
                    byId[span.CallbackId],
                    span.PhaseId,
                    span.DurationTicks,
                    span.StartTimestamp,
                    span.EndTimestamp,
                    span.ThreadId,
                    span.Sequence));
            }
            return result.ToArray();
        }

        internal static long MeasureReader(Func<GameLoopTimingSnapshot> reader, int iterations)
        {
            if (reader is null || iterations <= 0)
            {
                return 0;
            }
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                _ = reader();
            }
            return Stopwatch.GetTimestamp() - start;
        }

        private static void AddEventType(HashSet<Type> eventTypes, object? value, int phaseId)
        {
            if (value is not null)
            {
                Type type = value.GetType();
                eventTypes.Add(type);
                RuntimeTracePhaseContext.RegisterEvent(value, phaseId);
            }
        }

        private static IEnumerable<CodeInstruction> TranspileCallbackInvocations(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            // The same concrete Event type is used by multiple loop phases. Pass the actual
            // dispatcher instance through the generated call so the wrapper can resolve its
            // phase without consulting global simulation state.
            const int phaseId = RuntimeTracePhase.Unknown;
            List<CodeInstruction> source = instructions.ToList();
            var result = new List<CodeInstruction>(source.Count + 8);
            for (int index = 0; index < source.Count; index++)
            {
                CodeInstruction instruction = source[index];
                if (index >= 2 && instruction.opcode == OpCodes.Callvirt &&
                    instruction.operand is MethodInfo invoke &&
                    invoke.Name == "Invoke" && invoke.DeclaringType is Type delegateType &&
                    typeof(Delegate).IsAssignableFrom(delegateType) &&
                    TryGetCallbackField(source[index - 1], out FieldInfo? callbackField))
                {
                    bool hasOwner = TryGetOwnerField(source[index - 2], callbackField!, out FieldInfo? ownerField);
                    MethodInfo? wrapper = GetWrapper(delegateType, hasOwner);
                    if (wrapper is not null)
                    {
                        if (hasOwner)
                        {
                            // The value-load and callback-field load have already been emitted.
                            // Replace that pair so the wrapper receives (owner, callback) in the
                            // correct order while retaining any labels/exception blocks.
                            CodeInstruction valueLoad = result[result.Count - 2];
                            CodeInstruction callbackLoad = result[result.Count - 1];
                            result.RemoveRange(result.Count - 2, 2);
                            result.Add(new CodeInstruction(valueLoad));
                            result.Add(new CodeInstruction(OpCodes.Ldfld, ownerField));
                            result.Add(new CodeInstruction(valueLoad));
                            result.Add(new CodeInstruction(callbackLoad));
                        }
                        result.Add(new CodeInstruction(OpCodes.Ldc_I4, phaseId));
                        result.Add(new CodeInstruction(
                            __originalMethod.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0));
                        instruction = new CodeInstruction(OpCodes.Call, wrapper);
                        Interlocked.Increment(ref s_transpiledInvocationCount);
                    }
                }
                result.Add(instruction);
            }
            return result;
        }

        private static bool TryGetCallbackField(CodeInstruction instruction, out FieldInfo? field)
        {
            field = instruction.operand as FieldInfo;
            return instruction.opcode == OpCodes.Ldfld &&
                field is not null &&
                (field.Name == "Callback" || field.Name == "Action");
        }

        private static bool TryGetOwnerField(CodeInstruction valueLoad, FieldInfo callbackField, out FieldInfo? ownerField)
        {
            ownerField = callbackField.DeclaringType?.GetField(
                "Owner",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (ownerField is null || valueLoad.opcode == OpCodes.Nop)
            {
                ownerField = null;
                return false;
            }
            return valueLoad.opcode == OpCodes.Ldloc ||
                   valueLoad.opcode == OpCodes.Ldloc_0 ||
                   valueLoad.opcode == OpCodes.Ldloc_1 ||
                   valueLoad.opcode == OpCodes.Ldloc_2 ||
                   valueLoad.opcode == OpCodes.Ldloc_3 ||
                   valueLoad.opcode == OpCodes.Ldloc_S ||
                   valueLoad.opcode == OpCodes.Ldloca ||
                   valueLoad.opcode == OpCodes.Ldloca_S;
        }

        private static MethodInfo? GetWrapper(Type delegateType, bool hasOwner)
        {
            Type genericDefinition = delegateType.IsGenericType
                ? delegateType.GetGenericTypeDefinition()
                : delegateType;
            if (genericDefinition == typeof(Action))
            {
                return hasOwner ? s_invokeAction : s_invokeActionWithoutOwner;
            }
            if (genericDefinition == typeof(Action<>))
            {
                Type argument = delegateType.GetGenericArguments()[0];
                MethodInfo method = hasOwner ? s_invokeAction1 : s_invokeAction1WithoutOwner;
                return method.MakeGenericMethod(argument);
            }
            if (genericDefinition == typeof(Action<,>))
            {
                Type[] arguments = delegateType.GetGenericArguments();
                MethodInfo method = hasOwner ? s_invokeAction2 : s_invokeAction2WithoutOwner;
                return method.MakeGenericMethod(arguments);
            }
            return null;
        }

        private static MethodInfo GetGenericWrapper(string name, int genericArgumentCount)
        {
            return typeof(DeepCallbackRecorder).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(x => x.Name == name && x.IsGenericMethodDefinition &&
                             x.GetGenericArguments().Length == genericArgumentCount);
        }

        private static void InvokeWithOwner(object? owner, Action action, int phaseId, object? eventSource)
        {
            if (!IsActive)
            {
                action();
                return;
            }
            long wrapperStart = Stopwatch.GetTimestamp();
            RuntimeTracePhaseContext.PhaseScope phaseScope = RuntimeTracePhaseContext.Enter(eventSource, phaseId);
            CallbackToken token = default;
            long callbackStart = 0;
            long callbackEnd = 0;
            try
            {
                token = Begin(owner, action, RuntimeTracePhaseContext.CurrentPhase);
                callbackStart = Stopwatch.GetTimestamp();
                try { action(); }
                finally { callbackEnd = End(token); }
            }
            finally
            {
                phaseScope.Dispose();
                if (callbackStart > 0)
                {
                    RecordCallbackOverhead(
                        wrapperStart,
                        callbackStart,
                        callbackEnd,
                        Stopwatch.GetTimestamp(),
                        token.Generation);
                }
            }
        }

        private static void InvokeWithoutOwner(Action action, int phaseId, object? eventSource) =>
            InvokeWithOwner(action.Target, action, phaseId, eventSource);

        private static void InvokeWithOwner<T>(object? owner, Action<T> action, T arg, int phaseId, object? eventSource)
        {
            if (!IsActive)
            {
                action(arg);
                return;
            }
            long wrapperStart = Stopwatch.GetTimestamp();
            RuntimeTracePhaseContext.PhaseScope phaseScope = RuntimeTracePhaseContext.Enter(eventSource, phaseId);
            CallbackToken token = default;
            long callbackStart = 0;
            long callbackEnd = 0;
            try
            {
                token = Begin(owner, action, RuntimeTracePhaseContext.CurrentPhase);
                callbackStart = Stopwatch.GetTimestamp();
                try { action(arg); }
                finally { callbackEnd = End(token); }
            }
            finally
            {
                phaseScope.Dispose();
                if (callbackStart > 0)
                {
                    RecordCallbackOverhead(
                        wrapperStart,
                        callbackStart,
                        callbackEnd,
                        Stopwatch.GetTimestamp(),
                        token.Generation);
                }
            }
        }

        private static void InvokeWithoutOwner<T>(Action<T> action, T arg, int phaseId, object? eventSource) =>
            InvokeWithOwner(action.Target, action, arg, phaseId, eventSource);

        private static void InvokeWithOwner<T1, T2>(
            object? owner,
            Action<T1, T2> action,
            T1 arg1,
            T2 arg2,
            int phaseId,
            object? eventSource)
        {
            if (!IsActive)
            {
                action(arg1, arg2);
                return;
            }
            long wrapperStart = Stopwatch.GetTimestamp();
            RuntimeTracePhaseContext.PhaseScope phaseScope = RuntimeTracePhaseContext.Enter(eventSource, phaseId);
            CallbackToken token = default;
            long callbackStart = 0;
            long callbackEnd = 0;
            try
            {
                token = Begin(owner, action, RuntimeTracePhaseContext.CurrentPhase);
                callbackStart = Stopwatch.GetTimestamp();
                try { action(arg1, arg2); }
                finally { callbackEnd = End(token); }
            }
            finally
            {
                phaseScope.Dispose();
                if (callbackStart > 0)
                {
                    RecordCallbackOverhead(
                        wrapperStart,
                        callbackStart,
                        callbackEnd,
                        Stopwatch.GetTimestamp(),
                        token.Generation);
                }
            }
        }

        private static void InvokeWithoutOwner<T1, T2>(Action<T1, T2> action, T1 arg1, T2 arg2, int phaseId, object? eventSource) =>
            InvokeWithOwner(action.Target, action, arg1, arg2, phaseId, eventSource);

        private static CallbackToken Begin(object? owner, Delegate callback, int phaseId)
        {
            if (!IsActive)
            {
                return default;
            }
            long now = Stopwatch.GetTimestamp();
            if (now >= Volatile.Read(ref s_captureEndTimestamp))
            {
                Volatile.Write(ref s_active, 0);
                return default;
            }
            int callbackId = InternMetadata(owner, callback);
            return new CallbackToken(
                now,
                callbackId,
                phaseId,
                Thread.CurrentThread.ManagedThreadId,
                Interlocked.Increment(ref s_spanSequence),
                Volatile.Read(ref s_captureGeneration));
        }

        private static long End(CallbackToken token)
        {
            long end = Stopwatch.GetTimestamp();
            if (!token.IsActive || token.Generation != Volatile.Read(ref s_captureGeneration))
            {
                return end;
            }
            TraceSpanRing? ring = GetRing(token.ThreadId);
            ring?.Add(new RuntimeTraceSpan(
                token.StartTimestamp,
                end,
                token.CallbackId,
                token.PhaseId,
                token.ThreadId,
                token.Sequence,
                0));
            return end;
        }

        private static void RecordCallbackOverhead(
            long wrapperStart,
            long callbackStart,
            long callbackEnd,
            long wrapperEnd,
            int generation)
        {
            if (generation != Volatile.Read(ref s_captureGeneration))
            {
                return;
            }
            long overheadTicks = Math.Max(0, callbackStart - wrapperStart) +
                Math.Max(0, wrapperEnd - callbackEnd);
            long accountingStart = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref s_deepCallbackCount);
            Interlocked.Add(ref s_deepOverheadTicks, overheadTicks);
            long accountingEnd = Stopwatch.GetTimestamp();
            Interlocked.Add(ref s_deepOverheadTicks, Math.Max(0, accountingEnd - accountingStart));
        }

        private static int InternMetadata(object? owner, Delegate callback)
        {
            if (s_delegateMetadata.TryGetValue(callback, out CallbackMetadataCache? cached))
            {
                return cached.Id;
            }
            if (Volatile.Read(ref s_metadataCount) >= MetadataCapacity)
            {
                return 0;
            }
            MethodInfo method = callback.Method;
            OwnerMetadata? ownerMetadata = owner is null ? null : s_ownerMetadata.GetValue(owner, CreateOwnerMetadata);
            string ownerType = ownerMetadata?.TypeName ??
                callback.Target?.GetType().FullName ??
                method.DeclaringType?.FullName ?? "<static>";
            string assembly = ownerMetadata?.AssemblyName ?? RuntimeTraceText.AssemblyName(method);
            var key = new CallbackMetadataKey(ownerType, method.Name, assembly);
            lock (s_metadataGate)
            {
                if (s_metadataCount >= MetadataCapacity)
                {
                    return 0;
                }
                if (s_metadataByKey.TryGetValue(key, out CallbackMetadataSnapshot existing))
                {
                    TryCacheDelegateMetadata(callback, existing.Id);
                    return existing.Id;
                }
                CallbackMetadataSnapshot metadata = new(s_metadata.Count + 1, ownerType, method.Name, assembly);
                s_metadataByKey.Add(key, metadata);
                s_metadata.Add(metadata);
                Volatile.Write(ref s_metadataCount, s_metadata.Count);
                TryCacheDelegateMetadata(callback, metadata.Id);
                return metadata.Id;
            }
        }

        private static void TryCacheDelegateMetadata(Delegate callback, int id)
        {
            try
            {
                s_delegateMetadata.Add(callback, new CallbackMetadataCache(id));
            }
            catch (ArgumentException)
            {
                // Another callback thread won the weak-table race; its ID is equivalent.
            }
        }

        private static TraceSpanRing? GetRing(int threadId)
        {
            if (Volatile.Read(ref s_benchmarkMode) != 0)
            {
                return s_benchmarkRing;
            }
            if (s_threadRingId == threadId && s_threadRing is not null)
            {
                return s_threadRing;
            }
            if (s_rings.TryGetValue(threadId, out TraceSpanRing? existing))
            {
                s_threadRingId = threadId;
                s_threadRing = existing;
                return existing;
            }
            if (s_rings.Count >= MaximumTraceThreads)
            {
                return null;
            }
            TraceSpanRing ring = s_rings.GetOrAdd(threadId, _ => new TraceSpanRing(SpanCapacityPerThread));
            s_threadRingId = threadId;
            s_threadRing = ring;
            return ring;
        }

        private static OwnerMetadata CreateOwnerMetadata(object value)
        {
            Type type = value.GetType();
            return new OwnerMetadata(type.FullName ?? type.Name, RuntimeTraceText.AssemblyName(type));
        }

        private sealed class OwnerMetadata
        {
            internal OwnerMetadata(string typeName, string assemblyName)
            {
                TypeName = typeName;
                AssemblyName = assemblyName;
            }

            internal string TypeName { get; }
            internal string AssemblyName { get; }
        }

        private sealed class CallbackMetadataCache
        {
            internal CallbackMetadataCache(int id)
            {
                Id = id;
            }

            internal int Id { get; }
        }

        private readonly struct CallbackMetadataKey : IEquatable<CallbackMetadataKey>
        {
            internal CallbackMetadataKey(string ownerType, string methodName, string assemblyName)
            {
                OwnerType = ownerType;
                MethodName = methodName;
                AssemblyName = assemblyName;
            }

            private string OwnerType { get; }
            private string MethodName { get; }
            private string AssemblyName { get; }

            public bool Equals(CallbackMetadataKey other) =>
                string.Equals(OwnerType, other.OwnerType, StringComparison.Ordinal) &&
                string.Equals(MethodName, other.MethodName, StringComparison.Ordinal) &&
                string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is CallbackMetadataKey other && Equals(other);

            public override int GetHashCode() =>
                StringComparer.Ordinal.GetHashCode(OwnerType) * 397 ^
                StringComparer.Ordinal.GetHashCode(MethodName) * 31 ^
                StringComparer.Ordinal.GetHashCode(AssemblyName);
        }

        private readonly struct CallbackMetricKey : IEquatable<CallbackMetricKey>
        {
            internal CallbackMetricKey(int callbackId, int phaseId)
            {
                CallbackId = callbackId;
                PhaseId = phaseId;
            }

            internal int CallbackId { get; }
            internal int PhaseId { get; }

            public bool Equals(CallbackMetricKey other) => CallbackId == other.CallbackId && PhaseId == other.PhaseId;
            public override bool Equals(object? obj) => obj is CallbackMetricKey other && Equals(other);
            public override int GetHashCode() => CallbackId * 397 ^ PhaseId;
        }

        private sealed class CallbackMetricBuilder
        {
            private readonly List<long> m_durations = new();
            internal long TotalTicks { get; private set; }
            internal long MaxTicks { get; private set; }
            internal long SlowCallCount { get; private set; }
            internal long WorstStartTimestamp { get; private set; }

            internal void Add(long ticks, long startTimestamp)
            {
                TotalTicks = RuntimeTraceMath.SaturatingAdd(TotalTicks, ticks);
                if (ticks > MaxTicks || (ticks == MaxTicks &&
                    (WorstStartTimestamp <= 0 || startTimestamp < WorstStartTimestamp)))
                {
                    MaxTicks = ticks;
                    WorstStartTimestamp = startTimestamp;
                }
                if (ticks >= SlowCallbackTicks)
                {
                    SlowCallCount++;
                }
                m_durations.Add(ticks);
            }

            internal CallbackMetricSnapshot ToSnapshot(
                CallbackMetadataSnapshot metadata,
                int phaseId,
                long capturedCallbackTicks)
            {
                m_durations.Sort();
                return new CallbackMetricSnapshot(
                    metadata,
                    phaseId,
                    m_durations.Count,
                    TotalTicks,
                    Percentile(m_durations, 0.95),
                    Percentile(m_durations, 0.99),
                    MaxTicks,
                    SlowCallCount,
                    capturedCallbackTicks <= 0
                        ? 0
                        : TotalTicks * 100.0 / capturedCallbackTicks,
                    WorstStartTimestamp);
            }

            private static long Percentile(IReadOnlyList<long> values, double percentile)
            {
                if (values.Count == 0)
                {
                    return 0;
                }
                int index = (int)Math.Ceiling(values.Count * percentile) - 1;
                return values[Math.Max(0, Math.Min(values.Count - 1, index))];
            }
        }

        private sealed class TraceSpanRing
        {
            private readonly RuntimeTraceSpan[] m_spans;
            private int m_next;
            private int m_count;
            private int m_generation;

            internal TraceSpanRing(int capacity)
            {
                m_spans = new RuntimeTraceSpan[capacity];
            }

            internal void Add(RuntimeTraceSpan span)
            {
                int generation = Volatile.Read(ref s_captureGeneration);
                if (m_generation != generation)
                {
                    m_next = 0;
                    m_count = 0;
                    m_generation = generation;
                }
                int index = Interlocked.Increment(ref m_next) - 1;
                m_spans[index % m_spans.Length] = span;
                Volatile.Write(ref m_count, Math.Min(m_spans.Length, index + 1));
            }

            internal void Reset(int generation)
            {
                m_next = 0;
                m_count = 0;
                m_generation = generation;
            }

            internal RuntimeTraceSpan[] Snapshot(int generation)
            {
                if (m_generation != generation)
                {
                    return Array.Empty<RuntimeTraceSpan>();
                }
                int count = Math.Min(m_spans.Length, Volatile.Read(ref m_count));
                int next = Volatile.Read(ref m_next);
                var result = new RuntimeTraceSpan[count];
                int start = Math.Max(0, next - count);
                for (int index = 0; index < count; index++)
                {
                    result[index] = m_spans[(start + index) % m_spans.Length];
                }
                return result;
            }
        }
    }
}
