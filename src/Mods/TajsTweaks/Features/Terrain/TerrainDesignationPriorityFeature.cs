// Taj's COI Mods | TerrainDesignationPriorityFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Products;
using Mafi.Core.Terrain.Designation;

namespace TajsCOI.Tweaks.Features.Terrain
{
    /// <summary>
    ///     Optional preference over the native ready-designation scorer. The native method remains
    ///     the sole eligibility/scoring authority for the selected class and vanilla is untouched
    ///     when the setting is off or the target changes.
    /// </summary>
    internal static class TerrainDesignationPriorityFeature
    {
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            MethodInfo target = AccessTools.Method(
                typeof(TerrainDesignationsManager),
                "TryFindBestReadyToFulfill",
                new[]
                {
                    typeof(IEnumerable<TerrainDesignation>),
                    typeof(Tile2i),
                    typeof(Vehicle),
                    typeof(TerrainDesignation).MakeByRefType(),
                    typeof(Option<LooseProductProto>),
                    typeof(bool),
                }) ?? throw new MissingMethodException(typeof(TerrainDesignationsManager).FullName, "TryFindBestReadyToFulfill");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TerrainDesignationPriorityFeature), nameof(Prefix)));
            s_installed = true;
        }

        internal static void Reset() => s_installed = false;

        private static void Prefix(ref IEnumerable<TerrainDesignation> designations)
        {
            TerrainWorkClass preferred = TerrainDesignationPriorityPolicy.Parse(TajsTweaksRuntimeState.TerrainDesignationPriority);
            if (preferred == TerrainWorkClass.Unknown)
            {
                return;
            }
            try
            {
                designations = TerrainDesignationPriorityPolicy.Prefer(designations, preferred);
            }
            catch
            {
                // A changed iterator/prototype shape leaves the native scorer active.
            }
        }
    }
}
