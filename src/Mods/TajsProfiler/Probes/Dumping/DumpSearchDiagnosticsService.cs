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
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.Vehicles.Trucks.JobProviders;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

#endregion

namespace TajsCOI.Profiler.Probes.Dumping
{
    /// <summary>
    ///     Records low-overhead runtime statistics for dumping-destination searches. This service is
    ///     intentionally diagnostics-only: it never suppresses, retries, reorders, or changes a
    ///     vanilla dumping search result. Any instrumentation failure fails open for that call.
    ///     The 0.8.7a search retains the instrumented 0.8.7 signatures while adding vanilla tower-filter
    ///     caching and bounded nearby expansion. A globally-forbidden product with no explicit tower list
    ///     can still enter the local-tower fallback when one or more MineTowers accept it; that path remains
    ///     the primary forensic distinction recorded here.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class DumpSearchDiagnosticsService
    {
        private const string HarmonyId = "TajsCOI.Profiler.Dumping";
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
        private static readonly SearchBreakdownStats s_breakdownStats = new();
        private static readonly object s_profileGate = new();
        private static readonly List<ProfileSnapshot> s_profileHistory = new(MaxProfileHistory);

        private static IGameConsole? s_console;
        private static ITajsLogger? s_log;

        private static int s_patchesApplied;
        private static int s_tickBoundaryPatchApplied;
        private static int s_pfEnqueuePatchApplied;
        private static int s_callerPatchCount;
        private static int s_cachePatchCount;
        private static int s_breakdownPatchCount;
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
        private static int s_bestSelectionPrefixErrorLogged;
        private static int s_bestSelectionPostfixErrorLogged;
        private static int s_bestSelectionFinalizerErrorLogged;
        private static int s_nearbyDiagnosticsErrorLogged;

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

        private static readonly string[] s_searchPathNames =
        {
            "unknown",
            "global-allowed",
            "global-forbidden-local-fallback",
            "global-forbidden-no-local-tower",
            "explicit-tower",
            "explicit-tower-global-forbidden-local",
            "explicit-tower-global-forbidden-rejected",
        };

        private static readonly string[] s_searchStageNames =
        {
            "pre-selection", "candidate-filtering", "TryFindBestReadyToFulfill", "nearby-expansion", "unaccounted",
        };

        private static readonly string[] s_latencyBucketNames =
        {
            "<0.1ms", "0.1-1ms", "1-5ms", "5-10ms", "10-25ms", "25-50ms", "50-100ms", "100-250ms", ">=250ms",
        };

        private static readonly string[] s_searchCallerNames =
        {
            "other", "VehicleBuffersRegistry.balanceBuffers", "DumpingJob", "DefaultTruckJobProvider.TryGetJobFor",
        };

        public DumpSearchDiagnosticsService(IGameConsole console, ITajsRuntime runtime)
        {
            // Static Harmony callbacks publish deferred reports through these process-lifetime
            // services; CoI creates this global dependency once per game process.
#pragma warning disable S2696
            s_console = console;
            s_log = runtime.GetLogger("TajsProfiler", "Dumping");
#pragma warning restore S2696
            EnsurePatchesApplied();
            ReportCompatibility(runtime);
        }

        [ConsoleCommand(
            documentation: "Shows behavior-neutral dumping-search path, caller, cache, candidate and timing diagnostics.",
            customCommandName: "tajs_dump_search_stats")]
        public string GetStats()
        {
            List<ProductSearchSnapshot> snapshot = SnapshotProductStats();
            SortProductSnapshots(snapshot);

            long[] pathCalls = SnapshotCounters(s_pathCalls);
            long[] callerCalls = SnapshotCounters(s_callerCalls);
            long[] pathCandidateDesignations = SnapshotCounters(s_pathCandidateDesignations);
            long[] pathCandidateCalls = SnapshotCounters(s_pathCandidateCalls);
            SearchBreakdownSnapshot breakdown = s_breakdownStats.Snapshot();

            long totalCalls = Read(ref s_totalCalls);
            long completedCalls = Read(ref s_totalTrueResults) + Read(ref s_totalFalseResults);
            long incompleteCalls = Math.Max(0, totalCalls - completedCalls);
            double totalMs = StopwatchTicksToMilliseconds(Read(ref s_totalElapsedTicks));
            double avgUs = completedCalls > 0
                ? StopwatchTicksToMicroseconds(Read(ref s_totalElapsedTicks)) / completedCalls
                : 0.0;

            var builder = new StringBuilder(2048);
            builder.Append("Dump search diagnostics: active=")
                .Append(ReadInt(ref s_patchesApplied) != 0)
                .Append(", functional limiter=disabled, tick buckets=")
                .Append(ReadInt(ref s_tickBoundaryPatchApplied) != 0)
                .Append(", PF enqueue diagnostics=")
                .Append(ReadInt(ref s_pfEnqueuePatchApplied) != 0)
                .Append(", breakdown patches=")
                .Append(ReadInt(ref s_breakdownPatchCount))
                .Append("; calls current/last/peak=")
                .Append(Read(ref s_currentCalls))
                .Append('/')
                .Append(Read(ref s_lastCalls))
                .Append('/')
                .Append(Read(ref s_peakCalls))
                .Append("; total=")
                .Append(totalCalls)
                .Append("; returned true/false/incomplete=")
                .Append(Read(ref s_totalTrueResults))
                .Append('/')
                .Append(Read(ref s_totalFalseResults))
                .Append('/')
                .Append(incompleteCalls)
                .Append("; completed-call time=")
                .Append(totalMs.ToString("F2"))
                .Append(" ms, avg=")
                .Append(avgUs.ToString("F1"))
                .Append(" us, max=")
                .Append(StopwatchTicksToMilliseconds(Read(ref s_maxElapsedTicks)).ToString("F3"))
                .Append(" ms.");

            long globalCacheCalls = Read(ref s_globalEligibleCacheCalls);
            long towerCacheCalls = Read(ref s_towerEligibleCacheCalls);
            builder.Append("\nPaths: ")
                .Append(FormatCounterSummary(s_searchPathNames, pathCalls))
                .Append("\nCallers: ")
                .Append(FormatCounterSummary(s_searchCallerNames, callerCalls))
                .Append("\nEligible-cache timing: global calls=")
                .Append(globalCacheCalls)
                .Append(", total=")
                .Append(StopwatchTicksToMilliseconds(Read(ref s_globalEligibleCacheElapsedTicks)).ToString("F2"))
                .Append(" ms, avg=")
                .Append(FormatMicroseconds(AverageTicks(Read(ref s_globalEligibleCacheElapsedTicks), globalCacheCalls)))
                .Append(" us, max=")
                .Append(FormatMilliseconds(Read(ref s_globalEligibleCacheMaxElapsedTicks)))
                .Append(" ms; per-tower calls=")
                .Append(towerCacheCalls)
                .Append(", total=")
                .Append(StopwatchTicksToMilliseconds(Read(ref s_towerEligibleCacheElapsedTicks)).ToString("F2"))
                .Append(" ms, avg=")
                .Append(FormatMicroseconds(AverageTicks(Read(ref s_towerEligibleCacheElapsedTicks), towerCacheCalls)))
                .Append(" us, max=")
                .Append(FormatMilliseconds(Read(ref s_towerEligibleCacheMaxElapsedTicks)))
                .Append(" ms")
                .Append("\nResidual after eligible-cache calls: total=")
                .Append(FormatMilliseconds(Read(ref s_nestedResidualElapsedTicks)))
                .Append(" ms, avg=")
                .Append(FormatMicroseconds(AverageTicks(Read(ref s_nestedResidualElapsedTicks), completedCalls)))
                .Append(" us, max=")
                .Append(FormatMilliseconds(Read(ref s_nestedResidualMaxElapsedTicks)))
                .Append(", accounting anomalies=")
                .Append(Read(ref s_nestedResidualAccountingAnomalies))
                .Append(", raw eligible-cache candidates total/searches/max=")
                .Append(Read(ref s_totalCandidateDesignations))
                .Append('/')
                .Append(Read(ref s_observedCandidateCalls))
                .Append('/')
                .Append(Read(ref s_maxCandidateDesignations))
                .Append("\nBreakdown: ")
                .Append(FormatStageSummary(breakdown))
                .Append("\nBreakdown counts: final m_designationsCache total/searches/max=")
                .Append(FormatCountSummary(breakdown.FinalCandidateCount, breakdown.FinalCandidateCalls, breakdown.FinalCandidateMax))
                .Append(", nearby scanned/accepted/added=")
                .Append(breakdown.NearbyScanned)
                .Append('/')
                .Append(breakdown.NearbyAccepted)
                .Append('/')
                .Append(breakdown.NearbyAdded)
                .Append(", nearby mode tower/global/unknown=")
                .Append(breakdown.NearbyModeCalls[(int)NearbyMode.Tower])
                .Append('/')
                .Append(breakdown.NearbyModeCalls[(int)NearbyMode.Global])
                .Append('/')
                .Append(breakdown.NearbyModeCalls[(int)NearbyMode.Unknown])
                .Append("\nPath raw eligible-cache candidates total/searches: ")
                .Append(FormatCounterSummary(s_searchPathNames, pathCandidateDesignations, pathCandidateCalls))
                .Append("\nPF enqueues current/last/peak/total=")
                .Append(Read(ref s_currentPfEnqueues))
                .Append('/')
                .Append(Read(ref s_lastPfEnqueues))
                .Append('/')
                .Append(Read(ref s_peakPfEnqueues))
                .Append('/')
                .Append(Read(ref s_totalPfEnqueues))
                .Append("; search time current/last/peak=")
                .Append(FormatMilliseconds(Read(ref s_currentPfSearchElapsedTicks)))
                .Append('/')
                .Append(FormatMilliseconds(Read(ref s_lastPfSearchElapsedTicks)))
                .Append('/')
                .Append(FormatMilliseconds(Read(ref s_peakPfSearchElapsedTicks)))
                .Append(" ms, peak tick calls=")
                .Append(Read(ref s_peakPfSearchCalls))
                .Append(", peak-tick worst individual=")
                .Append(FormatMilliseconds(Read(ref s_peakPfMaxIndividualSearchElapsedTicks)))
                .Append(" ms.")
                .Append("\nOuter latency buckets: ")
                .Append(FormatLatencyBuckets(s_latencyBuckets));
            AppendPathBreakdown(builder, breakdown, pathCalls);

            WorstCallSnapshot? worstCall = Volatile.Read(ref s_worstCall);
            if (worstCall is not null)
            {
                AppendWorstCall(builder, worstCall);
            }

            if (snapshot.Count == 0)
            {
                builder.Append("\nNo dumping searches recorded since the last reset.");
                return builder.ToString();
            }

            builder.Append("\nTop products since reset:");
            int count = Math.Min(snapshot.Count, MaxProductsInConsoleReport);
            for (int i = 0; i < count; i++)
            {
                ProductSearchSnapshot stats = snapshot[i];
                long completed = stats.TrueResults + stats.FalseResults;
                long incomplete = Math.Max(0, stats.Calls - completed);
                double productAvgUs = completed > 0
                    ? StopwatchTicksToMicroseconds(stats.ElapsedTicks) / completed
                    : 0.0;

                builder.Append("\n  ")
                    .Append(stats.ProductId)
                    .Append(": calls=")
                    .Append(stats.Calls)
                    .Append(" [")
                    .Append(FormatCounterSummary(s_searchPathNames, stats.PathCalls))
                    .Append("; callers=")
                    .Append(FormatCounterSummary(s_searchCallerNames, stats.CallerCalls))
                    .Append("; raw eligible-cache candidates total/cache-searches=")
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
                    .Append(StopwatchTicksToMilliseconds(stats.ElapsedTicks).ToString("F2"))
                    .Append(" ms, avg=")
                    .Append(productAvgUs.ToString("F1"))
                    .Append(" us, max=")
                    .Append(StopwatchTicksToMilliseconds(stats.MaxElapsedTicks).ToString("F3"))
                    .Append(" ms; cache global=")
                    .Append(stats.GlobalCacheCalls)
                    .Append('/')
                    .Append(FormatMilliseconds(stats.GlobalCacheElapsedTicks))
                    .Append(" ms avg=")
                    .Append(FormatMicroseconds(AverageTicks(stats.GlobalCacheElapsedTicks, stats.GlobalCacheCalls)))
                    .Append(" us max=")
                    .Append(FormatMilliseconds(stats.GlobalCacheMaxElapsedTicks))
                    .Append(" ms; tower=")
                    .Append(stats.TowerCacheCalls)
                    .Append('/')
                    .Append(FormatMilliseconds(stats.TowerCacheElapsedTicks))
                    .Append(" ms avg=")
                    .Append(FormatMicroseconds(AverageTicks(stats.TowerCacheElapsedTicks, stats.TowerCacheCalls)))
                    .Append(" us max=")
                    .Append(FormatMilliseconds(stats.TowerCacheMaxElapsedTicks))
                    .Append(" ms; residual=")
                    .Append(FormatMilliseconds(stats.ResidualElapsedTicks))
                    .Append(" ms, latency=")
                    .Append(FormatLatencyBuckets(stats.LatencyBuckets));
                builder.Append("; breakdown=").Append(FormatStageSummary(stats.Breakdown));
                builder.Append("; final-cache total/searches/max=")
                    .Append(
                        FormatCountSummary(
                            stats.Breakdown.FinalCandidateCount,
                            stats.Breakdown.FinalCandidateCalls,
                            stats.Breakdown.FinalCandidateMax));
                builder.Append("; nearby scanned/accepted/added=")
                    .Append(stats.Breakdown.NearbyScanned)
                    .Append('/')
                    .Append(stats.Breakdown.NearbyAccepted)
                    .Append('/')
                    .Append(stats.Breakdown.NearbyAdded)
                    .Append(" mode tower/global/unknown=")
                    .Append(stats.Breakdown.NearbyModeCalls[(int)NearbyMode.Tower])
                    .Append('/')
                    .Append(stats.Breakdown.NearbyModeCalls[(int)NearbyMode.Global])
                    .Append('/')
                    .Append(stats.Breakdown.NearbyModeCalls[(int)NearbyMode.Unknown]);
                AppendProductPathMetrics(builder, stats);
            }

            if (snapshot.Count > count)
            {
                builder.Append("\n  ... ").Append(snapshot.Count - count).Append(" more product(s) omitted.");
            }

            builder.Append(
                "\nNote: the old dumping-search limiter is removed. A globally-forbidden product with a locally accepting tower is reported as a fallback path; all vanilla results continue through unchanged.");
            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Compatibility alias for the dumping-search and path-finding diagnostics.",
            customCommandName: "tajs_dump_pf_stats")]
        public string GetPathfindingStats() => GetStats();

