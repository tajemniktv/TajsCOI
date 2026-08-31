// Taj's COI Mods | TerrainDesignationPriorityFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Terrain.Designation;

namespace TajsCOI.Tweaks.Features.Terrain
{
    /// <summary>
    ///     Optional bounded preference over the native designation scorer. Native candidate
    ///     eligibility remains untouched and the setting is a no-op when disabled or unknown.
    /// </summary>
    internal static class TerrainDesignationPriorityFeature
    {
        private const string ScorerTypeName = "DesignationScorer";
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            MethodInfo target = FindScorePartialTarget()
                ?? throw new MissingMethodException(typeof(TerrainDesignationsManager).FullName, "DesignationScorer.ScorePartial");
            harmony.Patch(
                target,
                postfix: new HarmonyMethod(typeof(TerrainDesignationPriorityFeature), nameof(Postfix)));
            s_installed = true;
        }

        internal static void Reset() => s_installed = false;

        internal static MethodInfo? FindScorePartialTarget()
        {
            Type? scorerType = typeof(TerrainDesignationsManager).GetNestedType(ScorerTypeName, BindingFlags.NonPublic);
            return scorerType is null
                ? null
                : AccessTools.Method(scorerType, "ScorePartial", new[] { typeof(TerrainDesignation) });
        }

        private static void Postfix(TerrainDesignation designation, ref Fix32 __result)
        {
            try
            {
                TerrainWorkClass preferred = TerrainDesignationPriorityPolicy.Parse(TajsTweaksRuntimeState.TerrainDesignationPriority);
                if (preferred == TerrainWorkClass.Unknown)
                {
                    return;
                }

                int adjustment = TerrainDesignationPriorityPolicy.Adjustment(
                    enabled: true,
                    TerrainDesignationPriorityPolicy.Classify(designation),
                    preferred,
                    __result.ToIntFloored());
                if (adjustment > 0)
                {
                    __result -= (Fix32)adjustment;
                }
            }
            catch
            {
                // A changed scorer/prototype shape leaves the native score active.
            }
        }
    }
}
