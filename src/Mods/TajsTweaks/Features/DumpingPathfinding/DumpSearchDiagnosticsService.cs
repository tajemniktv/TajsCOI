// Taj's COI Mods | DumpSearchDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ReadonlyCollections;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Console;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.Vehicles.Trucks.JobProviders;

#endregion

namespace TajsTweaks.Features.DumpingPathfinding;

/// <summary>
///     Records low-overhead runtime statistics for dumping-destination searches. This service is
///     intentionally diagnostics-only: it never suppresses, retries, reorders, or changes a
///     vanilla dumping search result. Any instrumentation failure fails open for that call.
///
///     The 0.8.2c and 0.8.7 reference snapshots contain the same search structure. In particular,
///     a globally-forbidden product with no explicit tower list is promoted into the local-tower
///     path when one or more MineTowers accept it. That fallback is the primary forensic distinction
///     recorded here.
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class DumpSearchDiagnosticsService
{
    private const string HarmonyId = "tajemniktv.tajstweaks.dump-search-diagnostics";
    private const string UnknownProductId = "<none>";
    private const int MaxProductsInConsoleReport = 12;

    private static readonly ConcurrentDictionary<string, ProductSearchStats> s_productStats = new(StringComparer.Ordinal);
    private static readonly long[] s_pathCalls = new long[(int)SearchPath.Count];
    private static readonly long[] s_callerCalls = new long[(int)SearchCaller.Count];
    private static readonly long[] s_pathCandidateDesignations = new long[(int)SearchPath.Count];
    private static readonly long[] s_pathCandidateCalls = new long[(int)SearchPath.Count];

    private static readonly FieldInfo? s_towersManagerField = AccessTools.Field(
        typeof(TerrainDumpingManager),
        "m_towersManager");
    private static readonly FieldInfo? s_designationsCacheField = AccessTools.Field(
        typeof(TerrainDumpingManager),
        "m_designationsCache");
    private static readonly FieldInfo? s_landEligibleCacheValidField = AccessTools.Field(
        typeof(TerrainDumpingManager),
        "m_isEligibleDesignationsCacheValid");
    private static readonly FieldInfo? s_oceanEligibleCacheValidField = AccessTools.Field(
        typeof(TerrainDumpingManager),
        "m_isEligibleOceanDesignationsCacheValid");
    private static readonly FieldInfo? s_landTowerEligibleCacheField = AccessTools.Field(
        typeof(TerrainDumpingManager),
        "m_eligibleDesignationsCached");
    private static readonly FieldInfo? s_oceanTowerEligibleCacheField = AccessTools.Field(
        typeof(TerrainDumpingManager),
        "m_eligibleOceanDesignationsCached");

    private static int s_patchesApplied;
    private static int s_tickBoundaryPatchApplied;
    private static int s_pfEnqueuePatchApplied;
    private static int s_callerPatchCount;
    private static int s_cachePatchCount;
    private static int s_prefixErrorLogged;
    private static int s_postfixErrorLogged;
    private static int s_tickErrorLogged;
    private static int s_optionalPatchErrorLogged;

    private static long s_totalCalls;
    private static long s_totalTrueResults;
    private static long s_totalFalseResults;
    private static long s_totalElapsedTicks;
    private static long s_maxElapsedTicks;
    private static long s_currentCalls;
    private static long s_lastCalls;
    private static long s_peakCalls;

    private static long s_totalTowerFilterCandidates;
    private static long s_totalTowerFilterAccepted;
    private static long s_totalCandidateDesignations;
    private static long s_completedCandidateCalls;
    private static long s_maxCandidateDesignations;

    private static long s_globalEligibleCacheHits;
    private static long s_globalEligibleCacheMisses;
    private static long s_towerEligibleCacheCalls;
    private static long s_towerEligibleCacheHits;
    private static long s_towerEligibleCacheMisses;

    private static long s_currentPfEnqueues;
    private static long s_lastPfEnqueues;
    private static long s_peakPfEnqueues;
    private static long s_totalPfEnqueues;

    [ThreadStatic]
    private static SearchCaller s_currentCaller;

    public DumpSearchDiagnosticsService()
    {
        ensurePatchesApplied();
    }

    [ConsoleCommand(
        documentation: "Shows behavior-neutral dumping-search path, caller, cache, candidate and timing diagnostics.",
        customCommandName: "tajs_dump_search_stats")]
    public string GetStats()
    {
        var snapshot = snapshotProductStats();
        snapshot.Sort(static (left, right) => right.Calls.CompareTo(left.Calls));

        var totalCalls = read(ref s_totalCalls);
        var completedCalls = read(ref s_totalTrueResults) + read(ref s_totalFalseResults);
        var incompleteCalls = Math.Max(0, totalCalls - completedCalls);
        var totalMs = stopwatchTicksToMilliseconds(read(ref s_totalElapsedTicks));
        var avgUs = completedCalls > 0
            ? stopwatchTicksToMicroseconds(read(ref s_totalElapsedTicks)) / completedCalls
            : 0.0;

        var builder = new StringBuilder(2048);
        builder.Append("Dump search diagnostics: active=")
            .Append(readInt(ref s_patchesApplied) != 0)
            .Append(", functional limiter=disabled, tick buckets=")
            .Append(readInt(ref s_tickBoundaryPatchApplied) != 0)
            .Append(", PF enqueue diagnostics=")
            .Append(readInt(ref s_pfEnqueuePatchApplied) != 0)
            .Append("; calls current/last/peak=")
            .Append(read(ref s_currentCalls))
            .Append('/')
            .Append(read(ref s_lastCalls))
            .Append('/')
            .Append(read(ref s_peakCalls))
            .Append("; total=")
            .Append(totalCalls)
            .Append("; returned true/false/incomplete=")
            .Append(read(ref s_totalTrueResults))
            .Append('/')
            .Append(read(ref s_totalFalseResults))
            .Append('/')
            .Append(incompleteCalls)
            .Append("; completed-call time=")
            .Append(totalMs.ToString("F2"))
            .Append(" ms, avg=")
            .Append(avgUs.ToString("F1"))
            .Append(" us, max=")
            .Append(stopwatchTicksToMilliseconds(read(ref s_maxElapsedTicks)).ToString("F3"))
            .Append(" ms.");

        builder.Append("\nPaths: ")
            .Append(formatPathCounts())
            .Append("\nCallers: ")
            .Append(formatCallerCounts())
            .Append("\nTower filter candidates/accepted=")
            .Append(read(ref s_totalTowerFilterCandidates))
            .Append('/')
            .Append(read(ref s_totalTowerFilterAccepted))
            .Append(", candidate designations total/calls/max=")
            .Append(read(ref s_totalCandidateDesignations))
            .Append('/')
            .Append(read(ref s_completedCandidateCalls))
            .Append('/')
            .Append(read(ref s_maxCandidateDesignations))
            .Append("\nPath candidate designations total/calls: ")
            .Append(formatPathCandidateCounts())
            .Append("\nCaches: global eligible hit/miss=")
            .Append(read(ref s_globalEligibleCacheHits))
            .Append('/')
            .Append(read(ref s_globalEligibleCacheMisses))
            .Append(", per-tower eligible calls/hit/miss=")
            .Append(read(ref s_towerEligibleCacheCalls))
            .Append('/')
            .Append(read(ref s_towerEligibleCacheHits))
            .Append('/')
            .Append(read(ref s_towerEligibleCacheMisses))
            .Append("\nPF enqueues current/last/peak/total=")
            .Append(read(ref s_currentPfEnqueues))
            .Append('/')
            .Append(read(ref s_lastPfEnqueues))
            .Append('/')
            .Append(read(ref s_peakPfEnqueues))
            .Append('/')
            .Append(read(ref s_totalPfEnqueues))
            .Append(".");

        if (snapshot.Count == 0)
        {
            builder.Append("\nNo dumping searches recorded since the last reset.");
            return builder.ToString();
        }

        builder.Append("\nTop products since reset:");
        var count = Math.Min(snapshot.Count, MaxProductsInConsoleReport);
        for (var i = 0; i < count; i++)
        {
            var stats = snapshot[i];
            var completed = stats.TrueResults + stats.FalseResults;
            var incomplete = Math.Max(0, stats.Calls - completed);
            var productAvgUs = completed > 0
                ? stopwatchTicksToMicroseconds(stats.ElapsedTicks) / completed
                : 0.0;

            builder.Append("\n  ")
                .Append(stats.ProductId)
                .Append(": calls=")
                .Append(stats.Calls)
                .Append(" [")
                .Append(stats.GetPathSummary())
                .Append("; callers=")
                .Append(stats.GetCallerSummary())
                .Append("; tower-filter=")
                .Append(stats.TowerFilterCandidates)
                .Append('/')
                .Append(stats.TowerFilterAccepted)
                .Append("], true/false/incomplete=")
                .Append(stats.TrueResults)
                .Append('/')
                .Append(stats.FalseResults)
                .Append('/')
                .Append(incomplete)
                .Append(", time=")
                .Append(stopwatchTicksToMilliseconds(stats.ElapsedTicks).ToString("F2"))
                .Append(" ms, avg=")
                .Append(productAvgUs.ToString("F1"))
                .Append(" us, max=")
                .Append(stopwatchTicksToMilliseconds(stats.MaxElapsedTicks).ToString("F3"))
                .Append(" ms");
        }

        if (snapshot.Count > count)
            builder.Append("\n  ... ").Append(snapshot.Count - count).Append(" more product(s) omitted.");

        builder.Append("\nNote: the old dumping-search limiter is removed. A globally-forbidden product with a locally accepting tower is reported as a fallback path; all vanilla results continue through unchanged.");
        return builder.ToString();
    }

    [ConsoleCommand(
        documentation: "Compatibility alias for the dumping-search and path-finding diagnostics.",
        customCommandName: "tajs_dump_pf_stats")]
    public string GetPathfindingStats()
    {
        return GetStats();
    }

    [ConsoleCommand(
        documentation: "Resets accumulated dumping-search and path-finding diagnostics.",
        customCommandName: "tajs_dump_search_stats_reset")]
    public string ResetStats()
    {
        resetAllStats();
        return "Dump search diagnostics reset.";
    }

    [ConsoleCommand(
        documentation: "Compatibility alias for resetting dumping-search and path-finding diagnostics.",
        customCommandName: "tajs_dump_pf_stats_reset")]
    public string ResetPathfindingStats()
    {
        resetAllStats();
        return "Dump search diagnostics reset.";
    }

    private static void ensurePatchesApplied()
    {
        if (readInt(ref s_patchesApplied) != 0)
            return;

        var dumpSearch = findInstanceMethod(
            typeof(TerrainDumpingManager),
            "TryFindClosestReadyToDump",
            typeof(bool),
            typeof(Tile2i),
            typeof(Option<LooseProductProto>),
            typeof(Truck),
            typeof(ulong?),
            typeof(TerrainDesignation).MakeByRefType(),
            typeof(IIndexable<MineTower>),
            typeof(bool),
            typeof(Lyst<TerrainDesignation>));

        if (dumpSearch is null)
        {
            Log.Error("TajsTweaks: dump-search diagnostics disabled; exact CoI 0.8.7 dumping-search signature was not found.");
            return;
        }

        var harmony = new Harmony(HarmonyId);
        try
        {
            var prefix = new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beforeDumpSearch))
            {
                priority = Priority.First,
            };
            var postfix = new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(afterDumpSearch))
            {
                priority = Priority.Last,
            };
            harmony.Patch(dumpSearch, prefix: prefix, postfix: postfix);
            Interlocked.Exchange(ref s_patchesApplied, 1);
        }
        catch (Exception ex)
        {
            try
            {
                harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception rollbackException)
            {
                Log.Error($"TajsTweaks: dump-search diagnostics rollback also failed: {rollbackException}");
            }

            Log.Error($"TajsTweaks: dump-search diagnostics failed to patch dumping search: {ex}");
            return;
        }

        patchTickBoundary(harmony);
        patchPathFindingEnqueue(harmony);
        patchCallerMethods(harmony);
        patchCacheMethods(harmony);

        Log.Info(
            $"TajsTweaks: dump-search diagnostics active; functional limiter disabled, PF tick buckets={readInt(ref s_tickBoundaryPatchApplied) != 0}, " +
            $"PF enqueue diagnostics={readInt(ref s_pfEnqueuePatchApplied) != 0}, caller patches={readInt(ref s_callerPatchCount)}, cache patches={readInt(ref s_cachePatchCount)}.");
    }

    private static void patchTickBoundary(Harmony harmony)
    {
        var simUpdate = findInstanceMethod(
            typeof(VehiclePathFindingManager),
            "SimUpdateInternal",
            typeof(void));
        if (simUpdate is null)
        {
            logOptionalPatchFailure("VehiclePathFindingManager.SimUpdateInternal was not found");
            return;
        }

        try
        {
            harmony.Patch(
                simUpdate,
                prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beginPathFindingTick)));
            Interlocked.Exchange(ref s_tickBoundaryPatchApplied, 1);
        }
        catch (Exception ex)
        {
            logOptionalPatchFailure($"PF tick boundary patch failed: {ex}");
        }
    }

    private static void patchPathFindingEnqueue(Harmony harmony)
    {
        var enqueueTask = findInstanceMethod(
            typeof(VehiclePathFindingManager),
            "EnqueueTask",
            typeof(void),
            typeof(IManagedVehiclePathFindingTask),
            typeof(int));
        if (enqueueTask is null)
        {
            logOptionalPatchFailure("VehiclePathFindingManager.EnqueueTask was not found");
            return;
        }

        try
        {
            harmony.Patch(
                enqueueTask,
                prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beforePathFindingEnqueue)));
            Interlocked.Exchange(ref s_pfEnqueuePatchApplied, 1);
        }
        catch (Exception ex)
        {
            logOptionalPatchFailure($"PF enqueue diagnostics patch failed: {ex}");
        }
    }

    private static void patchCallerMethods(Harmony harmony)
    {
        patchCaller(
            harmony,
            findInstanceMethod(
                typeof(VehicleBuffersRegistry),
                "balanceBuffers",
                typeof(void),
                typeof(Percent)),
            nameof(beginVehicleBuffersBalanceBuffers),
            "VehicleBuffersRegistry.balanceBuffers");

        patchCaller(
            harmony,
            findInstanceMethod(
                typeof(DefaultTruckJobProvider),
                "TryGetJobFor",
                typeof(bool),
                typeof(Truck)),
            nameof(beginDefaultTruckJobProvider),
            "DefaultTruckJobProvider.TryGetJobFor");

        patchCaller(
            harmony,
            findUniqueInstanceMethod(typeof(DumpingJob), "handleFindMoreDesignations", 0),
            nameof(beginDumpingJob),
            "DumpingJob.handleFindMoreDesignations");

        var factoryMethods = AccessTools.GetDeclaredMethods(typeof(DumpingJob.Factory));
        foreach (var method in factoryMethods)
        {
            if (method.Name == "TryCreateAndEnqueueJob" && !method.IsStatic)
                patchCaller(harmony, method, nameof(beginDumpingJob), "DumpingJob.Factory.TryCreateAndEnqueueJob");
        }
    }

    private static void patchCaller(Harmony harmony, MethodInfo? method, string prefixName, string label)
    {
        if (method is null)
        {
            logOptionalPatchFailure($"caller patch target not found: {label}");
            return;
        }

        try
        {
            harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), prefixName),
                finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(endCallerContext)));
            Interlocked.Increment(ref s_callerPatchCount);
        }
        catch (Exception ex)
        {
            logOptionalPatchFailure($"caller patch failed for {label}: {ex}");
        }
    }

    private static void patchCacheMethods(Harmony harmony)
    {
        var globalCache = findInstanceMethod(
            typeof(TerrainDumpingManager),
            "getAllEligibleCached",
            typeof(LystStruct<TerrainDesignation>),
            typeof(bool));
        if (globalCache is not null)
        {
            try
            {
                harmony.Patch(
                    globalCache,
                    prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beforeGlobalEligibleCache)));
                Interlocked.Increment(ref s_cachePatchCount);
            }
            catch (Exception ex)
            {
                logOptionalPatchFailure($"global eligible-cache patch failed: {ex}");
            }
        }
        else
        {
            logOptionalPatchFailure("TerrainDumpingManager.getAllEligibleCached was not found");
        }

        var towerCache = findInstanceMethod(
            typeof(TerrainDumpingManager),
            "getAllEligibleCachedFor",
            typeof(Lyst<TerrainDesignation>),
            typeof(MineTower),
            typeof(bool));
        if (towerCache is not null)
        {
            try
            {
                harmony.Patch(
                    towerCache,
                    prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beforeTowerEligibleCache)));
                Interlocked.Increment(ref s_cachePatchCount);
            }
            catch (Exception ex)
            {
                logOptionalPatchFailure($"per-tower eligible-cache patch failed: {ex}");
            }
        }
        else
        {
            logOptionalPatchFailure("TerrainDumpingManager.getAllEligibleCachedFor was not found");
        }
    }

    private static void beforeDumpSearch(
        TerrainDumpingManager __instance,
        Option<LooseProductProto> __1,
        ulong? __3,
        IIndexable<MineTower>? __5,
        out DumpSearchCallState __state)
    {
        __state = default;
        try
        {
            var product = __1.ValueOrNull;
            var path = classifySearch(__instance, product, __3, __5, out var towerFilter);
            var caller = s_currentCaller;
            var productId = product?.Id.Value ?? UnknownProductId;
            var stats = getOrCreateProductStats(productId);

            stats.RecordCall(path, caller, towerFilter.Candidates, towerFilter.Accepted);
            Interlocked.Increment(ref s_pathCalls[(int)path]);
            Interlocked.Increment(ref s_callerCalls[(int)caller]);
            Interlocked.Increment(ref s_totalCalls);
            Interlocked.Increment(ref s_currentCalls);
            Interlocked.Add(ref s_totalTowerFilterCandidates, towerFilter.Candidates);
            Interlocked.Add(ref s_totalTowerFilterAccepted, towerFilter.Accepted);

            __state = new DumpSearchCallState(
                stats,
                __instance,
                path,
                Stopwatch.GetTimestamp());
        }
        catch (Exception ex)
        {
            logOnce(ref s_prefixErrorLogged, "dump-search diagnostics prefix", ex);
        }
    }

    private static void afterDumpSearch(bool __result, DumpSearchCallState __state)
    {
        if (__state.Stats is null)
            return;

        try
        {
            var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - __state.StartTimestamp);
            var candidateCount = getCandidateCount(__state.Manager);
            __state.Stats.RecordResult(__result, elapsedTicks);

            if (__result)
                Interlocked.Increment(ref s_totalTrueResults);
            else
                Interlocked.Increment(ref s_totalFalseResults);

            Interlocked.Add(ref s_totalElapsedTicks, elapsedTicks);
            updateMax(ref s_maxElapsedTicks, elapsedTicks);

            if (candidateCount >= 0)
            {
                Interlocked.Add(ref s_totalCandidateDesignations, candidateCount);
                Interlocked.Increment(ref s_completedCandidateCalls);
                Interlocked.Add(ref s_pathCandidateDesignations[(int)__state.Path], candidateCount);
                Interlocked.Increment(ref s_pathCandidateCalls[(int)__state.Path]);
                updateMax(ref s_maxCandidateDesignations, candidateCount);
            }
        }
        catch (Exception ex)
        {
            logOnce(ref s_postfixErrorLogged, "dump-search diagnostics postfix", ex);
        }
    }

    private static void beforePathFindingEnqueue()
    {
        Interlocked.Increment(ref s_currentPfEnqueues);
        Interlocked.Increment(ref s_totalPfEnqueues);
    }

    private static void beginPathFindingTick()
    {
        try
        {
            var currentCalls = Interlocked.Exchange(ref s_currentCalls, 0);
            var currentPfEnqueues = Interlocked.Exchange(ref s_currentPfEnqueues, 0);
            Interlocked.Exchange(ref s_lastCalls, currentCalls);
            Interlocked.Exchange(ref s_lastPfEnqueues, currentPfEnqueues);
            updateMax(ref s_peakCalls, currentCalls);
            updateMax(ref s_peakPfEnqueues, currentPfEnqueues);
        }
        catch (Exception ex)
        {
            logOnce(ref s_tickErrorLogged, "dump-search diagnostics tick snapshot", ex);
        }
    }

    private static void beforeGlobalEligibleCache(TerrainDumpingManager __instance, bool __0)
    {
        try
        {
            var validField = __0 ? s_oceanEligibleCacheValidField : s_landEligibleCacheValidField;
            var cacheWasValid = validField?.GetValue(__instance) is true;
            if (cacheWasValid)
                Interlocked.Increment(ref s_globalEligibleCacheHits);
            else
                Interlocked.Increment(ref s_globalEligibleCacheMisses);
        }
        catch (Exception ex)
        {
            logOnce(ref s_optionalPatchErrorLogged, "global eligible-cache diagnostics", ex);
        }
    }

    private static void beforeTowerEligibleCache(TerrainDumpingManager __instance, MineTower __0, bool __1)
    {
        try
        {
            Interlocked.Increment(ref s_towerEligibleCacheCalls);
            var field = __1 ? s_oceanTowerEligibleCacheField : s_landTowerEligibleCacheField;
            var cache = field?.GetValue(__instance) as Dict<MineTower, Lyst<TerrainDesignation>>;
            var cacheHit = cache is not null && cache.TryGetValue(__0, out _);
            if (cacheHit)
                Interlocked.Increment(ref s_towerEligibleCacheHits);
            else
                Interlocked.Increment(ref s_towerEligibleCacheMisses);
        }
        catch (Exception ex)
        {
            logOnce(ref s_optionalPatchErrorLogged, "per-tower eligible-cache diagnostics", ex);
        }
    }

    private static SearchPath classifySearch(
        TerrainDumpingManager dumpingManager,
        LooseProductProto? product,
        ulong? zoneMask,
        IIndexable<MineTower>? towersToEnforce,
        out TowerFilterSnapshot towerFilter)
    {
        towerFilter = default;
        if (product is null)
            return SearchPath.UnknownProduct;

        var globalAllowed = dumpingManager.ProductsAllowedToDump.Contains(product);
        var effectiveZoneMask = zoneMask ?? ulong.MaxValue;
        // This mirrors TerrainDumpingManager's `flag2 = towersToEnforce != null`: an empty,
        // non-null list is still an explicit enforced-tower search, not the global fallback.
        if (towersToEnforce is not null)
        {
            towerFilter = inspectExplicitTowers(towersToEnforce, product, effectiveZoneMask);
            if (globalAllowed)
                return SearchPath.ExplicitTower;

            return towerFilter.Accepted > 0
                ? SearchPath.ExplicitTowerGlobalForbiddenLocal
                : SearchPath.ExplicitTowerGlobalForbiddenRejected;
        }

        if (globalAllowed)
            return SearchPath.GlobalAllowed;

        // In both reference snapshots, the vanilla `!flag` branch populates its temporary tower
        // set and then re-enters the per-tower cached-designation path. This is the path whose
        // cost differs sharply from the global eligible-designation cache in the reproduction.
        towerFilter = inspectGlobalTowerFilter(dumpingManager, product, effectiveZoneMask);
        return towerFilter.Accepted > 0
            ? SearchPath.GlobalForbiddenLocalFallback
            : SearchPath.GlobalForbiddenNoLocalTower;
    }

    private static TowerFilterSnapshot inspectExplicitTowers(
        IIndexable<MineTower> towers,
        LooseProductProto product,
        ulong zoneMask)
    {
        var accepted = 0;
        for (var i = 0; i < towers.Count; i++)
        {
            var tower = towers[i];
            if (tower.IsEnabled &&
                tower.ManagedDumpingDesignations.Count > 0 &&
                (tower.ZoneMask & zoneMask) != 0L &&
                tower.DumpableProducts.Contains(product))
            {
                accepted++;
            }
        }

        return new TowerFilterSnapshot(towers.Count, accepted);
    }

    private static TowerFilterSnapshot inspectGlobalTowerFilter(
        TerrainDumpingManager dumpingManager,
        LooseProductProto product,
        ulong zoneMask)
    {
        if (s_towersManagerField?.GetValue(dumpingManager) is not MineTowersManager towersManager)
            return default;

        var towers = towersManager.Towers;
        var accepted = 0;
        for (var i = 0; i < towers.Count; i++)
        {
            var tower = towers[i];
            if (tower.IsEnabled &&
                (tower.ZoneMask & zoneMask) != 0L &&
                (tower.AllowNonAssignedOutput || !tower.HasOutputStorageOrTowerAssigned) &&
                tower.DumpableProducts.Contains(product))
            {
                accepted++;
            }
        }

        return new TowerFilterSnapshot(towers.Count, accepted);
    }

    private static ProductSearchStats getOrCreateProductStats(string productId)
    {
        if (s_productStats.TryGetValue(productId, out var stats))
            return stats;

        var created = new ProductSearchStats(productId);
        return s_productStats.GetOrAdd(productId, created);
    }

    private static List<ProductSearchStats> snapshotProductStats()
    {
        var snapshot = new List<ProductSearchStats>();
        foreach (var stats in s_productStats.Values)
        {
            if (stats.Calls > 0)
                snapshot.Add(stats);
        }

        return snapshot;
    }

    private static int getCandidateCount(TerrainDumpingManager dumpingManager)
    {
        if (s_designationsCacheField?.GetValue(dumpingManager) is Lyst<TerrainDesignation> designations)
            return designations.Count;

        return -1;
    }

    private static void resetAllStats()
    {
        resetDumpSearchStats();
        Interlocked.Exchange(ref s_currentPfEnqueues, 0);
        Interlocked.Exchange(ref s_lastPfEnqueues, 0);
        Interlocked.Exchange(ref s_peakPfEnqueues, 0);
        Interlocked.Exchange(ref s_totalPfEnqueues, 0);
    }

    private static void resetDumpSearchStats()
    {
        foreach (var stats in s_productStats.Values)
            stats.Reset();

        Interlocked.Exchange(ref s_totalCalls, 0);
        Interlocked.Exchange(ref s_totalTrueResults, 0);
        Interlocked.Exchange(ref s_totalFalseResults, 0);
        Interlocked.Exchange(ref s_totalElapsedTicks, 0);
        Interlocked.Exchange(ref s_maxElapsedTicks, 0);
        Interlocked.Exchange(ref s_currentCalls, 0);
        Interlocked.Exchange(ref s_lastCalls, 0);
        Interlocked.Exchange(ref s_peakCalls, 0);
        Interlocked.Exchange(ref s_totalTowerFilterCandidates, 0);
        Interlocked.Exchange(ref s_totalTowerFilterAccepted, 0);
        Interlocked.Exchange(ref s_totalCandidateDesignations, 0);
        Interlocked.Exchange(ref s_completedCandidateCalls, 0);
        Interlocked.Exchange(ref s_maxCandidateDesignations, 0);
        Interlocked.Exchange(ref s_globalEligibleCacheHits, 0);
        Interlocked.Exchange(ref s_globalEligibleCacheMisses, 0);
        Interlocked.Exchange(ref s_towerEligibleCacheCalls, 0);
        Interlocked.Exchange(ref s_towerEligibleCacheHits, 0);
        Interlocked.Exchange(ref s_towerEligibleCacheMisses, 0);

        for (var i = 0; i < s_pathCalls.Length; i++)
            Interlocked.Exchange(ref s_pathCalls[i], 0);
        for (var i = 0; i < s_callerCalls.Length; i++)
            Interlocked.Exchange(ref s_callerCalls[i], 0);
        for (var i = 0; i < s_pathCandidateDesignations.Length; i++)
        {
            Interlocked.Exchange(ref s_pathCandidateDesignations[i], 0);
            Interlocked.Exchange(ref s_pathCandidateCalls[i], 0);
        }
    }

    private static string formatPathCounts()
    {
        var builder = new StringBuilder(256);
        for (var i = 0; i < (int)SearchPath.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(SearchPathNames[i]).Append('=').Append(read(ref s_pathCalls[i]));
        }

        return builder.ToString();
    }

    private static string formatCallerCounts()
    {
        var builder = new StringBuilder(192);
        for (var i = 0; i < (int)SearchCaller.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(SearchCallerNames[i]).Append('=').Append(read(ref s_callerCalls[i]));
        }

        return builder.ToString();
    }

    private static string formatPathCandidateCounts()
    {
        var builder = new StringBuilder(256);
        for (var i = 0; i < (int)SearchPath.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(SearchPathNames[i])
                .Append('=')
                .Append(read(ref s_pathCandidateDesignations[i]))
                .Append('/')
                .Append(read(ref s_pathCandidateCalls[i]));
        }

        return builder.ToString();
    }

    private static string formatSummary(long[] counters, string[] names)
    {
        var builder = new StringBuilder(160);
        for (var i = 0; i < names.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(names[i]).Append('=').Append(Interlocked.Read(ref counters[i]));
        }

        return builder.ToString();
    }

    private static MethodInfo? findInstanceMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: Type.DefaultBinder,
            types: parameterTypes,
            modifiers: Array.Empty<ParameterModifier>());

        return method is { IsStatic: false } && method.ReturnType == returnType
            ? method
            : null;
    }

    private static MethodInfo? findUniqueInstanceMethod(Type type, string name, int parameterCount)
    {
        MethodInfo? found = null;
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != name || method.IsStatic || method.GetParameters().Length != parameterCount)
                continue;

            if (found is not null)
                return null;
            found = method;
        }

        return found;
    }

    private static void beginVehicleBuffersBalanceBuffers(out CallerContextState __state)
    {
        __state = enterCaller(SearchCaller.VehicleBuffersRegistryBalanceBuffers);
    }

    private static void beginDefaultTruckJobProvider(out CallerContextState __state)
    {
        __state = enterCaller(SearchCaller.DefaultTruckJobProvider);
    }

    private static void beginDumpingJob(out CallerContextState __state)
    {
        __state = enterCaller(SearchCaller.DumpingJob);
    }

    private static CallerContextState enterCaller(SearchCaller caller)
    {
        var state = new CallerContextState(s_currentCaller);
        s_currentCaller = caller;
        return state;
    }

    private static Exception? endCallerContext(Exception? __exception, CallerContextState __state)
    {
        s_currentCaller = __state.Previous;
        return __exception;
    }

    private static void logOptionalPatchFailure(string message)
    {
        logOnce(ref s_optionalPatchErrorLogged, message, null);
    }

    private static void logOnce(ref int alreadyLogged, string operation, Exception? exception)
    {
        if (Interlocked.CompareExchange(ref alreadyLogged, 1, 0) != 0)
            return;

        var suffix = exception is null ? string.Empty : $": {exception}";
        Log.Error($"TajsTweaks: {operation}; diagnostics will remain fail-open{suffix}");
    }

    private static long read(ref long value)
    {
        return Interlocked.Read(ref value);
    }

    private static int readInt(ref int value)
    {
        return Volatile.Read(ref value);
    }

    private static void updateMax(ref long target, long value)
    {
        var current = read(ref target);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
                return;
            current = previous;
        }
    }

    private static double stopwatchTicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double stopwatchTicksToMicroseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }

    private enum SearchPath
    {
        UnknownProduct,
        GlobalAllowed,
        GlobalForbiddenLocalFallback,
        GlobalForbiddenNoLocalTower,
        ExplicitTower,
        ExplicitTowerGlobalForbiddenLocal,
        ExplicitTowerGlobalForbiddenRejected,
        Count,
    }

    private static readonly string[] SearchPathNames =
    {
        "unknown",
        "global-allowed",
        "global-forbidden-local-fallback",
        "global-forbidden-no-local-tower",
        "explicit-tower",
        "explicit-tower-global-forbidden-local",
        "explicit-tower-global-forbidden-rejected",
    };

    private enum SearchCaller
    {
        Other,
        VehicleBuffersRegistryBalanceBuffers,
        DumpingJob,
        DefaultTruckJobProvider,
        Count,
    }

    private static readonly string[] SearchCallerNames =
    {
        "other",
        "VehicleBuffersRegistry.balanceBuffers",
        "DumpingJob",
        "DefaultTruckJobProvider.TryGetJobFor",
    };

    private readonly struct TowerFilterSnapshot
    {
        public TowerFilterSnapshot(int candidates, int accepted)
        {
            Candidates = candidates;
            Accepted = accepted;
        }

        public int Candidates { get; }
        public int Accepted { get; }
    }

    private readonly struct CallerContextState
    {
        public CallerContextState(SearchCaller previous)
        {
            Previous = previous;
        }

        public SearchCaller Previous { get; }
    }

    private readonly struct DumpSearchCallState
    {
        public DumpSearchCallState(
            ProductSearchStats stats,
            TerrainDumpingManager manager,
            SearchPath path,
            long startTimestamp)
        {
            Stats = stats;
            Manager = manager;
            Path = path;
            StartTimestamp = startTimestamp;
        }

        public ProductSearchStats? Stats { get; }
        public TerrainDumpingManager Manager { get; }
        public SearchPath Path { get; }
        public long StartTimestamp { get; }
    }

    private sealed class ProductSearchStats
    {
        private readonly long[] m_pathCalls = new long[(int)SearchPath.Count];
        private readonly long[] m_callerCalls = new long[(int)SearchCaller.Count];
        private long m_calls;
        private long m_trueResults;
        private long m_falseResults;
        private long m_elapsedTicks;
        private long m_maxElapsedTicks;
        private long m_towerFilterCandidates;
        private long m_towerFilterAccepted;

        public ProductSearchStats(string productId)
        {
            ProductId = productId;
        }

        public string ProductId { get; }
        public long Calls => read(ref m_calls);
        public long TrueResults => read(ref m_trueResults);
        public long FalseResults => read(ref m_falseResults);
        public long ElapsedTicks => read(ref m_elapsedTicks);
        public long MaxElapsedTicks => read(ref m_maxElapsedTicks);
        public long TowerFilterCandidates => read(ref m_towerFilterCandidates);
        public long TowerFilterAccepted => read(ref m_towerFilterAccepted);

        public void RecordCall(SearchPath path, SearchCaller caller, int towerCandidates, int towerAccepted)
        {
            Interlocked.Increment(ref m_calls);
            Interlocked.Increment(ref m_pathCalls[(int)path]);
            Interlocked.Increment(ref m_callerCalls[(int)caller]);
            Interlocked.Add(ref m_towerFilterCandidates, towerCandidates);
            Interlocked.Add(ref m_towerFilterAccepted, towerAccepted);
        }

        public void RecordResult(bool result, long elapsedTicks)
        {
            if (result)
                Interlocked.Increment(ref m_trueResults);
            else
                Interlocked.Increment(ref m_falseResults);
            Interlocked.Add(ref m_elapsedTicks, elapsedTicks);
            updateMax(ref m_maxElapsedTicks, elapsedTicks);
        }

        public string GetPathSummary()
        {
            return formatSummary(m_pathCalls, SearchPathNames);
        }

        public string GetCallerSummary()
        {
            return formatSummary(m_callerCalls, SearchCallerNames);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref m_calls, 0);
            Interlocked.Exchange(ref m_trueResults, 0);
            Interlocked.Exchange(ref m_falseResults, 0);
            Interlocked.Exchange(ref m_elapsedTicks, 0);
            Interlocked.Exchange(ref m_maxElapsedTicks, 0);
            Interlocked.Exchange(ref m_towerFilterCandidates, 0);
            Interlocked.Exchange(ref m_towerFilterAccepted, 0);
            for (var i = 0; i < m_pathCalls.Length; i++)
                Interlocked.Exchange(ref m_pathCalls[i], 0);
            for (var i = 0; i < m_callerCalls.Length; i++)
                Interlocked.Exchange(ref m_callerCalls[i], 0);
        }
    }
}