        [ConsoleCommand(
            documentation: "Resets accumulated dumping-search and path-finding diagnostics.",
            customCommandName: "tajs_dump_search_stats_reset")]
        public string ResetStats()
        {
            ResetAllStats();
            return "Dump search diagnostics reset.";
        }

        [ConsoleCommand(
            documentation: "Compatibility alias for resetting dumping-search and path-finding diagnostics.",
            customCommandName: "tajs_dump_pf_stats_reset")]
        public string ResetPathfindingStats() => ResetStats();

        [ConsoleCommand(
            documentation: "Starts a timed dumping-search profile: seconds, optional label, optional warmup seconds.",
            customCommandName: "tajs_dump_profile")]
        public string StartProfile(float seconds, string? label = null, float? warmupSeconds = null)
        {
            double requestedSeconds = seconds;
            double requestedWarmupSeconds = warmupSeconds.HasValue ? warmupSeconds.Value : 0.0;
            if (!IsValidProfileDuration(requestedSeconds, 0.25, 300.0))
            {
                return "Dump profile rejected: duration must be finite and between 0.25 and 300 seconds.";
            }
            if (!IsValidProfileDuration(requestedWarmupSeconds, 0.0, 300.0))
            {
                return "Dump profile rejected: warmup must be finite and between 0 and 300 seconds.";
            }

            string? normalizedLabel = label?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedLabel))
            {
                normalizedLabel = null;
            }
            ProfileSession? active;
            lock (s_profileGate)
            {
                active = s_activeProfile;
                if (active is not null)
                {
                    return $"Dump profile rejected: '{active.Label}' is already {DescribeProfileState(active.State)}.";
                }

                if (normalizedLabel is null)
                {
                    do
                    {
                        normalizedLabel = "run-" + Interlocked.Increment(ref s_nextAutomaticLabel).ToString(CultureInfo.InvariantCulture);
                    }
                    while (FindProfileLocked(normalizedLabel) is not null);
                }
                else if (FindProfileLocked(normalizedLabel) is not null)
                {
                    return $"Dump profile rejected: completed profile label '{normalizedLabel}' already exists.";
                }

                ResetAllStats();
                long now = Stopwatch.GetTimestamp();
                ProfileState sessionState = requestedWarmupSeconds > 0.0 ? ProfileState.WarmingUp : ProfileState.Recording;
                long recordingStart = sessionState == ProfileState.Recording ? now : 0L;
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
                ? $"Dump profile '{active.Label}' armed: warmup={FormatSeconds(requestedWarmupSeconds)}, recording={FormatSeconds(requestedSeconds)}."
                : $"Dump profile '{active.Label}' recording for {FormatSeconds(requestedSeconds)}.";
        }

        [ConsoleCommand(
            documentation: "Shows the current timed dumping-search profile state.",
            customCommandName: "tajs_dump_profile_status")]
        public string GetProfileStatus()
        {
            lock (s_profileGate)
            {
                ProfileSession? active = s_activeProfile;
                if (active is null)
                {
                    return "Dump profile status: idle.";
                }

                long now = Stopwatch.GetTimestamp();
                double elapsed = StopwatchTicksToSeconds(Math.Max(0, now - active.StartTimestamp));
                long deadline = active.State == ProfileState.WarmingUp ? active.WarmupEndTimestamp : active.RecordingEndTimestamp;
                double remaining = deadline == 0L ? 0.0 : Math.Max(0.0, StopwatchTicksToSeconds(deadline - now));
                return
                    $"Dump profile status: {DescribeProfileState(active.State)}, label='{active.Label}', requested={FormatSeconds(active.RequestedSeconds)}, warmup={FormatSeconds(active.WarmupSeconds)}, elapsed={FormatSeconds(elapsed)}, remaining={FormatSeconds(remaining)}, calls={Read(ref s_totalCalls)}.";
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
                {
                    return "Dump profile stop: no active profile.";
                }

                completed = FinishProfileLocked(Stopwatch.GetTimestamp());
            }

            PublishCompletedProfile(completed, false);
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
                {
                    return "Dump profile cancel: no active profile.";
                }

                string label = s_activeProfile.Label;
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
                {
                    return "Dump profiles: none stored.";
                }

                StringBuilder builder = new StringBuilder(1024).Append("Dump profiles:");
                foreach (ProfileSnapshot profile in s_profileHistory)
                {
                    builder.Append("\n  ")
                        .Append(profile.Label)
                        .Append(" [sequence=")
                        .Append(profile.Sequence)
                        .Append("] duration=")
                        .Append(FormatSeconds(profile.ActualRecordingSeconds))
                        .Append(", calls=")
                        .Append(profile.TotalCalls)
                        .Append(", search=")
                        .Append(FormatMilliseconds(profile.TotalElapsedTicks))
                        .Append(" ms, dominant=")
                        .Append(profile.DominantPath);

                    ProductSearchSnapshot? dirt = FindProduct(profile.Products, "Product_Dirt");
                    if (dirt is not null)
                    {
                        ProductSearchSnapshot dirtValue = dirt.Value;
                        builder.Append(", Product_Dirt avg=")
                            .Append(FormatMicroseconds(AverageTicks(dirtValue.ElapsedTicks, dirtValue.CompletedCalls)))
                            .Append(" us");
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
            {
                return "Dump profile show: label is required.";
            }

            lock (s_profileGate)
            {
                ProfileSnapshot? profile = FindProfileLocked(label.Trim());
                return profile is null
                    ? $"Dump profile show: no completed profile named '{label.Trim()}'."
                    : FormatProfileReport(profile);
            }
        }

        [ConsoleCommand(
            documentation: "Clears completed timed dumping-search profiles without affecting an active profile.",
            customCommandName: "tajs_dump_profile_clear")]
        public string ClearProfiles()
        {
            lock (s_profileGate)
            {
                int count = s_profileHistory.Count;
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
            {
                return "Dump profile compare: two labels are required.";
            }

            lock (s_profileGate)
            {
                ProfileSnapshot? first = FindProfileLocked(labelA.Trim());
                ProfileSnapshot? second = FindProfileLocked(labelB.Trim());
                if (first is null || second is null)
                {
                    return $"Dump profile compare: missing profile(s); A='{labelA.Trim()}', B='{labelB.Trim()}'.";
                }

                return FormatProfileComparison(first, second);
            }
        }

        private static void EnsurePatchesApplied()
        {
            if (ReadInt(ref s_patchesApplied) != 0)
            {
                return;
            }

            MethodInfo? dumpSearch = FindInstanceMethod(
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
                LogError("Dump-search diagnostics disabled; expected CoI 0.8.7/0.8.7a dumping-search signature was not found.");
                return;
            }

            var harmony = new Harmony(HarmonyId);
            try
            {
                var prefix = new HarmonyMethod(
                    typeof(DumpSearchDiagnosticsService),
                    nameof(BeforeDumpSearch)) { priority = Priority.First };
                var postfix = new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(AfterDumpSearch)) { priority = Priority.Last };
                harmony.Patch(
                    dumpSearch,
                    prefix,
                    postfix,
                    finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(EndDumpSearchContext)));
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
                    LogError($"Dump-search diagnostics rollback also failed: {rollbackException}");
                }

                LogError($"Dump-search diagnostics failed to patch dumping search: {ex}");
                return;
            }

            PatchTickBoundary(harmony);
            PatchPathFindingEnqueue(harmony);
            PatchCallerMethods(harmony);
            PatchCacheMethods(harmony);
            PatchBreakdownMethods(harmony);

            LogInfo(
                $"Dump-search diagnostics active; functional limiter disabled, PF tick buckets={ReadInt(ref s_tickBoundaryPatchApplied) != 0}, " +
                $"PF enqueue diagnostics={ReadInt(ref s_pfEnqueuePatchApplied) != 0}, caller patches={ReadInt(ref s_callerPatchCount)}, cache patches={ReadInt(ref s_cachePatchCount)}, breakdown patches={ReadInt(ref s_breakdownPatchCount)}.");
        }

        private static void PatchTickBoundary(Harmony harmony)
        {
            MethodInfo? simUpdate = FindInstanceMethod(
                typeof(VehiclePathFindingManager),
                "SimUpdateInternal",
                typeof(void));
            if (simUpdate is null)
            {
                LogOptionalPatchFailure("VehiclePathFindingManager.SimUpdateInternal was not found");
                return;
            }

            try
            {
                harmony.Patch(
                    simUpdate,
                    new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(BeginPathFindingTick)));
                Interlocked.Exchange(ref s_tickBoundaryPatchApplied, 1);
            }
            catch (Exception ex)
            {
                LogOptionalPatchFailure($"PF tick boundary patch failed: {ex}");
            }
        }

        private static void PatchPathFindingEnqueue(Harmony harmony)
        {
            MethodInfo? enqueueTask = FindInstanceMethod(
                typeof(VehiclePathFindingManager),
                "EnqueueTask",
                typeof(void),
                typeof(IManagedVehiclePathFindingTask),
                typeof(int));
            if (enqueueTask is null)
            {
                LogOptionalPatchFailure("VehiclePathFindingManager.EnqueueTask was not found");
                return;
            }

            try
            {
                harmony.Patch(
                    enqueueTask,
                    new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(BeforePathFindingEnqueue)));
                Interlocked.Exchange(ref s_pfEnqueuePatchApplied, 1);
            }
            catch (Exception ex)
            {
                LogOptionalPatchFailure($"PF enqueue diagnostics patch failed: {ex}");
            }
        }

        private static void PatchCallerMethods(Harmony harmony)
        {
            PatchCaller(
                harmony,
                FindInstanceMethod(
                    typeof(VehicleBuffersRegistry),
                    "balanceBuffers",
                    typeof(void),
                    typeof(Percent)),
                nameof(BeginVehicleBuffersBalanceBuffers),
                "VehicleBuffersRegistry.balanceBuffers");

            PatchCaller(
                harmony,
                FindInstanceMethod(
                    typeof(DefaultTruckJobProvider),
                    "TryGetJobFor",
                    typeof(bool),
                    typeof(Truck)),
                nameof(BeginDefaultTruckJobProvider),
                "DefaultTruckJobProvider.TryGetJobFor");

            PatchCaller(
                harmony,
                FindUniqueInstanceMethod(typeof(DumpingJob), "handleFindMoreDesignations", 0),
                nameof(BeginDumpingJob),
                "DumpingJob.handleFindMoreDesignations");

            List<MethodInfo> factoryMethods = AccessTools.GetDeclaredMethods(typeof(DumpingJob.Factory));
            foreach (MethodInfo method in factoryMethods)
            {
                if (method.Name == "TryCreateAndEnqueueJob" && !method.IsStatic)
                {
                    PatchCaller(harmony, method, nameof(BeginDumpingJob), "DumpingJob.Factory.TryCreateAndEnqueueJob");
                }
            }
        }

        private static void PatchCaller(Harmony harmony, MethodInfo? method, string prefixName, string label)
        {
            if (method is null)
            {
                LogOptionalPatchFailure($"caller patch target not found: {label}");
                return;
            }

            try
            {
                harmony.Patch(
                    method,
                    new HarmonyMethod(typeof(DumpSearchDiagnosticsService), prefixName),
                    finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(EndCallerContext)));
                Interlocked.Increment(ref s_callerPatchCount);
            }
            catch (Exception ex)
            {
                LogOptionalPatchFailure($"caller patch failed for {label}: {ex}");
            }
        }

        private static void PatchCacheMethods(Harmony harmony)
        {
            MethodInfo? globalCache = FindInstanceMethod(
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
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(BeforeGlobalEligibleCache)),
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(AfterGlobalEligibleCache)),
                        finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(EndEligibleCacheContext)));
                    Interlocked.Increment(ref s_cachePatchCount);
                }
                catch (Exception ex)
                {
                    LogOptionalPatchFailure($"global eligible-cache patch failed: {ex}");
                }
            }
            else
            {
                LogOptionalPatchFailure("TerrainDumpingManager.getAllEligibleCached was not found");
            }

            MethodInfo? towerCache = FindInstanceMethod(
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
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(BeforeTowerEligibleCache)),
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(AfterTowerEligibleCache)),
                        finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(EndEligibleCacheContext)));
                    Interlocked.Increment(ref s_cachePatchCount);
                }
                catch (Exception ex)
                {
                    LogOptionalPatchFailure($"per-tower eligible-cache patch failed: {ex}");
                }
            }
            else
            {
                LogOptionalPatchFailure("TerrainDumpingManager.getAllEligibleCachedFor was not found");
            }
        }

        private static void BeforeDumpSearch(
            TerrainDumpingManager __instance,
            Option<LooseProductProto> __1,
            IIndexable<MineTower>? __5,
            Lyst<TerrainDesignation>? __7,
            out DumpSearchCallState __state)
        {
            __state = default;
            SearchDiagnosticContext? previousContext = s_currentSearchContext;
            try
            {
                ServiceProfileDeadlineAtSearchBoundary();
                LooseProductProto? product = __1.ValueOrNull;
                SearchPath path = ClassifySearch(__instance, product, __5);
                SearchCaller caller = s_currentCaller;
                string productId = product?.Id.Value ?? UnknownProductId;
                ProductSearchStats stats = GetOrCreateProductStats(productId);

                var context = new SearchDiagnosticContext(
                    previousContext,
                    stats,
                    path,
                    caller,
                    Stopwatch.GetTimestamp(),
                    __7);
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

        private static void PatchBreakdownMethods(Harmony harmony)
        {
            MethodInfo? bestSelection = FindInstanceMethod(
                typeof(TerrainDesignationsManager),
                "TryFindBestReadyToFulfill",
                typeof(bool),
                typeof(IEnumerable<TerrainDesignation>),
                typeof(Tile2i),
                typeof(Vehicle),
                typeof(TerrainDesignation).MakeByRefType(),
                typeof(Option<LooseProductProto>),
                typeof(bool));
            if (bestSelection is null)
            {
                LogOptionalPatchFailure("TerrainDesignationsManager.TryFindBestReadyToFulfill was not found");
            }
            else
            {
                try
                {
                    harmony.Patch(
                        bestSelection,
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(BeforeBestReadyToFulfill)),
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(AfterBestReadyToFulfill)),
                        finalizer: new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(EndBestReadyToFulfill)));
                    Interlocked.Increment(ref s_breakdownPatchCount);
                }
                catch (Exception ex)
                {
                    LogOptionalPatchFailure($"best-designation timing patch failed: {ex}");
                }
            }

            MethodInfo? nearbyEligibility = FindInstanceMethod(
                typeof(TerrainDumpingManager),
                "isEligibleAsNearbyFor",
                typeof(bool),
                typeof(TerrainDesignation),
                typeof(TerrainDesignation),
                typeof(bool));
            if (nearbyEligibility is null)
            {
                LogOptionalPatchFailure("TerrainDumpingManager.isEligibleAsNearbyFor was not found");
            }
            else
            {
                try
                {
                    harmony.Patch(
                        nearbyEligibility,
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(BeforeNearbyEligibility)),
                        new HarmonyMethod(typeof(DumpSearchDiagnosticsService), nameof(AfterNearbyEligibility)));
                    Interlocked.Increment(ref s_breakdownPatchCount);
                }
                catch (Exception ex)
                {
                    LogOptionalPatchFailure($"nearby-designation diagnostics patch failed: {ex}");
                }
            }
        }

        private static void AfterDumpSearch(bool __result, DumpSearchCallState __state)
        {
            if (__state.Context is null)
            {
                return;
            }

            try
            {
                CompleteDumpSearch(__state.Context, true, __result);
            }
            catch (Exception ex)
            {
                logOnce(ref s_postfixErrorLogged, "dump-search diagnostics postfix", ex);
            }
        }

        private static Exception? EndDumpSearchContext(Exception? __exception, DumpSearchCallState __state)
        {
            SearchDiagnosticContext? context = __state.Context;
            if (context is null)
            {
                return __exception;
            }

            try
            {
                if (__exception is not null)
                {
                    CompleteDumpSearch(context, false, false);
                }
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

        private static void CompleteDumpSearch(SearchDiagnosticContext context, bool hasResult, bool result)
        {
            if (Interlocked.CompareExchange(ref context.CompletionRecorded, 1, 0) != 0)
            {
                return;
            }

            long elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - context.StartTimestamp);
            SearchPath path = context.Path;
            long candidateDesignations = Interlocked.Read(ref context.CandidateDesignations);
            int candidateCalls = Volatile.Read(ref context.CandidateCalls);
            CacheSummary cacheSummary = context.GetCacheSummary();
            long residualTicks = CalculateResidualTicks(elapsedTicks, cacheSummary.TotalElapsedTicks);
            SearchBreakdown breakdown = context.CompleteBreakdown(
                elapsedTicks,
                cacheSummary.TotalElapsedTicks,
                context.StartTimestamp + elapsedTicks);

            context.Stats.RecordCompletion(
                path,
                context.Caller,
                hasResult,
                result,
                elapsedTicks,
                candidateDesignations,
                candidateCalls,
                cacheSummary,
                residualTicks,
                breakdown);
            s_breakdownStats.Record(path, breakdown);
            Interlocked.Increment(ref s_pathCalls[(int)path]);
            Interlocked.Increment(ref s_callerCalls[(int)context.Caller]);
            Interlocked.Increment(ref s_latencyBuckets[GetLatencyBucket(elapsedTicks)]);
            Interlocked.Increment(ref s_pathLatencyBuckets[(int)path * LatencyBucketCount + GetLatencyBucket(elapsedTicks)]);
            Interlocked.Add(ref s_nestedResidualElapsedTicks, residualTicks);
            UpdateMax(ref s_nestedResidualMaxElapsedTicks, residualTicks);
            UpdateWorstCall(context, elapsedTicks, candidateDesignations, candidateCalls, cacheSummary, residualTicks);
            Interlocked.Add(ref s_currentPfSearchElapsedTicks, elapsedTicks);
            UpdateMax(ref s_currentPfMaxIndividualSearchElapsedTicks, elapsedTicks);

            if (hasResult)
            {
                if (result)
                {
                    Interlocked.Increment(ref s_totalTrueResults);
                }
                else
                {
                    Interlocked.Increment(ref s_totalFalseResults);
                }

                Interlocked.Add(ref s_totalElapsedTicks, elapsedTicks);
                UpdateMax(ref s_maxElapsedTicks, elapsedTicks);
            }

            if (candidateCalls > 0)
            {
                Interlocked.Add(ref s_totalCandidateDesignations, candidateDesignations);
                Interlocked.Increment(ref s_observedCandidateCalls);
                Interlocked.Add(ref s_pathCandidateDesignations[(int)path], candidateDesignations);
                Interlocked.Increment(ref s_pathCandidateCalls[(int)path]);
                UpdateMax(ref s_maxCandidateDesignations, candidateDesignations);
            }
        }

        private static void BeforePathFindingEnqueue()
        {
            Interlocked.Increment(ref s_currentPfEnqueues);
            Interlocked.Increment(ref s_totalPfEnqueues);
        }

        private static void BeginPathFindingTick()
        {
            try
            {
                ServiceProfileDeadline();
                long currentCalls = Interlocked.Exchange(ref s_currentCalls, 0);
                long currentPfEnqueues = Interlocked.Exchange(ref s_currentPfEnqueues, 0);
                long currentSearchElapsed = Interlocked.Exchange(ref s_currentPfSearchElapsedTicks, 0);
                long currentMaxIndividualSearch = Interlocked.Exchange(ref s_currentPfMaxIndividualSearchElapsedTicks, 0);
                Interlocked.Exchange(ref s_lastCalls, currentCalls);
                Interlocked.Exchange(ref s_lastPfEnqueues, currentPfEnqueues);
                Interlocked.Exchange(ref s_lastPfSearchElapsedTicks, currentSearchElapsed);
                Interlocked.Exchange(ref s_lastPfMaxIndividualSearchElapsedTicks, currentMaxIndividualSearch);
                UpdateMax(ref s_peakCalls, currentCalls);
                UpdateMax(ref s_peakPfEnqueues, currentPfEnqueues);
                long previousPeakSearchElapsed = Read(ref s_peakPfSearchElapsedTicks);
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

        private static void BeforeGlobalEligibleCache(out EligibleCacheCallState __state)
        {
            __state = default;
            try
            {
                var context = new EligibleCacheDiagnosticContext(
                    s_currentEligibleCacheContext,
                    s_currentSearchContext,
                    false,
                    Stopwatch.GetTimestamp());
                context.OuterSearch?.BeginEligibleCache(context.StartTimestamp);
                s_currentEligibleCacheContext = context;
                __state = new EligibleCacheCallState(context);
            }
            catch (Exception ex)
            {
                logOnce(ref s_globalCacheDiagnosticsErrorLogged, "global eligible-cache diagnostics", ex);
            }
        }

        private static void AfterGlobalEligibleCache(
            LystStruct<TerrainDesignation> __result,
            EligibleCacheCallState __state) =>
            CompleteEligibleCache(__state.Context, __result.Count, true);

        private static void BeforeTowerEligibleCache(out EligibleCacheCallState __state)
        {
            __state = default;
            try
            {
                var context = new EligibleCacheDiagnosticContext(
                    s_currentEligibleCacheContext,
                    s_currentSearchContext,
                    true,
                    Stopwatch.GetTimestamp());
                context.OuterSearch?.BeginEligibleCache(context.StartTimestamp);
                s_currentEligibleCacheContext = context;
                __state = new EligibleCacheCallState(context);
            }
            catch (Exception ex)
            {
                logOnce(ref s_towerCacheDiagnosticsErrorLogged, "per-tower eligible-cache diagnostics", ex);
            }
        }

        private static void AfterTowerEligibleCache(
            Lyst<TerrainDesignation> __result,
            EligibleCacheCallState __state) =>
            CompleteEligibleCache(__state.Context, __result?.Count ?? 0, true);

        private static Exception? EndEligibleCacheContext(
            Exception? __exception,
            EligibleCacheCallState __state)
        {
            EligibleCacheDiagnosticContext? context = __state.Context;
            if (context is null)
            {
                return __exception;
            }

            try
            {
                if (__exception is not null)
                {
                    CompleteEligibleCache(context, 0, false);
                }
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

        private static void CompleteEligibleCache(
            EligibleCacheDiagnosticContext? context,
            int candidateCount,
            bool hasResult)
        {
            if (context is null || Interlocked.CompareExchange(ref context.CompletionRecorded, 1, 0) != 0)
            {
                return;
            }

            try
            {
                long completedTimestamp = Stopwatch.GetTimestamp();
                long elapsedTicks = Math.Max(0, completedTimestamp - context.StartTimestamp);
                if (context.IsPerTower)
                {
                    Interlocked.Increment(ref s_towerEligibleCacheCalls);
                    Interlocked.Add(ref s_towerEligibleCacheElapsedTicks, elapsedTicks);
                    UpdateMax(ref s_towerEligibleCacheMaxElapsedTicks, elapsedTicks);
                }
                else
                {
                    Interlocked.Increment(ref s_globalEligibleCacheCalls);
                    Interlocked.Add(ref s_globalEligibleCacheElapsedTicks, elapsedTicks);
                    UpdateMax(ref s_globalEligibleCacheMaxElapsedTicks, elapsedTicks);
                }

                SearchDiagnosticContext? outer = context.OuterSearch;
                if (outer is null)
                {
                    return;
                }

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
                outer.RecordEligibleCacheEnd(completedTimestamp);
            }
            catch (Exception ex)
            {
                logOnce(
                    ref context.IsPerTower ? ref s_towerCacheDiagnosticsErrorLogged : ref s_globalCacheDiagnosticsErrorLogged,
                    context.IsPerTower ? "per-tower eligible-cache diagnostics" : "global eligible-cache diagnostics",
                    ex);
            }
        }

        private static void BeforeBestReadyToFulfill(
            IEnumerable<TerrainDesignation> __0,
            out BestReadyToFulfillCallState __state)
        {
            __state = default;
            SearchDiagnosticContext? context = s_currentSearchContext;
            if (context is null)
            {
                return;
            }

            try
            {
                long timestamp = Stopwatch.GetTimestamp();
                int candidateCount = __0 is ICollectionWithCount counted ? counted.Count : -1;
                context.BeginBestReadyToFulfill(timestamp, candidateCount);
                __state = new BestReadyToFulfillCallState(context);
            }
            catch (Exception ex)
            {
                logOnce(ref s_bestSelectionPrefixErrorLogged, "best-designation diagnostics prefix", ex);
            }
        }

        private static void AfterBestReadyToFulfill(bool __result, BestReadyToFulfillCallState __state)
        {
            try
            {
                __state.Context?.CompleteBestReadyToFulfill(Stopwatch.GetTimestamp(), __result);
            }
            catch (Exception ex)
            {
                logOnce(ref s_bestSelectionPostfixErrorLogged, "best-designation diagnostics postfix", ex);
            }
        }

        private static Exception? EndBestReadyToFulfill(Exception? __exception, BestReadyToFulfillCallState __state)
        {
            if (__exception is not null)
            {
                try
                {
                    __state.Context?.CompleteBestReadyToFulfill(Stopwatch.GetTimestamp(), false);
                }
                catch (Exception ex)
                {
                    logOnce(ref s_bestSelectionFinalizerErrorLogged, "best-designation diagnostics finalizer", ex);
                }
            }

            return __exception;
        }

        private static void BeforeNearbyEligibility() => s_currentSearchContext?.RecordNearbyDesignationScanned();

        private static void AfterNearbyEligibility(bool __result)
        {
            try
            {
                s_currentSearchContext?.RecordNearbyDesignationResult(__result);
            }
            catch (Exception ex)
            {
                logOnce(ref s_nearbyDiagnosticsErrorLogged, "nearby-designation diagnostics", ex);
            }
        }

        private static void ServiceProfileDeadline()
        {
            ProfileSnapshot? completed = null;
            try
            {
                lock (s_profileGate)
                {
                    ProfileSession? active = s_activeProfile;
                    if (active is null)
                    {
                        return;
                    }

                    long now = Stopwatch.GetTimestamp();
                    if (active.State == ProfileState.WarmingUp && now >= active.WarmupEndTimestamp)
                    {
                        ResetAllStats();
                        active.BeginRecording(now);
                        Volatile.Write(ref s_profileState, (int)ProfileState.Recording);
                    }

                    if (active.State == ProfileState.Recording && now >= active.RecordingEndTimestamp)
                    {
                        completed = FinishProfileLocked(now);
                    }
                }

                if (completed is not null)
                {
                    PublishCompletedProfile(completed, true);
                }
            }
            catch (Exception ex)
            {
                logOnce(ref s_profileErrorLogged, "timed dump profile deadline handling", ex);
            }
        }

        private static void ServiceProfileDeadlineAtSearchBoundary()
        {
            if ((ProfileState)Volatile.Read(ref s_profileState) != ProfileState.Recording)
            {
                return;
            }

            ProfileSession? active = Volatile.Read(ref s_activeProfile);
            if (active is null || Stopwatch.GetTimestamp() < active.RecordingEndTimestamp)
            {
                return;
            }

            // Finalize before admitting a search that starts after the recording window.
            // The normal pathfinding-tick hook remains the fallback when no search arrives.
            ServiceProfileDeadline();
        }

        private static ProfileSnapshot FinishProfileLocked(long now)
        {
            ProfileSession active = s_activeProfile ?? throw new InvalidOperationException("No active dump profile.");
            if (active.State == ProfileState.WarmingUp)
            {
                ResetAllStats();
            }
            long recordingStart = active.State == ProfileState.Recording
                ? active.RecordingStartTimestamp
                : active.StartTimestamp;
            long actualRecordingTicks = active.State == ProfileState.Recording
                ? Math.Max(0, now - recordingStart)
                : 0L;
            ProfileSnapshot snapshot = SnapshotCurrentProfile(active, actualRecordingTicks);

            s_activeProfile = null;
            Volatile.Write(ref s_profileState, (int)ProfileState.Idle);
            s_profileHistory.Add(snapshot);
            if (s_profileHistory.Count > MaxProfileHistory)
            {
                s_profileHistory.RemoveAt(0);
            }
            return snapshot;
        }

        private static void PublishCompletedProfile(ProfileSnapshot profile, bool automatic)
        {
            try
            {
                string prefix = automatic ? "[TajsProfiler] " : string.Empty;
                s_console?.WriteLine(prefix + FormatProfileReport(profile), ColorRgba.White);
            }
            catch (Exception ex)
            {
                logOnce(ref s_profileOutputErrorLogged, "timed dump profile console output", ex);
            }
        }

        private static long CalculateResidualTicks(long outerTicks, long nestedTicks)
        {
            long residual = outerTicks - nestedTicks;
            if (residual < 0 && residual >= -TinyResidualRoundingToleranceTicks)
            {
                return 0;
            }

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

        private static SearchPath ClassifySearch(
            TerrainDumpingManager dumpingManager,
            LooseProductProto? product,
            IIndexable<MineTower>? towersToEnforce)
        {
            if (product is null)
            {
                return SearchPath.UnknownProduct;
            }

            bool globalAllowed = dumpingManager.ProductsAllowedToDump.Contains(product);
            // This mirrors TerrainDumpingManager's `flag2 = towersToEnforce != null`: an empty,
            // non-null list is still an explicit enforced-tower search, not the global fallback.
            if (towersToEnforce is not null)
            {
                if (globalAllowed)
                {
                    return SearchPath.ExplicitTower;
                }

                return SearchPath.ExplicitTowerGlobalForbiddenRejected;
            }

            if (globalAllowed)
            {
                return SearchPath.GlobalAllowed;
            }

            return SearchPath.GlobalForbiddenNoLocalTower;
        }

        private static ProductSearchStats GetOrCreateProductStats(string productId)
        {
            if (s_productStats.TryGetValue(productId, out ProductSearchStats? stats))
            {
                return stats;
            }

            var created = new ProductSearchStats(productId);
            return s_productStats.GetOrAdd(productId, created);
        }

        private static List<ProductSearchSnapshot> SnapshotProductStats()
        {
            var snapshot = new List<ProductSearchSnapshot>();
            foreach (ProductSearchStats stats in s_productStats.Values)
            {
                ProductSearchSnapshot productSnapshot = stats.Snapshot();
                if (productSnapshot.Calls > 0)
                {
                    snapshot.Add(productSnapshot);
                }
            }

            return snapshot;
        }

        private static void SortProductSnapshots(List<ProductSearchSnapshot> snapshots)
        {
            snapshots.Sort(static (left, right) =>
            {
                int calls = right.Calls.CompareTo(left.Calls);
                return calls != 0 ? calls : string.CompareOrdinal(left.ProductId, right.ProductId);
            });
        }

        private static long[] SnapshotCounters(long[] counters)
        {
            long[] snapshot = new long[counters.Length];
            for (int i = 0; i < counters.Length; i++)
            {
                snapshot[i] = Interlocked.Read(ref counters[i]);
            }

            return snapshot;
        }

        private static void ResetCounters(long[] counters)
        {
            for (int i = 0; i < counters.Length; i++)
            {
                Interlocked.Exchange(ref counters[i], 0);
            }
        }

        private static void ResetAllStats()
        {
            ResetDumpSearchStats();
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

        private static void ResetDumpSearchStats()
        {
            foreach (ProductSearchStats stats in s_productStats.Values)
            {
                stats.Reset();
            }
            s_breakdownStats.Reset();

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

            for (int i = 0; i < s_pathCalls.Length; i++)
            {
                Interlocked.Exchange(ref s_pathCalls[i], 0);
            }
            for (int i = 0; i < s_callerCalls.Length; i++)
            {
                Interlocked.Exchange(ref s_callerCalls[i], 0);
            }
            for (int i = 0; i < s_pathCandidateDesignations.Length; i++)
            {
                Interlocked.Exchange(ref s_pathCandidateDesignations[i], 0);
                Interlocked.Exchange(ref s_pathCandidateCalls[i], 0);
            }
            for (int i = 0; i < s_latencyBuckets.Length; i++)
            {
                Interlocked.Exchange(ref s_latencyBuckets[i], 0);
            }
            for (int i = 0; i < s_pathLatencyBuckets.Length; i++)
            {
                Interlocked.Exchange(ref s_pathLatencyBuckets[i], 0);
            }
        }

        private static string FormatCounterSummary(string[] names, long[] primary, long[]? secondary = null)
        {
            var builder = new StringBuilder(160);
            int count = Math.Min(names.Length, primary.Length);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(names[i]).Append('=').Append(primary[i]);
                if (secondary is not null && i < secondary.Length)
                {
                    builder.Append('/').Append(secondary[i]);
                }
            }

            return builder.ToString();
        }

        private static MethodInfo? FindInstanceMethod(
            Type type,
            string name,
            Type returnType,
            params Type[] parameterTypes)
        {
            MethodInfo? method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                Type.DefaultBinder,
                parameterTypes,
                Array.Empty<ParameterModifier>());

            return method is { IsStatic: false } && method.ReturnType == returnType
                ? method
                : null;
        }

        private static MethodInfo? FindUniqueInstanceMethod(Type type, string name, int parameterCount)
        {
            MethodInfo? found = null;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name != name || method.IsStatic || method.GetParameters().Length != parameterCount)
                {
                    continue;
                }

                if (found is not null)
                {
                    return null;
                }
                found = method;
            }

            return found;
        }

        private static void BeginVehicleBuffersBalanceBuffers(out CallerContextState __state) =>
            __state = EnterCaller(SearchCaller.VehicleBuffersRegistryBalanceBuffers);

        private static void BeginDefaultTruckJobProvider(out CallerContextState __state) =>
            __state = EnterCaller(SearchCaller.DefaultTruckJobProvider);

        private static void BeginDumpingJob(out CallerContextState __state) => __state = EnterCaller(SearchCaller.DumpingJob);

        private static CallerContextState EnterCaller(SearchCaller caller)
        {
            var state = new CallerContextState(s_currentCaller);
            s_currentCaller = caller;
            return state;
        }

        private static Exception? EndCallerContext(Exception? __exception, CallerContextState __state)
        {
            s_currentCaller = __state.Previous;
            return __exception;
        }

        private static void LogOptionalPatchFailure(string message) =>
            LogError($"{message}; diagnostics will remain fail-open.");

        private static void logOnce(ref int alreadyLogged, string operation, Exception? exception)
        {
            if (Interlocked.CompareExchange(ref alreadyLogged, 1, 0) != 0)
            {
                return;
            }

            string suffix = exception is null ? string.Empty : $": {exception}";
            LogError($"{operation}; diagnostics will remain fail-open{suffix}");
        }

        private static void ReportCompatibility(ITajsRuntime runtime)
        {
            bool rootPatched = ReadInt(ref s_patchesApplied) != 0;
            bool optionalPatchesComplete =
                ReadInt(ref s_tickBoundaryPatchApplied) != 0 &&
                ReadInt(ref s_pfEnqueuePatchApplied) != 0 &&
                ReadInt(ref s_callerPatchCount) > 0 &&
                ReadInt(ref s_cachePatchCount) == 2 &&
                ReadInt(ref s_breakdownPatchCount) > 0;
            CompatibilityState state = !rootPatched
                ? CompatibilityState.Disabled
                : optionalPatchesComplete
                    ? CompatibilityState.Compatible
                    : CompatibilityState.Degraded;
            string observed =
                $"root={rootPatched}, PF tick={ReadInt(ref s_tickBoundaryPatchApplied) != 0}, " +
                $"PF enqueue={ReadInt(ref s_pfEnqueuePatchApplied) != 0}, callers={ReadInt(ref s_callerPatchCount)}, " +
                $"cache={ReadInt(ref s_cachePatchCount)}, breakdown={ReadInt(ref s_breakdownPatchCount)}";
            string reason = state switch
            {
                CompatibilityState.Compatible => "All required and optional dumping instrumentation resolved.",
                CompatibilityState.Degraded => "The root probe is active, but optional instrumentation is incomplete.",
                _ => "The required dumping-search signature or root patch could not be installed.",
            };

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsProfiler",
                "Dumping",
                state,
                "CoI 0.8.7/0.8.7a dumping-search root and optional diagnostic signatures",
                observed,
                reason));
        }

        private static void LogInfo(string message) => s_log?.Info(message);

        private static void LogError(string message) => s_log?.Error(message);

        private static long Read(ref long value) => Interlocked.Read(ref value);

        private static int ReadInt(ref int value) => Volatile.Read(ref value);

        private static void UpdateMax(ref long target, long value)
        {
            long current = Read(ref target);
            while (value > current)
            {
                long previous = Interlocked.CompareExchange(ref target, value, current);
                if (previous == current)
                {
                    return;
                }
                current = previous;
            }
        }

        private static double StopwatchTicksToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static double StopwatchTicksToMicroseconds(long ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;

        private static void UpdateWorstCall(
            SearchDiagnosticContext context,
            long elapsedTicks,
            long candidateDesignations,
            int candidateCalls,
            CacheSummary cacheSummary,
            long residualTicks)
        {
            WorstCallSnapshot? currentGlobal = Volatile.Read(ref s_worstCall);
            WorstCallSnapshot? currentProduct = context.Stats.WorstCall;
            if (currentGlobal is not null && currentGlobal.ElapsedTicks >= elapsedTicks &&
                currentProduct is not null && currentProduct.ElapsedTicks >= elapsedTicks)
            {
                return;
            }

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

            UpdateWorstCall(ref s_worstCall, candidate);
            context.Stats.RecordWorst(candidate);
        }

        private static void UpdateWorstCall(ref WorstCallSnapshot? target, WorstCallSnapshot candidate)
        {
            while (true)
            {
                WorstCallSnapshot? current = Volatile.Read(ref target);
                if (current is not null && current.ElapsedTicks >= candidate.ElapsedTicks)
                {
                    return;
                }
                if (ReferenceEquals(Interlocked.CompareExchange(ref target, candidate, current), current))
                {
                    return;
                }
            }
        }

        private static int GetLatencyBucket(long elapsedTicks)
        {
            double milliseconds = StopwatchTicksToMilliseconds(elapsedTicks);
            if (milliseconds < 0.1)
            {
                return 0;
            }
            if (milliseconds < 1.0)
            {
                return 1;
            }
            if (milliseconds < 5.0)
            {
                return 2;
            }
            if (milliseconds < 10.0)
            {
                return 3;
            }
            if (milliseconds < 25.0)
            {
                return 4;
            }
            if (milliseconds < 50.0)
            {
                return 5;
            }
            if (milliseconds < 100.0)
            {
                return 6;
            }
            if (milliseconds < 250.0)
            {
                return 7;
            }
            return 8;
        }

        private static string FormatLatencyBuckets(long[] buckets)
        {
            var builder = new StringBuilder(180);
            for (int i = 0; i < Math.Min(s_latencyBucketNames.Length, buckets.Length); i++)
            {
                if (buckets[i] == 0)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(s_latencyBucketNames[i]).Append('=').Append(buckets[i]);
            }

            return builder.Length == 0 ? "none" : builder.ToString();
        }

        private static ProfileSnapshot SnapshotCurrentProfile(ProfileSession session, long actualRecordingTicks)
        {
            List<ProductSearchSnapshot> products = SnapshotProductStats();
            SortProductSnapshots(products);
            long completedCalls = Read(ref s_totalTrueResults) + Read(ref s_totalFalseResults);
            return new ProfileSnapshot(
                session,
                actualRecordingTicks,
                Read(ref s_totalCalls),
                completedCalls,
                Read(ref s_totalElapsedTicks),
                SnapshotCounters(s_pathCalls),
                SnapshotCounters(s_callerCalls),
                SnapshotCounters(s_pathCandidateDesignations),
                SnapshotCounters(s_pathCandidateCalls),
                SnapshotCounters(s_pathLatencyBuckets),
                SnapshotCounters(s_latencyBuckets),
                Read(ref s_totalCandidateDesignations),
                Read(ref s_observedCandidateCalls),
                Read(ref s_globalEligibleCacheCalls),
                Read(ref s_globalEligibleCacheElapsedTicks),
                Read(ref s_globalEligibleCacheMaxElapsedTicks),
                Read(ref s_towerEligibleCacheCalls),
                Read(ref s_towerEligibleCacheElapsedTicks),
                Read(ref s_towerEligibleCacheMaxElapsedTicks),
                Read(ref s_nestedResidualElapsedTicks),
                Read(ref s_nestedResidualMaxElapsedTicks),
                Read(ref s_nestedResidualAccountingAnomalies),
                Read(ref s_lastPfSearchElapsedTicks),
                Read(ref s_peakPfSearchElapsedTicks),
                Read(ref s_peakPfSearchCalls),
                Read(ref s_peakPfMaxIndividualSearchElapsedTicks),
                Volatile.Read(ref s_worstCall),
                s_breakdownStats.Snapshot(),
                products.ToArray());
        }

        private static string FormatProfileReport(ProfileSnapshot profile)
        {
            double callsPerSecond = profile.ActualRecordingSeconds > 0.0
                ? profile.TotalCalls / profile.ActualRecordingSeconds
                : 0.0;
            double utilization = profile.ActualRecordingSeconds > 0.0
                ? StopwatchTicksToSeconds(profile.TotalElapsedTicks) / profile.ActualRecordingSeconds * 100.0
                : 0.0;
            var builder = new StringBuilder(4096);
            builder.Append("Dump profile \"")
                .Append(profile.Label)
                .AppendLine("\" complete")
                .Append("requested=")
                .Append(FormatSeconds(profile.RequestedSeconds))
                .Append(" actual=")
                .Append(FormatSeconds(profile.ActualRecordingSeconds))
                .Append(" warmup=")
                .Append(FormatSeconds(profile.WarmupSeconds))
                .AppendLine()
                .Append("calls=")
                .Append(profile.TotalCalls)
                .Append(" (")
                .Append(FormatDecimal(callsPerSecond))
                .Append("/s), cumulative outer search time=")
                .Append(FormatMilliseconds(profile.TotalElapsedTicks))
                .Append(" ms, utilization=")
                .Append(FormatDecimal(utilization))
                .AppendLine("% (cumulative; concurrent calls may exceed 100%)")
                .Append("Paths: ")
                .Append(FormatCounterSummary(s_searchPathNames, profile.PathCalls))
                .AppendLine()
                .Append("Callers: ")
                .Append(FormatCounterSummary(s_searchCallerNames, profile.CallerCalls))
                .AppendLine()
                .Append("Stage breakdown: ")
                .Append(FormatStageSummary(profile.Breakdown))
                .AppendLine()
                .Append("Final m_designationsCache total/searches/max: ")
                .Append(
                    FormatCountSummary(
                        profile.Breakdown.FinalCandidateCount,
                        profile.Breakdown.FinalCandidateCalls,
                        profile.Breakdown.FinalCandidateMax))
                .AppendLine()
                .Append("Nearby scanned/accepted/added: ")
                .Append(profile.Breakdown.NearbyScanned)
                .Append('/')
                .Append(profile.Breakdown.NearbyAccepted)
                .Append('/')
                .Append(profile.Breakdown.NearbyAdded)
                .Append("; mode tower/global/unknown=")
                .Append(profile.Breakdown.NearbyModeCalls[(int)NearbyMode.Tower])
                .Append('/')
                .Append(profile.Breakdown.NearbyModeCalls[(int)NearbyMode.Global])
                .Append('/')
                .Append(profile.Breakdown.NearbyModeCalls[(int)NearbyMode.Unknown])
                .AppendLine()
                .Append("Eligible caches:")
                .AppendLine()
                .Append("  global: calls=")
                .Append(profile.GlobalCacheCalls)
                .Append(", total=")
                .Append(FormatMilliseconds(profile.GlobalCacheElapsedTicks))
                .Append(" ms, avg=")
                .Append(FormatMicroseconds(AverageTicks(profile.GlobalCacheElapsedTicks, profile.GlobalCacheCalls)))
                .Append(" us, max=")
                .Append(FormatMilliseconds(profile.GlobalCacheMaxElapsedTicks))
                .AppendLine(" ms")
                .Append("  per-tower: calls=")
                .Append(profile.TowerCacheCalls)
                .Append(", total=")
                .Append(FormatMilliseconds(profile.TowerCacheElapsedTicks))
                .Append(" ms, avg=")
                .Append(FormatMicroseconds(AverageTicks(profile.TowerCacheElapsedTicks, profile.TowerCacheCalls)))
                .Append(" us, max=")
                .Append(FormatMilliseconds(profile.TowerCacheMaxElapsedTicks))
                .AppendLine(" ms")
                .Append("Residual after eligible-cache calls: total=")
                .Append(FormatMilliseconds(profile.ResidualElapsedTicks))
                .Append(" ms, avg=")
                .Append(FormatMicroseconds(AverageTicks(profile.ResidualElapsedTicks, profile.CompletedCalls)))
                .Append(" us, max=")
                .Append(FormatMilliseconds(profile.ResidualMaxElapsedTicks))
                .Append(", accounting anomalies=")
                .AppendLine(profile.ResidualAccountingAnomalies.ToString(CultureInfo.InvariantCulture))
                .Append("Raw eligible-cache candidates: total=")
                .Append(profile.TotalCandidateDesignations)
                .Append(", cache searches=")
                .Append(profile.ObservedCandidateCalls)
                .Append(", avg=")
                .Append(FormatDecimal(Average(profile.TotalCandidateDesignations, profile.ObservedCandidateCalls)))
                .AppendLine()
                .Append("PF tick search workload: peak calls/tick=")
                .Append(profile.PeakPfSearchCalls)
                .Append(", peak cumulative=")
                .Append(FormatMilliseconds(profile.PeakPfSearchElapsedTicks))
                .Append(" ms, worst individual in peak tick=")
                .Append(FormatMilliseconds(profile.PeakPfMaxIndividualSearchElapsedTicks))
                .AppendLine(" ms")
                .Append("Latency: ")
                .Append(FormatLatencyBuckets(profile.LatencyBuckets));

            AppendPathBreakdown(builder, profile.Breakdown, profile.PathCalls);
            builder.AppendLine().Append("Path latency:");

            for (int i = 0; i < s_searchPathNames.Length; i++)
            {
                long[] pathBuckets = new long[LatencyBucketCount];
                Array.Copy(profile.PathLatencyBuckets, i * LatencyBucketCount, pathBuckets, 0, LatencyBucketCount);
                if (HasNonZero(pathBuckets))
                {
                    builder.Append("\n  ").Append(s_searchPathNames[i]).Append(": ").Append(FormatLatencyBuckets(pathBuckets));
                }
            }

            if (profile.WorstCall is not null)
            {
                AppendWorstCall(builder, profile.WorstCall);
            }

            builder.AppendLine().Append("Products:");
            int count = Math.Min(profile.Products.Length, MaxProductsInConsoleReport);
            for (int i = 0; i < count; i++)
            {
                AppendProductReport(builder, profile.Products[i]);
            }
            if (profile.Products.Length > count)
            {
                builder.Append("\n  ... ").Append(profile.Products.Length - count).Append(" more product(s) omitted.");
            }

            return builder.ToString();
        }

        private static string FormatCountSummary(long total, long calls, long max)
        {
            return total.ToString(CultureInfo.InvariantCulture) + "/" + calls.ToString(CultureInfo.InvariantCulture) + "/" +
                   max.ToString(CultureInfo.InvariantCulture) +
                   " avg=" + FormatDecimal(Average(total, calls));
        }

        private static string FormatStageSummary(SearchBreakdownSnapshot breakdown)
        {
            var builder = new StringBuilder(240);
            for (int i = 0; i < (int)SearchStage.Count && i < s_searchStageNames.Length; i++)
            {
                var stage = (SearchStage)i;
                long calls = breakdown.StageCalls[i];
                if (calls == 0)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(s_searchStageNames[i])
                    .Append('=')
                    .Append(FormatMilliseconds(breakdown.StageElapsedTicks[i]))
                    .Append("ms avg=")
                    .Append(FormatMilliseconds(AverageTicks(breakdown.StageElapsedTicks[i], calls)))
                    .Append(" max=")
                    .Append(FormatMilliseconds(breakdown.StageMaxElapsedTicks[i]))
                    .Append("ms");
            }

            return builder.Length == 0 ? "none" : builder.ToString();
        }

        private static string FormatStageComparison(SearchBreakdownSnapshot first, SearchBreakdownSnapshot second)
        {
            var builder = new StringBuilder(240);
            for (int i = 0; i < (int)SearchStage.Count && i < s_searchStageNames.Length; i++)
            {
                double firstAvg = AverageTicks(first.StageElapsedTicks[i], first.StageCalls[i]);
                double secondAvg = AverageTicks(second.StageElapsedTicks[i], second.StageCalls[i]);
                if (first.StageCalls[i] == 0 && second.StageCalls[i] == 0)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(s_searchStageNames[i])
                    .Append(" A=")
                    .Append(FormatMilliseconds(firstAvg))
                    .Append("ms B=")
                    .Append(FormatMilliseconds(secondAvg))
                    .Append("ms B/A=")
                    .Append(FormatRatio(secondAvg, firstAvg));
            }

            return builder.Length == 0 ? "none" : builder.ToString();
        }

        private static string FormatProfileComparison(ProfileSnapshot first, ProfileSnapshot second)
        {
            StringBuilder builder = new StringBuilder(4096)
                .Append("Dump profile comparison: A=\"")
                .Append(first.Label)
                .Append("\", B=\"")
                .Append(second.Label)
                .AppendLine("\"")
                .Append("A actual=")
                .Append(FormatSeconds(first.ActualRecordingSeconds))
                .Append(", calls=")
                .Append(first.TotalCalls)
                .Append(", calls/sec=")
                .Append(FormatDecimal(CallsPerSecond(first)))
                .AppendLine()
                .Append("B actual=")
                .Append(FormatSeconds(second.ActualRecordingSeconds))
                .Append(", calls=")
                .Append(second.TotalCalls)
                .Append(", calls/sec=")
                .Append(FormatDecimal(CallsPerSecond(second)))
                .AppendLine()
                .Append("Outer search: A avg=")
                .Append(FormatMilliseconds(AverageTicks(first.TotalElapsedTicks, first.CompletedCalls)))
                .Append(" ms, B avg=")
                .Append(FormatMilliseconds(AverageTicks(second.TotalElapsedTicks, second.CompletedCalls)))
                .Append(" ms, B/A=")
                .Append(
                    FormatRatio(
                        second.TotalElapsedTicks / (double)Math.Max(1, second.CompletedCalls),
                        first.TotalElapsedTicks / (double)Math.Max(1, first.CompletedCalls)))
                .AppendLine()
                .Append("  total wall time: A=")
                .Append(FormatMilliseconds(first.TotalElapsedTicks))
                .Append(" ms, B=")
                .Append(FormatMilliseconds(second.TotalElapsedTicks))
                .AppendLine(" ms")
                .Append("  utilization: A=")
                .Append(FormatDecimal(Utilization(first)))
                .Append("%, B=")
                .Append(FormatDecimal(Utilization(second)))
                .AppendLine("%")
                .Append("Nested eligible cache: global A=")
                .Append(
                    FormatCacheComparison(
                        first.GlobalCacheCalls,
                        first.GlobalCacheElapsedTicks,
                        second.GlobalCacheCalls,
                        second.GlobalCacheElapsedTicks))
                .Append(", per-tower A=")
                .Append(
                    FormatCacheComparison(
                        first.TowerCacheCalls,
                        first.TowerCacheElapsedTicks,
                        second.TowerCacheCalls,
                        second.TowerCacheElapsedTicks))
                .AppendLine()
                .Append("Residual: A=")
                .Append(FormatMilliseconds(first.ResidualElapsedTicks))
                .Append(" ms total / ")
                .Append(FormatMilliseconds(AverageTicks(first.ResidualElapsedTicks, first.CompletedCalls)))
                .Append(" ms avg, B=")
                .Append(FormatMilliseconds(second.ResidualElapsedTicks))
                .Append(" ms total / ")
                .Append(FormatMilliseconds(AverageTicks(second.ResidualElapsedTicks, second.CompletedCalls)))
                .AppendLine(" ms avg")
                .Append("Stage averages: ")
                .Append(FormatStageComparison(first.Breakdown, second.Breakdown))
                .AppendLine()
                .Append("Final m_designationsCache avg/searches: A=")
                .Append(FormatDecimal(Average(first.Breakdown.FinalCandidateCount, first.Breakdown.FinalCandidateCalls)));
            builder.Append(", B=")
                .Append(FormatDecimal(Average(second.Breakdown.FinalCandidateCount, second.Breakdown.FinalCandidateCalls)))
                .AppendLine()
                .Append("Nearby scanned/accepted/added: A=")
                .Append(first.Breakdown.NearbyScanned)
                .Append('/')
                .Append(first.Breakdown.NearbyAccepted)
                .Append('/')
                .Append(first.Breakdown.NearbyAdded)
                .Append(", B=")
                .Append(second.Breakdown.NearbyScanned)
                .Append('/')
                .Append(second.Breakdown.NearbyAccepted)
                .Append('/')
                .Append(second.Breakdown.NearbyAdded)
                .AppendLine()
                .Append("Raw eligible-cache candidates: A avg=")
                .Append(FormatDecimal(Average(first.TotalCandidateDesignations, first.ObservedCandidateCalls)))
                .Append(", B avg=")
                .Append(FormatDecimal(Average(second.TotalCandidateDesignations, second.ObservedCandidateCalls)))
                .Append(", B/A=")
                .Append(
                    FormatRatio(
                        Average(second.TotalCandidateDesignations, second.ObservedCandidateCalls),
                        Average(first.TotalCandidateDesignations, first.ObservedCandidateCalls)))
                .AppendLine()
                .Append("Callers A: ")
                .Append(FormatPercentageSummary(s_searchCallerNames, first.CallerCalls))
                .AppendLine()
                .Append("Callers B: ")
                .Append(FormatPercentageSummary(s_searchCallerNames, second.CallerCalls))
                .AppendLine()
                .Append("Paths A: ")
                .Append(FormatPercentageSummary(s_searchPathNames, first.PathCalls))
                .AppendLine()
                .Append("Paths B: ")
                .Append(FormatPercentageSummary(s_searchPathNames, second.PathCalls));

            ProductSearchSnapshot? firstDirt = FindProduct(first.Products, "Product_Dirt");
            ProductSearchSnapshot? secondDirt = FindProduct(second.Products, "Product_Dirt");
            if (firstDirt is not null || secondDirt is not null)
            {
                ProductSearchSnapshot a = firstDirt ?? default;
                ProductSearchSnapshot b = secondDirt ?? default;
                double aAvg = AverageTicks(a.ElapsedTicks, a.CompletedCalls);
                double bAvg = AverageTicks(b.ElapsedTicks, b.CompletedCalls);
                builder.AppendLine()
                    .Append("Product_Dirt: A calls=")
                    .Append(a.Calls)
                    .Append(", B calls=")
                    .Append(b.Calls)
                    .Append(", outer avg A=")
                    .Append(FormatMilliseconds(aAvg))
                    .Append(" ms, B=")
                    .Append(FormatMilliseconds(bAvg))
                    .Append(" ms, B/A=")
                    .Append(FormatRatio(bAvg, aAvg))
                    .AppendLine()
                    .Append("  raw eligible-cache candidates/search: A=")
                    .Append(FormatDecimal(Average(a.CandidateDesignations, a.CandidateCalls)))
                    .Append(", B=")
                    .Append(FormatDecimal(Average(b.CandidateDesignations, b.CandidateCalls)))
                    .Append(", B/A=")
                    .Append(
                        FormatRatio(Average(b.CandidateDesignations, b.CandidateCalls), Average(a.CandidateDesignations, a.CandidateCalls)))
                    .AppendLine()
                    .Append("  stage averages A: ")
                    .Append(FormatStageSummary(a.Breakdown))
                    .AppendLine()
                    .Append("  stage averages B: ")
                    .Append(FormatStageSummary(b.Breakdown))
                    .AppendLine()
                    .Append("  final-cache avg A=")
                    .Append(FormatDecimal(Average(a.Breakdown.FinalCandidateCount, a.Breakdown.FinalCandidateCalls)))
                    .Append(", B=")
                    .Append(FormatDecimal(Average(b.Breakdown.FinalCandidateCount, b.Breakdown.FinalCandidateCalls)))
                    .Append("; nearby scanned/accepted/added A=")
                    .Append(a.Breakdown.NearbyScanned)
                    .Append('/')
                    .Append(a.Breakdown.NearbyAccepted)
                    .Append('/')
                    .Append(a.Breakdown.NearbyAdded)
                    .Append(", B=")
                    .Append(b.Breakdown.NearbyScanned)
                    .Append('/')
                    .Append(b.Breakdown.NearbyAccepted)
                    .Append('/')
                    .Append(b.Breakdown.NearbyAdded);
            }

            return builder.ToString();
        }

        private static void AppendProductReport(StringBuilder builder, ProductSearchSnapshot stats)
        {
            long completed = stats.TrueResults + stats.FalseResults;
            builder.Append("\n  ")
                .Append(stats.ProductId)
                .Append(": calls=")
                .Append(stats.Calls)
                .Append(", outer avg=")
                .Append(FormatMilliseconds(AverageTicks(stats.ElapsedTicks, completed)))
                .Append(" ms, max=")
                .Append(FormatMilliseconds(stats.MaxElapsedTicks))
                .Append(" ms")
                .Append(", global-cache=")
                .Append(stats.GlobalCacheCalls)
                .Append("/")
                .Append(FormatMilliseconds(stats.GlobalCacheElapsedTicks))
                .Append(" ms")
                .Append(", per-tower-cache=")
                .Append(stats.TowerCacheCalls)
                .Append("/")
                .Append(FormatMilliseconds(stats.TowerCacheElapsedTicks))
                .Append(" ms")
                .Append(", residual=")
                .Append(FormatMilliseconds(stats.ResidualElapsedTicks))
                .Append(" ms")
                .Append(", raw-cache-candidates=")
                .Append(FormatDecimal(Average(stats.CandidateDesignations, stats.CandidateCalls)))
                .Append(", breakdown=")
                .Append(FormatStageSummary(stats.Breakdown))
                .Append(", final-cache total/searches/max=")
                .Append(
                    FormatCountSummary(
                        stats.Breakdown.FinalCandidateCount,
                        stats.Breakdown.FinalCandidateCalls,
                        stats.Breakdown.FinalCandidateMax))
                .Append(", nearby scanned/accepted/added=")
                .Append(stats.Breakdown.NearbyScanned)
                .Append('/')
                .Append(stats.Breakdown.NearbyAccepted)
                .Append('/')
                .Append(stats.Breakdown.NearbyAdded);
            AppendProductPathMetrics(builder, stats);
        }

        private static void AppendPathBreakdown(
            StringBuilder builder,
            SearchBreakdownSnapshot breakdown,
            long[] pathCalls)
        {
            builder.Append("\nPath stage breakdown:");
            int pathCount = Math.Min(s_searchPathNames.Length, pathCalls.Length);
            for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
            {
                if (pathCalls[pathIndex] == 0)
                {
                    continue;
                }

                builder.Append("\n  ").Append(s_searchPathNames[pathIndex]).Append(": ");
                AppendPathStageMetrics(builder, breakdown, pathIndex);
            }
        }

        private static void AppendPathStageMetrics(
            StringBuilder builder,
            SearchBreakdownSnapshot breakdown,
            int pathIndex)
        {
            bool wroteStage = false;
            for (int stageIndex = 0; stageIndex < (int)SearchStage.Count; stageIndex++)
            {
                var stage = (SearchStage)stageIndex;
                long stageCalls = breakdown.PathStageCallsFor(pathIndex, stage);
                if (stageCalls == 0)
                {
                    continue;
                }
                if (wroteStage)
                {
                    builder.Append(", ");
                }
                wroteStage = true;
                builder.Append(s_searchStageNames[stageIndex])
                    .Append('=')
                    .Append(FormatMilliseconds(breakdown.PathStageElapsed(pathIndex, stage)))
                    .Append("ms avg=")
                    .Append(FormatMilliseconds(AverageTicks(breakdown.PathStageElapsed(pathIndex, stage), stageCalls)))
                    .Append(" max=")
                    .Append(FormatMilliseconds(breakdown.PathStageMax(pathIndex, stage)))
                    .Append("ms");
            }

            builder.Append("; final-cache total/searches/max=")
                .Append(
                    FormatCountSummary(
                        breakdown.PathFinalCandidateCount[pathIndex],
                        breakdown.PathFinalCandidateCalls[pathIndex],
                        breakdown.PathFinalCandidateMax[pathIndex]))
                .Append("; nearby scanned/accepted/added=")
                .Append(breakdown.PathNearbyScanned[pathIndex])
                .Append('/')
                .Append(breakdown.PathNearbyAccepted[pathIndex])
                .Append('/')
                .Append(breakdown.PathNearbyAdded[pathIndex])
                .Append(" mode tower/global/unknown=")
                .Append(breakdown.PathNearbyMode(pathIndex, NearbyMode.Tower))
                .Append('/')
                .Append(breakdown.PathNearbyMode(pathIndex, NearbyMode.Global))
                .Append('/')
                .Append(breakdown.PathNearbyMode(pathIndex, NearbyMode.Unknown));
        }

        private static void AppendProductPathMetrics(StringBuilder builder, ProductSearchSnapshot stats)
        {
            for (int i = 0; i < s_searchPathNames.Length && i < stats.PathCalls.Length; i++)
            {
                long calls = stats.PathCalls[i];
                if (calls == 0)
                {
                    continue;
                }

                builder.Append("\n    ")
                    .Append(s_searchPathNames[i])
                    .Append(": outer=")
                    .Append(calls)
                    .Append('/')
                    .Append(FormatMilliseconds(stats.PathElapsedTicks[i]))
                    .Append(" ms avg=")
                    .Append(FormatMilliseconds(AverageTicks(stats.PathElapsedTicks[i], calls)))
                    .Append(" ms max=")
                    .Append(FormatMilliseconds(stats.PathMaxElapsedTicks[i]))
                    .Append("; global-cache=")
                    .Append(stats.PathGlobalCacheCalls[i])
                    .Append('/')
                    .Append(FormatMilliseconds(stats.PathGlobalCacheElapsedTicks[i]))
                    .Append(" ms avg=")
                    .Append(FormatMicroseconds(AverageTicks(stats.PathGlobalCacheElapsedTicks[i], stats.PathGlobalCacheCalls[i])))
                    .Append(" us max=")
                    .Append(FormatMilliseconds(stats.PathGlobalCacheMaxElapsedTicks[i]))
                    .Append(" ms; per-tower-cache=")
                    .Append(stats.PathTowerCacheCalls[i])
                    .Append('/')
                    .Append(FormatMilliseconds(stats.PathTowerCacheElapsedTicks[i]))
                    .Append(" ms avg=")
                    .Append(FormatMicroseconds(AverageTicks(stats.PathTowerCacheElapsedTicks[i], stats.PathTowerCacheCalls[i])))
                    .Append(" us max=")
                    .Append(FormatMilliseconds(stats.PathTowerCacheMaxElapsedTicks[i]))
                    .Append(" ms; residual total=")
                    .Append(FormatMilliseconds(stats.PathResidualElapsedTicks[i]))
                    .Append(" ms avg=")
                    .Append(FormatMicroseconds(AverageTicks(stats.PathResidualElapsedTicks[i], calls)))
                    .Append(" us max=")
                    .Append(FormatMilliseconds(stats.PathResidualMaxElapsedTicks[i]))
                    .Append(" ms")
                    .Append("; breakdown=");
                AppendPathStageMetrics(builder, stats.Breakdown, i);
            }
        }

        private static void AppendWorstCall(StringBuilder builder, WorstCallSnapshot worst)
        {
            builder.Append("\nWorst search: elapsed=")
                .Append(FormatMilliseconds(worst.ElapsedTicks))
                .Append(" ms, product=")
                .Append(worst.ProductId)
                .Append(", path=")
                .Append(s_searchPathNames[(int)worst.Path])
                .Append(", caller=")
                .Append(s_searchCallerNames[(int)worst.Caller])
                .Append(", candidates=")
                .Append(worst.CandidateDesignations)
                .Append(", cache global=")
                .Append(FormatMilliseconds(worst.GlobalCacheElapsedTicks))
                .Append(" ms, per-tower=")
                .Append(FormatMilliseconds(worst.TowerCacheElapsedTicks))
                .Append(" ms, nested=")
                .Append(FormatMilliseconds(worst.NestedCacheElapsedTicks))
                .Append(" ms, residual=")
                .Append(FormatMilliseconds(worst.ResidualElapsedTicks))
                .Append(" ms, cache calls=")
                .Append(worst.GlobalCacheCalls)
                .Append('/')
                .Append(worst.TowerCacheCalls);
        }

        private static string FormatCacheComparison(long callsA, long ticksA, long callsB, long ticksB) =>
            $"A={callsA} calls/{FormatMilliseconds(ticksA)} ms avg {FormatMicroseconds(AverageTicks(ticksA, callsA))} us, B={callsB} calls/{FormatMilliseconds(ticksB)} ms avg {FormatMicroseconds(AverageTicks(ticksB, callsB))} us";

        private static string FormatPercentageSummary(string[] names, long[] counters)
        {
            long total = 0L;
            for (int i = 0; i < Math.Min(names.Length, counters.Length); i++)
            {
                total += counters[i];
            }
            if (total == 0)
            {
                return "none";
            }

            var builder = new StringBuilder(160);
            for (int i = 0; i < Math.Min(names.Length, counters.Length); i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(names[i]).Append('=').Append(FormatDecimal(counters[i] * 100.0 / total)).Append('%');
            }

            return builder.ToString();
        }

        private static bool HasNonZero(long[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static ProductSearchSnapshot? FindProduct(ProductSearchSnapshot[] products, string productId)
        {
            for (int i = 0; i < products.Length; i++)
            {
                if (string.Equals(products[i].ProductId, productId, StringComparison.Ordinal))
                {
                    return products[i];
                }
            }
            return null;
        }

        private static string FindDominantPath(long[] pathCalls)
        {
            int index = 0;
            long max = 0L;
            for (int i = 0; i < Math.Min(pathCalls.Length, s_searchPathNames.Length); i++)
            {
                if (pathCalls[i] > max)
                {
                    index = i;
                    max = pathCalls[i];
                }
            }

            return max == 0 ? "none" : s_searchPathNames[index];
        }

        private static double CallsPerSecond(ProfileSnapshot profile) =>
            profile.ActualRecordingSeconds > 0.0 ? profile.TotalCalls / profile.ActualRecordingSeconds : 0.0;

        private static double Utilization(ProfileSnapshot profile)
        {
            return profile.ActualRecordingSeconds > 0.0
                ? StopwatchTicksToSeconds(profile.TotalElapsedTicks) / profile.ActualRecordingSeconds * 100.0
                : 0.0;
        }

        private static string FormatRatio(double numerator, double denominator) =>
            denominator == 0.0 ? "n/a" : FormatDecimal(numerator / denominator) + "x";

        private static double Average(long total, long count) => count > 0 ? total / (double)count : 0.0;

        private static double AverageTicks(long totalTicks, long count) => count > 0 ? totalTicks / (double)count : 0.0;

        private static bool IsValidProfileDuration(double value, double minimum, double maximum) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;

        private static string DescribeProfileState(ProfileState state) =>
            state == ProfileState.WarmingUp ? "warming-up" : state == ProfileState.Recording ? "recording" : "idle";

        private static ProfileSnapshot? FindProfileLocked(string label)
        {
            for (int i = 0; i < s_profileHistory.Count; i++)
            {
                if (string.Equals(s_profileHistory[i].Label, label, StringComparison.Ordinal))
                {
                    return s_profileHistory[i];
                }
            }
            return null;
        }

        private static long SecondsToStopwatchTicks(double seconds) =>
            (long)Math.Round(seconds * Stopwatch.Frequency, MidpointRounding.AwayFromZero);

        private static double StopwatchTicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        private static string FormatSeconds(double seconds) => seconds.ToString("F2", CultureInfo.InvariantCulture) + "s";

        private static string FormatMilliseconds(double ticks) =>
            (ticks * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture);

        private static string FormatMicroseconds(double ticks) =>
            (ticks * 1_000_000.0 / Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture);

        private static string FormatDecimal(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

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

        private enum SearchStage
        {
            PreSelection,
            CandidateFiltering,
            BestReadyToFulfill,
            NearbyExpansion,
            Unaccounted,
            Count,
        }

        private enum NearbyMode
        {
            Unknown,
            Global,
            Tower,
            Count,
        }

        private enum SearchCaller
        {
            Other,
            VehicleBuffersRegistryBalanceBuffers,
            DumpingJob,
            DefaultTruckJobProvider,
            Count,
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

        private readonly struct BestReadyToFulfillCallState
        {
            public BestReadyToFulfillCallState(SearchDiagnosticContext? context)
            {
                Context = context;
            }

            public SearchDiagnosticContext? Context { get; }
        }

        private sealed class EligibleCacheDiagnosticContext
        {
            public int CompletionRecorded;

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
        }

        private sealed class SearchDiagnosticContext
        {
            public int BestReadyToFulfillCalls;
            public int BestReadyToFulfillCompletionRecorded;
            public long BestReadyToFulfillElapsedTicks;
            public long BestReadyToFulfillEndTimestamp;
            public bool BestReadyToFulfillResult;
            public long BestReadyToFulfillStartTimestamp;
            public int CandidateCalls;
            public long CandidateDesignations;
            public int CandidateFilteringCalls;
            public long CandidateFilteringElapsedTicks;
            public int CompletionRecorded;
            public int FinalCandidateCount = -1;
            public int FinalCandidateCountCalls;
            public long FirstEligibleCacheStartTimestamp;
            public int GlobalCacheCalls;
            public long GlobalCacheElapsedTicks;
            public long GlobalCacheMaxElapsedTicks;
            public long LastEligibleCacheEndTimestamp;
            public int NearbyAccepted;
            public long NearbyAdded;
            public long NearbyExpansionCalls;
            public long NearbyExpansionElapsedTicks;
            public NearbyMode NearbyMode;
            public int NearbyScanned;
            public long PreSelectionElapsedTicks;
            public int TowerCacheCalls;
            public long TowerCacheElapsedTicks;
            public long TowerCacheMaxElapsedTicks;
            public long UnaccountedElapsedTicks;

            public SearchDiagnosticContext(
                SearchDiagnosticContext? previous,
                ProductSearchStats stats,
                SearchPath path,
                SearchCaller caller,
                long startTimestamp,
                Lyst<TerrainDesignation>? nearbyDesignations)
            {
                Previous = previous;
                Stats = stats;
                Path = path;
                Caller = caller;
                StartTimestamp = startTimestamp;
                NearbyDesignations = nearbyDesignations;
                NearbyDesignationsInitialCount = nearbyDesignations?.Count ?? -1;
                NearbyMode = path == SearchPath.GlobalAllowed
                    ? NearbyMode.Global
                    : path is SearchPath.ExplicitTower or SearchPath.ExplicitTowerGlobalForbiddenRejected
                        ? NearbyMode.Tower
                        : NearbyMode.Unknown;
            }

            public SearchDiagnosticContext? Previous { get; }
            public ProductSearchStats Stats { get; }
            public SearchPath Path { get; private set; }
            public SearchCaller Caller { get; }
            public long StartTimestamp { get; }
            public Lyst<TerrainDesignation>? NearbyDesignations { get; }
            public int NearbyDesignationsInitialCount { get; }

            public void BeginEligibleCache(long timestamp)
            {
                if (FirstEligibleCacheStartTimestamp == 0)
                {
                    FirstEligibleCacheStartTimestamp = timestamp;
                    PreSelectionElapsedTicks = Math.Max(0, timestamp - StartTimestamp);
                }
                else if (LastEligibleCacheEndTimestamp != 0)
                {
                    CandidateFilteringElapsedTicks += Math.Max(0, timestamp - LastEligibleCacheEndTimestamp);
                    CandidateFilteringCalls = 1;
                }
            }

            public void RecordEligibleCacheEnd(long timestamp) => LastEligibleCacheEndTimestamp = timestamp;

            public void BeginBestReadyToFulfill(long timestamp, int candidateCount)
            {
                if (LastEligibleCacheEndTimestamp != 0)
                {
                    CandidateFilteringElapsedTicks += Math.Max(0, timestamp - LastEligibleCacheEndTimestamp);
                    CandidateFilteringCalls = 1;
                }
                else if (FirstEligibleCacheStartTimestamp == 0)
                {
                    PreSelectionElapsedTicks = Math.Max(0, timestamp - StartTimestamp);
                }

                if (candidateCount >= 0)
                {
                    FinalCandidateCount = candidateCount;
                    FinalCandidateCountCalls++;
                }

                BestReadyToFulfillCalls++;
                BestReadyToFulfillCompletionRecorded = 0;
                BestReadyToFulfillStartTimestamp = timestamp;
                BestReadyToFulfillEndTimestamp = 0;
            }

            public void CompleteBestReadyToFulfill(long timestamp, bool result)
            {
                if (Interlocked.CompareExchange(ref BestReadyToFulfillCompletionRecorded, 1, 0) != 0)
                {
                    return;
                }

                BestReadyToFulfillElapsedTicks += Math.Max(0, timestamp - BestReadyToFulfillStartTimestamp);
                BestReadyToFulfillResult = result;
                BestReadyToFulfillEndTimestamp = timestamp;
            }

            public void RecordNearbyDesignationScanned() => NearbyScanned++;

            public void RecordNearbyDesignationResult(bool accepted)
            {
                if (accepted)
                {
                    NearbyAccepted++;
                }
            }

            public SearchBreakdown CompleteBreakdown(long outerElapsedTicks, long nestedCacheElapsedTicks, long completionTimestamp)
            {
                if (BestReadyToFulfillEndTimestamp != 0 && BestReadyToFulfillResult && NearbyDesignations is not null)
                {
                    NearbyExpansionElapsedTicks = Math.Max(0, completionTimestamp - BestReadyToFulfillEndTimestamp);
                    NearbyExpansionCalls = 1;
                    NearbyAdded = Math.Max(0, NearbyDesignations.Count - NearbyDesignationsInitialCount);
                }
                else if (BestReadyToFulfillEndTimestamp == 0 && LastEligibleCacheEndTimestamp != 0)
                {
                    CandidateFilteringElapsedTicks += Math.Max(0, completionTimestamp - LastEligibleCacheEndTimestamp);
                    CandidateFilteringCalls = 1;
                }

                long accountedTicks = PreSelectionElapsedTicks + CandidateFilteringElapsedTicks + nestedCacheElapsedTicks +
                                      BestReadyToFulfillElapsedTicks + NearbyExpansionElapsedTicks;
                UnaccountedElapsedTicks = Math.Max(0, outerElapsedTicks - accountedTicks);
                if (accountedTicks > outerElapsedTicks + TinyResidualRoundingToleranceTicks)
                {
                    UnaccountedElapsedTicks = 0;
                }

                return new SearchBreakdown(
                    PreSelectionElapsedTicks,
                    CandidateFilteringElapsedTicks,
                    BestReadyToFulfillElapsedTicks,
                    NearbyExpansionElapsedTicks,
                    UnaccountedElapsedTicks,
                    FinalCandidateCount,
                    FinalCandidateCountCalls,
                    BestReadyToFulfillCalls,
                    NearbyScanned,
                    NearbyAccepted,
                    NearbyAdded,
                    NearbyExpansionCalls,
                    NearbyMode,
                    CandidateFilteringCalls);
            }

            public void MarkTowerEligibleCacheCall()
            {
                if (Path == SearchPath.GlobalForbiddenNoLocalTower)
                {
                    Path = SearchPath.GlobalForbiddenLocalFallback;
                }
                else if (Path == SearchPath.ExplicitTowerGlobalForbiddenRejected)
                {
                    Path = SearchPath.ExplicitTowerGlobalForbiddenLocal;
                }
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
                UpdateMax(ref GlobalCacheMaxElapsedTicks, elapsedTicks);
            }

            public void RecordTowerCache(long elapsedTicks)
            {
                Interlocked.Add(ref TowerCacheElapsedTicks, elapsedTicks);
                Interlocked.Increment(ref TowerCacheCalls);
                UpdateMax(ref TowerCacheMaxElapsedTicks, elapsedTicks);
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

        private readonly struct SearchBreakdown
        {
            public SearchBreakdown(
                long preSelectionElapsedTicks,
                long candidateFilteringElapsedTicks,
                long bestReadyToFulfillElapsedTicks,
                long nearbyExpansionElapsedTicks,
                long unaccountedElapsedTicks,
                int finalCandidateCount,
                int finalCandidateCountCalls,
                int bestReadyToFulfillCalls,
                int nearbyScanned,
                int nearbyAccepted,
                long nearbyAdded,
                long nearbyExpansionCalls,
                NearbyMode nearbyMode,
                int candidateFilteringCalls = 0)
            {
                PreSelectionElapsedTicks = preSelectionElapsedTicks;
                CandidateFilteringElapsedTicks = candidateFilteringElapsedTicks;
                BestReadyToFulfillElapsedTicks = bestReadyToFulfillElapsedTicks;
                NearbyExpansionElapsedTicks = nearbyExpansionElapsedTicks;
                UnaccountedElapsedTicks = unaccountedElapsedTicks;
                FinalCandidateCount = finalCandidateCount;
                FinalCandidateCountCalls = finalCandidateCountCalls;
                BestReadyToFulfillCalls = bestReadyToFulfillCalls;
                NearbyScanned = nearbyScanned;
                NearbyAccepted = nearbyAccepted;
                NearbyAdded = nearbyAdded;
                NearbyExpansionCalls = nearbyExpansionCalls;
                NearbyMode = nearbyMode;
                CandidateFilteringCalls = candidateFilteringCalls;
            }

            public long PreSelectionElapsedTicks { get; }
            public long CandidateFilteringElapsedTicks { get; }
            public long BestReadyToFulfillElapsedTicks { get; }
            public long NearbyExpansionElapsedTicks { get; }
            public long UnaccountedElapsedTicks { get; }
            public int FinalCandidateCount { get; }
            public int FinalCandidateCountCalls { get; }
            public int BestReadyToFulfillCalls { get; }
            public int NearbyScanned { get; }
            public int NearbyAccepted { get; }
            public long NearbyAdded { get; }
            public long NearbyExpansionCalls { get; }
            public NearbyMode NearbyMode { get; }
            public int CandidateFilteringCalls { get; }

            public long GetElapsedTicks(SearchStage stage)
            {
                return stage switch
                {
                    SearchStage.PreSelection => PreSelectionElapsedTicks,
                    SearchStage.CandidateFiltering => CandidateFilteringElapsedTicks,
                    SearchStage.BestReadyToFulfill => BestReadyToFulfillElapsedTicks,
                    SearchStage.NearbyExpansion => NearbyExpansionElapsedTicks,
                    SearchStage.Unaccounted => UnaccountedElapsedTicks,
                    _ => 0,
                };
            }

            public long GetCalls(SearchStage stage)
            {
                return stage switch
                {
                    SearchStage.PreSelection => 1,
                    SearchStage.CandidateFiltering => CandidateFilteringCalls,
                    SearchStage.BestReadyToFulfill => BestReadyToFulfillCalls,
                    SearchStage.NearbyExpansion => NearbyExpansionCalls,
                    SearchStage.Unaccounted => 1,
                    _ => 0,
                };
            }

        }

        private sealed class SearchBreakdownStats
        {
            private readonly long[] m_nearbyModeCalls = new long[(int)NearbyMode.Count];
            private readonly long[] m_pathFinalCandidateCalls = new long[(int)SearchPath.Count];
            private readonly long[] m_pathFinalCandidateCount = new long[(int)SearchPath.Count];
            private readonly long[] m_pathFinalCandidateMax = new long[(int)SearchPath.Count];
            private readonly long[] m_pathNearbyAccepted = new long[(int)SearchPath.Count];
            private readonly long[] m_pathNearbyAdded = new long[(int)SearchPath.Count];
            private readonly long[] m_pathNearbyExpansionCalls = new long[(int)SearchPath.Count];
            private readonly long[] m_pathNearbyModeCalls = new long[(int)SearchPath.Count * (int)NearbyMode.Count];
            private readonly long[] m_pathNearbyScanned = new long[(int)SearchPath.Count];
            private readonly long[] m_pathStageCalls = new long[(int)SearchPath.Count * (int)SearchStage.Count];
            private readonly long[] m_pathStageElapsedTicks = new long[(int)SearchPath.Count * (int)SearchStage.Count];
            private readonly long[] m_pathStageMaxElapsedTicks = new long[(int)SearchPath.Count * (int)SearchStage.Count];
            private readonly long[] m_stageCalls = new long[(int)SearchStage.Count];
            private readonly long[] m_stageElapsedTicks = new long[(int)SearchStage.Count];
            private readonly long[] m_stageMaxElapsedTicks = new long[(int)SearchStage.Count];
            private long m_finalCandidateCalls;
            private long m_finalCandidateCount;
            private long m_finalCandidateMax;
            private long m_nearbyAccepted;
            private long m_nearbyAdded;
            private long m_nearbyExpansionCalls;
            private long m_nearbyScanned;

            public void Record(SearchPath path, SearchBreakdown breakdown)
            {
                int pathIndex = (int)path;
                for (int i = 0; i < (int)SearchStage.Count; i++)
                {
                    var stage = (SearchStage)i;
                    long elapsedTicks = breakdown.GetElapsedTicks(stage);
                    long calls = breakdown.GetCalls(stage);
                    if (calls > 0)
                    {
                        Interlocked.Add(ref m_stageElapsedTicks[i], elapsedTicks);
                        Interlocked.Add(ref m_stageCalls[i], calls);
                        UpdateMax(ref m_stageMaxElapsedTicks[i], elapsedTicks);
                        int pathOffset = pathIndex * (int)SearchStage.Count + i;
                        Interlocked.Add(ref m_pathStageElapsedTicks[pathOffset], elapsedTicks);
                        Interlocked.Add(ref m_pathStageCalls[pathOffset], calls);
                        UpdateMax(ref m_pathStageMaxElapsedTicks[pathOffset], elapsedTicks);
                    }
                }

                if (breakdown.FinalCandidateCountCalls > 0)
                {
                    Interlocked.Add(ref m_finalCandidateCount, breakdown.FinalCandidateCount);
                    Interlocked.Add(ref m_finalCandidateCalls, breakdown.FinalCandidateCountCalls);
                    UpdateMax(ref m_finalCandidateMax, breakdown.FinalCandidateCount);
                    Interlocked.Add(ref m_pathFinalCandidateCount[pathIndex], breakdown.FinalCandidateCount);
                    Interlocked.Add(ref m_pathFinalCandidateCalls[pathIndex], breakdown.FinalCandidateCountCalls);
                    UpdateMax(ref m_pathFinalCandidateMax[pathIndex], breakdown.FinalCandidateCount);
                }

                Interlocked.Add(ref m_nearbyScanned, breakdown.NearbyScanned);
                Interlocked.Add(ref m_nearbyAccepted, breakdown.NearbyAccepted);
                Interlocked.Add(ref m_nearbyAdded, breakdown.NearbyAdded);
                Interlocked.Add(ref m_nearbyExpansionCalls, breakdown.NearbyExpansionCalls);
                Interlocked.Add(ref m_pathNearbyScanned[pathIndex], breakdown.NearbyScanned);
                Interlocked.Add(ref m_pathNearbyAccepted[pathIndex], breakdown.NearbyAccepted);
                Interlocked.Add(ref m_pathNearbyAdded[pathIndex], breakdown.NearbyAdded);
                Interlocked.Add(ref m_pathNearbyExpansionCalls[pathIndex], breakdown.NearbyExpansionCalls);
                if (breakdown.NearbyExpansionCalls > 0)
                {
                    Interlocked.Increment(ref m_nearbyModeCalls[(int)breakdown.NearbyMode]);
                    Interlocked.Increment(ref m_pathNearbyModeCalls[pathIndex * (int)NearbyMode.Count + (int)breakdown.NearbyMode]);
                }
            }

            public SearchBreakdownSnapshot Snapshot()
            {
                return new SearchBreakdownSnapshot(
                    SnapshotCounters(m_stageElapsedTicks),
                    SnapshotCounters(m_stageCalls),
                    SnapshotCounters(m_stageMaxElapsedTicks),
                    SnapshotCounters(m_pathStageElapsedTicks),
                    SnapshotCounters(m_pathStageCalls),
                    SnapshotCounters(m_pathStageMaxElapsedTicks),
                    Read(ref m_finalCandidateCount),
                    Read(ref m_finalCandidateCalls),
                    Read(ref m_finalCandidateMax),
                    Read(ref m_nearbyScanned),
                    Read(ref m_nearbyAccepted),
                    Read(ref m_nearbyAdded),
                    Read(ref m_nearbyExpansionCalls),
                    SnapshotCounters(m_nearbyModeCalls),
                    SnapshotCounters(m_pathFinalCandidateCount),
                    SnapshotCounters(m_pathFinalCandidateCalls),
                    SnapshotCounters(m_pathFinalCandidateMax),
                    SnapshotCounters(m_pathNearbyScanned),
                    SnapshotCounters(m_pathNearbyAccepted),
                    SnapshotCounters(m_pathNearbyAdded),
                    SnapshotCounters(m_pathNearbyExpansionCalls),
                    SnapshotCounters(m_pathNearbyModeCalls));
            }

            public void Reset()
            {
                ResetCounters(m_stageElapsedTicks);
                ResetCounters(m_stageCalls);
                ResetCounters(m_stageMaxElapsedTicks);
                ResetCounters(m_pathStageElapsedTicks);
                ResetCounters(m_pathStageCalls);
                ResetCounters(m_pathStageMaxElapsedTicks);
                ResetCounters(m_pathFinalCandidateCount);
                ResetCounters(m_pathFinalCandidateCalls);
                ResetCounters(m_pathFinalCandidateMax);
                ResetCounters(m_pathNearbyScanned);
                ResetCounters(m_pathNearbyAccepted);
                ResetCounters(m_pathNearbyAdded);
                ResetCounters(m_pathNearbyExpansionCalls);
                ResetCounters(m_pathNearbyModeCalls);
                Interlocked.Exchange(ref m_finalCandidateCount, 0);
                Interlocked.Exchange(ref m_finalCandidateCalls, 0);
                Interlocked.Exchange(ref m_finalCandidateMax, 0);
                Interlocked.Exchange(ref m_nearbyScanned, 0);
                Interlocked.Exchange(ref m_nearbyAccepted, 0);
                Interlocked.Exchange(ref m_nearbyAdded, 0);
                Interlocked.Exchange(ref m_nearbyExpansionCalls, 0);
                ResetCounters(m_nearbyModeCalls);
            }
        }

        private sealed class SearchBreakdownSnapshot
        {
            public SearchBreakdownSnapshot(
                long[] stageElapsedTicks,
                long[] stageCalls,
                long[] stageMaxElapsedTicks,
                long[] pathStageElapsedTicks,
                long[] pathStageCalls,
                long[] pathStageMaxElapsedTicks,
                long finalCandidateCount,
                long finalCandidateCalls,
                long finalCandidateMax,
                long nearbyScanned,
                long nearbyAccepted,
                long nearbyAdded,
                long nearbyExpansionCalls,
                long[] nearbyModeCalls,
                long[] pathFinalCandidateCount,
                long[] pathFinalCandidateCalls,
                long[] pathFinalCandidateMax,
                long[] pathNearbyScanned,
                long[] pathNearbyAccepted,
                long[] pathNearbyAdded,
                long[] pathNearbyExpansionCalls,
                long[] pathNearbyModeCalls)
            {
                StageElapsedTicks = stageElapsedTicks;
                StageCalls = stageCalls;
                StageMaxElapsedTicks = stageMaxElapsedTicks;
                PathStageElapsedTicks = pathStageElapsedTicks;
                PathStageCalls = pathStageCalls;
                PathStageMaxElapsedTicks = pathStageMaxElapsedTicks;
                FinalCandidateCount = finalCandidateCount;
                FinalCandidateCalls = finalCandidateCalls;
                FinalCandidateMax = finalCandidateMax;
                NearbyScanned = nearbyScanned;
                NearbyAccepted = nearbyAccepted;
                NearbyAdded = nearbyAdded;
                NearbyExpansionCalls = nearbyExpansionCalls;
                NearbyModeCalls = nearbyModeCalls;
                PathFinalCandidateCount = pathFinalCandidateCount;
                PathFinalCandidateCalls = pathFinalCandidateCalls;
                PathFinalCandidateMax = pathFinalCandidateMax;
                PathNearbyScanned = pathNearbyScanned;
                PathNearbyAccepted = pathNearbyAccepted;
                PathNearbyAdded = pathNearbyAdded;
                PathNearbyExpansionCalls = pathNearbyExpansionCalls;
                PathNearbyModeCalls = pathNearbyModeCalls;
            }

            public long[] StageElapsedTicks { get; }
            public long[] StageCalls { get; }
            public long[] StageMaxElapsedTicks { get; }
            public long[] PathStageElapsedTicks { get; }
            public long[] PathStageCalls { get; }
            public long[] PathStageMaxElapsedTicks { get; }
            public long FinalCandidateCount { get; }
            public long FinalCandidateCalls { get; }
            public long FinalCandidateMax { get; }
            public long NearbyScanned { get; }
            public long NearbyAccepted { get; }
            public long NearbyAdded { get; }
            public long NearbyExpansionCalls { get; }
            public long[] NearbyModeCalls { get; }
            public long[] PathFinalCandidateCount { get; }
            public long[] PathFinalCandidateCalls { get; }
            public long[] PathFinalCandidateMax { get; }
            public long[] PathNearbyScanned { get; }
            public long[] PathNearbyAccepted { get; }
            public long[] PathNearbyAdded { get; }
            public long[] PathNearbyExpansionCalls { get; }
            public long[] PathNearbyModeCalls { get; }

            public long PathStageElapsed(int path, SearchStage stage) => PathStageElapsedTicks[path * (int)SearchStage.Count + (int)stage];
            public long PathStageCallsFor(int path, SearchStage stage) => PathStageCalls[path * (int)SearchStage.Count + (int)stage];
            public long PathStageMax(int path, SearchStage stage) => PathStageMaxElapsedTicks[path * (int)SearchStage.Count + (int)stage];
            public long PathNearbyMode(int path, NearbyMode mode) => PathNearbyModeCalls[path * (int)NearbyMode.Count + (int)mode];
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
                    ? startTimestamp + SecondsToStopwatchTicks(warmupSeconds)
                    : 0L;
                RecordingEndTimestamp = state == ProfileState.Recording
                    ? recordingStartTimestamp + SecondsToStopwatchTicks(requestedSeconds)
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
                RecordingEndTimestamp = now + SecondsToStopwatchTicks(RequestedSeconds);
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
                SearchBreakdownSnapshot breakdown,
                ProductSearchSnapshot[] products)
            {
                Label = session.Label;
                Sequence = session.Sequence;
                RequestedSeconds = session.RequestedSeconds;
                WarmupSeconds = session.WarmupSeconds;
                ActualRecordingSeconds = StopwatchTicksToSeconds(actualRecordingTicks);
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
                Breakdown = breakdown;
                Products = (ProductSearchSnapshot[])products.Clone();
                DominantPath = FindDominantPath(PathCalls);
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
            public SearchBreakdownSnapshot Breakdown { get; }
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

        private sealed class ProductSearchStats
        {
            private readonly SearchBreakdownStats m_breakdown = new();
            private readonly long[] m_callerCalls = new long[(int)SearchCaller.Count];
            private readonly long[] m_latencyBuckets = new long[LatencyBucketCount];
            private readonly long[] m_pathCalls = new long[(int)SearchPath.Count];
            private readonly long[] m_pathElapsedTicks = new long[(int)SearchPath.Count];
            private readonly long[] m_pathGlobalCacheCalls = new long[(int)SearchPath.Count];
            private readonly long[] m_pathGlobalCacheElapsedTicks = new long[(int)SearchPath.Count];
            private readonly long[] m_pathGlobalCacheMaxElapsedTicks = new long[(int)SearchPath.Count];
            private readonly long[] m_pathMaxElapsedTicks = new long[(int)SearchPath.Count];
            private readonly long[] m_pathResidualElapsedTicks = new long[(int)SearchPath.Count];
            private readonly long[] m_pathResidualMaxElapsedTicks = new long[(int)SearchPath.Count];
            private readonly long[] m_pathTowerCacheCalls = new long[(int)SearchPath.Count];
            private readonly long[] m_pathTowerCacheElapsedTicks = new long[(int)SearchPath.Count];
            private readonly long[] m_pathTowerCacheMaxElapsedTicks = new long[(int)SearchPath.Count];
            private long m_calls;
            private long m_candidateCalls;
            private long m_candidateDesignations;
            private long m_elapsedTicks;
            private long m_falseResults;
            private long m_globalCacheCalls;
            private long m_globalCacheElapsedTicks;
            private long m_globalCacheMaxElapsedTicks;
            private long m_maxElapsedTicks;
            private long m_residualElapsedTicks;
            private long m_residualMaxElapsedTicks;
            private long m_towerCacheCalls;
            private long m_towerCacheElapsedTicks;
            private long m_towerCacheMaxElapsedTicks;
            private long m_trueResults;
            private WorstCallSnapshot? m_worstCall;

            public ProductSearchStats(string productId)
            {
                ProductId = productId;
            }

            public string ProductId { get; }
            public long Calls => Read(ref m_calls);
            public long TrueResults => Read(ref m_trueResults);
            public long FalseResults => Read(ref m_falseResults);
            public long ElapsedTicks => Read(ref m_elapsedTicks);
            public long MaxElapsedTicks => Read(ref m_maxElapsedTicks);
            public long CandidateDesignations => Read(ref m_candidateDesignations);
            public long CandidateCalls => Read(ref m_candidateCalls);
            public long GlobalCacheCalls => Read(ref m_globalCacheCalls);
            public long GlobalCacheElapsedTicks => Read(ref m_globalCacheElapsedTicks);
            public long GlobalCacheMaxElapsedTicks => Read(ref m_globalCacheMaxElapsedTicks);
            public long TowerCacheCalls => Read(ref m_towerCacheCalls);
            public long TowerCacheElapsedTicks => Read(ref m_towerCacheElapsedTicks);
            public long TowerCacheMaxElapsedTicks => Read(ref m_towerCacheMaxElapsedTicks);
            public long ResidualElapsedTicks => Read(ref m_residualElapsedTicks);
            public long ResidualMaxElapsedTicks => Read(ref m_residualMaxElapsedTicks);
            public SearchBreakdownSnapshot Breakdown => m_breakdown.Snapshot();
            public WorstCallSnapshot? WorstCall => Volatile.Read(ref m_worstCall);

            public void RecordCallStart() => Interlocked.Increment(ref m_calls);

            public void RecordCompletion(
                SearchPath path,
                SearchCaller caller,
                bool hasResult,
                bool result,
                long elapsedTicks,
                long candidateDesignations,
                int candidateCalls,
                CacheSummary cacheSummary,
                long residualTicks,
                SearchBreakdown breakdown)
            {
                Interlocked.Increment(ref m_pathCalls[(int)path]);
                Interlocked.Increment(ref m_callerCalls[(int)caller]);
                int pathIndex = (int)path;

                if (hasResult)
                {
                    if (result)
                    {
                        Interlocked.Increment(ref m_trueResults);
                    }
                    else
                    {
                        Interlocked.Increment(ref m_falseResults);
                    }
                    Interlocked.Add(ref m_elapsedTicks, elapsedTicks);
                    UpdateMax(ref m_maxElapsedTicks, elapsedTicks);
                    Interlocked.Add(ref m_pathElapsedTicks[pathIndex], elapsedTicks);
                    UpdateMax(ref m_pathMaxElapsedTicks[pathIndex], elapsedTicks);
                }

                if (candidateCalls > 0)
                {
                    Interlocked.Add(ref m_candidateDesignations, candidateDesignations);
                    Interlocked.Increment(ref m_candidateCalls);
                }

                Interlocked.Add(ref m_globalCacheCalls, cacheSummary.GlobalCalls);
                Interlocked.Add(ref m_globalCacheElapsedTicks, cacheSummary.GlobalElapsedTicks);
                UpdateMax(ref m_globalCacheMaxElapsedTicks, cacheSummary.GlobalMaxElapsedTicks);
                Interlocked.Add(ref m_towerCacheCalls, cacheSummary.TowerCalls);
                Interlocked.Add(ref m_towerCacheElapsedTicks, cacheSummary.TowerElapsedTicks);
                UpdateMax(ref m_towerCacheMaxElapsedTicks, cacheSummary.TowerMaxElapsedTicks);
                Interlocked.Add(ref m_residualElapsedTicks, residualTicks);
                UpdateMax(ref m_residualMaxElapsedTicks, residualTicks);
                Interlocked.Increment(ref m_latencyBuckets[GetLatencyBucket(elapsedTicks)]);
                Interlocked.Add(ref m_pathGlobalCacheCalls[pathIndex], cacheSummary.GlobalCalls);
                Interlocked.Add(ref m_pathGlobalCacheElapsedTicks[pathIndex], cacheSummary.GlobalElapsedTicks);
                UpdateMax(ref m_pathGlobalCacheMaxElapsedTicks[pathIndex], cacheSummary.GlobalMaxElapsedTicks);
                Interlocked.Add(ref m_pathTowerCacheCalls[pathIndex], cacheSummary.TowerCalls);
                Interlocked.Add(ref m_pathTowerCacheElapsedTicks[pathIndex], cacheSummary.TowerElapsedTicks);
                UpdateMax(ref m_pathTowerCacheMaxElapsedTicks[pathIndex], cacheSummary.TowerMaxElapsedTicks);
                Interlocked.Add(ref m_pathResidualElapsedTicks[pathIndex], residualTicks);
                UpdateMax(ref m_pathResidualMaxElapsedTicks[pathIndex], residualTicks);
                m_breakdown.Record(path, breakdown);
            }

            public ProductSearchSnapshot Snapshot() => new(this, CreateSnapshotCounters());

            private ProductSnapshotCounters CreateSnapshotCounters()
            {
                return new ProductSnapshotCounters
                {
                    PathCalls = SnapshotCounters(m_pathCalls),
                    CallerCalls = SnapshotCounters(m_callerCalls),
                    LatencyBuckets = SnapshotCounters(m_latencyBuckets),
                    PathElapsedTicks = SnapshotCounters(m_pathElapsedTicks),
                    PathMaxElapsedTicks = SnapshotCounters(m_pathMaxElapsedTicks),
                    PathGlobalCacheCalls = SnapshotCounters(m_pathGlobalCacheCalls),
                    PathGlobalCacheElapsedTicks = SnapshotCounters(m_pathGlobalCacheElapsedTicks),
                    PathGlobalCacheMaxElapsedTicks = SnapshotCounters(m_pathGlobalCacheMaxElapsedTicks),
                    PathTowerCacheCalls = SnapshotCounters(m_pathTowerCacheCalls),
                    PathTowerCacheElapsedTicks = SnapshotCounters(m_pathTowerCacheElapsedTicks),
                    PathTowerCacheMaxElapsedTicks = SnapshotCounters(m_pathTowerCacheMaxElapsedTicks),
                    PathResidualElapsedTicks = SnapshotCounters(m_pathResidualElapsedTicks),
                    PathResidualMaxElapsedTicks = SnapshotCounters(m_pathResidualMaxElapsedTicks),
                };
            }

            public void Reset()
            {
                m_breakdown.Reset();
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
                for (int i = 0; i < m_pathCalls.Length; i++)
                {
                    Interlocked.Exchange(ref m_pathCalls[i], 0);
                }
                for (int i = 0; i < m_callerCalls.Length; i++)
                {
                    Interlocked.Exchange(ref m_callerCalls[i], 0);
                }
                for (int i = 0; i < m_latencyBuckets.Length; i++)
                {
                    Interlocked.Exchange(ref m_latencyBuckets[i], 0);
                }
                for (int i = 0; i < m_pathElapsedTicks.Length; i++)
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
                    WorstCallSnapshot? current = Volatile.Read(ref m_worstCall);
                    if (current is not null && current.ElapsedTicks >= candidate.ElapsedTicks)
                    {
                        return;
                    }
                    if (ReferenceEquals(Interlocked.CompareExchange(ref m_worstCall, candidate, current), current))
                    {
                        return;
                    }
                }
            }
        }

        private sealed class ProductSnapshotCounters
        {
            public long[] CallerCalls = Array.Empty<long>();
            public long[] LatencyBuckets = Array.Empty<long>();
            public long[] PathCalls = Array.Empty<long>();
            public long[] PathElapsedTicks = Array.Empty<long>();
            public long[] PathGlobalCacheCalls = Array.Empty<long>();
            public long[] PathGlobalCacheElapsedTicks = Array.Empty<long>();
            public long[] PathGlobalCacheMaxElapsedTicks = Array.Empty<long>();
            public long[] PathMaxElapsedTicks = Array.Empty<long>();
            public long[] PathResidualElapsedTicks = Array.Empty<long>();
            public long[] PathResidualMaxElapsedTicks = Array.Empty<long>();
            public long[] PathTowerCacheCalls = Array.Empty<long>();
            public long[] PathTowerCacheElapsedTicks = Array.Empty<long>();
            public long[] PathTowerCacheMaxElapsedTicks = Array.Empty<long>();
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
                Breakdown = source.Breakdown;
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
            public SearchBreakdownSnapshot Breakdown { get; }
        }
    }
}
