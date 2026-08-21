// Taj's COI Mods | DumpingPathfindingGuardService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles.Trucks;

#endregion

namespace TajsTweaks.Features.DumpingPathfinding;

/// <summary>
///     Prevents a single simulation tick from spending an unbounded amount of work repeatedly
///     searching dumping destinations for the same trucks. This is intentionally a rate limiter,
///     not a dumping-filter rewrite: skipped searches are retried on a later simulation tick.
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class DumpingPathfindingGuardService
{
    private const string HarmonyId = "tajemniktv.tajstweaks.dumping-pathfinding-guard";

    private static readonly Dictionary<Truck, int> s_searchesPerTruck = new();

    private static bool s_patchesApplied;
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
        if (s_patchesApplied)
            return;

        try
        {
            var dumpSearch = findUniqueMethod(typeof(TerrainDumpingManager), "TryFindClosestReadyToDump", 8);
            var simUpdate = findUniqueMethod(typeof(VehiclePathFindingManager), "SimUpdateInternal", 0);

            if (dumpSearch is null || dumpSearch.ReturnType != typeof(bool))
            {
                Log.Info("TajsTweaks: dumping/PF guard disabled; compatible TryFindClosestReadyToDump was not found.");
                return;
            }

            if (simUpdate is null)
            {
                Log.Info("TajsTweaks: dumping/PF guard disabled; VehiclePathFindingManager.SimUpdateInternal was not found.");
                return;
            }

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(
                dumpSearch,
                prefix: new HarmonyMethod(typeof(DumpingPathfindingGuardService), nameof(beforeDumpSearch)));
            harmony.Patch(
                simUpdate,
                prefix: new HarmonyMethod(typeof(DumpingPathfindingGuardService), nameof(beginPathFindingTick)));

            var enqueueTask = findUniqueMethod(typeof(VehiclePathFindingManager), "EnqueueTask", 2);
            if (enqueueTask is not null)
            {
                harmony.Patch(
                    enqueueTask,
                    prefix: new HarmonyMethod(typeof(DumpingPathfindingGuardService), nameof(beforePathFindingEnqueue)));
            }

            s_patchesApplied = true;
            Log.Info(
                $"TajsTweaks: dumping/PF guard active; per-truck cap={DumpingPathfindingGuardSettings.SearchesPerTruckPerTick}, " +
                $"total cap={DumpingPathfindingGuardSettings.TotalSearchesPerTick}, PF enqueue diagnostics={enqueueTask is not null}.");
        }
        catch (Exception ex)
        {
            Log.Info($"TajsTweaks: dumping/PF guard failed to patch: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [ConsoleCommand(
        documentation: "Shows dumping-search throttling and vehicle path-finding enqueue statistics.",
        customCommandName: "tajs_dump_pf_stats")]
    public string GetStats()
    {
        return
            $"Dump/PF guard: active={s_patchesApplied}, epoch={s_epoch}, " +
            $"caps={DumpingPathfindingGuardSettings.SearchesPerTruckPerTick}/truck and {DumpingPathfindingGuardSettings.TotalSearchesPerTick}/tick; " +
            $"current searches={s_currentDumpSearches}, throttled={s_currentThrottled}, PF enqueues={s_currentPfEnqueues}, max/truck={s_currentMaxSearchesForTruck}; " +
            $"last searches={s_lastDumpSearches}, throttled={s_lastThrottled}, PF enqueues={s_lastPfEnqueues}, max/truck={s_lastMaxSearchesForTruck}; " +
            $"peaks searches={s_peakDumpSearches}, throttled={s_peakThrottled}, PF enqueues={s_peakPfEnqueues}; total throttled={s_totalThrottled}.";
    }

    [ConsoleCommand(
        documentation: "Resets accumulated dumping/path-finding guard peak statistics.",
        customCommandName: "tajs_dump_pf_stats_reset")]
    public string ResetStats()
    {
        s_peakDumpSearches = 0;
        s_peakPfEnqueues = 0;
        s_peakThrottled = 0;
        s_totalThrottled = 0;
        return "Dump/PF guard accumulated statistics reset.";
    }

    private static bool beforeDumpSearch(Truck __2, ref TerrainDesignation __4, ref bool __result)
    {
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
        __4 = default!;
        __result = false;
        s_currentThrottled++;
        s_totalThrottled++;
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

    private static MethodInfo? findUniqueMethod(Type type, string name, int parameterCount)
    {
        MethodInfo? found = null;
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != name || method.GetParameters().Length != parameterCount)
                continue;

            if (found is not null)
                return null;

            found = method;
        }

        return found;
    }
}
