// Taj's COI Mods | TerrainDesignationPriorityPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi.Core;
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

        /// <summary>
        ///     Returns the number of score units to subtract from the native
        ///     lower-is-better score. The adjustment is deliberately bounded so
        ///     native distance, reservation, reachability, and product scoring
        ///     remain authoritative.
        /// </summary>
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

        internal static TerrainWorkClass Classify(TerrainDesignation? designation)
        {
            // ProtoId is the same identity the native mining/dumping managers compare.
            return ClassifyId(designation?.ProtoId.Value);
        }

        /// <summary>
        ///     Only the three native terraforming prototypes are recognized.
        ///     Modded or otherwise unknown IDs must not be guessed from their
        ///     names and accidentally receive a native priority.
        /// </summary>
        internal static TerrainWorkClass ClassifyId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return TerrainWorkClass.Unknown;
            }

            if (string.Equals(id, IdsCore.TerrainDesignators.LevelDesignator.Value, StringComparison.Ordinal))
            {
                return TerrainWorkClass.Leveling;
            }
            if (string.Equals(id, IdsCore.TerrainDesignators.MiningDesignator.Value, StringComparison.Ordinal))
            {
                return TerrainWorkClass.Digging;
            }
            if (string.Equals(id, IdsCore.TerrainDesignators.DumpingDesignator.Value, StringComparison.Ordinal))
            {
                return TerrainWorkClass.Filling;
            }

            return TerrainWorkClass.Other;
        }

        /// <summary>
        ///     Compatibility helper for callers that need stable class ordering. Every native
        ///     candidate is retained; native eligibility and scoring remain authoritative. The
        ///     active feature uses the narrower ScorePartial seam and does not materialize this list.
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
            var nonMatching = new List<TerrainDesignation>(all.Length);
            foreach (TerrainDesignation designation in all)
            {
                if (designation is not null && Classify(designation) == preferred)
                {
                    matching.Add(designation);
                }
                else
                {
                    nonMatching.Add(designation!);
                }
            }

            if (matching.Count == 0)
            {
                return all;
            }

            matching.AddRange(nonMatching);
            return matching;
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
