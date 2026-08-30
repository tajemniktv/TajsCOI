// Taj's COI Mods | DiseaseScalingFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Population;
using Mafi.Core.Terrain;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    /// <summary>
    /// Applies map-aware thresholds only to future native disease selection.  Custom-trigger
    /// diseases and the serialized CurrentDisease value are intentionally untouched.
    /// </summary>
    internal static class DiseaseScalingFeature
    {
        internal const string HarmonyId = "TajsCOI.Tweaks.Difficulty.DiseaseScaling";

        private static readonly DiseaseThresholdCache s_cache = new();
        private static readonly FieldInfo? s_fleetField = typeof(PopsHealthManager).GetField(
            "m_fleetManager", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_diseasesField = typeof(PopsHealthManager).GetField(
            "m_diseasesWithoutCustomTrigger", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_randomField = typeof(PopsHealthManager).GetField(
            "m_random", BindingFlags.Instance | BindingFlags.NonPublic);
        private static IReadOnlyList<int> s_thresholds = DiseaseScalingPolicy.VanillaThresholds;
        private static DiseaseScalingMode s_mode = DiseaseScalingMode.Vanilla;
        private static bool s_configured;
        private static bool s_installed;

        internal static IReadOnlyList<int> Thresholds => s_thresholds;

        internal static void Reset()
        {
            s_configured = false;
            s_thresholds = DiseaseScalingPolicy.VanillaThresholds;
            s_mode = DiseaseScalingMode.Vanilla;
            s_cache.Reset();
        }

        internal static void Configure(TerrainManager terrainManager)
        {
            if (s_configured || terrainManager is null)
            {
                return;
            }

            // Disease travel uses world-map coordinates.  TerrainSize is the actual selected
            // map configuration; using its largest axis is an explicit, bounded product policy
            // rather than silently assuming every map is the default 4096 square.
            RelTile2i size = terrainManager.TerrainSize;
            int mapSpan = Math.Max(size.X, size.Y);
            if (!DiseaseScalingPolicy.TryParseMode(TajsTweaksRuntimeState.DiseaseScalingPolicy, out DiseaseScalingMode mode))
            {
                mode = DiseaseScalingMode.Vanilla;
            }
            s_mode = mode;
            s_thresholds = s_cache.GetOrCompute(
                DiseaseScalingPolicy.VanillaThresholds,
                mapSpan,
                mode,
                TajsTweaksRuntimeState.DiseaseScalingCustomFractions);
            s_configured = true;
        }

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            MethodInfo? target = AccessTools.Method(typeof(PopsHealthManager), "generateDisease");
            if (target is null || s_fleetField is null || s_diseasesField is null || s_randomField is null)
            {
                throw new MissingMethodException(typeof(PopsHealthManager).FullName, "generateDisease");
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(DiseaseScalingFeature), nameof(GenerateDiseasePrefix)));
            s_installed = true;
        }

        private static bool GenerateDiseasePrefix(PopsHealthManager __instance, ref Option<DiseaseProto> __result)
        {
            if (!s_configured || s_mode == DiseaseScalingMode.Vanilla)
            {
                return true;
            }

            try
            {
                if (!TryReadFarthestLocationVisited(__instance, out int distance))
                {
                    return true;
                }
                if (s_diseasesField!.GetValue(__instance) is not IEnumerable<DiseaseProto> diseases ||
                    s_randomField!.GetValue(__instance) is not IRandom random)
                {
                    return true;
                }

                // Reproduce COI's native top-three-by-health-penalty selection, changing only
                // the distance predicate.  Existing DiseaseProto instances are reused; custom
                // trigger diseases are absent from this native list and remain untouched.
                DiseaseProto[] eligible = diseases
                    .Where(disease => IsEligible(disease, distance))
                    .OrderByDescending(disease => disease.HealthPenalty)
                    .Take(3)
                    .ToArray();
                __result = eligible.Length == 0
                    ? Option<DiseaseProto>.None
                    : eligible[random.NextInt(eligible.Length)];
                return false;
            }
            catch
            {
                // A changed private collection/random shape leaves native disease selection
                // active rather than risking a gameplay exception.
                return true;
            }
        }

        private static bool IsEligible(DiseaseProto disease, int distance)
        {
            int tier = FindTier(disease.MinDistanceTraveled);
            return tier < 0 || tier >= s_thresholds.Count || DiseaseScalingPolicy.IsEligible(distance, s_thresholds[tier]);
        }

        private static int FindTier(int vanillaDistance)
        {
            for (int index = 0; index < DiseaseScalingPolicy.VanillaThresholds.Length; index++)
            {
                if (DiseaseScalingPolicy.VanillaThresholds[index] == vanillaDistance)
                {
                    return index;
                }
            }
            return -1;
        }

        private static bool TryReadFarthestLocationVisited(PopsHealthManager manager, out int distance)
        {
            try
            {
                object? lazyFleet = s_fleetField!.GetValue(manager);
                object? fleet = lazyFleet?.GetType().GetProperty("ValueOrNull")?.GetValue(lazyFleet);
                object? value = fleet?.GetType().GetProperty("FarthestLocationVisited")?.GetValue(fleet);
                distance = value is int integer ? integer : Convert.ToInt32(value ?? 0);
                return true;
            }
            catch
            {
                distance = 0;
                return false;
            }
        }
    }
}
