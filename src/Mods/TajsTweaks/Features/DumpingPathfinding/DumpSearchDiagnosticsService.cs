// Taj's COI Mods | DumpSearchDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    private const int MaxProfileHistory = 16;
    private const int LatencyBucketCount = 9;
    private const long TinyResidualRoundingToleranceTicks = 16;

    private static readonly ConcurrentDictionary<string, ProductSearchStats> s_productStats = new(StringComparer.Ordinal);
    private static readonly long[] s_pathCalls = new long[(int)SearchPath.Count];
    private static readonly long[] s_callerCalls = new long[(int)SearchCaller.Count];
    private static readonly long[] s_pathCandidateDesignations = new long[(int)SearchPath.Count];
    private static readonly long[] s_pathCandidateCalls = new long[(int)SearchPath.Count];
    private static readonly long[] s_pathLatencyBuckets = new long[(int)SearchPath.Count * LatencyBucketCount];
    private static readonly long[] s_latencyBuckets = new long[LatencyBucketCount];
    private static readonly object s_profileGate = new();
    private static readonly List<ProfileSnapshot> s_profileHistory = new(MaxProfileHistory);

    private static IGameConsole? s_console;

    private static int s_patchesApplied;
    private static int s_tickBoundaryPatchApplied;
    private static int s_pfEnqueuePatchApplied;
    private static int s_callerPatchCount;
    private static int s_cachePatchCount;
    private static int s_prefixErrorLogged;
    private static int s_postfixErrorLogged;
    private static int s_tickErrorLogged;
    private static int s_finalizerErrorLogged;
    private static int s_globalCacheDiagnosticsErrorLogged;
    private static int s_towerCacheDiagnosticsErrorLogged;
    private static int s_cacheFinalizerErrorLogged;
    private static int s_profileErrorLogged;
    private static int s_profileOutputErrorLogged;
    private static int s_residualAccountingErrorLogged;

    private static long s_totalCalls;
    private static long s_totalTrueResults;
    private static long s_totalFalseResults;
    private static long s_totalElapsedTicks;
    private static long s_maxElapsedTicks;
    private static long s_currentCalls;
    private static long s_lastCalls;
    private static long s_peakCalls;

    private static long s_totalCandidateDesignations;
    private static long s_observedCandidateCalls;
    private static long s_maxCandidateDesignations;

    private static long s_globalEligibleCacheCalls;
    private static long s_towerEligibleCacheCalls;
    private static long s_globalEligibleCacheElapsedTicks;
    private static long s_towerEligibleCacheElapsedTicks;
    private static long s_globalEligibleCacheMaxElapsedTicks;
    private static long s_towerEligibleCacheMaxElapsedTicks;
    private static long s_nestedResidualElapsedTicks;
    private static long s_nestedResidualMaxElapsedTicks;
    private static long s_nestedResidualAccountingAnomalies;
    private static WorstCallSnapshot? s_worstCall;

    private static long s_currentPfEnqueues;
    private static long s_lastPfEnqueues;
    private static long s_peakPfEnqueues;
    private static long s_totalPfEnqueues;
    private static long s_currentPfSearchElapsedTicks;
    private static long s_lastPfSearchElapsedTicks;
    private static long s_peakPfSearchElapsedTicks;
    private static long s_currentPfMaxIndividualSearchElapsedTicks;
    private static long s_lastPfMaxIndividualSearchElapsedTicks;
    private static long s_peakPfMaxIndividualSearchElapsedTicks;
    private static long s_peakPfSearchCalls;

    private static ProfileSession? s_activeProfile;
    private static int s_profileState;
    private static long s_nextProfileSequence;
    private static long s_nextAutomaticLabel;

    [ThreadStatic]
    private static SearchCaller s_currentCaller;

    [ThreadStatic]
    private static SearchDiagnosticContext? s_currentSearchContext;

    [ThreadStatic]
    private static EligibleCacheDiagnosticContext? s_currentEligibleCacheContext;

    public DumpSearchDiagnosticsService(IGameConsole console)
    {
        s_console = console;
        ensurePatchesApplied();
    }

    [ConsoleCommand(
        documentation: "Shows behavior-neutral dumping-search path, caller, cache, candidate and timing diagnostics.",
        customCommandName: "tajs_dump_search_stats")]
    public string GetStats()
    {
        var snapshot = snapshotProductStats();
        snapshot.Sort(static (left, right) => right.Calls.CompareTo(left.Calls));

        var pathCalls = snapshotCounters(s_pathCalls);
        var callerCalls = snapshotCounters(s_callerCalls);
        var pathCandidateDesignations = snapshotCounters(s_pathCandidateDesignations);
        var pathCandidateCalls = snapshotCounters(s_pathCandidateCalls);

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

        var globalCacheCalls = read(ref s_globalEligibleCacheCalls);
        var towerCacheCalls = read(ref s_towerEligibleCacheCalls);
        builder.Append("\nPaths: ")
            .Append(formatCounterSummary(SearchPathNames, pathCalls))
            .Append("\nCallers: ")
            .Append(formatCounterSummary(SearchCallerNames, callerCalls))
            .Append("\nEligible-cache timing: global calls=")
            .Append(globalCacheCalls)
            .Append(", total=")
            .Append(stopwatchTicksToMilliseconds(read(ref s_globalEligibleCacheElapsedTicks)).ToString("F2"))
            .Append(" ms, avg=")
            .Append(formatMicroseconds(averageTicks(read(ref s_globalEligibleCacheElapsedTicks), globalCacheCalls)))
            .Append(" us, max=")
            .Append(formatMilliseconds(read(ref s_globalEligibleCacheMaxElapsedTicks)))
            .Append(" ms; per-tower calls=")
            .Append(towerCacheCalls)
            .Append(", total=")
            .Append(stopwatchTicksToMilliseconds(read(ref s_towerEligibleCacheElapsedTicks)).ToString("F2"))
            .Append(" ms, avg=")
            .Append(formatMicroseconds(averageTicks(read(ref s_towerEligibleCacheElapsedTicks), towerCacheCalls)))
            .Append(" us, max=")
            .Append(formatMilliseconds(read(ref s_towerEligibleCacheMaxElapsedTicks)))
            .Append(" ms")
            .Append("\nResidual after eligible-cache calls: total=")
            .Append(formatMilliseconds(read(ref s_nestedResidualElapsedTicks)))
            .Append(" ms, avg=")
            .Append(formatMicroseconds(averageTicks(read(ref s_nestedResidualElapsedTicks), completedCalls)))
            .Append(" us, max=")
            .Append(formatMilliseconds(read(ref s_nestedResidualMaxElapsedTicks)))
            .Append(", accounting anomalies=")
            .Append(read(ref s_nestedResidualAccountingAnomalies))
            .Append(", observed cache candidates total/searches/max=")
            .Append(read(ref s_totalCandidateDesignations))
            .Append('/')
            .Append(read(ref s_observedCandidateCalls))
            .Append('/')
            .Append(read(ref s_maxCandidateDesignations))
            .Append("\nPath observed cache candidates total/searches: ")
            .Append(formatCounterSummary(SearchPathNames, pathCandidateDesignations, pathCandidateCalls))
            .Append("\nPF enqueues current/last/peak/total=")
            .Append(read(ref s_currentPfEnqueues))
            .Append('/')
            .Append(read(ref s_lastPfEnqueues))
            .Append('/')
            .Append(read(ref s_peakPfEnqueues))
            .Append('/')
            .Append(read(ref s_totalPfEnqueues))
            .Append("; search time current/last/peak=")
            .Append(formatMilliseconds(read(ref s_currentPfSearchElapsedTicks)))
            .Append('/')
            .Append(formatMilliseconds(read(ref s_lastPfSearchElapsedTicks)))
            .Append('/')
            .Append(formatMilliseconds(read(ref s_peakPfSearchElapsedTicks)))
            .Append(" ms, peak tick calls=")
            .Append(read(ref s_peakPfSearchCalls))
            .Append(", peak-tick worst individual=")
            .Append(formatMilliseconds(read(ref s_peakPfMaxIndividualSearchElapsedTicks)))
            .Append(" ms.")
            .Append("\nOuter latency buckets: ")
            .Append(formatLatencyBuckets(s_latencyBuckets));

        var worstCall = Volatile.Read(ref s_worstCall);
        if (worstCall is not null)
            appendWorstCall(builder, worstCall);

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
                .Append(formatCounterSummary(SearchPathNames, stats.PathCalls))
                .Append("; callers=")
                .Append(formatCounterSummary(SearchCallerNames, stats.CallerCalls))
                .Append("; observed-cache-candidates total/cache-invocations=")
                .Append(stats.CandidateDesignations)
                .Append('/')
                .Append(stats.CandidateCalls)
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
                .Append(" ms; cache global=")
                .Append(stats.GlobalCacheCalls)
                .Append('/')
                .Append(formatMilliseconds(stats.GlobalCacheElapsedTicks))
                .Append(" ms avg=")
                .Append(formatMicroseconds(averageTicks(stats.GlobalCacheElapsedTicks, stats.GlobalCacheCalls)))
                .Append(" us max=")
                .Append(formatMilliseconds(stats.GlobalCacheMaxElapsedTicks))
                .Append(" ms; tower=")
                .Append(stats.TowerCacheCalls)
                .Append('/')
                .Append(formatMilliseconds(stats.TowerCacheElapsedTicks))
                .Append(" ms avg=")
                .Append(formatMicroseconds(averageTicks(stats.TowerCacheElapsedTicks, stats.TowerCacheCalls)))
                .Append(" us max=")
                .Append(formatMilliseconds(stats.TowerCacheMaxElapsedTicks))
                .Append(" ms; residual=")
                .Append(formatMilliseconds(stats.ResidualElapsedTicks))
                .Append(" ms, latency=")
                .Append(formatLatencyBuckets(stats.LatencyBuckets));
            appendProductPathMetrics(builder, stats);
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
        return ResetStats();
    }

    [ConsoleCommand(
        documentation: "Starts a timed dumping-search profile: seconds, optional label, optional warmup seconds.",
        customCommandName: "tajs_dump_profile")]
    public string StartProfile(float seconds, string? label = null, float? warmupSeconds = null)
    {
        var requestedSeconds = (double)seconds;
        var requestedWarmupSeconds = warmupSeconds.HasValue ? (double)warmupSeconds.Value : 0.0;
        if (!isValidProfileDuration(requestedSeconds, minimum: 0.25, maximum: 300.0))
            return "Dump profile rejected: duration must be finite and between 0.25 and 300 seconds.";
        if (!isValidProfileDuration(requestedWarmupSeconds, minimum: 0.0, maximum: 300.0))
            return "Dump profile rejected: warmup must be finite and between 0 and 300 seconds.";

        var normalizedLabel = label?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedLabel))
            normalizedLabel = null;
        ProfileSession? active;
        lock (s_profileGate)
        {
            active = s_activeProfile;
            if (active is not null)
                return $"Dump profile rejected: '{active.Label}' is already {describeProfileState(active.State)}.";

            if (normalizedLabel is null)
            {
                do
                {
                    normalizedLabel = "run-" + Interlocked.Increment(ref s_nextAutomaticLabel).ToString(CultureInfo.InvariantCulture);
                }
                while (findProfileLocked(normalizedLabel) is not null);
            }
            else if (findProfileLocked(normalizedLabel) is not null)
            {
                return $"Dump profile rejected: completed profile label '{normalizedLabel}' already exists.";
            }

            resetAllStats();
            var now = Stopwatch.GetTimestamp();
            var sessionState = requestedWarmupSeconds > 0.0 ? ProfileState.WarmingUp : ProfileState.Recording;
            var recordingStart = sessionState == ProfileState.Recording ? now : 0L;
            active = new ProfileSession(
                normalizedLabel,
                Interlocked.Increment(ref s_nextProfileSequence),
                requestedSeconds,
                requestedWarmupSeconds,
                now,
                sessionState,
                recordingStart);
            s_activeProfile = active;
            Volatile.Write(ref s_profileState, (int)sessionState);
        }

        return requestedWarmupSeconds > 0.0
            ? $"Dump profile '{active.Label}' armed: warmup={formatSeconds(requestedWarmupSeconds)}, recording={formatSeconds(requestedSeconds)}."
            : $"Dump profile '{active.Label}' recording for {formatSeconds(requestedSeconds)}.";
    }

    [ConsoleCommand(
        documentation: "Shows the current timed dumping-search profile state.",
        customCommandName: "tajs_dump_profile_status")]
    public string GetProfileStatus()
    {
        lock (s_profileGate)
        {
            var active = s_activeProfile;
            if (active is null)
                return "Dump profile status: idle.";

            var now = Stopwatch.GetTimestamp();
            var elapsed = stopwatchTicksToSeconds(Math.Max(0, now - active.StartTimestamp));
            var deadline = active.State == ProfileState.WarmingUp ? active.WarmupEndTimestamp : active.RecordingEndTimestamp;
            var remaining = deadline == 0L ? 0.0 : Math.Max(0.0, stopwatchTicksToSeconds(deadline - now));
            return $"Dump profile status: {describeProfileState(active.State)}, label='{active.Label}', requested={formatSeconds(active.RequestedSeconds)}, warmup={formatSeconds(active.WarmupSeconds)}, elapsed={formatSeconds(elapsed)}, remaining={formatSeconds(remaining)}, calls={read(ref s_totalCalls)}.";
        }
    }

    [ConsoleCommand(
        documentation: "Stops and stores the current timed dumping-search profile.",
        customCommandName: "tajs_dump_profile_stop")]
    public string StopProfile()
    {
        ProfileSnapshot? completed;
        lock (s_profileGate)
        {
            if (s_activeProfile is null)
                return "Dump profile stop: no active profile.";

            completed = finishProfileLocked(Stopwatch.GetTimestamp());
        }

        publishCompletedProfile(completed, automatic: false);
        return $"Dump profile '{completed.Label}' stopped and stored.";
    }

    [ConsoleCommand(
        documentation: "Cancels the current timed dumping-search profile without storing it.",
        customCommandName: "tajs_dump_profile_cancel")]
    public string CancelProfile()
    {
        lock (s_profileGate)
        {
            if (s_activeProfile is null)
                return "Dump profile cancel: no active profile.";

            var label = s_activeProfile.Label;
            s_activeProfile = null;
            Volatile.Write(ref s_profileState, (int)ProfileState.Idle);
            return $"Dump profile '{label}' cancelled; no completed profile was stored.";
        }
    }

    [ConsoleCommand(
        documentation: "Lists completed timed dumping-search profiles.",
        customCommandName: "tajs_dump_profiles")]
    public string ListProfiles()
    {
        lock (s_profileGate)
        {
            if (s_profileHistory.Count == 0)
                return "Dump profiles: none stored.";

            var builder = new StringBuilder(1024).Append("Dump profiles:");
            foreach (var profile in s_profileHistory)
            {
                builder.Append("\n  ")
                    .Append(profile.Label)
                    .Append(" [sequence=")
                    .Append(profile.Sequence)
                    .Append("] duration=")
                    .Append(formatSeconds(profile.ActualRecordingSeconds))
                    .Append(", calls=")
                    .Append(profile.TotalCalls)
                    .Append(", search=")
                    .Append(formatMilliseconds(profile.TotalElapsedTicks))
                    .Append(" ms, dominant=")
                    .Append(profile.DominantPath);

                var dirt = findProduct(profile.Products, "Product_Dirt");
                if (dirt is not null)
                {
                    var dirtValue = dirt.Value;
                    builder.Append(", Product_Dirt avg=").Append(formatMicroseconds(averageTicks(dirtValue.ElapsedTicks, dirtValue.CompletedCalls))).Append(" us");
                }
            }

            return builder.ToString();
        }
    }

    [ConsoleCommand(
        documentation: "Shows a completed timed dumping-search profile by label.",
        customCommandName: "tajs_dump_profile_show")]
    public string ShowProfile(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "Dump profile show: label is required.";

        lock (s_profileGate)
        {
            var profile = findProfileLocked(label.Trim());
            return profile is null
                ? $"Dump profile show: no completed profile named '{label.Trim()}'."
                : formatProfileReport(profile);
        }
    }

    [ConsoleCommand(
        documentation: "Clears completed timed dumping-search profiles without affecting an active profile.",
        customCommandName: "tajs_dump_profile_clear")]
    public string ClearProfiles()
    {
        lock (s_profileGate)
        {
            var count = s_profileHistory.Count;
            s_profileHistory.Clear();
            return $"Dump profile history cleared ({count} profile(s)); active profile unchanged.";
        }
    }

    [ConsoleCommand(
        documentation: "Compares two completed timed dumping-search profiles.",
        customCommandName: "tajs_dump_profile_compare")]
    public string CompareProfiles(string labelA, string labelB)
    {
        if (string.IsNullOrWhiteSpace(labelA) || string.IsNullOrWhiteSpace(labelB))
            return "Dump profile compare: two labels are required.";

        lock (s_profileGate)
        {
            var first = findProfileLocked(labelA.Trim());
            var second = findProfileLocked(labelB.Trim());
            if (first is null || second is null)
                return $"Dump profile compare: missing profile(s); A='{labelA.Trim()}', B='{labelB.Trim()}'.";

            return formatProfileComparison(first, second);
        }
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
            harmony.Patch(
                dumpSearch,
                prefix: prefix,
                postfix: postfix,
                finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(endDumpSearchContext)));
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
                    prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beforeGlobalEligibleCache)),
                    postfix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(afterGlobalEligibleCache)),
                    finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(endEligibleCacheContext)));
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
                    prefix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(beforeTowerEligibleCache)),
                    postfix: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(afterTowerEligibleCache)),
                    finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(endEligibleCacheContext)));
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
        IIndexable<MineTower>? __5,
        out DumpSearchCallState __state)
    {
        __state = default;
        var previousContext = s_currentSearchContext;
        try
        {
            serviceProfileDeadlineAtSearchBoundary();
            var product = __1.ValueOrNull;
            var path = classifySearch(__instance, product, __5);
            var caller = s_currentCaller;
            var productId = product?.Id.Value ?? UnknownProductId;
            var stats = getOrCreateProductStats(productId);

            var context = new SearchDiagnosticContext(
                previousContext,
                stats,
                path,
                caller,
                Stopwatch.GetTimestamp());
            s_currentSearchContext = context;
            stats.RecordCallStart();
            Interlocked.Increment(ref s_totalCalls);
            Interlocked.Increment(ref s_currentCalls);
            __state = new DumpSearchCallState(context);
        }
        catch (Exception ex)
        {
            s_currentSearchContext = previousContext;
            logOnce(ref s_prefixErrorLogged, "dump-search diagnostics prefix", ex);
        }
    }

    private static void afterDumpSearch(bool __result, DumpSearchCallState __state)
    {
        if (__state.Context is null)
            return;

        try
        {
            completeDumpSearch(__state.Context, hasResult: true, result: __result);
        }
        catch (Exception ex)
        {
            logOnce(ref s_postfixErrorLogged, "dump-search diagnostics postfix", ex);
        }
    }

    private static Exception? endDumpSearchContext(Exception? __exception, DumpSearchCallState __state)
    {
        var context = __state.Context;
        if (context is null)
            return __exception;

        try
        {
            if (__exception is not null)
                completeDumpSearch(context, hasResult: false, result: false);
        }
        catch (Exception ex)
        {
            logOnce(ref s_finalizerErrorLogged, "dump-search diagnostics finalizer", ex);
        }
        finally
        {
            s_currentSearchContext = context.Previous;
        }

        return __exception;
    }

    private static void completeDumpSearch(SearchDiagnosticContext context, bool hasResult, bool result)
    {
        if (Interlocked.CompareExchange(ref context.CompletionRecorded, 1, 0) != 0)
            return;

        var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - context.StartTimestamp);
        var path = context.Path;
        var candidateDesignations = Interlocked.Read(ref context.CandidateDesignations);
        var candidateCalls = Volatile.Read(ref context.CandidateCalls);
        var cacheSummary = context.GetCacheSummary();
        var residualTicks = calculateResidualTicks(elapsedTicks, cacheSummary.TotalElapsedTicks);

        context.Stats.RecordCompletion(
            path,
            context.Caller,
            hasResult,
            result,
            elapsedTicks,
            candidateDesignations,
            candidateCalls,
            cacheSummary,
            residualTicks);
        Interlocked.Increment(ref s_pathCalls[(int)path]);
        Interlocked.Increment(ref s_callerCalls[(int)context.Caller]);
        Interlocked.Increment(ref s_latencyBuckets[getLatencyBucket(elapsedTicks)]);
        Interlocked.Increment(ref s_pathLatencyBuckets[(int)path * LatencyBucketCount + getLatencyBucket(elapsedTicks)]);
        Interlocked.Add(ref s_nestedResidualElapsedTicks, residualTicks);
        updateMax(ref s_nestedResidualMaxElapsedTicks, residualTicks);
        updateWorstCall(context, elapsedTicks, candidateDesignations, candidateCalls, cacheSummary, residualTicks);
        Interlocked.Add(ref s_currentPfSearchElapsedTicks, elapsedTicks);
        updateMax(ref s_currentPfMaxIndividualSearchElapsedTicks, elapsedTicks);

        if (hasResult)
        {
            if (result)
                Interlocked.Increment(ref s_totalTrueResults);
            else
                Interlocked.Increment(ref s_totalFalseResults);

            Interlocked.Add(ref s_totalElapsedTicks, elapsedTicks);
            updateMax(ref s_maxElapsedTicks, elapsedTicks);
        }

        if (candidateCalls > 0)
        {
            Interlocked.Add(ref s_totalCandidateDesignations, candidateDesignations);
            Interlocked.Increment(ref s_observedCandidateCalls);
            Interlocked.Add(ref s_pathCandidateDesignations[(int)path], candidateDesignations);
            Interlocked.Increment(ref s_pathCandidateCalls[(int)path]);
            updateMax(ref s_maxCandidateDesignations, candidateDesignations);
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
            serviceProfileDeadline();
            var currentCalls = Interlocked.Exchange(ref s_currentCalls, 0);
            var currentPfEnqueues = Interlocked.Exchange(ref s_currentPfEnqueues, 0);
            var currentSearchElapsed = Interlocked.Exchange(ref s_currentPfSearchElapsedTicks, 0);
            var currentMaxIndividualSearch = Interlocked.Exchange(ref s_currentPfMaxIndividualSearchElapsedTicks, 0);
            Interlocked.Exchange(ref s_lastCalls, currentCalls);
            Interlocked.Exchange(ref s_lastPfEnqueues, currentPfEnqueues);
            Interlocked.Exchange(ref s_lastPfSearchElapsedTicks, currentSearchElapsed);
            Interlocked.Exchange(ref s_lastPfMaxIndividualSearchElapsedTicks, currentMaxIndividualSearch);
            updateMax(ref s_peakCalls, currentCalls);
            updateMax(ref s_peakPfEnqueues, currentPfEnqueues);
            var previousPeakSearchElapsed = read(ref s_peakPfSearchElapsedTicks);
            if (currentSearchElapsed > previousPeakSearchElapsed)
            {
                Interlocked.Exchange(ref s_peakPfSearchElapsedTicks, currentSearchElapsed);
                Interlocked.Exchange(ref s_peakPfSearchCalls, currentCalls);
                Interlocked.Exchange(ref s_peakPfMaxIndividualSearchElapsedTicks, currentMaxIndividualSearch);
            }
        }
        catch (Exception ex)
        {
            logOnce(ref s_tickErrorLogged, "dump-search diagnostics tick snapshot", ex);
        }
    }

    private static void beforeGlobalEligibleCache(out EligibleCacheCallState __state)
    {
        __state = default;
        try
        {
            var context = new EligibleCacheDiagnosticContext(
                s_currentEligibleCacheContext,
                s_currentSearchContext,
                isPerTower: false,
                Stopwatch.GetTimestamp());
            s_currentEligibleCacheContext = context;
            __state = new EligibleCacheCallState(context);
        }
        catch (Exception ex)
        {
            logOnce(ref s_globalCacheDiagnosticsErrorLogged, "global eligible-cache diagnostics", ex);
        }
    }

    private static void afterGlobalEligibleCache(
        LystStruct<TerrainDesignation> __result,
        EligibleCacheCallState __state)
    {
        completeEligibleCache(__state.Context, __result.Count, hasResult: true);
    }

    private static void beforeTowerEligibleCache(out EligibleCacheCallState __state)
    {
        __state = default;
        try
        {
            var context = new EligibleCacheDiagnosticContext(
                s_currentEligibleCacheContext,
                s_currentSearchContext,
                isPerTower: true,
                Stopwatch.GetTimestamp());
            s_currentEligibleCacheContext = context;
            __state = new EligibleCacheCallState(context);
        }
        catch (Exception ex)
        {
            logOnce(ref s_towerCacheDiagnosticsErrorLogged, "per-tower eligible-cache diagnostics", ex);
        }
    }

    private static void afterTowerEligibleCache(
        Lyst<TerrainDesignation> __result,
        EligibleCacheCallState __state)
    {
        completeEligibleCache(__state.Context, __result?.Count ?? 0, hasResult: true);
    }

    private static Exception? endEligibleCacheContext(
        Exception? __exception,
        EligibleCacheCallState __state)
    {
        var context = __state.Context;
        if (context is null)
            return __exception;

        try
        {
            if (__exception is not null)
                completeEligibleCache(context, 0, hasResult: false);
        }
        catch (Exception ex)
        {
            logOnce(ref s_cacheFinalizerErrorLogged, "eligible-cache diagnostics finalizer", ex);
        }
        finally
        {
            s_currentEligibleCacheContext = context.Previous;
        }

        return __exception;
    }

    private static void completeEligibleCache(
        EligibleCacheDiagnosticContext? context,
        int candidateCount,
        bool hasResult)
    {
        if (context is null || Interlocked.CompareExchange(ref context.CompletionRecorded, 1, 0) != 0)
            return;

        try
        {
            var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - context.StartTimestamp);
            if (context.IsPerTower)
            {
                Interlocked.Increment(ref s_towerEligibleCacheCalls);
                Interlocked.Add(ref s_towerEligibleCacheElapsedTicks, elapsedTicks);
                updateMax(ref s_towerEligibleCacheMaxElapsedTicks, elapsedTicks);
            }
            else
            {
                Interlocked.Increment(ref s_globalEligibleCacheCalls);
                Interlocked.Add(ref s_globalEligibleCacheElapsedTicks, elapsedTicks);
                updateMax(ref s_globalEligibleCacheMaxElapsedTicks, elapsedTicks);
            }

            var outer = context.OuterSearch;
            if (outer is null)
                return;

            if (context.IsPerTower)
            {
                outer.MarkTowerEligibleCacheCall();
                outer.RecordTowerCache(elapsedTicks);
            }
            else
            {
                outer.RecordGlobalCache(elapsedTicks);
            }

            outer.RecordCandidateDesignations(hasResult ? candidateCount : 0);
        }
        catch (Exception ex)
        {
            logOnce(
                ref (context.IsPerTower ? ref s_towerCacheDiagnosticsErrorLogged : ref s_globalCacheDiagnosticsErrorLogged),
                context.IsPerTower ? "per-tower eligible-cache diagnostics" : "global eligible-cache diagnostics",
                ex);
        }
    }

    private static void serviceProfileDeadline()
    {
        ProfileSnapshot? completed = null;
        try
        {
            lock (s_profileGate)
            {
                var active = s_activeProfile;
                if (active is null)
                    return;

                var now = Stopwatch.GetTimestamp();
                if (active.State == ProfileState.WarmingUp && now >= active.WarmupEndTimestamp)
                {
                    resetAllStats();
                    active.BeginRecording(now);
                    Volatile.Write(ref s_profileState, (int)ProfileState.Recording);
                }

                if (active.State == ProfileState.Recording && now >= active.RecordingEndTimestamp)
                    completed = finishProfileLocked(now);
            }

            if (completed is not null)
                publishCompletedProfile(completed, automatic: true);
        }
        catch (Exception ex)
        {
            logOnce(ref s_profileErrorLogged, "timed dump profile deadline handling", ex);
        }
    }

    private static void serviceProfileDeadlineAtSearchBoundary()
    {
        if ((ProfileState)Volatile.Read(ref s_profileState) != ProfileState.Recording)
            return;

        var active = Volatile.Read(ref s_activeProfile);
        if (active is null || Stopwatch.GetTimestamp() < active.RecordingEndTimestamp)
            return;

        // Finalize before admitting a search that starts after the recording window.
        // The normal pathfinding-tick hook remains the fallback when no search arrives.
        serviceProfileDeadline();
    }

    private static ProfileSnapshot finishProfileLocked(long now)
    {
        var active = s_activeProfile ?? throw new InvalidOperationException("No active dump profile.");
        if (active.State == ProfileState.WarmingUp)
            resetAllStats();
        var recordingStart = active.State == ProfileState.Recording
            ? active.RecordingStartTimestamp
            : active.StartTimestamp;
        var actualRecordingTicks = active.State == ProfileState.Recording
            ? Math.Max(0, now - recordingStart)
            : 0L;
        var snapshot = snapshotCurrentProfile(active, actualRecordingTicks);

        s_activeProfile = null;
        Volatile.Write(ref s_profileState, (int)ProfileState.Idle);
        s_profileHistory.Add(snapshot);
        if (s_profileHistory.Count > MaxProfileHistory)
            s_profileHistory.RemoveAt(0);
        return snapshot;
    }

    private static void publishCompletedProfile(ProfileSnapshot profile, bool automatic)
    {
        try
        {
            var prefix = automatic ? "[TajsTweaks] " : string.Empty;
            s_console?.WriteLine(prefix + formatProfileReport(profile), ColorRgba.White);
        }
        catch (Exception ex)
        {
            logOnce(ref s_profileOutputErrorLogged, "timed dump profile console output", ex);
        }
    }

    private static long calculateResidualTicks(long outerTicks, long nestedTicks)
    {
        var residual = outerTicks - nestedTicks;
        if (residual < 0 && residual >= -TinyResidualRoundingToleranceTicks)
            return 0;

        if (residual < 0)
        {
            Interlocked.Increment(ref s_nestedResidualAccountingAnomalies);
            logOnce(
                ref s_residualAccountingErrorLogged,
                "dump-search nested timing accounting anomaly",
                new InvalidOperationException($"nested={nestedTicks}, outer={outerTicks}"));
        }

        return residual;
    }

    private static SearchPath classifySearch(
        TerrainDumpingManager dumpingManager,
        LooseProductProto? product,
        IIndexable<MineTower>? towersToEnforce)
    {
        if (product is null)
            return SearchPath.UnknownProduct;

        var globalAllowed = dumpingManager.ProductsAllowedToDump.Contains(product);
        // This mirrors TerrainDumpingManager's `flag2 = towersToEnforce != null`: an empty,
        // non-null list is still an explicit enforced-tower search, not the global fallback.
        if (towersToEnforce is not null)
        {
            if (globalAllowed)
                return SearchPath.ExplicitTower;

            return SearchPath.ExplicitTowerGlobalForbiddenRejected;
        }

        if (globalAllowed)
            return SearchPath.GlobalAllowed;

        return SearchPath.GlobalForbiddenNoLocalTower;
    }

    private static ProductSearchStats getOrCreateProductStats(string productId)
    {
        if (s_productStats.TryGetValue(productId, out var stats))
            return stats;

        var created = new ProductSearchStats(productId);
        return s_productStats.GetOrAdd(productId, created);
    }

    private static List<ProductSearchSnapshot> snapshotProductStats()
    {
        var snapshot = new List<ProductSearchSnapshot>();
        foreach (var stats in s_productStats.Values)
        {
            var productSnapshot = stats.Snapshot();
            if (productSnapshot.Calls > 0)
                snapshot.Add(productSnapshot);
        }

        return snapshot;
    }

    private static long[] snapshotCounters(long[] counters)
    {
        var snapshot = new long[counters.Length];
        for (var i = 0; i < counters.Length; i++)
            snapshot[i] = Interlocked.Read(ref counters[i]);

        return snapshot;
    }

    private static void resetAllStats()
    {
        resetDumpSearchStats();
        Interlocked.Exchange(ref s_currentPfEnqueues, 0);
        Interlocked.Exchange(ref s_lastPfEnqueues, 0);
        Interlocked.Exchange(ref s_peakPfEnqueues, 0);
        Interlocked.Exchange(ref s_totalPfEnqueues, 0);
        Interlocked.Exchange(ref s_currentPfSearchElapsedTicks, 0);
        Interlocked.Exchange(ref s_lastPfSearchElapsedTicks, 0);
        Interlocked.Exchange(ref s_peakPfSearchElapsedTicks, 0);
        Interlocked.Exchange(ref s_currentPfMaxIndividualSearchElapsedTicks, 0);
        Interlocked.Exchange(ref s_lastPfMaxIndividualSearchElapsedTicks, 0);
        Interlocked.Exchange(ref s_peakPfMaxIndividualSearchElapsedTicks, 0);
        Interlocked.Exchange(ref s_peakPfSearchCalls, 0);
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
        Interlocked.Exchange(ref s_totalCandidateDesignations, 0);
        Interlocked.Exchange(ref s_observedCandidateCalls, 0);
        Interlocked.Exchange(ref s_maxCandidateDesignations, 0);
        Interlocked.Exchange(ref s_globalEligibleCacheCalls, 0);
        Interlocked.Exchange(ref s_towerEligibleCacheCalls, 0);
        Interlocked.Exchange(ref s_globalEligibleCacheElapsedTicks, 0);
        Interlocked.Exchange(ref s_towerEligibleCacheElapsedTicks, 0);
        Interlocked.Exchange(ref s_globalEligibleCacheMaxElapsedTicks, 0);
        Interlocked.Exchange(ref s_towerEligibleCacheMaxElapsedTicks, 0);
        Interlocked.Exchange(ref s_nestedResidualElapsedTicks, 0);
        Interlocked.Exchange(ref s_nestedResidualMaxElapsedTicks, 0);
        Interlocked.Exchange(ref s_nestedResidualAccountingAnomalies, 0);
        Volatile.Write(ref s_worstCall, null);

        for (var i = 0; i < s_pathCalls.Length; i++)
            Interlocked.Exchange(ref s_pathCalls[i], 0);
        for (var i = 0; i < s_callerCalls.Length; i++)
            Interlocked.Exchange(ref s_callerCalls[i], 0);
        for (var i = 0; i < s_pathCandidateDesignations.Length; i++)
        {
            Interlocked.Exchange(ref s_pathCandidateDesignations[i], 0);
            Interlocked.Exchange(ref s_pathCandidateCalls[i], 0);
        }
        for (var i = 0; i < s_latencyBuckets.Length; i++)
            Interlocked.Exchange(ref s_latencyBuckets[i], 0);
        for (var i = 0; i < s_pathLatencyBuckets.Length; i++)
            Interlocked.Exchange(ref s_pathLatencyBuckets[i], 0);
    }

    private static string formatCounterSummary(string[] names, long[] primary, long[]? secondary = null)
    {
        var builder = new StringBuilder(160);
        var count = Math.Min(names.Length, primary.Length);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(names[i]).Append('=').Append(primary[i]);
            if (secondary is not null && i < secondary.Length)
                builder.Append('/').Append(secondary[i]);
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
        Log.Error($"TajsTweaks: {message}; diagnostics will remain fail-open.");
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

    private static readonly string[] LatencyBucketNames =
    {
        "<0.1ms",
        "0.1-1ms",
        "1-5ms",
        "5-10ms",
        "10-25ms",
        "25-50ms",
        "50-100ms",
        "100-250ms",
        ">=250ms",
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
        public DumpSearchCallState(SearchDiagnosticContext? context)
        {
            Context = context;
        }

        public SearchDiagnosticContext? Context { get; }
    }

    private readonly struct EligibleCacheCallState
    {
        public EligibleCacheCallState(EligibleCacheDiagnosticContext? context)
        {
            Context = context;
        }

        public EligibleCacheDiagnosticContext? Context { get; }
    }

    private sealed class EligibleCacheDiagnosticContext
    {
        public EligibleCacheDiagnosticContext(
            EligibleCacheDiagnosticContext? previous,
            SearchDiagnosticContext? outerSearch,
            bool isPerTower,
            long startTimestamp)
        {
            Previous = previous;
            OuterSearch = outerSearch;
            IsPerTower = isPerTower;
            StartTimestamp = startTimestamp;
        }

        public EligibleCacheDiagnosticContext? Previous { get; }
        public SearchDiagnosticContext? OuterSearch { get; }
        public bool IsPerTower { get; }
        public long StartTimestamp { get; }
        public int CompletionRecorded;
    }

    private sealed class SearchDiagnosticContext
    {
        public SearchDiagnosticContext(
            SearchDiagnosticContext? previous,
            ProductSearchStats stats,
            SearchPath path,
            SearchCaller caller,
            long startTimestamp)
        {
            Previous = previous;
            Stats = stats;
            Path = path;
            Caller = caller;
            StartTimestamp = startTimestamp;
        }

        public SearchDiagnosticContext? Previous { get; }
        public ProductSearchStats Stats { get; }
        public SearchPath Path { get; private set; }
        public SearchCaller Caller { get; }
        public long StartTimestamp { get; }
        public long CandidateDesignations;
        public int CandidateCalls;
        public long GlobalCacheElapsedTicks;
        public long TowerCacheElapsedTicks;
        public long GlobalCacheMaxElapsedTicks;
        public long TowerCacheMaxElapsedTicks;
        public int GlobalCacheCalls;
        public int TowerCacheCalls;
        public int CompletionRecorded;

        public void MarkTowerEligibleCacheCall()
        {
            if (Path == SearchPath.GlobalForbiddenNoLocalTower)
                Path = SearchPath.GlobalForbiddenLocalFallback;
            else if (Path == SearchPath.ExplicitTowerGlobalForbiddenRejected)
                Path = SearchPath.ExplicitTowerGlobalForbiddenLocal;
        }

        public void RecordCandidateDesignations(int count)
        {
            Interlocked.Add(ref CandidateDesignations, Math.Max(0, count));
            Interlocked.Increment(ref CandidateCalls);
        }

        public void RecordGlobalCache(long elapsedTicks)
        {
            Interlocked.Add(ref GlobalCacheElapsedTicks, elapsedTicks);
            Interlocked.Increment(ref GlobalCacheCalls);
            updateMax(ref GlobalCacheMaxElapsedTicks, elapsedTicks);
        }

        public void RecordTowerCache(long elapsedTicks)
        {
            Interlocked.Add(ref TowerCacheElapsedTicks, elapsedTicks);
            Interlocked.Increment(ref TowerCacheCalls);
            updateMax(ref TowerCacheMaxElapsedTicks, elapsedTicks);
        }

        public CacheSummary GetCacheSummary()
        {
            return new CacheSummary(
                Volatile.Read(ref GlobalCacheCalls),
                Interlocked.Read(ref GlobalCacheElapsedTicks),
                Interlocked.Read(ref GlobalCacheMaxElapsedTicks),
                Volatile.Read(ref TowerCacheCalls),
                Interlocked.Read(ref TowerCacheElapsedTicks),
                Interlocked.Read(ref TowerCacheMaxElapsedTicks));
        }
    }

    private static void updateWorstCall(
        SearchDiagnosticContext context,
        long elapsedTicks,
        long candidateDesignations,
        int candidateCalls,
        CacheSummary cacheSummary,
        long residualTicks)
    {
        var currentGlobal = Volatile.Read(ref s_worstCall);
        var currentProduct = context.Stats.WorstCall;
        if ((currentGlobal is not null && currentGlobal.ElapsedTicks >= elapsedTicks) &&
            (currentProduct is not null && currentProduct.ElapsedTicks >= elapsedTicks))
            return;

        var candidate = new WorstCallSnapshot(
            elapsedTicks,
            context.Stats.ProductId,
            context.Path,
            context.Caller,
            candidateDesignations,
            cacheSummary.GlobalElapsedTicks,
            cacheSummary.TowerElapsedTicks,
            cacheSummary.TotalElapsedTicks,
            residualTicks,
            cacheSummary.GlobalCalls,
            cacheSummary.TowerCalls);

        updateWorstCall(ref s_worstCall, candidate);
        context.Stats.RecordWorst(candidate);
    }

    private static void updateWorstCall(ref WorstCallSnapshot? target, WorstCallSnapshot candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (current is not null && current.ElapsedTicks >= candidate.ElapsedTicks)
                return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref target, candidate, current), current))
                return;
        }
    }

    private static int getLatencyBucket(long elapsedTicks)
    {
        var milliseconds = stopwatchTicksToMilliseconds(elapsedTicks);
        if (milliseconds < 0.1)
            return 0;
        if (milliseconds < 1.0)
            return 1;
        if (milliseconds < 5.0)
            return 2;
        if (milliseconds < 10.0)
            return 3;
        if (milliseconds < 25.0)
            return 4;
        if (milliseconds < 50.0)
            return 5;
        if (milliseconds < 100.0)
            return 6;
        if (milliseconds < 250.0)
            return 7;
        return 8;
    }

    private static string formatLatencyBuckets(long[] buckets)
    {
        var builder = new StringBuilder(180);
        for (var i = 0; i < Math.Min(LatencyBucketNames.Length, buckets.Length); i++)
        {
            if (buckets[i] == 0)
                continue;
            if (builder.Length > 0)
                builder.Append(", ");
            builder.Append(LatencyBucketNames[i]).Append('=').Append(buckets[i]);
        }

        return builder.Length == 0 ? "none" : builder.ToString();
    }

    private readonly struct CacheSummary
    {
        public CacheSummary(
            int globalCalls,
            long globalElapsedTicks,
            long globalMaxElapsedTicks,
            int towerCalls,
            long towerElapsedTicks,
            long towerMaxElapsedTicks)
        {
            GlobalCalls = globalCalls;
            GlobalElapsedTicks = globalElapsedTicks;
            GlobalMaxElapsedTicks = globalMaxElapsedTicks;
            TowerCalls = towerCalls;
            TowerElapsedTicks = towerElapsedTicks;
            TowerMaxElapsedTicks = towerMaxElapsedTicks;
        }

        public int GlobalCalls { get; }
        public long GlobalElapsedTicks { get; }
        public long GlobalMaxElapsedTicks { get; }
        public int TowerCalls { get; }
        public long TowerElapsedTicks { get; }
        public long TowerMaxElapsedTicks { get; }
        public long TotalElapsedTicks => GlobalElapsedTicks + TowerElapsedTicks;
    }

    private enum ProfileState
    {
        Idle,
        WarmingUp,
        Recording,
    }

    private sealed class ProfileSession
    {
        public ProfileSession(
            string label,
            long sequence,
            double requestedSeconds,
            double warmupSeconds,
            long startTimestamp,
            ProfileState state,
            long recordingStartTimestamp)
        {
            Label = label;
            Sequence = sequence;
            RequestedSeconds = requestedSeconds;
            WarmupSeconds = warmupSeconds;
            StartTimestamp = startTimestamp;
            State = state;
            RecordingStartTimestamp = recordingStartTimestamp;
            WarmupEndTimestamp = state == ProfileState.WarmingUp
                ? startTimestamp + secondsToStopwatchTicks(warmupSeconds)
                : 0L;
            RecordingEndTimestamp = state == ProfileState.Recording
                ? recordingStartTimestamp + secondsToStopwatchTicks(requestedSeconds)
                : 0L;
        }

        public string Label { get; }
        public long Sequence { get; }
        public double RequestedSeconds { get; }
        public double WarmupSeconds { get; }
        public long StartTimestamp { get; }
        public ProfileState State { get; private set; }
        public long RecordingStartTimestamp { get; private set; }
        public long WarmupEndTimestamp { get; }
        public long RecordingEndTimestamp { get; private set; }

        public void BeginRecording(long now)
        {
            State = ProfileState.Recording;
            RecordingStartTimestamp = now;
            RecordingEndTimestamp = now + secondsToStopwatchTicks(RequestedSeconds);
        }
    }

    private sealed class ProfileSnapshot
    {
        public ProfileSnapshot(
            ProfileSession session,
            long actualRecordingTicks,
            long totalCalls,
            long completedCalls,
            long totalElapsedTicks,
            long[] pathCalls,
            long[] callerCalls,
            long[] pathCandidateDesignations,
            long[] pathCandidateCalls,
            long[] pathLatencyBuckets,
            long[] latencyBuckets,
            long totalCandidateDesignations,
            long observedCandidateCalls,
            long globalCacheCalls,
            long globalCacheElapsedTicks,
            long globalCacheMaxElapsedTicks,
            long towerCacheCalls,
            long towerCacheElapsedTicks,
            long towerCacheMaxElapsedTicks,
            long residualElapsedTicks,
            long residualMaxElapsedTicks,
            long residualAccountingAnomalies,
            long lastPfSearchElapsedTicks,
            long peakPfSearchElapsedTicks,
            long peakPfSearchCalls,
            long peakPfMaxIndividualSearchElapsedTicks,
            WorstCallSnapshot? worstCall,
            ProductSearchSnapshot[] products)
        {
            Label = session.Label;
            Sequence = session.Sequence;
            RequestedSeconds = session.RequestedSeconds;
            WarmupSeconds = session.WarmupSeconds;
            ActualRecordingSeconds = stopwatchTicksToSeconds(actualRecordingTicks);
            TotalCalls = totalCalls;
            CompletedCalls = completedCalls;
            TotalElapsedTicks = totalElapsedTicks;
            PathCalls = (long[])pathCalls.Clone();
            CallerCalls = (long[])callerCalls.Clone();
            PathCandidateDesignations = (long[])pathCandidateDesignations.Clone();
            PathCandidateCalls = (long[])pathCandidateCalls.Clone();
            PathLatencyBuckets = (long[])pathLatencyBuckets.Clone();
            LatencyBuckets = (long[])latencyBuckets.Clone();
            TotalCandidateDesignations = totalCandidateDesignations;
            ObservedCandidateCalls = observedCandidateCalls;
            GlobalCacheCalls = globalCacheCalls;
            GlobalCacheElapsedTicks = globalCacheElapsedTicks;
            GlobalCacheMaxElapsedTicks = globalCacheMaxElapsedTicks;
            TowerCacheCalls = towerCacheCalls;
            TowerCacheElapsedTicks = towerCacheElapsedTicks;
            TowerCacheMaxElapsedTicks = towerCacheMaxElapsedTicks;
            ResidualElapsedTicks = residualElapsedTicks;
            ResidualMaxElapsedTicks = residualMaxElapsedTicks;
            ResidualAccountingAnomalies = residualAccountingAnomalies;
            LastPfSearchElapsedTicks = lastPfSearchElapsedTicks;
            PeakPfSearchElapsedTicks = peakPfSearchElapsedTicks;
            PeakPfSearchCalls = peakPfSearchCalls;
            PeakPfMaxIndividualSearchElapsedTicks = peakPfMaxIndividualSearchElapsedTicks;
            WorstCall = worstCall;
            Products = (ProductSearchSnapshot[])products.Clone();
            DominantPath = findDominantPath(PathCalls);
        }

        public string Label { get; }
        public long Sequence { get; }
        public double RequestedSeconds { get; }
        public double WarmupSeconds { get; }
        public double ActualRecordingSeconds { get; }
        public long TotalCalls { get; }
        public long CompletedCalls { get; }
        public long TotalElapsedTicks { get; }
        public long[] PathCalls { get; }
        public long[] CallerCalls { get; }
        public long[] PathCandidateDesignations { get; }
        public long[] PathCandidateCalls { get; }
        public long[] PathLatencyBuckets { get; }
        public long[] LatencyBuckets { get; }
        public long TotalCandidateDesignations { get; }
        public long ObservedCandidateCalls { get; }
        public long GlobalCacheCalls { get; }
        public long GlobalCacheElapsedTicks { get; }
        public long GlobalCacheMaxElapsedTicks { get; }
        public long TowerCacheCalls { get; }
        public long TowerCacheElapsedTicks { get; }
        public long TowerCacheMaxElapsedTicks { get; }
        public long ResidualElapsedTicks { get; }
        public long ResidualMaxElapsedTicks { get; }
        public long ResidualAccountingAnomalies { get; }
        public long LastPfSearchElapsedTicks { get; }
        public long PeakPfSearchElapsedTicks { get; }
        public long PeakPfSearchCalls { get; }
        public long PeakPfMaxIndividualSearchElapsedTicks { get; }
        public WorstCallSnapshot? WorstCall { get; }
        public ProductSearchSnapshot[] Products { get; }
        public string DominantPath { get; }
    }

    private sealed class WorstCallSnapshot
    {
        public WorstCallSnapshot(
            long elapsedTicks,
            string productId,
            SearchPath path,
            SearchCaller caller,
            long candidateDesignations,
            long globalCacheElapsedTicks,
            long towerCacheElapsedTicks,
            long nestedCacheElapsedTicks,
            long residualElapsedTicks,
            int globalCacheCalls,
            int towerCacheCalls)
        {
            ElapsedTicks = elapsedTicks;
            ProductId = productId;
            Path = path;
            Caller = caller;
            CandidateDesignations = candidateDesignations;
            GlobalCacheElapsedTicks = globalCacheElapsedTicks;
            TowerCacheElapsedTicks = towerCacheElapsedTicks;
            NestedCacheElapsedTicks = nestedCacheElapsedTicks;
            ResidualElapsedTicks = residualElapsedTicks;
            GlobalCacheCalls = globalCacheCalls;
            TowerCacheCalls = towerCacheCalls;
        }

        public long ElapsedTicks { get; }
        public string ProductId { get; }
        public SearchPath Path { get; }
        public SearchCaller Caller { get; }
        public long CandidateDesignations { get; }
        public long GlobalCacheElapsedTicks { get; }
        public long TowerCacheElapsedTicks { get; }
        public long NestedCacheElapsedTicks { get; }
        public long ResidualElapsedTicks { get; }
        public int GlobalCacheCalls { get; }
        public int TowerCacheCalls { get; }
    }

    private static ProfileSnapshot snapshotCurrentProfile(ProfileSession session, long actualRecordingTicks)
    {
        var products = snapshotProductStats();
        products.Sort(static (left, right) => right.Calls.CompareTo(left.Calls));
        var completedCalls = read(ref s_totalTrueResults) + read(ref s_totalFalseResults);
        return new ProfileSnapshot(
            session,
            actualRecordingTicks,
            read(ref s_totalCalls),
            completedCalls,
            read(ref s_totalElapsedTicks),
            snapshotCounters(s_pathCalls),
            snapshotCounters(s_callerCalls),
            snapshotCounters(s_pathCandidateDesignations),
            snapshotCounters(s_pathCandidateCalls),
            snapshotCounters(s_pathLatencyBuckets),
            snapshotCounters(s_latencyBuckets),
            read(ref s_totalCandidateDesignations),
            read(ref s_observedCandidateCalls),
            read(ref s_globalEligibleCacheCalls),
            read(ref s_globalEligibleCacheElapsedTicks),
            read(ref s_globalEligibleCacheMaxElapsedTicks),
            read(ref s_towerEligibleCacheCalls),
            read(ref s_towerEligibleCacheElapsedTicks),
            read(ref s_towerEligibleCacheMaxElapsedTicks),
            read(ref s_nestedResidualElapsedTicks),
            read(ref s_nestedResidualMaxElapsedTicks),
            read(ref s_nestedResidualAccountingAnomalies),
            read(ref s_lastPfSearchElapsedTicks),
            read(ref s_peakPfSearchElapsedTicks),
            read(ref s_peakPfSearchCalls),
            read(ref s_peakPfMaxIndividualSearchElapsedTicks),
            Volatile.Read(ref s_worstCall),
            products.ToArray());
    }

    private static string formatProfileReport(ProfileSnapshot profile)
    {
        var callsPerSecond = profile.ActualRecordingSeconds > 0.0
            ? profile.TotalCalls / profile.ActualRecordingSeconds
            : 0.0;
        var utilization = profile.ActualRecordingSeconds > 0.0
            ? stopwatchTicksToSeconds(profile.TotalElapsedTicks) / profile.ActualRecordingSeconds * 100.0
            : 0.0;
        var builder = new StringBuilder(4096);
        builder.Append("Dump profile \"").Append(profile.Label).AppendLine("\" complete")
            .Append("requested=").Append(formatSeconds(profile.RequestedSeconds))
            .Append(" actual=").Append(formatSeconds(profile.ActualRecordingSeconds))
            .Append(" warmup=").Append(formatSeconds(profile.WarmupSeconds)).AppendLine()
            .Append("calls=").Append(profile.TotalCalls)
            .Append(" (").Append(formatDecimal(callsPerSecond)).Append("/s), cumulative outer search time=")
            .Append(formatMilliseconds(profile.TotalElapsedTicks)).Append(" ms, utilization=")
            .Append(formatDecimal(utilization)).AppendLine("% (cumulative; concurrent calls may exceed 100%)")
            .Append("Paths: ").Append(formatCounterSummary(SearchPathNames, profile.PathCalls)).AppendLine()
            .Append("Callers: ").Append(formatCounterSummary(SearchCallerNames, profile.CallerCalls)).AppendLine()
            .Append("Eligible caches:").AppendLine()
            .Append("  global: calls=").Append(profile.GlobalCacheCalls)
            .Append(", total=").Append(formatMilliseconds(profile.GlobalCacheElapsedTicks))
            .Append(" ms, avg=").Append(formatMicroseconds(averageTicks(profile.GlobalCacheElapsedTicks, profile.GlobalCacheCalls)))
            .Append(" us, max=").Append(formatMilliseconds(profile.GlobalCacheMaxElapsedTicks)).AppendLine(" ms")
            .Append("  per-tower: calls=").Append(profile.TowerCacheCalls)
            .Append(", total=").Append(formatMilliseconds(profile.TowerCacheElapsedTicks))
            .Append(" ms, avg=").Append(formatMicroseconds(averageTicks(profile.TowerCacheElapsedTicks, profile.TowerCacheCalls)))
            .Append(" us, max=").Append(formatMilliseconds(profile.TowerCacheMaxElapsedTicks)).AppendLine(" ms")
            .Append("Residual after eligible-cache calls: total=").Append(formatMilliseconds(profile.ResidualElapsedTicks))
            .Append(" ms, avg=").Append(formatMicroseconds(averageTicks(profile.ResidualElapsedTicks, profile.CompletedCalls)))
            .Append(" us, max=").Append(formatMilliseconds(profile.ResidualMaxElapsedTicks))
            .Append(", accounting anomalies=").AppendLine(profile.ResidualAccountingAnomalies.ToString(CultureInfo.InvariantCulture))
            .Append("Candidates: total=").Append(profile.TotalCandidateDesignations)
            .Append(", cache invocations=").Append(profile.ObservedCandidateCalls)
            .Append(", avg=").Append(formatDecimal(average(profile.TotalCandidateDesignations, profile.ObservedCandidateCalls))).AppendLine()
            .Append("PF tick search workload: peak calls/tick=").Append(profile.PeakPfSearchCalls)
            .Append(", peak cumulative=").Append(formatMilliseconds(profile.PeakPfSearchElapsedTicks))
            .Append(" ms, worst individual in peak tick=").Append(formatMilliseconds(profile.PeakPfMaxIndividualSearchElapsedTicks)).AppendLine(" ms")
            .Append("Latency: ").Append(formatLatencyBuckets(profile.LatencyBuckets)).AppendLine()
            .Append("Path latency:");

        for (var i = 0; i < SearchPathNames.Length; i++)
        {
            var pathBuckets = new long[LatencyBucketCount];
            Array.Copy(profile.PathLatencyBuckets, i * LatencyBucketCount, pathBuckets, 0, LatencyBucketCount);
            if (hasNonZero(pathBuckets))
                builder.Append("\n  ").Append(SearchPathNames[i]).Append(": ").Append(formatLatencyBuckets(pathBuckets));
        }

        if (profile.WorstCall is not null)
            appendWorstCall(builder, profile.WorstCall);

        builder.AppendLine().Append("Products:");
        var count = Math.Min(profile.Products.Length, MaxProductsInConsoleReport);
        for (var i = 0; i < count; i++)
            appendProductReport(builder, profile.Products[i]);
        if (profile.Products.Length > count)
            builder.Append("\n  ... ").Append(profile.Products.Length - count).Append(" more product(s) omitted.");

        return builder.ToString();
    }

    private static string formatProfileComparison(ProfileSnapshot first, ProfileSnapshot second)
    {
        var builder = new StringBuilder(4096)
            .Append("Dump profile comparison: A=\"").Append(first.Label).Append("\", B=\"").Append(second.Label).AppendLine("\"")
            .Append("A actual=").Append(formatSeconds(first.ActualRecordingSeconds)).Append(", calls=").Append(first.TotalCalls)
            .Append(", calls/sec=").Append(formatDecimal(callsPerSecond(first))).AppendLine()
            .Append("B actual=").Append(formatSeconds(second.ActualRecordingSeconds)).Append(", calls=").Append(second.TotalCalls)
            .Append(", calls/sec=").Append(formatDecimal(callsPerSecond(second))).AppendLine()
            .Append("Outer search: A avg=").Append(formatMilliseconds(averageTicks(first.TotalElapsedTicks, first.CompletedCalls)))
            .Append(" ms, B avg=").Append(formatMilliseconds(averageTicks(second.TotalElapsedTicks, second.CompletedCalls)))
            .Append(" ms, B/A=").Append(formatRatio(second.TotalElapsedTicks / (double)Math.Max(1, second.CompletedCalls), first.TotalElapsedTicks / (double)Math.Max(1, first.CompletedCalls))).AppendLine()
            .Append("  total wall time: A=").Append(formatMilliseconds(first.TotalElapsedTicks)).Append(" ms, B=").Append(formatMilliseconds(second.TotalElapsedTicks)).AppendLine(" ms")
            .Append("  utilization: A=").Append(formatDecimal(utilization(first))).Append("%, B=").Append(formatDecimal(utilization(second))).AppendLine("%")
            .Append("Nested eligible cache: global A=").Append(formatCacheComparison(first.GlobalCacheCalls, first.GlobalCacheElapsedTicks, second.GlobalCacheCalls, second.GlobalCacheElapsedTicks))
            .Append(", per-tower A=").Append(formatCacheComparison(first.TowerCacheCalls, first.TowerCacheElapsedTicks, second.TowerCacheCalls, second.TowerCacheElapsedTicks)).AppendLine()
            .Append("Residual: A=").Append(formatMilliseconds(first.ResidualElapsedTicks)).Append(" ms total / ").Append(formatMilliseconds(averageTicks(first.ResidualElapsedTicks, first.CompletedCalls))).Append(" ms avg, B=")
            .Append(formatMilliseconds(second.ResidualElapsedTicks)).Append(" ms total / ").Append(formatMilliseconds(averageTicks(second.ResidualElapsedTicks, second.CompletedCalls))).AppendLine(" ms avg")
            .Append("Candidates: A avg=").Append(formatDecimal(average(first.TotalCandidateDesignations, first.ObservedCandidateCalls)))
            .Append(", B avg=").Append(formatDecimal(average(second.TotalCandidateDesignations, second.ObservedCandidateCalls)))
            .Append(", B/A=").Append(formatRatio(average(second.TotalCandidateDesignations, second.ObservedCandidateCalls), average(first.TotalCandidateDesignations, first.ObservedCandidateCalls))).AppendLine()
            .Append("Callers A: ").Append(formatPercentageSummary(SearchCallerNames, first.CallerCalls)).AppendLine()
            .Append("Callers B: ").Append(formatPercentageSummary(SearchCallerNames, second.CallerCalls)).AppendLine()
            .Append("Paths A: ").Append(formatPercentageSummary(SearchPathNames, first.PathCalls)).AppendLine()
            .Append("Paths B: ").Append(formatPercentageSummary(SearchPathNames, second.PathCalls));

        var firstDirt = findProduct(first.Products, "Product_Dirt");
        var secondDirt = findProduct(second.Products, "Product_Dirt");
        if (firstDirt is not null || secondDirt is not null)
        {
            var a = firstDirt ?? default;
            var b = secondDirt ?? default;
            var aAvg = averageTicks(a.ElapsedTicks, a.CompletedCalls);
            var bAvg = averageTicks(b.ElapsedTicks, b.CompletedCalls);
            builder.AppendLine().Append("Product_Dirt: A calls=").Append(a.Calls)
                .Append(", B calls=").Append(b.Calls)
                .Append(", outer avg A=").Append(formatMilliseconds(aAvg))
                .Append(" ms, B=").Append(formatMilliseconds(bAvg))
                .Append(" ms, B/A=").Append(formatRatio(bAvg, aAvg)).AppendLine()
                .Append("  candidates/cache: A=").Append(formatDecimal(average(a.CandidateDesignations, a.CandidateCalls)))
                .Append(", B=").Append(formatDecimal(average(b.CandidateDesignations, b.CandidateCalls)))
                .Append(", B/A=").Append(formatRatio(average(b.CandidateDesignations, b.CandidateCalls), average(a.CandidateDesignations, a.CandidateCalls)));
        }

        return builder.ToString();
    }

    private static void appendProductReport(StringBuilder builder, ProductSearchSnapshot stats)
    {
        var completed = stats.TrueResults + stats.FalseResults;
        builder.Append("\n  ").Append(stats.ProductId)
            .Append(": calls=").Append(stats.Calls)
            .Append(", outer avg=").Append(formatMilliseconds(averageTicks(stats.ElapsedTicks, completed)))
            .Append(" ms, max=").Append(formatMilliseconds(stats.MaxElapsedTicks)).Append(" ms")
            .Append(", global-cache=").Append(stats.GlobalCacheCalls).Append("/").Append(formatMilliseconds(stats.GlobalCacheElapsedTicks)).Append(" ms")
            .Append(", per-tower-cache=").Append(stats.TowerCacheCalls).Append("/").Append(formatMilliseconds(stats.TowerCacheElapsedTicks)).Append(" ms")
            .Append(", residual=").Append(formatMilliseconds(stats.ResidualElapsedTicks)).Append(" ms")
            .Append(", candidates=").Append(formatDecimal(average(stats.CandidateDesignations, stats.CandidateCalls)));
        appendProductPathMetrics(builder, stats);
    }

    private static void appendProductPathMetrics(StringBuilder builder, ProductSearchSnapshot stats)
    {
        for (var i = 0; i < SearchPathNames.Length && i < stats.PathCalls.Length; i++)
        {
            var calls = stats.PathCalls[i];
            if (calls == 0)
                continue;

            builder.Append("\n    ").Append(SearchPathNames[i])
                .Append(": outer=").Append(calls).Append('/').Append(formatMilliseconds(stats.PathElapsedTicks[i]))
                .Append(" ms avg=").Append(formatMilliseconds(averageTicks(stats.PathElapsedTicks[i], calls)))
                .Append(" ms max=").Append(formatMilliseconds(stats.PathMaxElapsedTicks[i]))
                .Append("; global-cache=").Append(stats.PathGlobalCacheCalls[i]).Append('/').Append(formatMilliseconds(stats.PathGlobalCacheElapsedTicks[i]))
                .Append(" ms avg=").Append(formatMicroseconds(averageTicks(stats.PathGlobalCacheElapsedTicks[i], stats.PathGlobalCacheCalls[i])))
                .Append(" us max=").Append(formatMilliseconds(stats.PathGlobalCacheMaxElapsedTicks[i]))
                .Append(" ms; per-tower-cache=").Append(stats.PathTowerCacheCalls[i]).Append('/').Append(formatMilliseconds(stats.PathTowerCacheElapsedTicks[i]))
                .Append(" ms avg=").Append(formatMicroseconds(averageTicks(stats.PathTowerCacheElapsedTicks[i], stats.PathTowerCacheCalls[i])))
                .Append(" us max=").Append(formatMilliseconds(stats.PathTowerCacheMaxElapsedTicks[i]))
                .Append(" ms; residual total=").Append(formatMilliseconds(stats.PathResidualElapsedTicks[i]))
                .Append(" ms avg=").Append(formatMicroseconds(averageTicks(stats.PathResidualElapsedTicks[i], calls)))
                .Append(" us max=").Append(formatMilliseconds(stats.PathResidualMaxElapsedTicks[i])).Append(" ms");
        }
    }

    private static void appendWorstCall(StringBuilder builder, WorstCallSnapshot worst)
    {
        builder.Append("\nWorst search: elapsed=").Append(formatMilliseconds(worst.ElapsedTicks))
            .Append(" ms, product=").Append(worst.ProductId)
            .Append(", path=").Append(SearchPathNames[(int)worst.Path])
            .Append(", caller=").Append(SearchCallerNames[(int)worst.Caller])
            .Append(", candidates=").Append(worst.CandidateDesignations)
            .Append(", cache global=").Append(formatMilliseconds(worst.GlobalCacheElapsedTicks))
            .Append(" ms, per-tower=").Append(formatMilliseconds(worst.TowerCacheElapsedTicks))
            .Append(" ms, nested=").Append(formatMilliseconds(worst.NestedCacheElapsedTicks))
            .Append(" ms, residual=").Append(formatMilliseconds(worst.ResidualElapsedTicks))
            .Append(" ms, cache calls=").Append(worst.GlobalCacheCalls).Append('/').Append(worst.TowerCacheCalls);
    }

    private static string formatCacheComparison(long callsA, long ticksA, long callsB, long ticksB)
    {
        return $"A={callsA} calls/{formatMilliseconds(ticksA)} ms avg {formatMicroseconds(averageTicks(ticksA, callsA))} us, B={callsB} calls/{formatMilliseconds(ticksB)} ms avg {formatMicroseconds(averageTicks(ticksB, callsB))} us";
    }

    private static string formatPercentageSummary(string[] names, long[] counters)
    {
        var total = 0L;
        for (var i = 0; i < Math.Min(names.Length, counters.Length); i++)
            total += counters[i];
        if (total == 0)
            return "none";

        var builder = new StringBuilder(160);
        for (var i = 0; i < Math.Min(names.Length, counters.Length); i++)
        {
            if (i > 0)
                builder.Append(", ");
            builder.Append(names[i]).Append('=').Append(formatDecimal(counters[i] * 100.0 / total)).Append('%');
        }

        return builder.ToString();
    }

    private static bool hasNonZero(long[] values)
    {
        for (var i = 0; i < values.Length; i++)
            if (values[i] != 0)
                return true;
        return false;
    }

    private static ProductSearchSnapshot? findProduct(ProductSearchSnapshot[] products, string productId)
    {
        for (var i = 0; i < products.Length; i++)
            if (string.Equals(products[i].ProductId, productId, StringComparison.Ordinal))
                return products[i];
        return null;
    }

    private static string findDominantPath(long[] pathCalls)
    {
        var index = 0;
        var max = 0L;
        for (var i = 0; i < Math.Min(pathCalls.Length, SearchPathNames.Length); i++)
        {
            if (pathCalls[i] > max)
            {
                index = i;
                max = pathCalls[i];
            }
        }

        return max == 0 ? "none" : SearchPathNames[index];
    }

    private static double callsPerSecond(ProfileSnapshot profile)
    {
        return profile.ActualRecordingSeconds > 0.0 ? profile.TotalCalls / profile.ActualRecordingSeconds : 0.0;
    }

    private static double utilization(ProfileSnapshot profile)
    {
        return profile.ActualRecordingSeconds > 0.0
            ? stopwatchTicksToSeconds(profile.TotalElapsedTicks) / profile.ActualRecordingSeconds * 100.0
            : 0.0;
    }

    private static string formatRatio(double numerator, double denominator)
    {
        return denominator == 0.0 ? "n/a" : formatDecimal(numerator / denominator) + "x";
    }

    private static double average(long total, long count)
    {
        return count > 0 ? total / (double)count : 0.0;
    }

    private static double averageTicks(long totalTicks, long count)
    {
        return count > 0 ? totalTicks / (double)count : 0.0;
    }

    private static bool isValidProfileDuration(double value, double minimum, double maximum)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;
    }

    private static string describeProfileState(ProfileState state)
    {
        return state == ProfileState.WarmingUp ? "warming-up" : state == ProfileState.Recording ? "recording" : "idle";
    }

    private static ProfileSnapshot? findProfileLocked(string label)
    {
        for (var i = 0; i < s_profileHistory.Count; i++)
            if (string.Equals(s_profileHistory[i].Label, label, StringComparison.Ordinal))
                return s_profileHistory[i];
        return null;
    }

    private static long secondsToStopwatchTicks(double seconds)
    {
        return (long)Math.Round(seconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);
    }

    private static double stopwatchTicksToSeconds(long ticks)
    {
        return ticks / (double)Stopwatch.Frequency;
    }

    private static string formatSeconds(double seconds)
    {
        return seconds.ToString("F2", CultureInfo.InvariantCulture) + "s";
    }

    private static string formatMilliseconds(double ticks)
    {
        return (ticks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string formatMicroseconds(double ticks)
    {
        return (ticks * 1_000_000.0 / Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string formatDecimal(double value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private sealed class ProductSearchStats
    {
        private readonly long[] m_pathCalls = new long[(int)SearchPath.Count];
        private readonly long[] m_callerCalls = new long[(int)SearchCaller.Count];
        private readonly long[] m_latencyBuckets = new long[LatencyBucketCount];
        private readonly long[] m_pathElapsedTicks = new long[(int)SearchPath.Count];
        private readonly long[] m_pathMaxElapsedTicks = new long[(int)SearchPath.Count];
        private readonly long[] m_pathGlobalCacheCalls = new long[(int)SearchPath.Count];
        private readonly long[] m_pathGlobalCacheElapsedTicks = new long[(int)SearchPath.Count];
        private readonly long[] m_pathGlobalCacheMaxElapsedTicks = new long[(int)SearchPath.Count];
        private readonly long[] m_pathTowerCacheCalls = new long[(int)SearchPath.Count];
        private readonly long[] m_pathTowerCacheElapsedTicks = new long[(int)SearchPath.Count];
        private readonly long[] m_pathTowerCacheMaxElapsedTicks = new long[(int)SearchPath.Count];
        private readonly long[] m_pathResidualElapsedTicks = new long[(int)SearchPath.Count];
        private readonly long[] m_pathResidualMaxElapsedTicks = new long[(int)SearchPath.Count];
        private long m_calls;
        private long m_trueResults;
        private long m_falseResults;
        private long m_elapsedTicks;
        private long m_maxElapsedTicks;
        private long m_candidateDesignations;
        private long m_candidateCalls;
        private long m_globalCacheCalls;
        private long m_globalCacheElapsedTicks;
        private long m_globalCacheMaxElapsedTicks;
        private long m_towerCacheCalls;
        private long m_towerCacheElapsedTicks;
        private long m_towerCacheMaxElapsedTicks;
        private long m_residualElapsedTicks;
        private long m_residualMaxElapsedTicks;
        private WorstCallSnapshot? m_worstCall;

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
        public long CandidateDesignations => read(ref m_candidateDesignations);
        public long CandidateCalls => read(ref m_candidateCalls);
        public long GlobalCacheCalls => read(ref m_globalCacheCalls);
        public long GlobalCacheElapsedTicks => read(ref m_globalCacheElapsedTicks);
        public long GlobalCacheMaxElapsedTicks => read(ref m_globalCacheMaxElapsedTicks);
        public long TowerCacheCalls => read(ref m_towerCacheCalls);
        public long TowerCacheElapsedTicks => read(ref m_towerCacheElapsedTicks);
        public long TowerCacheMaxElapsedTicks => read(ref m_towerCacheMaxElapsedTicks);
        public long ResidualElapsedTicks => read(ref m_residualElapsedTicks);
        public long ResidualMaxElapsedTicks => read(ref m_residualMaxElapsedTicks);
        public WorstCallSnapshot? WorstCall => Volatile.Read(ref m_worstCall);

        public void RecordCallStart()
        {
            Interlocked.Increment(ref m_calls);
        }

        public void RecordCompletion(
            SearchPath path,
            SearchCaller caller,
            bool hasResult,
            bool result,
            long elapsedTicks,
            long candidateDesignations,
            int candidateCalls,
            CacheSummary cacheSummary,
            long residualTicks)
        {
            Interlocked.Increment(ref m_pathCalls[(int)path]);
            Interlocked.Increment(ref m_callerCalls[(int)caller]);
            var pathIndex = (int)path;

            if (hasResult)
            {
                if (result)
                    Interlocked.Increment(ref m_trueResults);
                else
                    Interlocked.Increment(ref m_falseResults);
                Interlocked.Add(ref m_elapsedTicks, elapsedTicks);
                updateMax(ref m_maxElapsedTicks, elapsedTicks);
                Interlocked.Add(ref m_pathElapsedTicks[pathIndex], elapsedTicks);
                updateMax(ref m_pathMaxElapsedTicks[pathIndex], elapsedTicks);
            }

            if (candidateCalls > 0)
            {
                Interlocked.Add(ref m_candidateDesignations, candidateDesignations);
                Interlocked.Increment(ref m_candidateCalls);
            }

            Interlocked.Add(ref m_globalCacheCalls, cacheSummary.GlobalCalls);
            Interlocked.Add(ref m_globalCacheElapsedTicks, cacheSummary.GlobalElapsedTicks);
            updateMax(ref m_globalCacheMaxElapsedTicks, cacheSummary.GlobalMaxElapsedTicks);
            Interlocked.Add(ref m_towerCacheCalls, cacheSummary.TowerCalls);
            Interlocked.Add(ref m_towerCacheElapsedTicks, cacheSummary.TowerElapsedTicks);
            updateMax(ref m_towerCacheMaxElapsedTicks, cacheSummary.TowerMaxElapsedTicks);
            Interlocked.Add(ref m_residualElapsedTicks, residualTicks);
            updateMax(ref m_residualMaxElapsedTicks, residualTicks);
            Interlocked.Increment(ref m_latencyBuckets[getLatencyBucket(elapsedTicks)]);
            Interlocked.Add(ref m_pathGlobalCacheCalls[pathIndex], cacheSummary.GlobalCalls);
            Interlocked.Add(ref m_pathGlobalCacheElapsedTicks[pathIndex], cacheSummary.GlobalElapsedTicks);
            updateMax(ref m_pathGlobalCacheMaxElapsedTicks[pathIndex], cacheSummary.GlobalMaxElapsedTicks);
            Interlocked.Add(ref m_pathTowerCacheCalls[pathIndex], cacheSummary.TowerCalls);
            Interlocked.Add(ref m_pathTowerCacheElapsedTicks[pathIndex], cacheSummary.TowerElapsedTicks);
            updateMax(ref m_pathTowerCacheMaxElapsedTicks[pathIndex], cacheSummary.TowerMaxElapsedTicks);
            Interlocked.Add(ref m_pathResidualElapsedTicks[pathIndex], residualTicks);
            updateMax(ref m_pathResidualMaxElapsedTicks[pathIndex], residualTicks);
        }

        public ProductSearchSnapshot Snapshot()
        {
            return new ProductSearchSnapshot(this, CreateSnapshotCounters());
        }

        private ProductSnapshotCounters CreateSnapshotCounters()
        {
            return new ProductSnapshotCounters
            {
                PathCalls = snapshotCounters(m_pathCalls),
                CallerCalls = snapshotCounters(m_callerCalls),
                LatencyBuckets = snapshotCounters(m_latencyBuckets),
                PathElapsedTicks = snapshotCounters(m_pathElapsedTicks),
                PathMaxElapsedTicks = snapshotCounters(m_pathMaxElapsedTicks),
                PathGlobalCacheCalls = snapshotCounters(m_pathGlobalCacheCalls),
                PathGlobalCacheElapsedTicks = snapshotCounters(m_pathGlobalCacheElapsedTicks),
                PathGlobalCacheMaxElapsedTicks = snapshotCounters(m_pathGlobalCacheMaxElapsedTicks),
                PathTowerCacheCalls = snapshotCounters(m_pathTowerCacheCalls),
                PathTowerCacheElapsedTicks = snapshotCounters(m_pathTowerCacheElapsedTicks),
                PathTowerCacheMaxElapsedTicks = snapshotCounters(m_pathTowerCacheMaxElapsedTicks),
                PathResidualElapsedTicks = snapshotCounters(m_pathResidualElapsedTicks),
                PathResidualMaxElapsedTicks = snapshotCounters(m_pathResidualMaxElapsedTicks),
            };
        }

        public void Reset()
        {
            Interlocked.Exchange(ref m_calls, 0);
            Interlocked.Exchange(ref m_trueResults, 0);
            Interlocked.Exchange(ref m_falseResults, 0);
            Interlocked.Exchange(ref m_elapsedTicks, 0);
            Interlocked.Exchange(ref m_maxElapsedTicks, 0);
            Interlocked.Exchange(ref m_candidateDesignations, 0);
            Interlocked.Exchange(ref m_candidateCalls, 0);
            Interlocked.Exchange(ref m_globalCacheCalls, 0);
            Interlocked.Exchange(ref m_globalCacheElapsedTicks, 0);
            Interlocked.Exchange(ref m_globalCacheMaxElapsedTicks, 0);
            Interlocked.Exchange(ref m_towerCacheCalls, 0);
            Interlocked.Exchange(ref m_towerCacheElapsedTicks, 0);
            Interlocked.Exchange(ref m_towerCacheMaxElapsedTicks, 0);
            Interlocked.Exchange(ref m_residualElapsedTicks, 0);
            Interlocked.Exchange(ref m_residualMaxElapsedTicks, 0);
            Volatile.Write(ref m_worstCall, null);
            for (var i = 0; i < m_pathCalls.Length; i++)
                Interlocked.Exchange(ref m_pathCalls[i], 0);
            for (var i = 0; i < m_callerCalls.Length; i++)
                Interlocked.Exchange(ref m_callerCalls[i], 0);
            for (var i = 0; i < m_latencyBuckets.Length; i++)
                Interlocked.Exchange(ref m_latencyBuckets[i], 0);
            for (var i = 0; i < m_pathElapsedTicks.Length; i++)
            {
                Interlocked.Exchange(ref m_pathElapsedTicks[i], 0);
                Interlocked.Exchange(ref m_pathMaxElapsedTicks[i], 0);
                Interlocked.Exchange(ref m_pathGlobalCacheCalls[i], 0);
                Interlocked.Exchange(ref m_pathGlobalCacheElapsedTicks[i], 0);
                Interlocked.Exchange(ref m_pathGlobalCacheMaxElapsedTicks[i], 0);
                Interlocked.Exchange(ref m_pathTowerCacheCalls[i], 0);
                Interlocked.Exchange(ref m_pathTowerCacheElapsedTicks[i], 0);
                Interlocked.Exchange(ref m_pathTowerCacheMaxElapsedTicks[i], 0);
                Interlocked.Exchange(ref m_pathResidualElapsedTicks[i], 0);
                Interlocked.Exchange(ref m_pathResidualMaxElapsedTicks[i], 0);
            }
        }

        public void RecordWorst(WorstCallSnapshot candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref m_worstCall);
                if (current is not null && current.ElapsedTicks >= candidate.ElapsedTicks)
                    return;
                if (ReferenceEquals(Interlocked.CompareExchange(ref m_worstCall, candidate, current), current))
                    return;
            }
        }
    }

    private sealed class ProductSnapshotCounters
    {
        public long[] PathCalls = Array.Empty<long>();
        public long[] CallerCalls = Array.Empty<long>();
        public long[] LatencyBuckets = Array.Empty<long>();
        public long[] PathElapsedTicks = Array.Empty<long>();
        public long[] PathMaxElapsedTicks = Array.Empty<long>();
        public long[] PathGlobalCacheCalls = Array.Empty<long>();
        public long[] PathGlobalCacheElapsedTicks = Array.Empty<long>();
        public long[] PathGlobalCacheMaxElapsedTicks = Array.Empty<long>();
        public long[] PathTowerCacheCalls = Array.Empty<long>();
        public long[] PathTowerCacheElapsedTicks = Array.Empty<long>();
        public long[] PathTowerCacheMaxElapsedTicks = Array.Empty<long>();
        public long[] PathResidualElapsedTicks = Array.Empty<long>();
        public long[] PathResidualMaxElapsedTicks = Array.Empty<long>();

    }

    private readonly struct ProductSearchSnapshot
    {
        public ProductSearchSnapshot(ProductSearchStats source, ProductSnapshotCounters counters)
        {
            ProductId = source.ProductId;
            Calls = source.Calls;
            TrueResults = source.TrueResults;
            FalseResults = source.FalseResults;
            ElapsedTicks = source.ElapsedTicks;
            MaxElapsedTicks = source.MaxElapsedTicks;
            CandidateDesignations = source.CandidateDesignations;
            CandidateCalls = source.CandidateCalls;
            GlobalCacheCalls = source.GlobalCacheCalls;
            GlobalCacheElapsedTicks = source.GlobalCacheElapsedTicks;
            GlobalCacheMaxElapsedTicks = source.GlobalCacheMaxElapsedTicks;
            TowerCacheCalls = source.TowerCacheCalls;
            TowerCacheElapsedTicks = source.TowerCacheElapsedTicks;
            TowerCacheMaxElapsedTicks = source.TowerCacheMaxElapsedTicks;
            ResidualElapsedTicks = source.ResidualElapsedTicks;
            ResidualMaxElapsedTicks = source.ResidualMaxElapsedTicks;
            WorstCall = source.WorstCall;
            PathCalls = counters.PathCalls;
            CallerCalls = counters.CallerCalls;
            LatencyBuckets = counters.LatencyBuckets;
            PathElapsedTicks = counters.PathElapsedTicks;
            PathMaxElapsedTicks = counters.PathMaxElapsedTicks;
            PathGlobalCacheCalls = counters.PathGlobalCacheCalls;
            PathGlobalCacheElapsedTicks = counters.PathGlobalCacheElapsedTicks;
            PathGlobalCacheMaxElapsedTicks = counters.PathGlobalCacheMaxElapsedTicks;
            PathTowerCacheCalls = counters.PathTowerCacheCalls;
            PathTowerCacheElapsedTicks = counters.PathTowerCacheElapsedTicks;
            PathTowerCacheMaxElapsedTicks = counters.PathTowerCacheMaxElapsedTicks;
            PathResidualElapsedTicks = counters.PathResidualElapsedTicks;
            PathResidualMaxElapsedTicks = counters.PathResidualMaxElapsedTicks;
        }

        public string ProductId { get; }
        public long Calls { get; }
        public long TrueResults { get; }
        public long FalseResults { get; }
        public long CompletedCalls => TrueResults + FalseResults;
        public long ElapsedTicks { get; }
        public long MaxElapsedTicks { get; }
        public long CandidateDesignations { get; }
        public long CandidateCalls { get; }
        public long GlobalCacheCalls { get; }
        public long GlobalCacheElapsedTicks { get; }
        public long GlobalCacheMaxElapsedTicks { get; }
        public long TowerCacheCalls { get; }
        public long TowerCacheElapsedTicks { get; }
        public long TowerCacheMaxElapsedTicks { get; }
        public long ResidualElapsedTicks { get; }
        public long ResidualMaxElapsedTicks { get; }
        public WorstCallSnapshot? WorstCall { get; }
        public long[] PathCalls { get; }
        public long[] CallerCalls { get; }
        public long[] LatencyBuckets { get; }
        public long[] PathElapsedTicks { get; }
        public long[] PathMaxElapsedTicks { get; }
        public long[] PathGlobalCacheCalls { get; }
        public long[] PathGlobalCacheElapsedTicks { get; }
        public long[] PathGlobalCacheMaxElapsedTicks { get; }
        public long[] PathTowerCacheCalls { get; }
        public long[] PathTowerCacheElapsedTicks { get; }
        public long[] PathTowerCacheMaxElapsedTicks { get; }
        public long[] PathResidualElapsedTicks { get; }
        public long[] PathResidualMaxElapsedTicks { get; }
    }
}
