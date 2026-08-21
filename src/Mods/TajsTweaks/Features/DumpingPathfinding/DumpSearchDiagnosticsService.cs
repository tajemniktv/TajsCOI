// Taj's COI Mods | DumpSearchDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
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
///     Records low-overhead runtime statistics for dumping-destination searches. Diagnostics never
///     suppress, reorder, or otherwise change the result of the target method. Any diagnostics failure
///     is swallowed after a one-time error log so vanilla simulation can continue unaffected.
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class DumpSearchDiagnosticsService
{
    private const string HarmonyId = "tajemniktv.tajstweaks.dump-search-diagnostics";
    private const string UnknownProductId = "<none>";
    private const int MaxProductsInConsoleReport = 12;

    private static readonly object s_productStatsLock = new();
    private static readonly Dictionary<string, ProductSearchStats> s_productStats = new(StringComparer.Ordinal);

    private static bool s_patchesApplied;
    private static bool s_tickBoundaryPatchApplied;
    private static bool s_prefixErrorLogged;
    private static bool s_postfixErrorLogged;
    private static bool s_tickErrorLogged;

    private static long s_totalCalls;
    private static long s_totalGlobalCalls;
    private static long s_totalTowerCalls;
    private static long s_totalTrueResults;
    private static long s_totalFalseResults;
    private static long s_totalElapsedTicks;
    private static long s_maxElapsedTicks;

    private static long s_currentCalls;
    private static long s_lastCalls;
    private static long s_peakCalls;

    public DumpSearchDiagnosticsService()
    {
        ensurePatchesApplied();
    }

    [ConsoleCommand(
        documentation: "Shows dumping destination-search counts, modes, outcomes and timing by loose product.",
        customCommandName: "tajs_dump_search_stats")]
    public string GetStats()
    {
        var snapshot = snapshotProductStats();
        snapshot.Sort(static (left, right) => right.Calls.CompareTo(left.Calls));

        var completedCalls = s_totalTrueResults + s_totalFalseResults;
        var incompleteCalls = Math.Max(0, s_totalCalls - completedCalls);
        var totalMs = stopwatchTicksToMilliseconds(s_totalElapsedTicks);
        var avgUs = completedCalls > 0
            ? stopwatchTicksToMicroseconds(s_totalElapsedTicks) / completedCalls
            : 0.0;

        var builder = new StringBuilder(768);
        builder.Append("Dump search diagnostics: active=")
            .Append(s_patchesApplied)
            .Append(", tick buckets=")
            .Append(s_tickBoundaryPatchApplied)
            .Append("; calls current/last/peak=")
            .Append(s_currentCalls)
            .Append('/')
            .Append(s_lastCalls)
            .Append('/')
            .Append(s_peakCalls)
            .Append("; total=")
            .Append(s_totalCalls)
            .Append(" (global=")
            .Append(s_totalGlobalCalls)
            .Append(", tower=")
            .Append(s_totalTowerCalls)
            .Append("); returned true/false/incomplete=")
            .Append(s_totalTrueResults)
            .Append('/')
            .Append(s_totalFalseResults)
            .Append('/')
            .Append(incompleteCalls)
            .Append("; observed completed-call time=")
            .Append(totalMs.ToString("F2"))
            .Append(" ms, avg=")
            .Append(avgUs.ToString("F1"))
            .Append(" us, max=")
            .Append(stopwatchTicksToMilliseconds(s_maxElapsedTicks).ToString("F3"))
            .Append(" ms.");

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
                .Append(" (global=")
                .Append(stats.GlobalCalls)
                .Append(", tower=")
                .Append(stats.TowerCalls)
                .Append("), true/false/incomplete=")
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

        builder.Append("\nNote: returned false includes any call intentionally short-circuited by the separate dumping guard; compare tajs_dump_pf_stats for throttle totals. Incomplete calls entered the diagnostic prefix but did not reach its postfix, usually because another patch or the original call threw.");
        return builder.ToString();
    }

    [ConsoleCommand(
        documentation: "Resets accumulated dumping destination-search diagnostics.",
        customCommandName: "tajs_dump_search_stats_reset")]
    public string ResetStats()
    {
        // Do not Clear() this dictionary: the simulation hot path deliberately performs an unlocked
        // fast lookup. Product keys are a tiny, bounded set after prototype registration, so keeping
        // the structure stable makes console-side resets safe without adding a lock to every search.
        lock (s_productStatsLock)
        {
            foreach (var stats in s_productStats.Values)
                stats.Reset();
        }

        s_totalCalls = 0;
        s_totalGlobalCalls = 0;
        s_totalTowerCalls = 0;
        s_totalTrueResults = 0;
        s_totalFalseResults = 0;
        s_totalElapsedTicks = 0;
        s_maxElapsedTicks = 0;
        s_currentCalls = 0;
        s_lastCalls = 0;
        s_peakCalls = 0;

        return "Dump search diagnostics reset.";
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
            s_patchesApplied = true;
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

        var simUpdate = findInstanceMethod(
            typeof(VehiclePathFindingManager),
            "SimUpdateInternal",
            typeof(void));
        if (simUpdate is not null)
        {
            try
            {
                harmony.Patch(
                    simUpdate,
                    prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beginPathFindingTick)));
                s_tickBoundaryPatchApplied = true;
            }
            catch (Exception ex)
            {
                Log.Error($"TajsTweaks: dump-search diagnostics could not patch PF tick boundaries; cumulative stats remain active: {ex}");
            }
        }

        Log.Info(
            $"TajsTweaks: dump-search diagnostics active; PF tick buckets={s_tickBoundaryPatchApplied}. " +
            "Use tajs_dump_search_stats to inspect product/mode/result timing.");
    }

    private static void beforeDumpSearch(
        Option<LooseProductProto> __1,
        IIndexable<MineTower>? __5,
        out DumpSearchCallState __state)
    {
        __state = default;
        try
        {
            var productId = __1.ValueOrNull?.Id.Value ?? UnknownProductId;
            var stats = getOrCreateProductStats(productId);
            var isTowerSearch = __5 is { Count: > 0 };

            stats.Calls++;
            if (isTowerSearch)
            {
                stats.TowerCalls++;
                s_totalTowerCalls++;
            }
            else
            {
                stats.GlobalCalls++;
                s_totalGlobalCalls++;
            }

            s_currentCalls++;
            s_totalCalls++;
            __state = new DumpSearchCallState(stats, Stopwatch.GetTimestamp());
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
            var stats = __state.Stats;

            if (__result)
            {
                stats.TrueResults++;
                s_totalTrueResults++;
            }
            else
            {
                stats.FalseResults++;
                s_totalFalseResults++;
            }

            stats.ElapsedTicks += elapsedTicks;
            if (elapsedTicks > stats.MaxElapsedTicks)
                stats.MaxElapsedTicks = elapsedTicks;

            s_totalElapsedTicks += elapsedTicks;
            if (elapsedTicks > s_maxElapsedTicks)
                s_maxElapsedTicks = elapsedTicks;
        }
        catch (Exception ex)
        {
            logOnce(ref s_postfixErrorLogged, "dump-search diagnostics postfix", ex);
        }
    }

    private static void beginPathFindingTick()
    {
        try
        {
            s_lastCalls = s_currentCalls;
            if (s_lastCalls > s_peakCalls)
                s_peakCalls = s_lastCalls;
            s_currentCalls = 0;
        }
        catch (Exception ex)
        {
            logOnce(ref s_tickErrorLogged, "dump-search diagnostics tick snapshot", ex);
        }
    }

    private static ProductSearchStats getOrCreateProductStats(string productId)
    {
        if (s_productStats.TryGetValue(productId, out var stats))
            return stats;

        lock (s_productStatsLock)
        {
            if (s_productStats.TryGetValue(productId, out stats))
                return stats;

            stats = new ProductSearchStats(productId);
            s_productStats.Add(productId, stats);
            return stats;
        }
    }

    private static List<ProductSearchStats> snapshotProductStats()
    {
        lock (s_productStatsLock)
            return new List<ProductSearchStats>(s_productStats.Values);
    }

    private static MethodInfo? findInstanceMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = AccessTools.DeclaredMethod(type, name, parameterTypes);
        return method is { IsStatic: false } && method.ReturnType == returnType
            ? method
            : null;
    }

    private static void logOnce(ref bool alreadyLogged, string operation, Exception exception)
    {
        if (alreadyLogged)
            return;

        alreadyLogged = true;
        Log.Error($"TajsTweaks: {operation} failed; diagnostics will fail open and vanilla simulation will continue: {exception}");
    }

    private static double stopwatchTicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double stopwatchTicksToMicroseconds(long ticks)
    {
        return ticks * 1_000_000.0 / Stopwatch.Frequency;
    }

    private readonly struct DumpSearchCallState
    {
        public DumpSearchCallState(ProductSearchStats stats, long startTimestamp)
        {
            Stats = stats;
            StartTimestamp = startTimestamp;
        }

        public ProductSearchStats? Stats { get; }
        public long StartTimestamp { get; }
    }

    private sealed class ProductSearchStats
    {
        public ProductSearchStats(string productId)
        {
            ProductId = productId;
        }

        public string ProductId { get; }
        public long Calls;
        public long GlobalCalls;
        public long TowerCalls;
        public long TrueResults;
        public long FalseResults;
        public long ElapsedTicks;
        public long MaxElapsedTicks;

        public void Reset()
        {
            Calls = 0;
            GlobalCalls = 0;
            TowerCalls = 0;
            TrueResults = 0;
            FalseResults = 0;
            ElapsedTicks = 0;
            MaxElapsedTicks = 0;
        }
    }
}
