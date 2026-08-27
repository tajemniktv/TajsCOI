// Taj's COI Mods | TerrainDesignationPriorityPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi.Core.Terrain.Designation;

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

        internal static TerrainWorkClass Classify(TerrainDesignation designation)
        {
            string id = designation?.Prototype?.Id.Value ?? string.Empty;
            if (id.IndexOf("level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("flatten", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return TerrainWorkClass.Leveling;
            }
            if (id.IndexOf("min", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("dig", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return TerrainWorkClass.Digging;
            }
            if (id.IndexOf("dump", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("fill", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return TerrainWorkClass.Filling;
            }
            return TerrainWorkClass.Other;
        }

        /// <summary>
        ///     Materializes the native ready cache once, retaining native eligibility and scoring
        ///     for the selected class. No second enumeration of the caller's cache is performed.
        /// </summary>
        internal static IReadOnlyList<TerrainDesignation> Prefer(
            IEnumerable<TerrainDesignation> designations,
            TerrainWorkClass preferred)
        {
            TerrainDesignation[] all = (designations ?? Enumerable.Empty<TerrainDesignation>()).ToArray();
            if (preferred == TerrainWorkClass.Unknown || all.Length == 0)
            {
                return all;
            }

            var matching = new List<TerrainDesignation>(all.Length);
            foreach (TerrainDesignation designation in all)
            {
                if (designation is not null && Classify(designation) == preferred)
                {
                    matching.Add(designation);
                }
            }
            return matching.Count == 0 ? all : matching;
        }

        internal static TerrainWorkClass Parse(string? value)
        {
            if (string.Equals(value, "leveling_first", StringComparison.OrdinalIgnoreCase))
            {
                return TerrainWorkClass.Leveling;
            }
            if (string.Equals(value, "digging_first", StringComparison.OrdinalIgnoreCase))
            {
                return TerrainWorkClass.Digging;
            }
            if (string.Equals(value, "filling_first", StringComparison.OrdinalIgnoreCase))
            {
                return TerrainWorkClass.Filling;
            }
            return TerrainWorkClass.Unknown;
        }
    }
}
