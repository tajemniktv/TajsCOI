// Taj's COI Mods | PathabilityInitializationFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance.Features.PathabilityInitialization
{
    /// <summary>
    ///     Opt-in candidate for the load-only ship pathability pass. The exact 0.8.7b load seam
    ///     performs a full-map blocking scan before any ship query can consume it. This candidate
    ///     defers that scan until the first pathability query, then runs the exact vanilla methods
    ///     synchronously and once. It is disabled by default because first-query latency and
    ///     thread/lifecycle behavior require real-game A/B validation.
    /// </summary>
    internal sealed class PathabilityInitializationFeature : IPerformanceFeature
    {
        private const string HarmonyId = "TajsCOI.Performance.PathabilityInitialization";
        private const string ProviderTypeName = "Mafi.Core.PathFinding.ShipsClearancePathabilityProvider";

        private static readonly object s_installGate = new();
        private static readonly ConditionalWeakTable<object, State> s_states = new();
        private static MethodInfo? s_computeInitialBlocking;
        private static MethodInfo? s_updateChangedTiles;
        private static int s_patchAttempted;

        [ThreadStatic]
        private static int s_loadInitializationDepth;

        [ThreadStatic]
        private static bool s_forceOriginal;

        public string Id => "PathabilityInitialization";

        public string ConfigKey => PathabilityInitializationSettings.EnableConfigKey;

        public bool IsProcessPatchInstalled() => HasProcessPatchInstalled();

        private static bool HasProcessPatchInstalled()
        {
            TargetSet? targets = FindTargets();
            MethodInfo? skip = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(SkipInitialBlocking));
            MethodInfo? ensure = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(EnsureInitialBlocking));
            MethodInfo? finalizeBlocking = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(FinalizeInitialBlocking));
            MethodInfo? beginLoad = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(BeginLoadInitialization));
            MethodInfo? finalizeLoad = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(FinalizeLoadInitialization));
            return targets is not null && skip is not null && ensure is not null && finalizeBlocking is not null &&
                   beginLoad is not null && finalizeLoad is not null &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.ComputeInitialBlocking)?.Prefixes, HarmonyId, skip) &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.ComputeInitialBlocking)?.Finalizers, HarmonyId, finalizeBlocking) &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.InitSelf)?.Prefixes, HarmonyId, beginLoad) &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.InitSelf)?.Finalizers, HarmonyId, finalizeLoad) &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.ComputeAllPathability)?.Prefixes, HarmonyId, ensure) &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.IsChunkBlocked)?.Prefixes, HarmonyId, ensure) &&
                   ProcessHarmonyPatchOwnership.HasExpected(Harmony.GetPatchInfo(targets.GetValidNeighboursForTile)?.Prefixes, HarmonyId, ensure);
        }

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            TargetSet targets = FindTargets() ?? throw new MissingMethodException(
                ProviderTypeName,
                "initSelf(int, DependencyResolver), computeInitialBlocking(), computeAllPathability(Tile2i, bool), and isChunkBlocked(Tile2i, int)");
            MethodInfo skip = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(SkipInitialBlocking))!;
            MethodInfo ensure = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(EnsureInitialBlocking))!;
            MethodInfo finalizeBlocking = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(FinalizeInitialBlocking))!;
            MethodInfo beginLoad = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(BeginLoadInitialization))!;
            MethodInfo finalizeLoad = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(FinalizeLoadInitialization))!;

            lock (s_installGate)
            {
                if (IsProcessPatchInstalled())
                {
                    runtime.ReportCompatibility(
                        new CompatibilityReport(
                            "TajsPerformance",
                            Id,
                            CompatibilityState.Compatible,
                            "Existing process-lifetime Harmony owner on exact 0.8.7b ship pathability seams",
                            "Already installed / compatible",
                            "No duplicate deferral prefixes were registered."));
                    return;
                }
                if (System.Threading.Interlocked.Exchange(ref s_patchAttempted, 1) != 0)
                {
                    return;
                }

                try
                {
                    InstallPatches(targets, skip, ensure, finalizeBlocking, beginLoad, finalizeLoad);
                }
                catch (Exception exception)
                {
                    System.Threading.Interlocked.Exchange(ref s_patchAttempted, 0);
                    throw new InvalidOperationException(
                        "Pathability initialization candidate installation failed open; vanilla initialization remains active.",
                        exception);
                }
            }

            log.Info(
                "Opt-in pathability initialization deferral installed; the exact vanilla blocking pass runs synchronously before the first ship pathability query.");
            runtime.ReportCompatibility(
                new CompatibilityReport(
                    "TajsPerformance",
                    Id,
                    CompatibilityState.Compatible,
                    "Exact 0.8.7b ship pathability load and query seams",
                    "Load-time blocking scan deferral installed",
                    "The full-map pass is deferred until first query and then runs once; first-query latency and correctness require in-game validation."));
        }

        /// <summary>
        ///     Installs the process-lifetime candidate from the data-only mod constructor when the
        ///     persisted opt-in is already true. This runs before dependency resolution creates
        ///     gameplay-scene services, so the load-time provider seam is actually covered.
        /// </summary>
        internal static bool TryInstallProcessEarly()
        {
            try
            {
                TargetSet? targets = FindTargets();
                if (targets is null)
                {
                    return false;
                }

                MethodInfo? skip = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(SkipInitialBlocking));
                MethodInfo? ensure = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(EnsureInitialBlocking));
                MethodInfo? finalizeBlocking = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(FinalizeInitialBlocking));
                MethodInfo? beginLoad = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(BeginLoadInitialization));
                MethodInfo? finalizeLoad = AccessTools.Method(typeof(PathabilityInitializationFeature), nameof(FinalizeLoadInitialization));
                if (skip is null || ensure is null || finalizeBlocking is null || beginLoad is null || finalizeLoad is null)
                {
                    return false;
                }

                lock (s_installGate)
                {
                    if (HasProcessPatchInstalled())
                    {
                        return true;
                    }

                    if (System.Threading.Interlocked.CompareExchange(ref s_patchAttempted, 1, 0) != 0)
                    {
                        return false;
                    }

                    try
                    {
                        InstallPatches(targets, skip, ensure, finalizeBlocking, beginLoad, finalizeLoad);
                        return true;
                    }
                    catch
                    {
                        System.Threading.Interlocked.Exchange(ref s_patchAttempted, 0);
                        return false;
                    }
                }
            }
            catch
            {
                // The early path is deliberately fail-open. The scene host can retry later and
                // vanilla initialization remains active when the private seam is unavailable.
                return false;
            }
        }

        private static void InstallPatches(
            TargetSet targets,
            MethodInfo skip,
            MethodInfo ensure,
            MethodInfo finalizeBlocking,
            MethodInfo beginLoad,
            MethodInfo finalizeLoad)
        {
            s_computeInitialBlocking = targets.ComputeInitialBlocking;
            s_updateChangedTiles = targets.UpdateChangedTiles;
            var harmony = new Harmony(HarmonyId);
            try
            {
                harmony.Patch(
                    targets.InitSelf,
                    prefix: new HarmonyMethod(beginLoad),
                    finalizer: new HarmonyMethod(finalizeLoad));
                harmony.Patch(
                    targets.ComputeInitialBlocking,
                    prefix: new HarmonyMethod(skip),
                    finalizer: new HarmonyMethod(finalizeBlocking));
                harmony.Patch(
                    targets.ComputeAllPathability,
                    prefix: new HarmonyMethod(ensure));
                harmony.Patch(
                    targets.IsChunkBlocked,
                    prefix: new HarmonyMethod(ensure));
                harmony.Patch(
                    targets.GetValidNeighboursForTile,
                    prefix: new HarmonyMethod(ensure));
            }
            catch
            {
                harmony.UnpatchAll(HarmonyId);
                s_computeInitialBlocking = null;
                s_updateChangedTiles = null;
                throw;
            }
        }

        /// <summary>
        ///     Compatibility alias retained for focused contract tests and callers that only need
        ///     the primary private method. Full candidate installation uses <see cref="FindTargets" />.
        /// </summary>
        internal static MethodInfo? FindTarget() => FindTargets()?.ComputeInitialBlocking;

        internal static TargetSet? FindTargets()
        {
            Type? provider = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, "Mafi.Core", StringComparison.Ordinal))
                ?.GetType(ProviderTypeName, throwOnError: false, ignoreCase: false);
            if (provider is null)
            {
                return null;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Type tile = typeof(Tile2i);
            Type resolver = typeof(DependencyResolver);
            Type pfNodeInfo = Type.GetType("Mafi.Core.PathFinding.PfNodeInfo, Mafi.Core") ?? throw new TypeLoadException("Mafi.Core.PathFinding.PfNodeInfo");
            Type pathfinderMode = Type.GetType("Mafi.Core.PathFinding.ShipsPathFinderMode, Mafi.Core") ??
                                  throw new TypeLoadException("Mafi.Core.PathFinding.ShipsPathFinderMode");
            Type edgeCache = typeof(Lyst<>).MakeGenericType(pfNodeInfo);
            MethodInfo? initSelf = FindMethod(provider, flags, "initSelf", typeof(void), typeof(int), resolver);
            MethodInfo? computeInitial = FindMethod(provider, flags, "computeInitialBlocking", typeof(void));
            MethodInfo? computeAll = FindMethod(provider, flags, "computeAllPathability", typeof(bool), tile, typeof(bool));
            MethodInfo? chunkBlocked = FindMethod(provider, flags, "isChunkBlocked", typeof(bool), tile, typeof(int));
            MethodInfo? updateChangedTiles = FindMethod(provider, flags, "UpdateChangedTiles", typeof(void));
            MethodInfo? getValidNeighbours = FindMethod(
                provider,
                flags,
                "GetValidNeighboursForTile",
                typeof(void),
                pfNodeInfo.MakeByRefType(),
                typeof(int),
                typeof(int),
                typeof(bool),
                edgeCache,
                pathfinderMode,
                typeof(Nullable<>).MakeGenericType(tile));
            if (initSelf is null || computeInitial is null || computeAll is null || chunkBlocked is null ||
                getValidNeighbours is null || updateChangedTiles is null)
            {
                return null;
            }

            return new TargetSet(initSelf, computeInitial, computeAll, chunkBlocked, getValidNeighbours, updateChangedTiles);
        }

        private static MethodInfo? FindMethod(Type type, BindingFlags flags, string name, Type returnType, params Type[] parameters)
        {
            MethodInfo[] methods = type.GetMethods(flags)
                .Where(x => x.Name == name && !x.IsStatic && x.ReturnType == returnType &&
                            x.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters))
                .ToArray();
            return methods.Length == 1 ? methods[0] : null;
        }

        private static void BeginLoadInitialization() => s_loadInitializationDepth++;

        private static Exception? FinalizeLoadInitialization(Exception? __exception)
        {
            if (s_loadInitializationDepth > 0)
            {
                s_loadInitializationDepth--;
            }
            return __exception;
        }

        private static bool SkipInitialBlocking(object __instance)
        {
            if (s_forceOriginal)
            {
                return true;
            }

            try
            {
                State state = s_states.GetValue(__instance, _ => new State());
                lock (state.Gate)
                {
                    if (state.Failed)
                    {
                        // The candidate has already failed for this provider. Let the exact
                        // vanilla method run on subsequent queries instead of retrying a
                        // reflection/thread-sensitive deferred pass forever.
                        return true;
                    }
                    if (state.Initialized)
                    {
                        state.SkippedDuplicateCalls++;
                        return false;
                    }
                    if (s_loadInitializationDepth > 0)
                    {
                        state.Deferred = true;
                        return false;
                    }
                    return true;
                }
            }
            catch
            {
                // A state-table/lock failure must preserve the vanilla call.
                return true;
            }
        }

        private static Exception? FinalizeInitialBlocking(
            Exception? __exception,
            object __instance,
            bool __runOriginal)
        {
            if (!__runOriginal || __exception is not null)
            {
                return __exception;
            }

            try
            {
                State state = s_states.GetValue(__instance, _ => new State());
                lock (state.Gate)
                {
                    state.Initialized = true;
                    state.Deferred = false;
                    System.Threading.Monitor.PulseAll(state.Gate);
                }
            }
            catch
            {
                // Preserve the original result if optional state bookkeeping fails.
            }
            return __exception;
        }

        private static void EnsureInitialBlocking(object __instance)
        {
            if (s_forceOriginal || s_computeInitialBlocking is null || s_updateChangedTiles is null)
            {
                return;
            }

            State? state = null;
            try
            {
                state = s_states.GetValue(__instance, _ => new State());
                lock (state.Gate)
                {
                    while (state.Initializing)
                    {
                        System.Threading.Monitor.Wait(state.Gate);
                    }
                    if (!state.Deferred || state.Initialized)
                    {
                        return;
                    }
                    state.Initializing = true;
                }

                try
                {
                    s_forceOriginal = true;
                    s_computeInitialBlocking.Invoke(__instance, null);
                    s_updateChangedTiles.Invoke(__instance, null);
                    lock (state.Gate)
                    {
                        state.Initialized = true;
                        state.Deferred = false;
                    }
                }
                finally
                {
                    s_forceOriginal = false;
                    lock (state.Gate)
                    {
                        state.Initializing = false;
                        System.Threading.Monitor.PulseAll(state.Gate);
                    }
                }
            }
            catch
            {
                // A reflection or first-query failure must not throw from the pathfinding query.
                // Mark the candidate terminally failed and replay the exact vanilla pass once;
                // this prevents an uninitialized provider and avoids repeated expensive retries.
                try
                {
                    if (state is null)
                    {
                        return;
                    }
                    lock (state.Gate)
                    {
                        state.Failed = true;
                        state.Deferred = false;
                        state.Initialized = false;
                    }
                    s_forceOriginal = true;
                    s_computeInitialBlocking.Invoke(__instance, null);
                    s_updateChangedTiles.Invoke(__instance, null);
                    lock (state.Gate)
                    {
                        state.Failed = false;
                        state.Initialized = true;
                    }
                }
                catch
                {
                    // Leave Failed=true so future calls use the vanilla path without another
                    // candidate retry. A partially unavailable provider remains fail-open.
                }
                finally
                {
                    s_forceOriginal = false;
                }
            }
        }

        private sealed class State
        {
            internal readonly object Gate = new();
            internal bool Deferred;
            internal bool Initializing;
            internal bool Initialized;
            internal bool Failed;
            internal long SkippedDuplicateCalls;
        }

        internal sealed class TargetSet
        {
            internal TargetSet(
                MethodInfo initSelf,
                MethodInfo computeInitialBlocking,
                MethodInfo computeAllPathability,
                MethodInfo isChunkBlocked,
                MethodInfo getValidNeighboursForTile,
                MethodInfo updateChangedTiles)
            {
                InitSelf = initSelf;
                ComputeInitialBlocking = computeInitialBlocking;
                ComputeAllPathability = computeAllPathability;
                IsChunkBlocked = isChunkBlocked;
                GetValidNeighboursForTile = getValidNeighboursForTile;
                UpdateChangedTiles = updateChangedTiles;
            }

            internal MethodInfo InitSelf { get; }
            internal MethodInfo ComputeInitialBlocking { get; }
            internal MethodInfo ComputeAllPathability { get; }
            internal MethodInfo IsChunkBlocked { get; }
            internal MethodInfo GetValidNeighboursForTile { get; }
            internal MethodInfo UpdateChangedTiles { get; }
        }
    }
}
