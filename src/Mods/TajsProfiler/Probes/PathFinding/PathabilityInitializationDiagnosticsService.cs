// Taj's COI Mods | PathabilityInitializationDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.PathFinding;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Profiler.Probes.PathFinding
{
    /// <summary>
    ///     Coarse, behavior-neutral measurements for the three pathability/connectivity load seams.
    ///     The probe records only bounded primitive summaries; it never changes pathfinding state.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class PathabilityInitializationDiagnosticsService
    {
        private const string HarmonyId = "TajsCOI.Profiler.PathabilityInitialization";
        private const string ClearanceInit = "ClearancePathabilityProvider.initSelf";
        private const string ClearanceChunkRecompute = "ClearancePathabilityProvider.DataChunk.RecomputeCoreData";
        private const string ClearanceConnectNeighbors = "ClearancePathabilityProvider.connectNeighbors";
        private const string ShipsInit = "ShipsClearancePathabilityProvider.initSelf";
        private const string ShipsInitialBlocking = "ShipsClearancePathabilityProvider.computeInitialBlocking";
        private const string ConnectivityInit = "VehiclesConnectivityManager.initAfterLoad";
        private const string ConnectivityNodeRestore = "VehiclesConnectivityManager.GetOrCreatePfNodeAt";

        private static readonly object s_metricsGate = new();
        private static readonly Dictionary<string, PathabilityAccumulator> s_metrics =
            new(StringComparer.Ordinal)
            {
                [ClearanceInit] = new PathabilityAccumulator(ClearanceInit, "O(area)"),
                [ClearanceChunkRecompute] = new PathabilityAccumulator(ClearanceChunkRecompute, "O(area)"),
                [ClearanceConnectNeighbors] = new PathabilityAccumulator(ClearanceConnectNeighbors, "O(graph)"),
                [ShipsInit] = new PathabilityAccumulator(ShipsInit, "O(area)"),
                [ShipsInitialBlocking] = new PathabilityAccumulator(ShipsInitialBlocking, "O(area) + O(entities-per-cell)"),
                [ConnectivityInit] = new PathabilityAccumulator(ConnectivityInit, "O(graph)"),
                [ConnectivityNodeRestore] = new PathabilityAccumulator(ConnectivityNodeRestore, "O(graph nodes)"),
            };

        private static readonly ConcurrentDictionary<MethodBase, string> s_targets = new();
        private static readonly FieldInfo? s_connectivityProviderField =
            typeof(VehiclesConnectivityManager).GetField(
                "m_pathabilityProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_providerDataField =
            typeof(ClearancePathabilityProvider).GetField(
                "m_data",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? s_getPfData = typeof(ClearancePathabilityProvider.DataChunk).GetMethod(
            "GetPfData",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(int) },
            null);
        private static readonly MethodInfo? s_allocatedBytesMethod = typeof(GC).GetMethod(
            "GetAllocatedBytesForCurrentThread",
            BindingFlags.Static | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        private static ITajsLogger? s_log;
        private static int s_patchAttempted;
        private static int s_patched;

        [ThreadStatic]
        private static PathabilityProbeState? s_connectivityState;

        public PathabilityInitializationDiagnosticsService(ITajsRuntime runtime)
        {
#pragma warning disable S2696
            s_log = runtime.GetLogger("TajsProfiler", "PathabilityInitialization");
#pragma warning restore S2696

            TargetSet? targets = FindTargets();
            if (targets is null)
            {
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        "TajsProfiler",
                        "PathabilityInitialization",
                        CompatibilityState.Disabled,
                        "Exact 0.8.7b pathability/connectivity initialization seams",
                        "One or more required methods were missing or ambiguous",
                        "No pathability instrumentation was installed; vanilla initialization remains active."));
                return;
            }

            try
            {
                Install(targets);
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        "TajsProfiler",
                        "PathabilityInitialization",
                        CompatibilityState.Compatible,
                        "Exact 0.8.7b pathability/connectivity initialization seams",
                        $"{s_patched} target(s) patched",
                        "Coarse dimensions, cell, graph, entity-input, and allocation/managed-delta summaries are available."));
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Pathability initialization probes failed open.");
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        "TajsProfiler",
                        "PathabilityInitialization",
                        CompatibilityState.Disabled,
                        "Exact 0.8.7b pathability/connectivity initialization seams",
                        exception.GetType().Name,
                        "Probe installation failed open; vanilla initialization remains active."));
            }
        }

        [ConsoleCommand(
            documentation: "Reports bounded 0.8.7b pathability/connectivity initialization measurements.",
            customCommandName: "tajs_profiler_pathability")]
        public string Report()
        {
            PathabilityMetricSnapshot[] snapshots;
            lock (s_metricsGate)
            {
                snapshots = s_metrics.Values
                    .Select(x => x.Snapshot())
                    .Where(x => x.Count > 0)
                    .OrderByDescending(x => x.MaxTicks)
                    .ToArray();
            }

            if (snapshots.Length == 0)
            {
                return "Pathability initialization: no samples captured.";
            }

            StringBuilder builder = new StringBuilder(2048)
                .Append("Pathability initialization (exact 0.8.7b; nested timings are non-additive):");
            foreach (PathabilityMetricSnapshot snapshot in snapshots)
            {
                builder.Append("\n  ")
                    .Append(snapshot.Name)
                    .Append(" count=").Append(snapshot.Count)
                    .Append(", total=").Append(FormatMilliseconds(snapshot.TotalTicks))
                    .Append(", max=").Append(FormatMilliseconds(snapshot.MaxTicks))
                    .Append(", dims=").Append(snapshot.Width).Append('x').Append(snapshot.Height)
                    .Append(", chunks=").Append(snapshot.Chunks)
                    .Append(", cells=").Append(snapshot.Cells)
                    .Append(", graph-nodes=").Append(snapshot.GraphNodes)
                    .Append(", graph-edges=").Append(snapshot.GraphEdges)
                    .Append(", entity-inputs=").Append(snapshot.EntityInputs)
                    .Append(", saved-node-inputs=").Append(snapshot.SavedNodeInputs)
                    .Append(", allocated-or-managed-delta=").Append(FormatBytes(snapshot.ManagedBytesDelta))
                    .Append(", scale=").Append(snapshot.Scale);
            }

            builder.Append("\nClassification: ships blocking is a full-map O(area) scan with O(entities) occupancy inputs; clearance chunk work is O(area) over loaded chunks; connectivity restore is O(graph) in saved nodes/edges.");
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Clears pathability/connectivity initialization measurements.",
            customCommandName: "tajs_profiler_pathability_clear")]
        public string Clear()
        {
            lock (s_metricsGate)
            {
                foreach (PathabilityAccumulator accumulator in s_metrics.Values)
                {
                    accumulator.Clear();
                }
            }
            return "Pathability initialization measurements cleared.";
        }

        /// <summary>
        ///     Resolves every pathability target consumed by this probe. This is deliberately strict:
        ///     a missing or ambiguous private signature disables this probe rather than guessing.
        /// </summary>
        internal static TargetSet? FindTargets()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Type? clearance = typeof(ClearancePathabilityProvider);
            Type? dataChunk = clearance.GetNestedType("DataChunk", BindingFlags.Public | BindingFlags.NonPublic);
            Type? ships = typeof(ShipsClearancePathabilityProvider);
            Type? connectivity = typeof(VehiclesConnectivityManager);
            if (dataChunk is null)
            {
                return null;
            }

            MethodInfo? clearanceInit = FindMethod(clearance, flags, "initSelf", typeof(DependencyResolver));
            MethodInfo? recompute = FindMethod(dataChunk, flags, "RecomputeCoreData");
            MethodInfo? connect = FindMethod(clearance, flags, "connectNeighbors", dataChunk);
            MethodInfo? shipsInit = FindMethod(ships, flags, "initSelf", typeof(int), typeof(DependencyResolver));
            MethodInfo? initialBlocking = FindMethod(ships, flags, "computeInitialBlocking");
            MethodInfo? connectivityInit = FindMethod(connectivity, flags, "initAfterLoad", typeof(int), typeof(DependencyResolver));
            MethodInfo? getOrCreate = FindAnyMethod(
                clearance,
                flags,
                "GetOrCreatePfNodeAt",
                typeof(Tile2i),
                typeof(int));

            if (clearanceInit is null || recompute is null || connect is null || shipsInit is null ||
                initialBlocking is null || connectivityInit is null || getOrCreate is null ||
                getOrCreate.ReturnType == typeof(void))
            {
                return null;
            }

            return new TargetSet(
                clearanceInit,
                recompute,
                connect,
                shipsInit,
                initialBlocking,
                connectivityInit,
                getOrCreate);
        }

        private static MethodInfo? FindMethod(Type type, BindingFlags flags, string name, params Type[] parameterTypes)
        {
            MethodInfo[] methods = type.GetMethods(flags)
                .Where(method => method.Name == name && !method.IsStatic && method.ReturnType == typeof(void) &&
                                 method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes))
                .ToArray();
            return methods.Length == 1 ? methods[0] : null;
        }

        private static MethodInfo? FindAnyMethod(Type type, BindingFlags flags, string name, params Type[] parameterTypes)
        {
            MethodInfo[] methods = type.GetMethods(flags)
                .Where(method => method.Name == name && !method.IsStatic &&
                                 method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes))
                .ToArray();
            return methods.Length == 1 ? methods[0] : null;
        }

        private static void Install(TargetSet targets)
        {
            if (System.Threading.Interlocked.Exchange(ref s_patchAttempted, 1) != 0)
            {
                return;
            }

            var harmony = new Harmony(HarmonyId);
            try
            {
                PatchTimed(harmony, targets.ClearanceInit, ClearanceInit);
                PatchTimed(harmony, targets.RecomputeCoreData, ClearanceChunkRecompute);
                PatchTimed(harmony, targets.ConnectNeighbors, ClearanceConnectNeighbors);
                PatchTimed(harmony, targets.ShipsInit, ShipsInit);
                PatchTimed(harmony, targets.ComputeInitialBlocking, ShipsInitialBlocking);
                PatchTimed(harmony, targets.ConnectivityInit, ConnectivityInit);
                s_targets[targets.GetOrCreatePfNodeAt] = ConnectivityNodeRestore;
                harmony.Patch(
                    targets.GetOrCreatePfNodeAt,
                    prefix: new HarmonyMethod(typeof(PathabilityInitializationDiagnosticsService), nameof(CountConnectivityNode)));
                s_patched = 7;
            }
            catch
            {
                harmony.UnpatchAll(HarmonyId);
                s_targets.Clear();
                s_patched = 0;
                throw;
            }
        }

        private static void PatchTimed(Harmony harmony, MethodInfo target, string name)
        {
            s_targets[target] = name;
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(PathabilityInitializationDiagnosticsService), nameof(BeforeTimed)),
                finalizer: new HarmonyMethod(typeof(PathabilityInitializationDiagnosticsService), nameof(FinalizeTimed)));
        }

        private static void BeforeTimed(MethodBase __originalMethod, object __instance, out PathabilityProbeState? __state)
        {
            __state = null;
            try
            {
                if (!s_targets.TryGetValue(__originalMethod, out string? name) || !s_metrics.ContainsKey(name))
                {
                    return;
                }

                __state = new PathabilityProbeState(
                    name,
                    __instance,
                    Stopwatch.GetTimestamp(),
                    GC.GetTotalMemory(false),
                    ReadAllocatedBytes(),
                    ReadObservation(__instance, name));
                if (name == ConnectivityInit)
                {
                    s_connectivityState = __state;
                }
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "pathability probe prefix");
            }
        }

        private static Exception? FinalizeTimed(
            Exception? __exception,
            PathabilityProbeState? __state,
            bool __runOriginal)
        {
            if (__state is null)
            {
                return __exception;
            }

            try
            {
                if (__state.Name == ConnectivityInit && ReferenceEquals(s_connectivityState, __state))
                {
                    s_connectivityState = null;
                }
                if (!__runOriginal)
                {
                    return __exception;
                }

                PathabilityObservation observation = __state.Observation;
                if (__state.Name == ConnectivityInit)
                {
                    long graphNodes = CountConnectivityGraph(__state.Instance, out long graphEdges);
                    observation.GraphNodes = graphNodes;
                    observation.GraphEdges = graphEdges;
                    observation.SavedNodeInputs = __state.RestoredNodeInputs;
                }

                long endAllocated = ReadAllocatedBytes();
                long allocatedDelta = __state.AllocatedBytes >= 0 && endAllocated >= __state.AllocatedBytes
                    ? endAllocated - __state.AllocatedBytes
                    : -1;
                Record(
                    __state.Name,
                    observation,
                    Stopwatch.GetTimestamp() - __state.StartTicks,
                    GC.GetTotalMemory(false) - __state.ManagedBytes,
                    allocatedDelta);
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "pathability probe finalizer");
            }

            return __exception;
        }

        private static void CountConnectivityNode()
        {
            try
            {
                s_connectivityState?.IncrementRestoredNodeInputs();
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "pathability node counter");
            }
        }

        private static PathabilityObservation ReadObservation(object instance, string name)
        {
            object? terrain = GetMember(instance, "TerrainManager");
            if (name == ClearanceChunkRecompute && terrain is null)
            {
                terrain = GetMember(GetMember(instance, "Parent"), "TerrainManager");
            }
            int width = ReadInt(terrain, "TerrainWidth");
            int height = ReadInt(terrain, "TerrainHeight");
            long chunks = name == ClearanceInit
                ? ReadChunkLoadCount(instance)
                : name == ClearanceChunkRecompute
                    ? 1
                    : name == ShipsInit || name == ShipsInitialBlocking
                        ? (long)(width >> 2) * (height >> 2)
                        : 0;
            long cells = name == ClearanceChunkRecompute
                ? 64
                : name == ClearanceInit
                    ? chunks * 64
                    : name == ShipsInitialBlocking || name == ShipsInit
                        ? (long)width * height
                        : 0;
            long entities = name == ShipsInitialBlocking || name == ShipsInit
                ? ReadEntityCount(instance)
                : 0;
            long savedNodes = name == ConnectivityInit ? ReadCollectionCount(instance, "m_savedNodeTiles") : 0;
            return new PathabilityObservation(width, height, chunks, cells, 0, 0, entities, savedNodes);
        }

        private static long ReadChunkLoadCount(object instance)
        {
            long count = ReadOptionArrayCount(GetField(instance, "m_chunkIndicesToLoad"));
            if (count > 0)
            {
                return count;
            }
            return ReadNonNullArrayCount(GetField(instance, "m_data"));
        }

        private static long ReadEntityCount(object instance)
        {
            object? manager = GetField(instance, "m_entitiesManager");
            object? entities = GetMember(manager, "Entities");
            return ReadIntMember(entities, "Count");
        }

        private static long CountConnectivityGraph(object manager, out long edges)
        {
            edges = 0;
            try
            {
                object? provider = s_connectivityProviderField?.GetValue(manager);
                if (provider is null || s_providerDataField is null || s_getPfData is null)
                {
                    return 0;
                }

                Array? data = s_providerDataField.GetValue(provider) as Array;
                int capabilities = ReadIntMember(provider, "PathabilityCapabilitiesCount");
                if (data is null || capabilities <= 0)
                {
                    return 0;
                }

                long nodesCount = 0;
                for (int dataIndex = 0; dataIndex < data.Length; dataIndex++)
                {
                    object? chunk = ReadOptionValue(data.GetValue(dataIndex));
                    if (chunk is null)
                    {
                        continue;
                    }

                    for (int capability = 0; capability < capabilities; capability++)
                    {
                        object? pfDataOption = s_getPfData.Invoke(chunk, new object[] { capability });
                        object? pfData = ReadOptionValue(pfDataOption);
                        object? nodes = GetMember(pfData, "Nodes");
                        // ReadOnlyArraySlice<T> intentionally exposes only the foreach pattern
                        // (it does not implement IEnumerable).  Use its allocation-free
                        // AsEnumerable bridge only for this post-init diagnostic summary.
                        IEnumerable? enumerable = nodes as IEnumerable;
                        if (enumerable is null && nodes is not null)
                        {
                            MethodInfo? asEnumerable = nodes.GetType().GetMethod(
                                "AsEnumerable",
                                BindingFlags.Instance | BindingFlags.Public,
                                null,
                                Type.EmptyTypes,
                                null);
                            enumerable = asEnumerable?.Invoke(nodes, null) as IEnumerable;
                        }
                        if (enumerable is null)
                        {
                            continue;
                        }

                        foreach (object? node in enumerable)
                        {
                            if (node is null || ReadBoolMember(node, "IsDestroyed"))
                            {
                                continue;
                            }
                            nodesCount++;
                            object? neighbours = GetMember(node, "CurrentNeighbors");
                            edges += ReadIntMember(neighbours, "Length");
                        }
                    }
                }
                return nodesCount;
            }
            catch (Exception exception)
            {
                LogCallbackFailure(exception, "pathability graph summary");
                edges = 0;
                return 0;
            }
        }

        private static void Record(
            string name,
            PathabilityObservation observation,
            long elapsedTicks,
            long managedDelta,
            long allocatedDelta)
        {
            if (!s_metrics.TryGetValue(name, out PathabilityAccumulator? accumulator))
            {
                return;
            }
            accumulator.Record(observation, elapsedTicks, managedDelta, allocatedDelta);
        }

        private static object? GetMember(object? instance, string name)
        {
            if (instance is null)
            {
                return null;
            }
            Type type = instance.GetType();
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance) ??
                   type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
        }

        private static object? GetField(object? instance, string name) =>
            instance?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);

        private static int ReadInt(object? instance, string name) => ReadIntMember(instance, name);

        private static int ReadIntMember(object? instance, string name)
        {
            object? value = GetMember(instance, name);
            return value switch
            {
                int intValue => intValue,
                long longValue when longValue <= int.MaxValue && longValue >= int.MinValue => (int)longValue,
                _ => 0,
            };
        }

        private static bool ReadBoolMember(object? instance, string name) => GetMember(instance, name) is bool value && value;

        private static long ReadCollectionCount(object instance, string fieldName)
        {
            object? value = GetField(instance, fieldName);
            return ReadIntMember(value, "Count");
        }

        private static long ReadNonNullArrayCount(object? value)
        {
            if (value is not Array array)
            {
                return 0;
            }
            long count = 0;
            foreach (object? item in array)
            {
                if (ReadOptionValue(item) is not null)
                {
                    count++;
                }
            }
            return count;
        }

        private static long ReadOptionArrayCount(object? value)
        {
            object? array = ReadOptionValue(value);
            return array is Array result ? result.LongLength : 0;
        }

        private static object? ReadOptionValue(object? value)
        {
            if (value is null)
            {
                return null;
            }
            return GetMember(value, "ValueOrNull") ??
                   (ReadBoolMember(value, "HasValue") ? GetMember(value, "Value") : null);
        }

        private static long ReadAllocatedBytes()
        {
            try
            {
                return s_allocatedBytesMethod?.Invoke(null, null) is long value ? value : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static void LogCallbackFailure(Exception exception, string context)
        {
            try
            {
                s_log?.Exception(exception, context + " failed open.");
            }
            catch
            {
                // Diagnostics must never affect the game path.
            }
        }

        private static string FormatMilliseconds(long ticks) =>
            ticks < 0
                ? "unavailable"
                : (ticks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms";

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "unavailable";
            }
            if (bytes >= 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " MiB";
            }
            if (bytes >= 1024)
            {
                return (bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture) + " KiB";
            }
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        internal sealed class TargetSet
        {
            internal TargetSet(
                MethodInfo clearanceInit,
                MethodInfo recomputeCoreData,
                MethodInfo connectNeighbors,
                MethodInfo shipsInit,
                MethodInfo computeInitialBlocking,
                MethodInfo connectivityInit,
                MethodInfo getOrCreatePfNodeAt)
            {
                ClearanceInit = clearanceInit;
                RecomputeCoreData = recomputeCoreData;
                ConnectNeighbors = connectNeighbors;
                ShipsInit = shipsInit;
                ComputeInitialBlocking = computeInitialBlocking;
                ConnectivityInit = connectivityInit;
                GetOrCreatePfNodeAt = getOrCreatePfNodeAt;
            }

            internal MethodInfo ClearanceInit { get; }
            internal MethodInfo RecomputeCoreData { get; }
            internal MethodInfo ConnectNeighbors { get; }
            internal MethodInfo ShipsInit { get; }
            internal MethodInfo ComputeInitialBlocking { get; }
            internal MethodInfo ConnectivityInit { get; }
            internal MethodInfo GetOrCreatePfNodeAt { get; }
        }

        private sealed class PathabilityProbeState
        {
            private long m_restoredNodeInputs;

            internal PathabilityProbeState(
                string name,
                object instance,
                long startTicks,
                long managedBytes,
                long allocatedBytes,
                PathabilityObservation observation)
            {
                Name = name;
                Instance = instance;
                StartTicks = startTicks;
                ManagedBytes = managedBytes;
                AllocatedBytes = allocatedBytes;
                Observation = observation;
            }

            internal string Name { get; }
            internal object Instance { get; }
            internal long StartTicks { get; }
            internal long ManagedBytes { get; }
            internal long AllocatedBytes { get; }
            internal PathabilityObservation Observation { get; }
            internal long RestoredNodeInputs => System.Threading.Interlocked.Read(ref m_restoredNodeInputs);

            internal void IncrementRestoredNodeInputs() => System.Threading.Interlocked.Increment(ref m_restoredNodeInputs);
        }

        private struct PathabilityObservation
        {
            internal PathabilityObservation(
                int width,
                int height,
                long chunks,
                long cells,
                long graphNodes,
                long graphEdges,
                long entityInputs,
                long savedNodeInputs)
            {
                Width = width;
                Height = height;
                Chunks = chunks;
                Cells = cells;
                GraphNodes = graphNodes;
                GraphEdges = graphEdges;
                EntityInputs = entityInputs;
                SavedNodeInputs = savedNodeInputs;
            }

            internal int Width;
            internal int Height;
            internal long Chunks;
            internal long Cells;
            internal long GraphNodes;
            internal long GraphEdges;
            internal long EntityInputs;
            internal long SavedNodeInputs;
        }

        private struct PathabilityMetricSnapshot
        {
            internal PathabilityMetricSnapshot(
                string name,
                string scale,
                long count,
                long totalTicks,
                long maxTicks,
                long width,
                long height,
                long chunks,
                long cells,
                long graphNodes,
                long graphEdges,
                long entityInputs,
                long savedNodeInputs,
                long managedBytesDelta)
            {
                Name = name;
                Scale = scale;
                Count = count;
                TotalTicks = totalTicks;
                MaxTicks = maxTicks;
                Width = width;
                Height = height;
                Chunks = chunks;
                Cells = cells;
                GraphNodes = graphNodes;
                GraphEdges = graphEdges;
                EntityInputs = entityInputs;
                SavedNodeInputs = savedNodeInputs;
                ManagedBytesDelta = managedBytesDelta;
            }

            internal string Name;
            internal string Scale;
            internal long Count;
            internal long TotalTicks;
            internal long MaxTicks;
            internal long Width;
            internal long Height;
            internal long Chunks;
            internal long Cells;
            internal long GraphNodes;
            internal long GraphEdges;
            internal long EntityInputs;
            internal long SavedNodeInputs;
            internal long ManagedBytesDelta;
        }

        private sealed class PathabilityAccumulator
        {
            private readonly object m_gate = new();
            private long m_count;
            private long m_totalTicks;
            private long m_maxTicks;
            private long m_width;
            private long m_height;
            private long m_chunks;
            private long m_cells;
            private long m_graphNodes;
            private long m_graphEdges;
            private long m_entityInputs;
            private long m_savedNodeInputs;
            private long m_managedBytes;

            internal PathabilityAccumulator(string name, string scale)
            {
                Name = name;
                Scale = scale;
            }

            internal string Name { get; }
            internal string Scale { get; }

            internal void Record(PathabilityObservation observation, long elapsedTicks, long managedDelta, long allocatedDelta)
            {
                if (elapsedTicks < 0)
                {
                    return;
                }
                lock (m_gate)
                {
                    m_count++;
                    m_totalTicks += elapsedTicks;
                    m_maxTicks = Math.Max(m_maxTicks, elapsedTicks);
                    m_width = observation.Width;
                    m_height = observation.Height;
                    m_chunks += observation.Chunks;
                    m_cells += observation.Cells;
                    m_graphNodes = observation.GraphNodes;
                    m_graphEdges = observation.GraphEdges;
                    m_entityInputs = observation.EntityInputs;
                    m_savedNodeInputs = observation.SavedNodeInputs;
                    // Prefer the runtime's allocation counter when available.  GC.GetTotalMemory
                    // is only a best-effort fallback because collections can make its delta shrink.
                    if (allocatedDelta >= 0)
                    {
                        m_managedBytes += allocatedDelta;
                    }
                    else if (managedDelta >= 0)
                    {
                        m_managedBytes += managedDelta;
                    }
                }
            }

            internal PathabilityMetricSnapshot Snapshot()
            {
                lock (m_gate)
                {
                    return new PathabilityMetricSnapshot(
                        Name,
                        Scale,
                        m_count,
                        m_totalTicks,
                        m_maxTicks,
                        m_width,
                        m_height,
                        m_chunks,
                        m_cells,
                        m_graphNodes,
                        m_graphEdges,
                        m_entityInputs,
                        m_savedNodeInputs,
                        m_managedBytes);
                }
            }

            internal void Clear()
            {
                lock (m_gate)
                {
                    m_count = 0;
                    m_totalTicks = 0;
                    m_maxTicks = 0;
                    m_width = 0;
                    m_height = 0;
                    m_chunks = 0;
                    m_cells = 0;
                    m_graphNodes = 0;
                    m_graphEdges = 0;
                    m_entityInputs = 0;
                    m_savedNodeInputs = 0;
                    m_managedBytes = 0;
                }
            }
        }
    }
}
