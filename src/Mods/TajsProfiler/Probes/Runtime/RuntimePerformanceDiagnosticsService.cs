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

        internal const string SaveSerialization = "save.serialization";
        internal const string SaveFinalize = "save.compression-write-total";
        internal const string SaveChecksum = "save.checksum-nested";
        internal const string ChecksumValidation = "load.checksum-validation";
        internal const string LoadHeaders = "load.decompress-headers-config";
        internal const string LoadFinalize = "load.deserialize-resolve-finalize";
        internal const string LoadDeserialization = "load.deserialization";
        internal const string LoadResolverFinalization = "load.resolver-finalization";
        internal const string SceneGarbageCollection = "scene.cleanup-full-gc";

        private static readonly string[] s_stageOrder =
        {
            SaveSerialization,
            SaveFinalize,
            SaveChecksum,
            ChecksumValidation,
            LoadHeaders,
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

        private static ITajsLogger? s_log;
        private static int s_callbackErrorLogged;
        private static long s_sequence;
        private static bool s_patchAttempted;
        private static PatchSummary s_patchSummary;

        private readonly DependencyResolver m_resolver;

        [ThreadStatic]
        private static int s_saveDataDepth;

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
                        .Append(", slots used/capacity=")
                        .Append(second.Products.UsedSlots - first.Products.UsedSlots).Append('/')
                        .Append(second.Products.CapacitySlots - first.Products.CapacitySlots);
                }
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
                stages);
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
                return new ProductRendererMetric(
                    true,
                    ReadIntProperty(rendererType, renderer, "StatInstances"),
                    ReadIntProperty(rendererType, renderer, "StatGpuInstances"),
                    ReadIntProperty(rendererType, renderer, "StatSlots"),
                    ReadIntProperty(rendererType, renderer, "StatSlotCapacity"),
                    fragmentedSlots,
                    freeRanges,
                    largestFreeRange,
                    Convert.ToInt64(memoryType.GetProperty("GpuTotalBytes")!.GetValue(memory), CultureInfo.InvariantCulture),
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
                required += PatchIterator(harmony, "Mafi.Core.SaveGame.GameLoader", "Mafi.Core", "FinishGameLoadAndDisposeTimeSliced", LoadFinalize) ? 1 : 0;
                required += PatchTimed(harmony, "Mafi.DependencyResolver", "Mafi", "DeserializeInto", LoadDeserialization) ? 1 : 0;
                required += PatchIterator(harmony, "Mafi.Serialization.BlobReader", "Mafi", "FinalizeLoadingTimeSliced", LoadResolverFinalization) ? 1 : 0;
                optional += PatchTimed(harmony, "Mafi.Unity.Main", "Mafi.Unity", "collectGarbageDrainingFinalizers", SceneGarbageCollection) ? 1 : 0;

                s_patchSummary = new PatchSummary(9, required, 1, optional);
                s_patchAttempted = true;
                return s_patchSummary;
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

        private static readonly ConcurrentDictionary<MethodBase, string> TimedTargets = new();

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
                    .Append(", instances CPU/GPU=").Append(products.Instances).Append('/').Append(products.GpuInstances)
                    .Append(", slots used/capacity/unused=").Append(products.UsedSlots).Append('/')
                    .Append(products.CapacitySlots).Append('/').Append(products.UnusedSlots)
                    .Append(", utilization=").Append(products.Utilization.ToString("F1", CultureInfo.InvariantCulture)).Append('%')
                    .Append(", fragmented slots/ranges/largest=").Append(products.FragmentedSlots).Append('/')
                    .Append(products.FreeRangeCount).Append('/').Append(products.LargestFreeRange);
            }
            else
            {
                builder.Append("\nProducts renderer: unavailable (").Append(products.Reason.Trim()).Append(')');
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
            new(false, 0, 0, 0, 0, 0, 0, 0, 0, reason);

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

        private sealed class CallbackState
        {
            internal int Recorded;
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
