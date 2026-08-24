// Taj's COI Mods | RuntimePerformanceDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using HarmonyLib;
using Mafi;
using Mafi.Core.Console;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Profiler.Core;

namespace TajsCOI.Profiler.Probes.Runtime
{
    /// <summary>
    ///     Broad, behavior-neutral save/load and memory instrumentation. All binding and callback
    ///     failures are contained so the original game path always continues unchanged.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class RuntimePerformanceDiagnosticsService
    {
        private const string HarmonyId = "TajsCOI.Profiler.RuntimePerformance";
        private const int MaxHistory = 16;
        private const int MaxGcPassHistory = 32;
        private const int MaxLifecycleCheckpointHistory = 64;
        private const int MaxWeakLifecycleWatchHistory = 32;

        internal const string SaveSerialization = "save.serialization";
        internal const string SaveFinalize = "save.compression-write-total";
        internal const string SaveCompression = "save.compression-io";
        internal const string SaveChecksum = "save.checksum-nested";
        internal const string FileChecksumValidation = "file.checksum-validation";
        internal const string LoadHeaders = "load.headers-config";
        internal const string LoadFinalize = "load.deserialize-resolve-finalize-slice";
        internal const string LoadDeserialization = "load.deserialization";
        internal const string LoadResolverFinalization = "load.resolver-finalization-slice";
        internal const string SceneGarbageCollection = "scene.cleanup-full-gc";

        private static readonly string[] s_stageOrder =
        {
            SaveSerialization,
            SaveFinalize,
            SaveCompression,
            SaveChecksum,
            FileChecksumValidation,
            LoadHeaders,
            LoadFinalize,
            LoadDeserialization,
            LoadResolverFinalization,
            SceneGarbageCollection,
        };

        private static readonly Dictionary<string, StageAccumulator> s_stages =
            s_stageOrder.ToDictionary(x => x, _ => new StageAccumulator(), StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<MethodBase, NamedTimingAccumulator> s_initializationTargets = new();
        private static readonly object s_historyGate = new();
        private static readonly object s_patchGate = new();
        private static readonly List<RuntimeProfileSnapshot> s_history = new(MaxHistory);
        private static readonly object s_gcPassGate = new();
        private static readonly List<GcPassMetric> s_gcPassHistory = new(MaxGcPassHistory);
        private static readonly object s_lifecycleGate = new();
        private static readonly List<LifecycleCheckpoint> s_lifecycleCheckpoints = new(MaxLifecycleCheckpointHistory);
        private static readonly List<WeakLifecycleWatch> s_weakLifecycleWatches = new(MaxWeakLifecycleWatchHistory);

        private static ITajsLogger? s_log;
        private static readonly ConcurrentDictionary<string, byte> s_loggedCallbackFailures =
            new(StringComparer.Ordinal);
        private static long s_sequence;
        private static long s_resetGeneration;
        private static long s_gcPassSequence;
        private static long s_lifecycleSequence;
        private static bool s_patchAttempted;
        private static PatchSummary s_patchSummary;

        private readonly DependencyResolver m_resolver;

        [ThreadStatic]
        private static int s_saveDataDepth;

        [ThreadStatic]
        private static int s_mainSaveFinalizeDepth;

        [ThreadStatic]
        private static GcPassState? s_gcPassState;

        public RuntimePerformanceDiagnosticsService(DependencyResolver resolver, ITajsRuntime runtime)
        {
            m_resolver = resolver;
            RecordWeakLifecycleWatch("resolver/runtime-diagnostics", resolver);
            RecordLifecycleCheckpoint("resolver/runtime-diagnostics-created");
            // Harmony callbacks are static, so this process-lifetime global dependency publishes
            // its component logger once for fail-open callback diagnostics.
#pragma warning disable S2696
            s_log = runtime.GetLogger("TajsProfiler", "RuntimePerformance");
#pragma warning restore S2696

            PatchSummary summary = InstallPatches();
            CompatibilityState state = summary.RequiredInstalled == summary.RequiredExpected
                ? (summary.OptionalInstalled == summary.OptionalExpected
                    ? CompatibilityState.Compatible
                    : CompatibilityState.Degraded)
                : CompatibilityState.Disabled;

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsProfiler",
                "RuntimePerformance",
                state,
                $"{summary.RequiredExpected} required and {summary.OptionalExpected} optional 0.8.7a probe targets",
                $"{summary.RequiredInstalled} required and {summary.OptionalInstalled} optional targets installed",
                state == CompatibilityState.Compatible
                    ? "Save/load, memory, GC, and renderer snapshot probes are available."
                    : state == CompatibilityState.Degraded
                        ? "Core save/load timing is available; optional scene or renderer instrumentation is unavailable."
                : "Required save/load contracts were not found; no behavior was changed."));
        }

        [ConsoleCommand(
            documentation: "Captures a broad process, managed, Mono, Unity, and GC lifecycle checkpoint.",
            customCommandName: "tajs_runtime_lifecycle_checkpoint")]
        public string CaptureLifecycleCheckpoint(string label)
        {
            string normalized = (label ?? string.Empty).Trim();
            return normalized.Length == 0
                ? "Runtime lifecycle checkpoint: label is required."
                : FormatLifecycleCheckpoint(RecordLifecycleCheckpoint(normalized));
        }

        [ConsoleCommand(
            documentation: "Lists bounded lifecycle memory checkpoints captured by the runtime profiler.",
            customCommandName: "tajs_runtime_lifecycle_checkpoints")]
        public string ListLifecycleCheckpoints()
        {
            lock (s_lifecycleGate)
            {
                return s_lifecycleCheckpoints.Count == 0
                    ? "Runtime lifecycle checkpoints: none stored."
                    : "Runtime lifecycle checkpoints:\n" + string.Join("\n", s_lifecycleCheckpoints.Select(FormatLifecycleCheckpoint));
            }
        }

        [ConsoleCommand(
            documentation: "Reports weak lifecycle sentinels without retaining the watched resolver-scoped objects.",
            customCommandName: "tajs_runtime_lifecycle_watches")]
        public string ListLifecycleWatches()
        {
            lock (s_lifecycleGate)
            {
                return s_weakLifecycleWatches.Count == 0
                    ? "Runtime lifecycle watches: none stored."
                    : "Runtime lifecycle watches:\n" + string.Join("\n", s_weakLifecycleWatches.Select(x =>
                        $"  {x.Label} [sequence={x.Sequence}, alive={x.Reference.TryGetTarget(out _)}, recorded={x.RecordedUtc:O}]"));
            }
        }

        [ConsoleCommand(
            documentation: "Audits current TajsCOI Harmony owners and flags duplicate registrations by target/kind/owner.",
            customCommandName: "tajs_runtime_harmony_audit")]
        public string HarmonyAudit() => BuildHarmonyAudit();

