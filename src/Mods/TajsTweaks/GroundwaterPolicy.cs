// Taj's COI Mods | GroundwaterPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Describes the extra groundwater policy owned by Taj's Tweaks. Vanilla leaves the
    ///     native weather-driven manager authoritative; the other modes only add missing stock.
    /// </summary>
    internal enum GroundwaterPolicy
    {
        Vanilla,
        Regenerate,
        MaintainMinimum,
        Infinite,
    }

    /// <summary>
    ///     Pure policy and capacity rules. Keeping these rules independent of the resolver makes
    ///     the boundary conditions testable without a game scene.
    /// </summary>
    internal static class GroundwaterPolicyRules
    {
        internal static GroundwaterPolicy Parse(string? value, bool legacyInfinite)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "regenerate" => GroundwaterPolicy.Regenerate,
                "maintain_minimum" or "maintain-minimum" or "maintainminimum" => GroundwaterPolicy.MaintainMinimum,
                "infinite" => GroundwaterPolicy.Infinite,
                "vanilla" => legacyInfinite ? GroundwaterPolicy.Infinite : GroundwaterPolicy.Vanilla,
                _ => legacyInfinite ? GroundwaterPolicy.Infinite : GroundwaterPolicy.Vanilla,
            };
        }

        internal static string ToSettingValue(GroundwaterPolicy policy) => policy switch
        {
            GroundwaterPolicy.Regenerate => "regenerate",
            GroundwaterPolicy.MaintainMinimum => "maintain_minimum",
            GroundwaterPolicy.Infinite => "infinite",
            _ => "vanilla",
        };

        /// <summary>
        ///     Computes one bounded refill operation. <paramref name="dailyAmount"/> is the
        ///     amount for this game-calendar day, already derived from the native configured
        ///     capacity. No operation can add more than the current native capacity allows.
        /// </summary>
        internal static int CalculateRefill(
            int current,
            int capacity,
            GroundwaterPolicy policy,
            int dailyAmount,
            int minimumPercent)
        {
            if (capacity <= 0)
            {
                return 0;
            }

            int safeCurrent = Math.Max(0, current);
            if (safeCurrent >= capacity)
            {
                return 0;
            }

            long missing = capacity - (long)safeCurrent;
            long target = policy switch
            {
                GroundwaterPolicy.Regenerate => Math.Max(0, dailyAmount),
                GroundwaterPolicy.MaintainMinimum =>
                    Math.Max(0, Math.Min(capacity, (long)capacity * Math.Min(100, Math.Max(0, minimumPercent)) / 100)) - safeCurrent,
                GroundwaterPolicy.Infinite => missing,
                _ => 0,
            };

            return (int)Math.Min(missing, Math.Max(0, target));
        }

        internal static bool UsesAutomaticCallback(GroundwaterPolicy policy) => policy != GroundwaterPolicy.Vanilla;

        internal static bool ShouldApplyAutomatic(
            GroundwaterPolicy policy,
            int? lastAppliedGameDay,
            int currentGameDay) =>
            UsesAutomaticCallback(policy) && (!lastAppliedGameDay.HasValue || lastAppliedGameDay.Value != currentGameDay);
    }
}
