// Taj's COI Mods | TerrainDesignationPriorityPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Tweaks.Features.Terrain
{
    internal enum TerrainWorkClass
    {
        Unknown,
        Leveling,
        Digging,
        Filling,
        Other,
    }

    /// <summary>Bounded tie-band only; native candidate eligibility and score ordering remain authoritative.</summary>
    internal static class TerrainDesignationPriorityPolicy
    {
        internal const int MaxTieBand = 4;

        internal static int Adjustment(bool enabled, TerrainWorkClass candidate, TerrainWorkClass preferred, int nativeScore)
        {
            if (!enabled || candidate == TerrainWorkClass.Unknown || preferred == TerrainWorkClass.Unknown || candidate != preferred)
            {
                return 0;
            }
            int magnitude = Math.Min(MaxTieBand, Math.Max(0, Math.Abs(nativeScore) / 100));
            return magnitude == 0 ? 1 : magnitude;
        }

        internal static TerrainWorkClass ChooseNearbyClass(
            TerrainWorkClass current,
            bool sameClassEligible,
            bool anyEligible,
            TerrainWorkClass preferred)
        {
            if (current != TerrainWorkClass.Unknown && sameClassEligible)
            {
                return current;
            }
            return anyEligible ? preferred : TerrainWorkClass.Unknown;
        }
    }
}
