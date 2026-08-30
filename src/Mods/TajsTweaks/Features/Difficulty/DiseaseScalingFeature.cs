// Taj's COI Mods | DiseaseScalingFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
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
        private static IReadOnlyList<int> s_thresholds = DiseaseScalingPolicy.VanillaThresholds;
        private static bool s_configured;

        internal static IReadOnlyList<int> Thresholds => s_thresholds;

        internal static void Reset()
        {
            s_configured = false;
            s_thresholds = DiseaseScalingPolicy.VanillaThresholds;
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
            s_thresholds = s_cache.GetOrCompute(
                DiseaseScalingPolicy.VanillaThresholds,
                mapSpan,
                mode,
                TajsTweaksRuntimeState.DiseaseScalingCustomFractions);
            s_configured = true;
        }

        internal static void Install(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(PopsHealthManager), "generateDisease");
            if (target is null || s_fleetField is null)
            {
                throw new MissingMethodException(typeof(PopsHealthManager).FullName, "generateDisease");
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(DiseaseScalingFeature), nameof(GenerateDiseasePostfix)));
        }

        private static void GenerateDiseasePostfix(PopsHealthManager __instance, ref Option<DiseaseProto> __result)
        {
            if (!s_configured || !__result.HasValue)
            {
                return;
            }

            DiseaseProto selected = __result.Value;
            int tier = FindTier(selected.MinDistanceTraveled);
            if (tier < 0 || tier >= s_thresholds.Count)
            {
                return;
            }

            int distance = ReadFarthestLocationVisited(__instance);
            if (!DiseaseScalingPolicy.IsEligible(distance, s_thresholds[tier]))
            {
                // Returning None is conservative when the vanilla candidate is no longer
                // eligible under a stricter scaled policy.  We never manufacture a replacement
                // prototype, and the next native disease attempt can select again later.
                __result = Option<DiseaseProto>.None;
            }
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

        private static int ReadFarthestLocationVisited(PopsHealthManager manager)
        {
            try
            {
                object? lazyFleet = s_fleetField!.GetValue(manager);
                object? fleet = lazyFleet?.GetType().GetProperty("ValueOrNull")?.GetValue(lazyFleet);
                object? distance = fleet?.GetType().GetProperty("FarthestLocationVisited")?.GetValue(fleet);
                return distance is int value ? value : Convert.ToInt32(distance ?? 0);
            }
            catch
            {
                return int.MaxValue;
            }
        }
    }
}
