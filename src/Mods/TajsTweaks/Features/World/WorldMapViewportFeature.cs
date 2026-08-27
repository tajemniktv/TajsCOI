// Taj's COI Mods | WorldMapViewportFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi.Core.World;
using Mafi.Unity.Ui.Library;

namespace TajsCOI.Tweaks.Features.World
{
    /// <summary>
    ///     Derives the viewport's lower zoom from the current map extents once per map view.
    ///     The vanilla PanAndZoom instance remains the owner of panning/clamping behavior.
    /// </summary>
    internal static class WorldMapViewportFeature
    {
        private static FieldInfo? s_minZoom;
        private static FieldInfo? s_maxZoom;
        private static PropertyInfo? s_mapProperty;
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            Type? mapViewType = Type.GetType("Mafi.Unity.Ui.World.WorldMapWindow+MapView, Mafi.Unity");
            ConstructorInfo? constructor = mapViewType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(item => item.GetParameters().Length == 6);
            if (mapViewType is null || constructor is null)
            {
                throw new MissingMethodException("WorldMapWindow.MapView.ctor");
            }
            s_mapProperty = AccessTools.Property(mapViewType, "Map");
            if (s_mapProperty is null)
            {
                throw new MissingMemberException("WorldMapWindow.MapView.Map");
            }
            s_minZoom = AccessTools.Field(typeof(PanAndZoom), "m_minZoom");
            s_maxZoom = AccessTools.Field(typeof(PanAndZoom), "m_maxZoom");
            if (s_minZoom is null || s_maxZoom is null)
            {
                throw new MissingMemberException("PanAndZoom zoom bounds");
            }
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(WorldMapViewportFeature), nameof(MapViewCreatedPostfix)));
            s_installed = true;
        }

        internal static void Reset()
        {
            s_installed = false;
            s_minZoom = null;
            s_maxZoom = null;
            s_mapProperty = null;
        }

        private static void MapViewCreatedPostfix(object __instance, WorldMapManager mapManager)
        {
            if (s_mapProperty is null || s_minZoom is null || s_maxZoom is null || mapManager is null)
            {
                return;
            }
            try
            {
                PanAndZoom? map = s_mapProperty.GetValue(__instance) as PanAndZoom;
                if (map is null)
                {
                    return;
                }
                float vanillaMin = Convert.ToSingle(s_minZoom.GetValue(map));
                float vanillaMax = Convert.ToSingle(s_maxZoom.GetValue(map));
                float mapExtent = Math.Max(mapManager.Map.Size.X, mapManager.Map.Size.Y);
                MapViewportBounds extents = new MapViewportBounds(0f, mapExtent, 0f, mapExtent);
                // 4096 is the ordinary-map baseline; larger maps get a proportional lower bound,
                // while ordinary maps retain their vanilla 0.5f floor.
                float derived = MapViewportMath.DeriveMinimumZoom(extents, 4096f, 4096f, vanillaMin);
                s_minZoom.SetValue(map, Math.Min(vanillaMax, derived));
            }
            catch
            {
                // The vanilla map view remains fully functional when this optional seam changes.
            }
        }
    }
}
