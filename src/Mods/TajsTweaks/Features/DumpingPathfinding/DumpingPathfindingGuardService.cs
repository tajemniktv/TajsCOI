// Taj's COI Mods | DumpingPathfindingGuardService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ReadonlyCollections;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Console;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles.Trucks;

#endregion

namespace TajsTweaks.Features.DumpingPathfinding;

/// <summary>
///     Prevents a single simulation tick from spending an unbounded amount of work repeatedly
///     searching dumping destinations in the pathological global-off/local-tower path. This is
///     intentionally a rate limiter, not a dumping-filter rewrite: skipped searches are retried
///     on a later simulation tick.
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class DumpingPathfindingGuardService
{
    private const string HarmonyId = "tajemniktv.tajstweaks.dumping-pathfinding-guard";

    private static readonly Dictionary<Truck, int> s_searchesPerTruck = new();

    private static readonly FieldInfo? s_looseProductOptionValueField = findLooseProductOptionValueField();

    private static bool s_patchesApplied;
    private static bool s_pfEnqueueDiagnosticsApplied;
    private static bool s_guardFailureLogged;
    private static long s_epoch;
    private static long s_totalThrottled;

    private static int s_currentDumpSearches;
    private static int s_currentThrottled;
    private static int s_currentPfEnqueues;
    private static int s_currentMaxSearchesForTruck;

    private static int s_lastDumpSearches;
    private static int s_lastThrottled;
    private static int s_lastPfEnqueues;
    private static int s_lastMaxSearchesForTruck;

    private static int s_peakDumpSearches;
    private static int s_peakPfEnqueues;
    private static int s_peakThrottled;

    public DumpingPathfindingGuardService()
    {
        ensurePatchesApplied();
    }

    [ConsoleCommand(
        documentation: "Shows guarded dumping-search throttling and vehicle path-finding enqueue statistics.",
        customCommandName: "tajs_dump_pf_stats")]
    public string GetStats()
    {
        return
            $"Dump/PF guard: active={s_patchesApplied}, epoch={s_epoch}, PF enqueue diagnostics={s_pfEnqueueDiagnosticsApplied}, " +
            $"caps={DumpingPathfindingGuardSettings.SearchesPerTruckPerTick}/truck and {DumpingPathfindingGuardSettings.TotalSearchesPerTick}/tick; " +
            $"current guarded searches={s_currentDumpSearches}, throttled={s_currentThrottled}, PF enqueues={s_currentPfEnqueues}, max/truck={s_currentMaxSearchesForTruck}; " +
            $"last guarded searches={s_lastDumpSearches}, throttled={s_lastThrottled}, PF enqueues={s_lastPfEnqueues}, max/truck={s_lastMaxSearchesForTruck}; " +
            $"peaks guarded searches={s_peakDumpSearches}, throttled={s_peakThrottled}, PF enqueues={s_peakPfEnqueues}; total throttled={s_totalThrottled}.";
    }

    [ConsoleCommand(
        documentation: "Resets accumulated dumping/path-finding guard peak statistics.",
        customCommandName: "tajs_dump_pf_stats_reset")]
    public string ResetStats()
    {
        resetAccumulatedStats();
        return "Dump/PF guard accumulated statistics reset.";
    }

    private static void ensurePatchesApplied()
    {
        if (s_patchesApplied)
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
        var simUpdate = findInstanceMethod(
            typeof(VehiclePathFindingManager),
            "SimUpdateInternal",
            typeof(void));

        if (dumpSearch is null)
        {
            Log.Error(
                "TajsTweaks: dumping/PF guard disabled; the exact CoI 0.8.7 TryFindClosestReadyToDump signature was not found.");
            return;
        }

        if (simUpdate is null)
        {
            Log.Error(
                "TajsTweaks: dumping/PF guard disabled; the exact VehiclePathFindingManager.SimUpdateInternal signature was not found.");
            return;
        }

        if (s_looseProductOptionValueField is null)
        {
            Log.Error(
                "TajsTweaks: dumping/PF guard disabled; Option<LooseProductProto> value storage could not be identified safely.");
            return;
        }

        var harmony = new Harmony(HarmonyId);
        try
        {
            // Install the reset hook before the limiter. If the limiter patch fails, roll both back.
            harmony.Patch(
                simUpdate,
                prefix: new HarmonyMethod(typeof(DumpingPathfindingGuardService), nameof(beginPathFindingTick)));
            harmony.Patch(
                dumpSearch,
                prefix: new HarmonyMethod(typeof(DumpingPathfindingGuardService), nameof(beforeDumpSearch)));
        }
        catch (Exception ex)
        {
            rollbackFunctionalPatches(harmony);
            Log.Error($"TajsTweaks: dumping/PF guard failed to apply functional patches: {ex}");
            return;
        }

        s_patchesApplied = true;

        var enqueueTask = findInstanceMethod(
            typeof(VehiclePathFindingManager),
            "EnqueueTask",
            typeof(void),
            typeof(IManagedVehiclePathFindingTask),
            typeof(int));
        if (enqueueTask is not null)
        {
            try
            {
                harmony.Patch(
                    enqueueTask,
                    prefix: new HarmonyMethod(typeof(DumpingPathfindingGuardService), nameof(beforePathFindingEnqueue)));
                s_pfEnqueueDiagnosticsApplied = true;
            }
            catch (Exception ex)
            {
                // Diagnostics are optional. Keep the functional guard active if only this patch fails.
                Log.Error($"TajsTweaks: dumping/PF enqueue diagnostics failed to patch: {ex}");
            }
        }

        Log.Info(
            $"TajsTweaks: dumping/PF guard active; per-truck cap={DumpingPathfindingGuardSettings.SearchesPerTruckPerTick}, " +
            $"total cap={DumpingPathfindingGuardSettings.TotalSearchesPerTick}, PF enqueue diagnostics={s_pfEnqueueDiagnosticsApplied}.");
    }

    private static bool beforeDumpSearch(
        TerrainDumpingManager __instance,
        Option<LooseProductProto> __1,
        Truck __2,
        ref TerrainDesignation? __4,
        IIndexable<MineTower>? __5,
        ref bool __result)
    {
        try
        {
            if (!shouldThrottleSearch(__instance, __1, __5))
                return true;

            s_currentDumpSearches++;

            if (!s_searchesPerTruck.TryGetValue(__2, out var truckSearches))
                truckSearches = 0;

            truckSearches++;
            s_searchesPerTruck[__2] = truckSearches;
            if (truckSearches > s_currentMaxSearchesForTruck)
                s_currentMaxSearchesForTruck = truckSearches;

            var perTruckLimit = DumpingPathfindingGuardSettings.SearchesPerTruckPerTick;
            var totalLimit = DumpingPathfindingGuardSettings.TotalSearchesPerTick;

            var exceedsPerTruckLimit = perTruckLimit > 0 && truckSearches > perTruckLimit;
            var exceedsTotalLimit = totalLimit > 0 && s_currentDumpSearches > totalLimit;
            if (!exceedsPerTruckLimit && !exceedsTotalLimit)
                return true;

            // TryFind... has an out TerrainDesignation. When Harmony skips the original call we must
            // provide the same safe state that a normal false return would expose to its caller.
            __4 = null;
            __result = false;
            s_currentThrottled++;
            s_totalThrottled++;
            return false;
        }
        catch (Exception ex)
        {
            // Harmony prefix failures propagate into the truck's simulation update. This guard is
            // a mitigation only, so any unexpected predicate/instrumentation failure must fail open
            // and let the original CoI search run rather than breaking vehicle logistics.
            if (!s_guardFailureLogged)
            {
                s_guardFailureLogged = true;
                Log.Error($"TajsTweaks: dumping/PF guard failed during a search and will fail open for this call: {ex}");
            }

            return true;
        }
    }

    private static bool shouldThrottleSearch(
        TerrainDumpingManager dumpingManager,
        Option<LooseProductProto> productOption,
        IIndexable<MineTower>? towersToEnforce)
    {
        // Null is a legitimate value for the global/non-tower search path. Only explicit,
        // non-empty tower-enforced searches are candidates for the local/global mismatch guard.
        if (towersToEnforce is null || towersToEnforce.Count == 0)
            return false;

        // The target method does not know whether its caller is specifically a storage export.
        // The reproduced pathological state that is observable here is narrower:
        // the search is tower-enforced, at least one enforced tower accepts the product locally,
        // and that same product is forbidden by the global dumping filter.
        var product = s_looseProductOptionValueField?.GetValue(productOption) as LooseProductProto;
        if (product is null || !anyTowerAcceptsDumpOf(towersToEnforce, product))
            return false;

        if (dumpingManager.ProductsAllowedToDump is not IEnumerable<LooseProductProto> globallyAllowedProducts)
            return false;

        return !containsProduct(globallyAllowedProducts, product);
    }

    private static bool anyTowerAcceptsDumpOf(IIndexable<MineTower> towers, LooseProductProto product)
    {
        for (var i = 0; i < towers.Count; i++)
        {
            if (towers[i].CanAcceptDumpOf(product))
                return true;
        }

        return false;
    }

    private static bool containsProduct(IEnumerable<LooseProductProto> products, LooseProductProto product)
    {
        // This method runs inside an already-hot simulation search path. Keep the early-exit loop
        // instead of LINQ's Where/Any delegate pipeline to avoid adding per-call iterator/delegate
        // overhead merely to satisfy the generic S3267 maintainability preference.
#pragma warning disable S3267
        foreach (var candidate in products)
        {
            if (ReferenceEquals(candidate, product) ||
                EqualityComparer<LooseProductProto>.Default.Equals(candidate, product))
                return true;
        }
#pragma warning restore S3267

        return false;
    }

    private static void beforePathFindingEnqueue()
    {
        s_currentPfEnqueues++;
    }

    private static void beginPathFindingTick()
    {
        s_lastDumpSearches = s_currentDumpSearches;
        s_lastThrottled = s_currentThrottled;
        s_lastPfEnqueues = s_currentPfEnqueues;
        s_lastMaxSearchesForTruck = s_currentMaxSearchesForTruck;

        if (s_lastDumpSearches > s_peakDumpSearches)
            s_peakDumpSearches = s_lastDumpSearches;
        if (s_lastPfEnqueues > s_peakPfEnqueues)
            s_peakPfEnqueues = s_lastPfEnqueues;
        if (s_lastThrottled > s_peakThrottled)
            s_peakThrottled = s_lastThrottled;

        s_currentDumpSearches = 0;
        s_currentThrottled = 0;
        s_currentPfEnqueues = 0;
        s_currentMaxSearchesForTruck = 0;
        s_searchesPerTruck.Clear();
        s_epoch++;
    }

    private static void resetAccumulatedStats()
    {
        s_peakDumpSearches = 0;
        s_peakPfEnqueues = 0;
        s_peakThrottled = 0;
        s_totalThrottled = 0;
    }

    private static void rollbackFunctionalPatches(Harmony harmony)
    {
        try
        {
            harmony.UnpatchAll(HarmonyId);
        }
        catch (Exception rollbackException)
        {
            Log.Error($"TajsTweaks: dumping/PF guard rollback also failed: {rollbackException}");
        }

        s_patchesApplied = false;
        s_pfEnqueueDiagnosticsApplied = false;
    }

    // S3011 is intentionally suppressed only for this compatibility seam. The mod targets a
    // fixed, exact CoI 0.8.7 signature and fails closed when it is not present. No user-supplied
    // type or member names are reflected here.
#pragma warning disable S3011
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

    private static FieldInfo? findLooseProductOptionValueField()
    {
        FieldInfo? found = null;
        foreach (var field in typeof(Option<LooseProductProto>).GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (field.FieldType != typeof(LooseProductProto))
                continue;

            if (found is not null)
                return null;

            found = field;
        }

        return found;
    }
#pragma warning restore S3011
}