        [ConsoleCommand(
            documentation: "Shows bounded top-N timing for known 0.8.7a dependency and renderer initialization targets.",
            customCommandName: "tajs_runtime_initialization_hotspots")]
        public string InitializationHotspots()
        {
            var metrics = s_initializationTargets.Values
                .Select(x => x.Snapshot())
                .Where(x => x.Count > 0)
                .OrderByDescending(x => x.MaxTicks)
                .ThenByDescending(x => x.TotalTicks)
                .Take(12)
                .ToArray();
            if (metrics.Length == 0)
            {
                return "Initialization hotspots: no named samples captured.";
            }

            var builder = new StringBuilder(1024).Append("Initialization hotspots (top ").Append(metrics.Length).Append("):");
            foreach (InitializationTimingMetric metric in metrics)
            {
                builder.Append("\n  ").Append(metric.Name)
                    .Append(" count=").Append(metric.Count)
                    .Append(", total=").Append(TicksToMilliseconds(metric.TotalTicks))
                    .Append(", max=").Append(TicksToMilliseconds(metric.MaxTicks));
            }
            builder.Append("\nNested timings are non-additive; iterator targets are measured slices.");
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Captures current save/load stage counters and memory telemetry under a unique label.",
            customCommandName: "tajs_runtime_profile_capture")]
        public string Capture(string label)
        {
            string normalized = (label ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return "Runtime profile capture: label is required.";
            }

            lock (s_historyGate)
            {
                if (FindLocked(normalized) is not null)
                {
                    return $"Runtime profile capture: label '{normalized}' already exists.";
                }

                RuntimeProfileSnapshot snapshot = CreateSnapshot(normalized);
                if (s_history.Count == MaxHistory)
                {
                    s_history.RemoveAt(0);
                }
                s_history.Add(snapshot);
                return Format(snapshot);
            }
        }

        [ConsoleCommand(
            documentation: "Lists stored save/load and memory captures.",
            customCommandName: "tajs_runtime_profiles")]
        public string List()
        {
            lock (s_historyGate)
            {
                return s_history.Count == 0
                    ? "Runtime profiles: none stored."
                    : "Runtime profiles:\n" + string.Join("\n", s_history.Select(x =>
                        $"  {x.Label} [sequence={x.Sequence}, captured={x.CapturedUtc:O}]"));
            }
        }

        [ConsoleCommand(
            documentation: "Shows a stored save/load and memory capture.",
            customCommandName: "tajs_runtime_profile_show")]
        public string Show(string label)
        {
            lock (s_historyGate)
            {
                RuntimeProfileSnapshot? snapshot = FindLocked((label ?? string.Empty).Trim());
                return snapshot is null
                    ? $"Runtime profile show: no capture named '{label}'."
                    : Format(snapshot);
            }
        }

        [ConsoleCommand(
            documentation: "Compares the counter and memory deltas between two stored captures.",
            customCommandName: "tajs_runtime_profile_compare")]
        public string Compare(string firstLabel, string secondLabel)
        {
            lock (s_historyGate)
            {
                RuntimeProfileSnapshot? first = FindLocked((firstLabel ?? string.Empty).Trim());
                RuntimeProfileSnapshot? second = FindLocked((secondLabel ?? string.Empty).Trim());
                if (first is null || second is null)
                {
                    return $"Runtime profile compare: missing capture(s); A='{firstLabel}', B='{secondLabel}'.";
                }
                if (first.Sequence >= second.Sequence)
                {
                    return "Runtime profile compare: the second capture must be newer than the first.";
                }
                if (first.ResetGeneration != second.ResetGeneration)
                {
                    return "Runtime profile compare: captures cross a counter reset and cannot be compared.";
                }

                int firstIndex = s_history.IndexOf(first);
                int secondIndex = s_history.IndexOf(second);

                var builder = new StringBuilder(1024)
                    .Append("Runtime profile comparison: ").Append(first.Label).Append(" -> ").Append(second.Label)
                    .Append("\nProcess delta: working-set=").Append(FormatOptionalBytes(second.ProcessWorkingSetBytes, first.ProcessWorkingSetBytes))
                    .Append(", private=").Append(FormatOptionalBytes(second.ProcessPrivateBytes, first.ProcessPrivateBytes))
                    .Append(", CPU=").Append(FormatOptionalMilliseconds(second.ProcessCpuMilliseconds, first.ProcessCpuMilliseconds))
                    .Append("\nMemory delta: managed=").Append(FormatBytes(second.ManagedBytes - first.ManagedBytes))
                    .Append(", Mono used=").Append(FormatOptionalBytes(second.MonoUsedBytes, first.MonoUsedBytes))
                    .Append(", Mono heap=").Append(FormatOptionalBytes(second.MonoHeapBytes, first.MonoHeapBytes))
                    .Append(", Unity allocated=").Append(FormatOptionalBytes(second.UnityAllocatedBytes, first.UnityAllocatedBytes))
                    .Append(", Unity reserved=").Append(FormatOptionalBytes(second.UnityReservedBytes, first.UnityReservedBytes))
                    .Append(", Unity unused reserved=").Append(FormatOptionalBytes(second.UnityUnusedReservedBytes, first.UnityUnusedReservedBytes))
                    .Append(", graphics=").Append(FormatUnityGraphicsDelta(second, first));

                foreach (string stage in s_stageOrder)
                {
                    long intervalMaxTicks = 0;
                    for (int index = firstIndex + 1; index <= secondIndex; index++)
                    {
                        intervalMaxTicks = Math.Max(intervalMaxTicks, s_history[index].Stages[stage].MaxTicks);
                    }
                    StageMetric delta = StageMetric.Difference(
                        second.Stages[stage],
                        first.Stages[stage],
                        intervalMaxTicks);
                    AppendStage(builder, stage, delta);
                }

                if (first.Products.Available && second.Products.Available)
                {
                    builder.Append("\nProducts renderer delta: GPU=")
                        .Append(FormatBytes(second.Products.GpuBytes - first.Products.GpuBytes))
                        .Append(", textures=").Append(FormatBytes(second.Products.TexturesBytes - first.Products.TexturesBytes))
                        .Append(", instance buffers=").Append(FormatBytes(second.Products.InstancesBytes - first.Products.InstancesBytes))
                        .Append(", slots live/high-water/capacity=")
                        .Append(second.Products.LiveSlots - first.Products.LiveSlots).Append('/')
                        .Append(second.Products.HighWaterSlots - first.Products.HighWaterSlots).Append('/')
                        .Append(second.Products.CapacitySlots - first.Products.CapacitySlots)
                        .Append(", live used/capacity=").Append(second.Products.LiveBufferUsed - first.Products.LiveBufferUsed).Append('/')
                        .Append(second.Products.LiveBufferCapacity - first.Products.LiveBufferCapacity)
                        .Append(", reserve used/capacity=").Append(second.Products.ReserveBufferUsed - first.Products.ReserveBufferUsed).Append('/')
                        .Append(second.Products.ReserveBufferCapacity - first.Products.ReserveBufferCapacity);
                }
                AppendGcInterval(builder, first, second);
                return builder.ToString();
            }
        }

        [ConsoleCommand(
            documentation: "Clears stored captures without resetting accumulated stage counters.",
            customCommandName: "tajs_runtime_profile_clear")]
        public string Clear()
        {
            lock (s_historyGate)
            {
                int count = s_history.Count;
                s_history.Clear();
                return $"Runtime profile history cleared ({count} capture(s)); counters unchanged.";
            }
        }

        [ConsoleCommand(
            documentation: "Resets accumulated save/load stage counters without clearing stored captures.",
            customCommandName: "tajs_runtime_profile_reset")]
        public string Reset()
        {
            lock (s_historyGate)
            {
                foreach (StageAccumulator stage in s_stages.Values)
                {
                    stage.Reset();
                }
                Interlocked.Increment(ref s_resetGeneration);
            }
            return "Runtime profile counters reset; stored captures remain visible but comparisons cannot cross this reset.";
        }

        private RuntimeProfileSnapshot CreateSnapshot(string label)
        {
            var stages = new Dictionary<string, StageMetric>(StringComparer.Ordinal);
            foreach (string stage in s_stageOrder)
            {
                stages.Add(stage, s_stages[stage].SnapshotAndResetIntervalMax());
            }

            ReadProcessMemory(out long processWorkingSet, out long processPrivate, out long processCpuMilliseconds);
            UnityMemorySnapshot unity = ReadUnityMemorySnapshot();
            return new RuntimeProfileSnapshot(
                label,
                Interlocked.Increment(ref s_sequence),
                Interlocked.Read(ref s_resetGeneration),
                DateTime.UtcNow,
                processWorkingSet,
                processPrivate,
                processCpuMilliseconds,
                GC.GetTotalMemory(false),
                unity.MonoUsedBytes,
                unity.MonoHeapBytes,
                unity.AllocatedBytes,
                unity.ReservedBytes,
                unity.UnusedReservedBytes,
                unity.GraphicsBytes,
                ReadProductsRenderer(),
                stages,
                SnapshotGcPasses(),
                Interlocked.Read(ref s_gcPassSequence));
        }

        internal static LifecycleCheckpoint RecordLifecycleCheckpoint(string label)
        {
            string normalized = string.IsNullOrWhiteSpace(label) ? "<unnamed>" : label.Trim();
            ReadProcessMemory(out long processWorkingSet, out long processPrivate, out long processCpuMilliseconds);
            UnityMemorySnapshot unity = ReadUnityMemorySnapshot();
            var checkpoint = new LifecycleCheckpoint(
                normalized,
                Interlocked.Increment(ref s_lifecycleSequence),
                DateTime.UtcNow,
                processWorkingSet,
                processPrivate,
                processCpuMilliseconds,
                GC.GetTotalMemory(false),
                unity.MonoUsedBytes,
                unity.MonoHeapBytes,
                unity.AllocatedBytes,
                unity.ReservedBytes,
                unity.UnusedReservedBytes,
                unity.GraphicsBytes,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));

            lock (s_lifecycleGate)
            {
                if (s_lifecycleCheckpoints.Count == MaxLifecycleCheckpointHistory)
                {
                    s_lifecycleCheckpoints.RemoveAt(0);
                }
                s_lifecycleCheckpoints.Add(checkpoint);
            }
            return checkpoint;
        }

        internal static void RecordWeakLifecycleWatch(string label, object? value)
        {
            if (value is null)
            {
                return;
            }

            lock (s_lifecycleGate)
            {
                if (s_weakLifecycleWatches.Count == MaxWeakLifecycleWatchHistory)
                {
                    s_weakLifecycleWatches.RemoveAt(0);
                }
                s_weakLifecycleWatches.Add(new WeakLifecycleWatch(
                    string.IsNullOrWhiteSpace(label) ? "<unnamed>" : label.Trim(),
                    Interlocked.Increment(ref s_lifecycleSequence),
                    DateTime.UtcNow,
                    new WeakReference<object>(value)));
            }
        }

        private ProductRendererMetric ReadProductsRenderer()
        {
            try
            {
                Type? rendererType = FindType("Mafi.Unity.InstancedRendering.Products.ProductsRenderer", "Mafi.Unity");
                if (rendererType is null)
                {
                    return UnavailableProducts("Mafi.Unity ProductsRenderer type is not loaded.");
                }

                object? renderer = m_resolver.TryResolve(rendererType).ValueOrNull;
                if (renderer is null)
                {
                    return UnavailableProducts("ProductsRenderer is not present in the active resolver (no gameplay scene). ");
                }

                object memory = rendererType.GetMethod("EstimateBufferMemory", BindingFlags.Instance | BindingFlags.Public)!
                    .Invoke(renderer, null);
                Type memoryType = memory.GetType();
                ReadSlotFragmentation(rendererType, renderer, out int fragmentedSlots, out int freeRanges, out int largestFreeRange);
                int highWaterSlots = ReadIntProperty(rendererType, renderer, "StatSlots");
                ReadInstanceBuffer(rendererType, renderer, "m_liveBuffer", "m_liveCountDraw", out int liveUsed, out int liveCapacity);
                ReadInstanceBuffer(rendererType, renderer, "m_reserveBuffer", "m_reserveCount", out int reserveUsed, out int reserveCapacity);
                return new ProductRendererMetric(
                    true,
                    ReadIntProperty(rendererType, renderer, "StatInstances"),
                    ReadIntProperty(rendererType, renderer, "StatGpuInstances"),
                    Math.Max(0, highWaterSlots - fragmentedSlots),
                    highWaterSlots,
                    ReadIntProperty(rendererType, renderer, "StatSlotCapacity"),
                    fragmentedSlots,
                    freeRanges,
                    largestFreeRange,
                    liveUsed,
                    liveCapacity,
                    reserveUsed,
                    reserveCapacity,
                    ReadLongField(memoryType, memory, "InstancesBytes"),
                    ReadLongField(memoryType, memory, "StaticOwnersBytes"),
                    ReadLongField(memoryType, memory, "DynamicOwnersBytes"),
                    ReadLongField(memoryType, memory, "SlotsBytes"),
                    ReadLongField(memoryType, memory, "TexturesBytes"),
                    "Public ProductsRenderer telemetry plus read-only slot free-list inspection");
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "ProductsRenderer snapshot");
                return UnavailableProducts($"{exception.GetType().Name}: {exception.Message}");
            }
        }

        private static PatchSummary InstallPatches()
        {
            lock (s_patchGate)
            {
                if (s_patchAttempted)
                {
                    return s_patchSummary;
                }

                var harmony = new Harmony(HarmonyId);
                int requiredExpected = 0;
                int required = 0;
                int optionalExpected = 0;
                int optional = 0;

                requiredExpected++; required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameSaver", "Mafi.Core", "StartSave", SaveSerialization) ? 1 : 0;
                requiredExpected++; required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameSaver", "Mafi.Core", "FinishSaveWriteToStream", SaveFinalize) ? 1 : 0;
                requiredExpected++; required += PatchSaveChecksum(harmony) ? 1 : 0;
                requiredExpected++; required += PatchTimed(harmony, "Mafi.Core.SaveGame.SaveLoadFileUtils", "Mafi.Core", "ValidateChecksum", FileChecksumValidation, typeof(string)) ? 1 : 0;
                requiredExpected++; required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameLoader", "Mafi.Core", "StartGameLoad", LoadHeaders) ? 1 : 0;
                requiredExpected++; required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameLoader", "Mafi.Core", "ContinueGameLoad", LoadHeaders) ? 1 : 0;
                requiredExpected++; required += PatchCompressionIo(
                    harmony,
                    "Mafi.Core.SaveGame.GameSaver",
                    "FinishSaveWriteToStream",
                    "CreateCompressingStream",
                    nameof(BeginMainSaveFinalize),
                    nameof(EndMainSaveFinalize),
                    nameof(WrapCompressingStream)) ? 1 : 0;
                requiredExpected++; required += PatchIterator(harmony, "Mafi.Core.SaveGame.GameLoader", "Mafi.Core", "FinishGameLoadAndDisposeTimeSliced", LoadFinalize) ? 1 : 0;
                requiredExpected++; required += PatchTimed(harmony, "Mafi.DependencyResolver", "Mafi", "DeserializeInto", LoadDeserialization) ? 1 : 0;
                requiredExpected++; required += PatchIterator(harmony, "Mafi.Serialization.BlobReader", "Mafi", "FinalizeLoadingTimeSliced", LoadResolverFinalization) ? 1 : 0;
                optionalExpected++; optional += PatchTimed(harmony, "Mafi.Unity.Main", "Mafi.Unity", "collectGarbageDrainingFinalizers", SceneGarbageCollection) ? 1 : 0;
                optionalExpected++; optional += PatchSceneGcPasses(harmony) ? 1 : 0;
                optionalExpected++; optional += PatchInitializationHotspots(harmony) ? 1 : 0;

                if (required != requiredExpected)
                {
                    harmony.UnpatchAll(HarmonyId);
                    required = 0;
                    optional = 0;
                    s_initializationTargets.Clear();
                }
                s_patchSummary = new PatchSummary(requiredExpected, required, optionalExpected, optional);
                s_patchAttempted = true;
                return s_patchSummary;
            }
        }

        private static bool PatchCompressionIo(
            Harmony harmony,
            string outerTypeName,
            string outerMethodName,
            string factoryMethodName,
            string beginCallback,
            string endCallback,
            string wrapCallback)
        {
            try
            {
                Type? outerType = FindType(outerTypeName, "Mafi.Core");
                Type? compressorType = FindType("Mafi.Core.SaveGame.GzipSaveCompressor", "Mafi.Core");
                MethodInfo? outer = outerType?.GetMethod(
                    outerMethodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo? factory = compressorType?.GetMethod(
                    factoryMethodName,
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(Stream) },
                    null);
                if (outer is null || factory is null || !typeof(Stream).IsAssignableFrom(factory.ReturnType))
                {
                    return false;
                }

                var begin = new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), beginCallback)
                {
                    priority = Priority.First,
                };
                harmony.Patch(
                    outer,
                    prefix: begin,
                    finalizer: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), endCallback));
                harmony.Patch(
                    factory,
                    postfix: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), wrapCallback));
                return true;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, $"patch gzip I/O for {outerTypeName}.{outerMethodName}");
                return false;
            }
        }

        private static bool PatchTimed(
            Harmony harmony,
            string typeName,
            string assemblyName,
            string methodName,
            string stage,
            params Type[] leadingParameterTypes)
        {
            try
            {
                Type? type = FindType(typeName, assemblyName);
                MethodInfo? method = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(x => x.Name == methodName)
                    .FirstOrDefault(x => leadingParameterTypes.Length == 0 ||
                        x.GetParameters().Take(leadingParameterTypes.Length).Select(p => p.ParameterType)
                            .SequenceEqual(leadingParameterTypes));
                if (method is null)
                {
                    return false;
                }

                TimedTargets.TryAdd(method, stage);
                harmony.Patch(
                    method,
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(BeforeTimed)),
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(AfterTimed)),
                    finalizer: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(FinalizeTimed)));
                return true;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, $"patch {typeName}.{methodName}");
                return false;
            }
        }

        private static bool PatchIterator(Harmony harmony, string typeName, string assemblyName, string methodName, string stage)
        {
            try
            {
                MethodInfo? factory = FindType(typeName, assemblyName)?.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Type? stateMachine = factory?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
                if (stateMachine is null && factory?.DeclaringType is not null)
                {
                    stateMachine = factory.DeclaringType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(x => x.Name.IndexOf(methodName, StringComparison.Ordinal) >= 0 &&
                            typeof(System.Collections.IEnumerator).IsAssignableFrom(x));
                }
                MethodInfo? moveNext = stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveNext is null)
                {
                    return false;
                }
                TimedTargets.TryAdd(moveNext, stage);
                harmony.Patch(
                    moveNext,
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(BeforeTimed)),
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(AfterTimed)),
                    finalizer: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(FinalizeTimed)));
                return true;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, $"patch iterator {typeName}.{methodName}");
                return false;
            }
        }

        private static bool PatchSaveChecksum(Harmony harmony)
        {
            try
            {
                MethodInfo? saveData = FindType("Mafi.Core.SaveGame.SaveLoadFileUtils", "Mafi.Core")?.GetMethod(
                    "SaveDataWithHeaders",
                    BindingFlags.Static | BindingFlags.Public);
                MethodInfo? crc = FindType("Mafi.Serialization.Crc32", "Mafi")?.GetMethod(
                    "Compute",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(System.IO.Stream), typeof(long).MakeByRefType() },
                    null);
                if (saveData is null || crc is null)
                {
                    return false;
                }

                harmony.Patch(
                    saveData,
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(BeginSaveData)),
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(EndSaveData)),
                    finalizer: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(FinalizeSaveData)));
                harmony.Patch(
                    crc,
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(BeforeSaveChecksum)),
                    new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(AfterSaveChecksum)),
                    finalizer: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(FinalizeSaveChecksum)));
                return true;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "patch nested save checksum");
                return false;
            }
        }

        private static bool PatchSceneGcPasses(Harmony harmony)
        {
            try
            {
                MethodInfo? target = FindType("Mafi.Unity.Main", "Mafi.Unity")?.GetMethod(
                    "collectGarbageDrainingFinalizers",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target is null)
                {
                    return false;
                }

                harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(InstrumentGcPasses)));
                return true;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "patch per-pass scene GC");
                return false;
            }
        }

        private static bool PatchInitializationHotspots(Harmony harmony)
        {
            var targets = new List<(MethodBase Target, string Name)>();
            int expected = 0;
            expected++;
            AddTimedInitializationTarget(
                targets,
                "Mafi.Core.PathFinding.ClearancePathabilityProvider",
                "Mafi.Core",
                "initSelf",
                "ClearancePathabilityProvider.initSelf",
                typeof(DependencyResolver));
            expected++;
            AddTimedInitializationTarget(
                targets,
                "Mafi.Core.PathFinding.ShipsClearancePathabilityProvider",
                "Mafi.Core",
                "initSelf",
                "ShipsClearancePathabilityProvider.initSelf",
                typeof(int),
                typeof(DependencyResolver));
            expected++;
            AddTimedInitializationTarget(
                targets,
                "Mafi.Core.PathFinding.VehiclesConnectivityManager",
                "Mafi.Core",
                "initAfterLoad",
                "VehiclesConnectivityManager.initAfterLoad",
                typeof(int),
                typeof(DependencyResolver));
            expected++;
            AddTimedInitializationTarget(
                targets,
                "Mafi.Unity.InputControl.ResVis.ResVisBarsRenderer",
                "Mafi.Unity",
                "initState",
                "ResVisBarsRenderer.initState");
            AddTimedInitializationTarget(
                targets,
                "Mafi.Unity.Terrain.TerrainRenderer",
                "Mafi.Unity",
                "initState",
                "TerrainRenderer.initState");
            expected++;
            AddTimedInitializationTarget(
                targets,
                "Mafi.Unity.Terrain.WaterRendererFft",
                "Mafi.Unity",
                "initialize",
                "WaterRendererFft.initialize");
            expected++;

            MethodBase? productsFactory = FindType("Mafi.Unity.InstancedRendering.Products.ProductsRenderer", "Mafi.Unity")
                ?.GetMethod("initState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodBase? productsMoveNext = GetIteratorMoveNext(productsFactory);
            if (productsMoveNext is null)
            {
                return false;
            }
            expected++;
            targets.Add((productsMoveNext, "ProductsRenderer.initState-slice"));

            if (targets.Count != expected || targets.Select(x => x.Target).Distinct().Count() != targets.Count)
            {
                return false;
            }

            try
            {
                foreach ((MethodBase target, string name) in targets)
                {
                    var accumulator = new NamedTimingAccumulator(name);
                    if (!s_initializationTargets.TryAdd(target, accumulator))
                    {
                        throw new InvalidOperationException($"Duplicate initialization timing target '{name}'.");
                    }
                    harmony.Patch(
                        target,
                        new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(BeforeTimed)),
                        new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(AfterTimed)),
                        finalizer: new HarmonyMethod(typeof(RuntimePerformanceDiagnosticsService), nameof(FinalizeTimed)));
                }
                return true;
            }
            catch (Exception exception)
            {
                foreach ((MethodBase target, _) in targets)
                {
                    harmony.Unpatch(target, HarmonyPatchType.All, HarmonyId);
                    s_initializationTargets.TryRemove(target, out _);
                }
                LogCallbackFailure(exception, "patch initialization hotspots");
                return false;
            }
        }

        private static void AddTimedInitializationTarget(
            ICollection<(MethodBase Target, string Name)> targets,
            string typeName,
            string assemblyName,
            string methodName,
            string displayName,
            params Type[] leadingParameterTypes)
        {
            Type? type = FindType(typeName, assemblyName);
            MethodInfo[] methods = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.Name == methodName &&
                    x.GetParameters().Length == leadingParameterTypes.Length &&
                    (leadingParameterTypes.Length == 0 || x.GetParameters().Take(leadingParameterTypes.Length)
                        .Select(p => p.ParameterType).SequenceEqual(leadingParameterTypes)))
                .ToArray() ?? Array.Empty<MethodInfo>();
            if (methods.Length == 1)
            {
                targets.Add((methods[0], displayName));
            }
        }

        private static MethodBase? GetIteratorMoveNext(MethodBase? factory)
        {
            if (factory is not MethodInfo method)
            {
                return null;
            }
            Type? stateMachine = method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            if (stateMachine is null && method.DeclaringType is not null)
            {
                stateMachine = method.DeclaringType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name.IndexOf(method.Name, StringComparison.Ordinal) >= 0 &&
                        typeof(System.Collections.IEnumerator).IsAssignableFrom(x));
            }
            return stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static IEnumerable<CodeInstruction> InstrumentGcPasses(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo collect = AccessTools.Method(
                typeof(GC),
                nameof(GC.Collect),
                new[] { typeof(int), typeof(GCCollectionMode), typeof(bool), typeof(bool) });
            MethodInfo wait = AccessTools.Method(typeof(GC), nameof(GC.WaitForPendingFinalizers), Type.EmptyTypes);
            MethodInfo measuredCollect = AccessTools.Method(
                typeof(RuntimePerformanceDiagnosticsService),
                nameof(CollectGarbageMeasured));
            MethodInfo measuredWait = AccessTools.Method(
                typeof(RuntimePerformanceDiagnosticsService),
                nameof(WaitForPendingFinalizersMeasured));
            int collectReplacements = 0;
            int waitReplacements = 0;
            var result = new List<CodeInstruction>();

            foreach (CodeInstruction instruction in instructions)
            {
                var replacement = new CodeInstruction(instruction);
                if (instruction.Calls(collect))
                {
                    replacement.operand = measuredCollect;
                    collectReplacements++;
                }
                else if (instruction.Calls(wait))
                {
                    replacement.operand = measuredWait;
                    waitReplacements++;
                }
                result.Add(replacement);
            }

            if (collectReplacements != 1 || waitReplacements != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one GC.Collect and one WaitForPendingFinalizers call, found {collectReplacements} and {waitReplacements}.");
            }
            return result;
        }

        private static void CollectGarbageMeasured(int generation, GCCollectionMode mode, bool blocking, bool compacting)
        {
            try
            {
                RecordLifecycleCheckpoint("scene.cleanup-full-gc-before");
                s_gcPassState = new GcPassState(
                    Stopwatch.GetTimestamp(),
                    GC.GetTotalMemory(false),
                    GC.CollectionCount(0),
                    GC.CollectionCount(1),
                    GC.CollectionCount(2));
            }
            catch (Exception exception)
            {
                s_gcPassState = null;
                LogCallbackFailure(exception, "per-pass GC prefix");
            }

            try
            {
                // The transpiler replaces vanilla's exact GC.Collect call with this measuring
                // wrapper. Removing or changing it would alter scene-cleanup semantics.
#pragma warning disable S1215
                GC.Collect(generation, mode, blocking, compacting);
#pragma warning restore S1215
            }
            catch
            {
                s_gcPassState = null;
                throw;
            }
        }

        private static void WaitForPendingFinalizersMeasured()
        {
            try
            {
                GC.WaitForPendingFinalizers();
            }
            finally
            {
                RecordGcPass();
            }
        }

        private static void RecordGcPass()
        {
            GcPassState? state = s_gcPassState;
            s_gcPassState = null;
            if (state is null)
            {
                return;
            }

            try
            {
                var metric = new GcPassMetric(
                    Interlocked.Increment(ref s_gcPassSequence),
                    Stopwatch.GetTimestamp() - state.StartTicks,
                    state.BeforeBytes,
                    GC.GetTotalMemory(false),
                    GC.CollectionCount(0) - state.Gen0,
                    GC.CollectionCount(1) - state.Gen1,
                    GC.CollectionCount(2) - state.Gen2);
                lock (s_gcPassGate)
                {
                    if (s_gcPassHistory.Count == MaxGcPassHistory)
                    {
                        s_gcPassHistory.RemoveAt(0);
                    }
                    s_gcPassHistory.Add(metric);
                }
                RecordLifecycleCheckpoint("scene.cleanup-full-gc-pass");
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "per-pass GC result");
            }
        }

        private static readonly ConcurrentDictionary<MethodBase, string> TimedTargets = new();

        private static void BeginMainSaveFinalize() => s_mainSaveFinalizeDepth++;

        private static Exception? EndMainSaveFinalize(Exception? __exception)
        {
            s_mainSaveFinalizeDepth = Math.Max(0, s_mainSaveFinalizeDepth - 1);
            return __exception;
        }

        private static void WrapCompressingStream(ref Stream __result)
        {
            try
            {
                if (s_mainSaveFinalizeDepth > 0 && __result is not TimedIoStream)
                {
                    __result = new TimedIoStream(__result, s_stages[SaveCompression]);
                }
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "wrap save compression stream");
            }
        }

        private static void BeginSaveData(out CallbackState? __state)
        {
            __state = null;
            try
            {
                __state = new CallbackState();
                s_saveDataDepth++;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "save checksum scope prefix");
            }
        }

        private static void EndSaveData(CallbackState? __state) => LeaveSaveData(__state);

        private static Exception? FinalizeSaveData(Exception? __exception, CallbackState? __state)
        {
            LeaveSaveData(__state);
            return __exception;
        }

        private static void LeaveSaveData(CallbackState? state)
        {
            if (state is not null && Interlocked.Exchange(ref state.Recorded, 1) == 0)
            {
                s_saveDataDepth = Math.Max(0, s_saveDataDepth - 1);
            }
        }

        private static void BeforeSaveChecksum(MethodBase __originalMethod, out TimingState? __state)
        {
            __state = null;
            if (s_saveDataDepth <= 0)
            {
                return;
            }

            try
            {
                __state = new TimingState(Stopwatch.GetTimestamp(), GC.GetTotalMemory(false), __originalMethod);
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "save checksum prefix");
            }
        }

        private static void AfterSaveChecksum(TimingState? __state) => RecordSaveChecksum(__state);

        private static Exception? FinalizeSaveChecksum(Exception? __exception, TimingState? __state)
        {
            RecordSaveChecksum(__state);
            return __exception;
        }

        private static void RecordSaveChecksum(TimingState? state)
        {
            if (state is null || Interlocked.Exchange(ref state.Recorded, 1) != 0)
            {
                return;
            }

            try
            {
                s_stages[SaveChecksum].Record(
                    Stopwatch.GetTimestamp() - state.StartTicks,
                    GC.GetTotalMemory(false) - state.ManagedBytes,
                    GC.CollectionCount(0) - state.Gen0Collections,
                    GC.CollectionCount(1) - state.Gen1Collections,
                    GC.CollectionCount(2) - state.Gen2Collections);
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "save checksum callback");
            }
        }

        private static void BeforeTimed(MethodBase __originalMethod, out TimingState? __state)
        {
            __state = null;
            try
            {
                if (TimedTargets.TryGetValue(__originalMethod, out string stage) &&
                    (stage == LoadHeaders || stage == SceneGarbageCollection))
                {
                    RecordLifecycleCheckpoint(stage + "-start");
                }
                __state = new TimingState(Stopwatch.GetTimestamp(), GC.GetTotalMemory(false), __originalMethod);
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "timing prefix");
            }
        }

        private static void AfterTimed(TimingState? __state)
        {
            Record(__state);
        }

        private static Exception? FinalizeTimed(Exception? __exception, TimingState? __state)
        {
            Record(__state);
            return __exception;
        }

        private static void Record(TimingState? state)
        {
            if (state is null || Interlocked.Exchange(ref state.Recorded, 1) != 0)
            {
                return;
            }
            try
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - state.StartTicks;
                if (s_initializationTargets.TryGetValue(state.Method, out NamedTimingAccumulator? initialization))
                {
                    initialization.Record(elapsedTicks);
                }
                if (TimedTargets.TryGetValue(state.Method, out string stage))
                {
                    s_stages[stage].Record(
                        elapsedTicks,
                        GC.GetTotalMemory(false) - state.ManagedBytes,
                        GC.CollectionCount(0) - state.Gen0Collections,
                        GC.CollectionCount(1) - state.Gen1Collections,
                        GC.CollectionCount(2) - state.Gen2Collections);
                    if (stage == LoadHeaders || stage == LoadDeserialization || stage == LoadResolverFinalization ||
                        stage == LoadFinalize || stage == SceneGarbageCollection)
                    {
                        RecordLifecycleCheckpoint(stage + "-complete");
                    }
                }
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "timing callback");
            }
        }

        private static string Format(RuntimeProfileSnapshot snapshot)
        {
            ProductRendererMetric products = snapshot.Products;
            var builder = new StringBuilder(1536)
                .Append("Runtime profile '").Append(snapshot.Label).Append("' [sequence=")
                .Append(snapshot.Sequence).Append(", reset generation=").Append(snapshot.ResetGeneration)
                .Append(", captured=").Append(snapshot.CapturedUtc.ToString("O")).Append(']')
                .Append("\nStage maxima cover the interval since the previous capture or counter reset.")
                .Append("\nProcess: working-set=").Append(FormatOptionalBytes(snapshot.ProcessWorkingSetBytes))
                .Append(", private=").Append(FormatOptionalBytes(snapshot.ProcessPrivateBytes))
                .Append(", CPU=").Append(snapshot.ProcessCpuMilliseconds < 0 ? "unavailable" : snapshot.ProcessCpuMilliseconds + " ms")
                .Append("\nMemory: managed=").Append(FormatBytes(snapshot.ManagedBytes))
                .Append(", Mono used=").Append(FormatOptionalBytes(snapshot.MonoUsedBytes))
                .Append(", Mono heap=").Append(FormatOptionalBytes(snapshot.MonoHeapBytes))
                .Append(", Unity allocated=").Append(FormatOptionalBytes(snapshot.UnityAllocatedBytes))
                .Append(", Unity reserved=").Append(FormatOptionalBytes(snapshot.UnityReservedBytes))
                .Append(", Unity unused reserved=").Append(FormatOptionalBytes(snapshot.UnityUnusedReservedBytes))
                .Append(", graphics=").Append(FormatUnityGraphicsBytes(snapshot.UnityGraphicsBytes, products));

            foreach (string stage in s_stageOrder)
            {
                AppendStage(builder, stage, snapshot.Stages[stage]);
            }

            if (products.Available)
            {
                builder.Append("\nProducts renderer: GPU=").Append(FormatBytes(products.GpuBytes))
                    .Append(" [instances=").Append(FormatBytes(products.InstancesBytes))
                    .Append(", static owners=").Append(FormatBytes(products.StaticOwnersBytes))
                    .Append(", dynamic owners=").Append(FormatBytes(products.DynamicOwnersBytes))
                    .Append(", slots=").Append(FormatBytes(products.SlotsBytes))
                    .Append(", textures=").Append(FormatBytes(products.TexturesBytes)).Append(']')
                    .Append(", instances CPU/GPU=").Append(products.Instances).Append('/').Append(products.GpuInstances)
                    .Append(", live buffer used/capacity=").Append(products.LiveBufferUsed).Append('/').Append(products.LiveBufferCapacity)
                    .Append(", reserve buffer used/capacity=").Append(products.ReserveBufferUsed).Append('/').Append(products.ReserveBufferCapacity)
                    .Append(", slots live/high-water/capacity=").Append(products.LiveSlots).Append('/')
                    .Append(products.HighWaterSlots).Append('/').Append(products.CapacitySlots)
                    .Append(", total free/unused capacity=").Append(products.TotalFreeSlots).Append('/')
                    .Append(products.UnusedCapacitySlots)
                    .Append(", utilization=").Append(products.Utilization.ToString("F1", CultureInfo.InvariantCulture)).Append('%')
                    .Append(", fragmented slots/ranges/largest=").Append(products.FragmentedSlots).Append('/')
                    .Append(products.FreeRangeCount).Append('/').Append(products.LargestFreeRange);
            }
            else
            {
                builder.Append("\nProducts renderer: unavailable (").Append(products.Reason.Trim()).Append(')');
            }

            if (snapshot.GcPasses.Count > 0)
            {
                builder.Append("\nRecent scene-cleanup GC passes:");
                foreach (GcPassMetric pass in snapshot.GcPasses)
                {
                    builder.Append("\n  #").Append(pass.Sequence)
                        .Append(": ").Append(pass.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms")
                        .Append(", reclaimed=").Append(FormatBytes(pass.ReclaimedBytes))
                        .Append(", before/after=").Append(FormatBytes(pass.BeforeBytes)).Append('/')
                        .Append(FormatBytes(pass.AfterBytes))
                        .Append(", GC0/1/2=").Append(pass.Gen0Collections).Append('/')
                        .Append(pass.Gen1Collections).Append('/').Append(pass.Gen2Collections);
                }
            }
            return builder.ToString();
        }

        private static void AppendStage(StringBuilder builder, string name, StageMetric metric)
        {
            builder.Append("\n  ").Append(name).Append(": count=").Append(metric.Count)
                .Append(", total=").Append(metric.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(", max=").Append(metric.MaxMilliseconds.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(", managed delta=").Append(FormatBytes(metric.ManagedBytesDelta))
                .Append(", GC0/1/2=").Append(metric.Gen0Collections).Append('/')
                .Append(metric.Gen1Collections).Append('/').Append(metric.Gen2Collections);
        }

        private static void AppendGcInterval(
            StringBuilder builder,
            RuntimeProfileSnapshot first,
            RuntimeProfileSnapshot second)
        {
            IReadOnlyList<GcPassMetric> passes = SelectGcPassInterval(
                second.GcPasses,
                first.GcPassSequence,
                second.GcPassSequence);
            long expectedCount = Math.Max(0, second.GcPassSequence - first.GcPassSequence);
            if (passes.Count == 0)
            {
                builder.Append(expectedCount == 0
                    ? "\nScene-cleanup GC interval: no passes."
                    : "\nScene-cleanup GC interval: unavailable (bounded pass history rolled over).");
                return;
            }

            long totalTicks = passes.Sum(x => x.ElapsedTicks);
            long maxTicks = passes.Max(x => x.ElapsedTicks);
            long reclaimed = passes.Sum(x => x.ReclaimedBytes);
            int dryPasses = passes.Count(x => x.ReclaimedBytes <= 16L * 1024 * 1024);
            builder.Append("\nScene-cleanup GC interval: passes=").Append(passes.Count);
            if (passes.Count < expectedCount)
            {
                builder.Append('/').Append(expectedCount).Append(" (history truncated)");
            }
            builder.Append(", total=")
                .Append((totalTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(", max=")
                .Append((maxTicks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture)).Append(" ms")
                .Append(", reclaimed=").Append(FormatBytes(reclaimed))
                .Append(", vanilla-dry passes=").Append(dryPasses);
        }

        internal static IReadOnlyList<GcPassMetric> SelectGcPassInterval(
            IReadOnlyList<GcPassMetric> available,
            long afterSequence,
            long throughSequence) =>
            available
                .Where(x => x.Sequence > afterSequence && x.Sequence <= throughSequence)
                .OrderBy(x => x.Sequence)
                .ToArray();

        private static void ReadProcessMemory(out long workingSet, out long privateBytes, out long cpuMilliseconds)
        {
            workingSet = privateBytes = cpuMilliseconds = -1;
            try
            {
                using Process process = Process.GetCurrentProcess();
                workingSet = process.WorkingSet64;
                privateBytes = process.PrivateMemorySize64;
                cpuMilliseconds = (long)process.TotalProcessorTime.TotalMilliseconds;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "process memory snapshot");
            }
        }

        private static UnityMemorySnapshot ReadUnityMemorySnapshot()
        {
            var result = new UnityMemorySnapshot(-1, -1, -1, -1, -1, -1);
            try
            {
                Type? profiler = FindType("UnityEngine.Profiling.Profiler", "UnityEngine.CoreModule");
                if (profiler is null)
                {
                    return result;
                }
                return new UnityMemorySnapshot(
                    TryInvokeLong(profiler, "GetTotalAllocatedMemoryLong"),
                    TryInvokeLong(profiler, "GetTotalReservedMemoryLong"),
                    TryInvokeLong(profiler, "GetTotalUnusedReservedMemoryLong"),
                    TryInvokeLong(profiler, "GetAllocatedMemoryForGraphicsDriver"),
                    TryInvokeLong(profiler, "GetMonoUsedSizeLong"),
                    TryInvokeLong(profiler, "GetMonoHeapSizeLong"));
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "Unity memory snapshot");
                return result;
            }
        }

        private static long TryInvokeLong(Type type, string method)
        {
            try
            {
                MethodInfo? target = type.GetMethod(method, BindingFlags.Static | BindingFlags.Public);
                return target is null
                    ? -1
                    : Convert.ToInt64(target.Invoke(null, null), CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
        }

        private static int ReadIntProperty(Type type, object instance, string name) =>
            Convert.ToInt32(type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance), CultureInfo.InvariantCulture);

        private static long ReadLongField(Type type, object instance, string name) =>
            Convert.ToInt64(type.GetField(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance), CultureInfo.InvariantCulture);

        private static void ReadInstanceBuffer(
            Type rendererType,
            object renderer,
            string bufferFieldName,
            string usedFieldName,
            out int used,
            out int capacity)
        {
            used = Convert.ToInt32(
                rendererType.GetField(usedFieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer),
                CultureInfo.InvariantCulture);
            object? buffer = rendererType.GetField(bufferFieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer);
            capacity = buffer is null
                ? 0
                : Convert.ToInt32(buffer.GetType().GetProperty("count", BindingFlags.Instance | BindingFlags.Public)!.GetValue(buffer), CultureInfo.InvariantCulture);
        }

        private static Type? FindType(string fullName, string assemblyName) =>
            Type.GetType(fullName + ", " + assemblyName, false) ??
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, assemblyName, StringComparison.Ordinal))
                ?.GetType(fullName, false);

        private static void ReadSlotFragmentation(
            Type rendererType,
            object renderer,
            out int fragmentedSlots,
            out int freeRanges,
            out int largestFreeRange)
        {
            fragmentedSlots = freeRanges = largestFreeRange = 0;
            object? slotBuffer = rendererType.GetField("m_slotBuffer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(renderer);
            object? freeList = slotBuffer?.GetType().GetField("m_freeList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(slotBuffer);
            if (freeList is not IEnumerable ranges)
            {
                return;
            }

            foreach (object range in ranges)
            {
                int count = Convert.ToInt32(range.GetType().GetField("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(range), CultureInfo.InvariantCulture);
                fragmentedSlots += count;
                freeRanges++;
                largestFreeRange = Math.Max(largestFreeRange, count);
            }
        }

        private static ProductRendererMetric UnavailableProducts(string reason) =>
            new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, reason);

        private static string FormatBytes(long bytes)
        {
            double absolute = Math.Abs((double)bytes);
            if (absolute >= 1024 * 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " GiB";
            }
            if (absolute >= 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " MiB";
            }
            if (absolute >= 1024)
            {
                return (bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " KiB";
            }
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        private static string TicksToMilliseconds(long ticks) =>
            (ticks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms";

        private static string FormatOptionalBytes(long value) => value < 0 ? "unavailable" : FormatBytes(value);

        private static string FormatOptionalBytes(long right, long left) =>
            right < 0 || left < 0 ? "unavailable" : FormatBytes(right - left);

        private static string FormatOptionalMilliseconds(long right, long left) =>
            right < 0 || left < 0
                ? "unavailable"
                : (right - left).ToString(CultureInfo.InvariantCulture) + " ms";

        private static string FormatLifecycleCheckpoint(LifecycleCheckpoint checkpoint) =>
            $"  {checkpoint.Label} [sequence={checkpoint.Sequence}, captured={checkpoint.CapturedUtc:O}; " +
            $"working-set={FormatOptionalBytes(checkpoint.ProcessWorkingSetBytes)}, " +
            $"private={FormatOptionalBytes(checkpoint.ProcessPrivateBytes)}, " +
            $"CPU={(checkpoint.ProcessCpuMilliseconds < 0 ? "unavailable" : checkpoint.ProcessCpuMilliseconds + " ms")}, " +
            $"managed={FormatBytes(checkpoint.ManagedBytes)}, " +
            $"Mono={FormatOptionalBytes(checkpoint.MonoUsedBytes)}/{FormatOptionalBytes(checkpoint.MonoHeapBytes)}, " +
            $"Unity={FormatOptionalBytes(checkpoint.UnityAllocatedBytes)}/{FormatOptionalBytes(checkpoint.UnityReservedBytes)}, " +
            $"unused-reserved={FormatOptionalBytes(checkpoint.UnityUnusedReservedBytes)}, " +
            $"graphics={FormatOptionalBytes(checkpoint.UnityGraphicsBytes)}, " +
            $"GC0/1/2={checkpoint.Gen0Collections}/{checkpoint.Gen1Collections}/{checkpoint.Gen2Collections}]";

        private static string BuildHarmonyAudit()
        {
            var lines = new List<string>();
            int duplicateCount = 0;
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().OrderBy(x => x.DeclaringType?.FullName).ThenBy(x => x.Name))
            {
                Patches? patches = Harmony.GetPatchInfo(original);
                duplicateCount += AppendHarmonyAuditLines(lines, original, "prefix", patches?.Prefixes);
                duplicateCount += AppendHarmonyAuditLines(lines, original, "postfix", patches?.Postfixes);
                duplicateCount += AppendHarmonyAuditLines(lines, original, "transpiler", patches?.Transpilers);
                duplicateCount += AppendHarmonyAuditLines(lines, original, "finalizer", patches?.Finalizers);
            }

            return lines.Count == 0
                ? "TajsCOI Harmony audit: no TajsCOI-owned patches found."
                : $"TajsCOI Harmony audit: {lines.Count} owner entries, duplicate registrations={duplicateCount}.\n" + string.Join("\n", lines);
        }

        private static int AppendHarmonyAuditLines(
            ICollection<string> lines,
            MethodBase original,
            string kind,
            IEnumerable<Patch>? patches)
        {
            if (patches is null)
            {
                return 0;
            }

            int duplicates = 0;
            foreach (IGrouping<string, Patch> group in patches
                .Where(x => x.owner.StartsWith("TajsCOI.", StringComparison.Ordinal))
                .GroupBy(x => x.owner, StringComparer.Ordinal))
            {
                int count = group.Count();
                if (count > 1)
                {
                    duplicates++;
                }
                string methods = string.Join(", ", group.Select(x => x.PatchMethod.DeclaringType?.FullName + "." + x.PatchMethod.Name));
                lines.Add($"  {original.DeclaringType?.FullName}.{original.Name} | {kind} | owner={group.Key} | count={count} | methods={methods}" +
                    (count > 1 ? " | DUPLICATE" : string.Empty));
            }
            return duplicates;
        }

        internal static string FormatUnityGraphicsBytes(long unityGraphicsBytes, ProductRendererMetric products)
        {
            if (unityGraphicsBytes < 0)
            {
                return "unavailable";
            }
            if (IsUnityGraphicsInconsistent(unityGraphicsBytes, products))
            {
                return "unavailable/inconsistent";
            }
            return FormatBytes(unityGraphicsBytes);
        }

        private static string FormatUnityGraphicsDelta(RuntimeProfileSnapshot right, RuntimeProfileSnapshot left)
        {
            if (IsUnityGraphicsInconsistent(right.UnityGraphicsBytes, right.Products) ||
                IsUnityGraphicsInconsistent(left.UnityGraphicsBytes, left.Products))
            {
                return "unavailable/inconsistent";
            }
            return FormatOptionalBytes(right.UnityGraphicsBytes, left.UnityGraphicsBytes);
        }

        private static bool IsUnityGraphicsInconsistent(long unityGraphicsBytes, ProductRendererMetric products) =>
            unityGraphicsBytes == 0 && products.Available && products.GpuBytes > 0;

        private static RuntimeProfileSnapshot? FindLocked(string label) =>
            s_history.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.Ordinal));

        private static IReadOnlyList<GcPassMetric> SnapshotGcPasses()
        {
            lock (s_gcPassGate)
            {
                return s_gcPassHistory.ToArray();
            }
        }

        private static void LogCallbackFailure(Exception exception, string operation)
        {
            if (s_loggedCallbackFailures.TryAdd(operation, 0))
            {
                s_log?.Exception(exception, $"Runtime performance {operation} failed; instrumentation remains fail-open.");
            }
        }

        private readonly struct UnityMemorySnapshot
        {
            internal UnityMemorySnapshot(
                long allocatedBytes,
                long reservedBytes,
                long unusedReservedBytes,
                long graphicsBytes,
                long monoUsedBytes,
                long monoHeapBytes)
            {
                AllocatedBytes = allocatedBytes;
                ReservedBytes = reservedBytes;
                UnusedReservedBytes = unusedReservedBytes;
                GraphicsBytes = graphicsBytes;
                MonoUsedBytes = monoUsedBytes;
                MonoHeapBytes = monoHeapBytes;
            }

            internal long AllocatedBytes { get; }
            internal long ReservedBytes { get; }
            internal long UnusedReservedBytes { get; }
            internal long GraphicsBytes { get; }
            internal long MonoUsedBytes { get; }
            internal long MonoHeapBytes { get; }
        }

        private sealed class WeakLifecycleWatch
        {
            internal WeakLifecycleWatch(string label, long sequence, DateTime recordedUtc, WeakReference<object> reference)
            {
                Label = label;
                Sequence = sequence;
                RecordedUtc = recordedUtc;
                Reference = reference;
            }

            internal string Label { get; }
            internal long Sequence { get; }
            internal DateTime RecordedUtc { get; }
            internal WeakReference<object> Reference { get; }
        }

        private sealed class NamedTimingAccumulator
        {
            private readonly object m_gate = new();
            private long m_count;
            private long m_totalTicks;
            private long m_maxTicks;

            internal NamedTimingAccumulator(string name)
            {
                Name = name;
            }

            internal string Name { get; }

            internal void Record(long ticks)
            {
                if (ticks < 0)
                {
                    return;
                }
                lock (m_gate)
                {
                    m_count++;
                    m_totalTicks += ticks;
                    m_maxTicks = Math.Max(m_maxTicks, ticks);
                }
            }

            internal InitializationTimingMetric Snapshot()
            {
                lock (m_gate)
                {
                    return new InitializationTimingMetric(Name, m_count, m_totalTicks, m_maxTicks);
                }
            }
        }

        private readonly struct InitializationTimingMetric
        {
            internal InitializationTimingMetric(string name, long count, long totalTicks, long maxTicks)
            {
                Name = name;
                Count = count;
                TotalTicks = totalTicks;
                MaxTicks = maxTicks;
            }

            internal string Name { get; }
            internal long Count { get; }
            internal long TotalTicks { get; }
            internal long MaxTicks { get; }
        }

        private sealed class TimingState
        {
            internal readonly long StartTicks;
            internal readonly long ManagedBytes;
            internal readonly MethodBase Method;
            internal readonly int Gen0Collections;
            internal readonly int Gen1Collections;
            internal readonly int Gen2Collections;
            internal int Recorded;

            internal TimingState(long startTicks, long managedBytes, MethodBase method)
            {
                StartTicks = startTicks;
                ManagedBytes = managedBytes;
                Method = method;
                Gen0Collections = GC.CollectionCount(0);
                Gen1Collections = GC.CollectionCount(1);
                Gen2Collections = GC.CollectionCount(2);
            }
        }

        private sealed class TimedIoStream : Stream
        {
            private readonly Stream m_inner;
            private readonly StageAccumulator m_accumulator;
            private int m_disposed;

            internal TimedIoStream(Stream inner, StageAccumulator accumulator)
            {
                m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
                m_accumulator = accumulator ?? throw new ArgumentNullException(nameof(accumulator));
            }

            public override bool CanRead => m_inner.CanRead;
            public override bool CanSeek => m_inner.CanSeek;
            public override bool CanWrite => m_inner.CanWrite;
            public override long Length => m_inner.Length;
            public override long Position
            {
                get => m_inner.Position;
                set => m_inner.Position = value;
            }

            public override void Flush()
            {
                long started = Stopwatch.GetTimestamp();
                try
                {
                    m_inner.Flush();
                }
                finally
                {
                    RecordElapsed(started, "gzip flush timing");
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                long started = Stopwatch.GetTimestamp();
                try
                {
                    return m_inner.Read(buffer, offset, count);
                }
                finally
                {
                    RecordElapsed(started, "gzip read timing");
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => m_inner.Seek(offset, origin);
            public override void SetLength(long value) => m_inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                long started = Stopwatch.GetTimestamp();
                try
                {
                    m_inner.Write(buffer, offset, count);
                }
                finally
                {
                    RecordElapsed(started, "gzip write timing");
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    long started = Stopwatch.GetTimestamp();
                    try
                    {
                        m_inner.Dispose();
                    }
                    finally
                    {
                        RecordElapsed(started, "gzip dispose timing");
                    }
                }
                base.Dispose(disposing);
            }

            private void RecordElapsed(long started, string operation)
            {
                try
                {
                    m_accumulator.Record(Stopwatch.GetTimestamp() - started, 0);
                }
                catch (Exception exception)
                {
                    LogCallbackFailure(exception, operation);
                }
            }
        }

        private sealed class CallbackState
        {
            internal int Recorded;
        }

        private sealed class GcPassState
        {
            internal GcPassState(long startTicks, long beforeBytes, int gen0, int gen1, int gen2)
            {
                StartTicks = startTicks;
                BeforeBytes = beforeBytes;
                Gen0 = gen0;
                Gen1 = gen1;
                Gen2 = gen2;
            }

            internal long StartTicks { get; }
            internal long BeforeBytes { get; }
            internal int Gen0 { get; }
            internal int Gen1 { get; }
            internal int Gen2 { get; }
        }

        private readonly struct PatchSummary
        {
            internal PatchSummary(int requiredExpected, int requiredInstalled, int optionalExpected, int optionalInstalled)
            {
                RequiredExpected = requiredExpected;
                RequiredInstalled = requiredInstalled;
                OptionalExpected = optionalExpected;
                OptionalInstalled = optionalInstalled;
            }

            internal int RequiredExpected { get; }
            internal int RequiredInstalled { get; }
            internal int OptionalExpected { get; }
            internal int OptionalInstalled { get; }
        }
    }
}
