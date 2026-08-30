// Taj's COI Mods | TransportFlowLimitState.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TajsCOI.Tweaks.Features.TransportFlowLimits
{
    /// <summary>
    ///     Save-scoped policy and simulation-only token state for ordinary transports. The policy
    ///     map is persisted outside the vanilla save; token balance and last simulation step are
    ///     deliberately reset on load so transient rate-limit debt never becomes save data.
    /// </summary>
    internal static class TransportFlowLimitState
    {
        internal const double MaxLimitUnitsPerSecond = 1_000_000d;
        internal const double MaxBurstSeconds = 1d;
        internal const int SimulationStepsPerSecond = 10;

        private const string StateHeader = "TajsTweaksTransportFlowLimitV1";
        private static readonly object s_persistenceGate = new();
        private static readonly ConcurrentDictionary<int, double> s_limits = new();
        private static readonly Dictionary<int, TokenBucket> s_buckets = new();
        private static string? s_stateFilePath;

        internal readonly struct Reservation
        {
            internal Reservation(int requested, int reserved)
            {
                Requested = requested;
                Reserved = reserved;
            }

            internal int Requested { get; }
            internal int Reserved { get; }
            internal bool IsEmpty => Reserved <= 0;
        }

        private struct TokenBucket
        {
            internal double Tokens;
            internal int LastSimulationStep;
        }

        internal static IReadOnlyDictionary<int, double> Limits => s_limits;

        internal static void LoadForSave(string? saveName)
        {
            s_limits.Clear();
            s_buckets.Clear();
            string safeName = SanitizeSaveName(saveName);
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsTweaks",
                "TransportFlowLimits",
                safeName.Length == 0 ? "current" : safeName);

            lock (s_persistenceGate)
            {
                s_stateFilePath = Path.Combine(directory, "state.txt");
            }

            string? path;
            lock (s_persistenceGate)
            {
                path = s_stateFilePath;
            }

            if (path is null || !File.Exists(path))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 || !string.Equals(lines[0], StateHeader, StringComparison.Ordinal))
                {
                    return;
                }

                foreach (string line in lines.Skip(1))
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length < 3 || !string.Equals(fields[0], "E", StringComparison.Ordinal) ||
                        !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entityId) ||
                        entityId < 0 ||
                        !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double limit) ||
                        !IsValidLimit(limit) || limit <= 0d)
                    {
                        continue;
                    }

                    s_limits[entityId] = limit;
                }
            }
            catch
            {
                // Optional policy metadata is fail-open. Native transport flow remains intact.
                s_limits.Clear();
                s_buckets.Clear();
            }
        }

        internal static void UnbindSave()
        {
            s_limits.Clear();
            s_buckets.Clear();
            lock (s_persistenceGate)
            {
                s_stateFilePath = null;
            }
        }

        internal static bool TryGetLimit(int entityId, out double unitsPerSimulationSecond) =>
            s_limits.TryGetValue(entityId, out unitsPerSimulationSecond) &&
            IsValidLimit(unitsPerSimulationSecond) &&
            unitsPerSimulationSecond > 0d;

        internal static bool TrySetLimit(int entityId, double unitsPerSimulationSecond)
        {
            if (entityId < 0 || !IsValidLimit(unitsPerSimulationSecond) || unitsPerSimulationSecond < 0d)
            {
                return false;
            }

            bool changed;
            if (unitsPerSimulationSecond <= 0d)
            {
                changed = s_limits.TryRemove(entityId, out _);
                s_buckets.Remove(entityId);
            }
            else
            {
                changed = !s_limits.TryGetValue(entityId, out double previous) ||
                          Math.Abs(previous - unitsPerSimulationSecond) > double.Epsilon;
                s_limits[entityId] = unitsPerSimulationSecond;
                // A setting change cannot inherit a bucket sized for a different rate. Keeping
                // only the new policy and starting the next sim call with a full bounded bucket
                // also makes reload/re-registration deterministic.
                if (changed)
                {
                    s_buckets.Remove(entityId);
                }
            }

            if (changed)
            {
                Persist();
            }

            return true;
        }

        internal static bool ClearLimit(int entityId)
        {
            bool changed = s_limits.TryRemove(entityId, out _);
            s_buckets.Remove(entityId);
            if (changed)
            {
                Persist();
            }

            return changed;
        }

        /// <summary>
        ///     Reserves an integer quantity from the configured bucket. The returned reservation
        ///     is completed after native acceptance so rejected product is refunded exactly.
        /// </summary>
        internal static bool TryReserve(
            int entityId,
            int requestedQuantity,
            int simulationStep,
            out int allowedQuantity,
            out Reservation reservation)
        {
            allowedQuantity = requestedQuantity;
            reservation = default;
            if (requestedQuantity <= 0 || !TryGetLimit(entityId, out double limit))
            {
                return false;
            }

            double capacity = BurstCapacity(limit);
            if (!s_buckets.TryGetValue(entityId, out TokenBucket bucket))
            {
                bucket = new TokenBucket { Tokens = capacity, LastSimulationStep = simulationStep };
            }
            else
            {
                Refill(ref bucket, limit, capacity, simulationStep);
            }

            int available = ToWholeQuantity(bucket.Tokens);
            allowedQuantity = Math.Min(requestedQuantity, available);
            if (allowedQuantity <= 0)
            {
                s_buckets[entityId] = bucket;
                return true;
            }

            bucket.Tokens -= allowedQuantity;
            if (bucket.Tokens < 0d || !IsFinite(bucket.Tokens))
            {
                bucket.Tokens = 0d;
            }
            s_buckets[entityId] = bucket;
            reservation = new Reservation(requestedQuantity, allowedQuantity);
            return true;
        }

        internal static void CompleteReservation(int entityId, Reservation reservation, int acceptedQuantity)
        {
            if (reservation.IsEmpty || !s_buckets.TryGetValue(entityId, out TokenBucket bucket))
            {
                return;
            }

            // The native receive seam returns the quantity it actually accepted. Refund the
            // reserved remainder (including a full refund when the native call accepted zero),
            // never the accepted quantity itself.
            int accepted = Math.Max(0, Math.Min(reservation.Reserved, acceptedQuantity));
            int rejected = reservation.Reserved - accepted;
            double tokens = bucket.Tokens + rejected;
            if (TryGetLimit(entityId, out double limit))
            {
                tokens = Math.Min(BurstCapacity(limit), tokens);
            }

            bucket.Tokens = IsFinite(tokens) ? Math.Max(0d, tokens) : 0d;
            s_buckets[entityId] = bucket;
        }

        internal static void RefundReservation(int entityId, Reservation reservation)
        {
            if (reservation.IsEmpty || !s_buckets.TryGetValue(entityId, out TokenBucket bucket))
            {
                return;
            }

            double tokens = bucket.Tokens + reservation.Reserved;
            if (TryGetLimit(entityId, out double limit))
            {
                tokens = Math.Min(BurstCapacity(limit), tokens);
            }

            bucket.Tokens = IsFinite(tokens) ? Math.Max(0d, tokens) : 0d;
            s_buckets[entityId] = bucket;
        }

        internal static void ClearRuntimeBuckets() => s_buckets.Clear();

        internal static void ResetForTests()
        {
            s_limits.Clear();
            s_buckets.Clear();
            lock (s_persistenceGate)
            {
                s_stateFilePath = null;
            }
        }

        private static void Refill(ref TokenBucket bucket, double limit, double capacity, int simulationStep)
        {
            long elapsed = (long)simulationStep - bucket.LastSimulationStep;
            bucket.LastSimulationStep = simulationStep;
            if (elapsed <= 0)
            {
                // A save/load or replay can move the simulation clock backwards. Never mint
                // tokens in that case; the next forward step resumes normal accrual.
                if (elapsed < 0)
                {
                    bucket.Tokens = Math.Min(capacity, Math.Max(0d, bucket.Tokens));
                }
                return;
            }

            double accrued = elapsed * (limit / SimulationStepsPerSecond);
            if (!IsFinite(accrued) || accrued < 0d)
            {
                return;
            }

            // Keep multiplication and accumulation bounded even if a replay jumps a very large
            // number of simulation steps.
            bucket.Tokens = Math.Min(capacity, Math.Max(0d, bucket.Tokens + Math.Min(capacity, accrued)));
        }

        private static double BurstCapacity(double limit) =>
            Math.Max(1d, Math.Min(MaxLimitUnitsPerSecond * MaxBurstSeconds, limit * MaxBurstSeconds));

        private static int ToWholeQuantity(double tokens)
        {
            if (!IsFinite(tokens) || tokens <= 0d)
            {
                return 0;
            }

            return tokens >= int.MaxValue ? int.MaxValue : (int)Math.Floor(tokens);
        }

        private static bool IsValidLimit(double value) =>
            IsFinite(value) && value >= 0d && value <= MaxLimitUnitsPerSecond;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static void Persist()
        {
            string? path;
            lock (s_persistenceGate)
            {
                path = s_stateFilePath;
            }

            if (path is null)
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new List<string> { StateHeader };
                foreach (KeyValuePair<int, double> pair in s_limits.OrderBy(pair => pair.Key))
                {
                    lines.Add(
                        "E\t" + pair.Key.ToString(CultureInfo.InvariantCulture) + "\t" +
                        pair.Value.ToString("R", CultureInfo.InvariantCulture));
                }

                string temporaryPath = path + ".tmp";
                File.WriteAllLines(temporaryPath, lines);
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            catch
            {
                // Policy persistence is best-effort and never blocks simulation.
            }
        }

        private static string SanitizeSaveName(string? value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }

            return result.Trim();
        }
    }
}
