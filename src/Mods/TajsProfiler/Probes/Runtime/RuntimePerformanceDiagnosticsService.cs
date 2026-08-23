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

        internal const string SaveSerialization = "save.serialization";
        internal const string SaveFinalize = "save.compression-write-total";
        internal const string SaveCompression = "save.compression-io";
        internal const string SaveChecksum = "save.checksum-nested";
        internal const string ChecksumValidation = "load.checksum-validation";
        internal const string LoadHeaders = "load.headers-config";
        internal const string LoadDecompression = "load.decompression-io";
        internal const string LoadFinalize = "load.deserialize-resolve-finalize";
        internal const string LoadDeserialization = "load.deserialization";
        internal const string LoadResolverFinalization = "load.resolver-finalization";
        internal const string SceneGarbageCollection = "scene.cleanup-full-gc";

        private static readonly string[] s_stageOrder =
        {
            SaveSerialization,
            SaveFinalize,
            SaveCompression,
            SaveChecksum,
            ChecksumValidation,
            LoadHeaders,
            LoadDecompression,
            LoadFinalize,
            LoadDeserialization,
            LoadResolverFinalization,
            SceneGarbageCollection,
        };

        private static readonly Dictionary<string, StageAccumulator> s_stages =
            s_stageOrder.ToDictionary(x => x, _ => new StageAccumulator(), StringComparer.Ordinal);
        private static readonly object s_historyGate = new();
        private static readonly object s_patchGate = new();
        private static readonly List<RuntimeProfileSnapshot> s_history = new(MaxHistory);
        private static readonly object s_gcPassGate = new();
        private static readonly List<GcPassMetric> s_gcPassHistory = new(MaxGcPassHistory);

        private static ITajsLogger? s_log;
        private static int s_callbackErrorLogged;
        private static long s_sequence;
        private static long s_gcPassSequence;
        private static bool s_patchAttempted;
        private static PatchSummary s_patchSummary;

        private readonly DependencyResolver m_resolver;

        [ThreadStatic]
        private static int s_saveDataDepth;

        [ThreadStatic]
        private static int s_mainSaveFinalizeDepth;

        [ThreadStatic]
        private static int s_mainLoadStartDepth;

        [ThreadStatic]
        private static GcPassState? s_gcPassState;

        public RuntimePerformanceDiagnosticsService(DependencyResolver resolver, ITajsRuntime runtime)
        {
            m_resolver = resolver;
            s_log = runtime.GetLogger("TajsProfiler", "RuntimePerformance");

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

                var builder = new StringBuilder(1024)
                    .Append("Runtime profile comparison: ").Append(first.Label).Append(" -> ").Append(second.Label)
                    .Append("\nMemory delta: managed=").Append(FormatBytes(second.ManagedBytes - first.ManagedBytes))
                    .Append(", Unity allocated=").Append(FormatOptionalBytes(second.UnityAllocatedBytes, first.UnityAllocatedBytes))
                    .Append(", Unity reserved=").Append(FormatOptionalBytes(second.UnityReservedBytes, first.UnityReservedBytes))
                    .Append(", graphics=").Append(FormatOptionalBytes(second.UnityGraphicsBytes, first.UnityGraphicsBytes));

                foreach (string stage in s_stageOrder)
                {
                    StageMetric delta = second.Stages[stage] - first.Stages[stage];
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
            foreach (StageAccumulator stage in s_stages.Values)
            {
                stage.Reset();
            }
            return "Runtime profile counters reset; stored captures unchanged.";
        }

        private RuntimeProfileSnapshot CreateSnapshot(string label)
        {
            var stages = new Dictionary<string, StageMetric>(StringComparer.Ordinal);
            foreach (string stage in s_stageOrder)
            {
                stages.Add(stage, s_stages[stage].Snapshot());
            }

            ReadUnityMemory(out long unityAllocated, out long unityReserved, out long unityGraphics);
            return new RuntimeProfileSnapshot(
                label,
                Interlocked.Increment(ref s_sequence),
                DateTime.UtcNow,
                GC.GetTotalMemory(false),
                unityAllocated,
                unityReserved,
                unityGraphics,
                ReadProductsRenderer(),
                stages,
                SnapshotGcPasses(),
                Interlocked.Read(ref s_gcPassSequence));
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
                return UnavailableProducts(exception.GetType().Name);
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
                int required = 0;
                int optional = 0;

                required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameSaver", "Mafi.Core", "StartSave", SaveSerialization) ? 1 : 0;
                required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameSaver", "Mafi.Core", "FinishSaveWriteToStream", SaveFinalize) ? 1 : 0;
                required += PatchSaveChecksum(harmony) ? 1 : 0;
                required += PatchTimed(harmony, "Mafi.Core.SaveGame.SaveLoadFileUtils", "Mafi.Core", "ValidateChecksum", ChecksumValidation, typeof(string)) ? 1 : 0;
                required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameLoader", "Mafi.Core", "StartGameLoad", LoadHeaders) ? 1 : 0;
                required += PatchTimed(harmony, "Mafi.Core.SaveGame.GameLoader", "Mafi.Core", "ContinueGameLoad", LoadHeaders) ? 1 : 0;
                required += PatchCompressionIo(
                    harmony,
                    "Mafi.Core.SaveGame.GameSaver",
                    "FinishSaveWriteToStream",
                    "CreateCompressingStream",
                    nameof(BeginMainSaveFinalize),
                    nameof(EndMainSaveFinalize),
                    nameof(WrapCompressingStream)) ? 1 : 0;
                required += PatchCompressionIo(
                    harmony,
                    "Mafi.Core.SaveGame.GameLoader",
                    "StartGameLoad",
                    "CreateDecompressingStream",
                    nameof(BeginMainLoadStart),
                    nameof(EndMainLoadStart),
                    nameof(WrapDecompressingStream)) ? 1 : 0;
                required += PatchIterator(harmony, "Mafi.Core.SaveGame.GameLoader", "Mafi.Core", "FinishGameLoadAndDisposeTimeSliced", LoadFinalize) ? 1 : 0;
                required += PatchTimed(harmony, "Mafi.DependencyResolver", "Mafi", "DeserializeInto", LoadDeserialization) ? 1 : 0;
                required += PatchIterator(harmony, "Mafi.Serialization.BlobReader", "Mafi", "FinalizeLoadingTimeSliced", LoadResolverFinalization) ? 1 : 0;
                optional += PatchTimed(harmony, "Mafi.Unity.Main", "Mafi.Unity", "collectGarbageDrainingFinalizers", SceneGarbageCollection) ? 1 : 0;
                optional += PatchSceneGcPasses(harmony) ? 1 : 0;

                s_patchSummary = new PatchSummary(11, required, 2, optional);
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
                GC.Collect(generation, mode, blocking, compacting);
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

        private static void BeginMainLoadStart() => s_mainLoadStartDepth++;

        private static Exception? EndMainLoadStart(Exception? __exception)
        {
            s_mainLoadStartDepth = Math.Max(0, s_mainLoadStartDepth - 1);
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

        private static void WrapDecompressingStream(ref Stream __result)
        {
            try
            {
                if (s_mainLoadStartDepth > 0 && __result is not TimedIoStream)
                {
                    __result = new TimedIoStream(__result, s_stages[LoadDecompression]);
                }
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "wrap load decompression stream");
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
                if (TimedTargets.TryGetValue(state.Method, out string stage))
                {
                    s_stages[stage].Record(
                        Stopwatch.GetTimestamp() - state.StartTicks,
                        GC.GetTotalMemory(false) - state.ManagedBytes,
                        GC.CollectionCount(0) - state.Gen0Collections,
                        GC.CollectionCount(1) - state.Gen1Collections,
                        GC.CollectionCount(2) - state.Gen2Collections);
                }
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "timing callback");
            }
        }

        private static string Format(RuntimeProfileSnapshot snapshot)
        {
            var builder = new StringBuilder(1536)
                .Append("Runtime profile '").Append(snapshot.Label).Append("' [sequence=")
                .Append(snapshot.Sequence).Append(", captured=").Append(snapshot.CapturedUtc.ToString("O")).Append(']')
                .Append("\nMemory: managed=").Append(FormatBytes(snapshot.ManagedBytes))
                .Append(", Unity allocated=").Append(FormatOptionalBytes(snapshot.UnityAllocatedBytes))
                .Append(", Unity reserved=").Append(FormatOptionalBytes(snapshot.UnityReservedBytes))
                .Append(", graphics=").Append(FormatOptionalBytes(snapshot.UnityGraphicsBytes));

            foreach (string stage in s_stageOrder)
            {
                AppendStage(builder, stage, snapshot.Stages[stage]);
            }

            ProductRendererMetric products = snapshot.Products;
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

        private static void ReadUnityMemory(out long allocated, out long reserved, out long graphics)
        {
            allocated = reserved = graphics = -1;
            try
            {
                Type? profiler = FindType("UnityEngine.Profiling.Profiler", "UnityEngine.CoreModule");
                if (profiler is null)
                {
                    return;
                }
                allocated = InvokeLong(profiler, "GetTotalAllocatedMemoryLong");
                reserved = InvokeLong(profiler, "GetTotalReservedMemoryLong");
                graphics = InvokeLong(profiler, "GetAllocatedMemoryForGraphicsDriver");
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "Unity memory snapshot");
            }
        }

        private static long InvokeLong(Type type, string method) =>
            Convert.ToInt64(type.GetMethod(method, BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null), CultureInfo.InvariantCulture);

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

        private static string FormatOptionalBytes(long value) => value < 0 ? "unavailable" : FormatBytes(value);

        private static string FormatOptionalBytes(long right, long left) =>
            right < 0 || left < 0 ? "unavailable" : FormatBytes(right - left);

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
            if (Interlocked.Exchange(ref s_callbackErrorLogged, 1) == 0)
            {
                s_log?.Exception(exception, $"Runtime performance {operation} failed; instrumentation remains fail-open.");
            }
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

            public override void Flush() => Measure(m_inner.Flush);

            public override int Read(byte[] buffer, int offset, int count)
            {
                int result = 0;
                Measure(() => result = m_inner.Read(buffer, offset, count));
                return result;
            }

            public override long Seek(long offset, SeekOrigin origin) => m_inner.Seek(offset, origin);
            public override void SetLength(long value) => m_inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count) =>
                Measure(() => m_inner.Write(buffer, offset, count));

            protected override void Dispose(bool disposing)
            {
                if (disposing && Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    Measure(m_inner.Dispose);
                }
                base.Dispose(disposing);
            }

            private void Measure(Action operation)
            {
                long started;
                long managedBefore;
                int gen0;
                int gen1;
                int gen2;
                try
                {
                    started = Stopwatch.GetTimestamp();
                    managedBefore = GC.GetTotalMemory(false);
                    gen0 = GC.CollectionCount(0);
                    gen1 = GC.CollectionCount(1);
                    gen2 = GC.CollectionCount(2);
                }
                catch (Exception exception)
                {
                    LogCallbackFailure(exception, "gzip I/O timing prefix");
                    operation();
                    return;
                }
                try
                {
                    operation();
                }
                finally
                {
                    try
                    {
                        m_accumulator.Record(
                            Stopwatch.GetTimestamp() - started,
                            GC.GetTotalMemory(false) - managedBefore,
                            GC.CollectionCount(0) - gen0,
                            GC.CollectionCount(1) - gen1,
                            GC.CollectionCount(2) - gen2);
                    }
                    catch (Exception exception)
                    {
                        LogCallbackFailure(exception, "gzip I/O timing result");
                    }
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
