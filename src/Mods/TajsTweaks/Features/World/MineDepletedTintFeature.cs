// Taj's COI Mods | MineDepletedTintFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.World;
using Mafi.Core.World.Entities;
using Mafi.Unity.Ui.World;

namespace TajsCOI.Tweaks.Features.World
{
    /// <summary>Presentation-only tint for owned, known-depleted world mines.</summary>
    internal static class MineDepletedTintFeature
    {
        private sealed class OriginalColors
        {
            internal object? Normal;
            internal object? Hover;
        }

        private static readonly ConditionalWeakTable<object, OriginalColors> s_original = new();
        private static FieldInfo? s_normalField;
        private static FieldInfo? s_hoverField;
        private static PropertyInfo? s_locationProperty;
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            Type? pinType = typeof(WorldMapWindow).GetNestedType("LocationPin", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? colorMethod = pinType is null
                ? null
                : AccessTools.Method(pinType, "setLocationColor");
            if (pinType is null || colorMethod is null)
            {
                throw new MissingMethodException("WorldMapWindow.LocationPin.setLocationColor");
            }
            s_normalField = AccessTools.Field(pinType, "m_markerColor");
            s_hoverField = AccessTools.Field(pinType, "m_markerHoverColor");
            s_locationProperty = AccessTools.Property(pinType, "Location");
            if (s_normalField is null || s_hoverField is null || s_locationProperty is null)
            {
                throw new MissingMemberException("WorldMapWindow.LocationPin color/location fields");
            }
            harmony.Patch(colorMethod, postfix: new HarmonyMethod(typeof(MineDepletedTintFeature), nameof(SetLocationColorPostfix)));
            s_installed = true;
        }

        internal static void Reset()
        {
            s_installed = false;
            s_normalField = null;
            s_hoverField = null;
            s_locationProperty = null;
        }

        internal static bool IsDepleted(WorldMapMine mine)
        {
            return MineDepletionClassifier.IsDepleted(
                mine.IsOwnedByPlayer,
                mine.QuantityAvailable.HasValue ? mine.QuantityAvailable.Value.Value : (double?)null);
        }

        private static void SetLocationColorPostfix(object __instance)
        {
            if (s_normalField is null || s_hoverField is null || s_locationProperty is null)
            {
                return;
            }
            try
            {
                OriginalColors original = s_original.GetValue(__instance, _ => new OriginalColors());
                object? normal = s_normalField.GetValue(__instance);
                object? hover = s_hoverField.GetValue(__instance);
                if (original.Normal is null)
                {
                    original.Normal = normal;
                    original.Hover = hover;
                }
                WorldMapLocation? location = s_locationProperty.GetValue(__instance) as WorldMapLocation;
                WorldMapMine? mine = location?.Entity.HasValue == true ? location.Entity.Value as WorldMapMine : null;
                if (mine is null || !IsDepleted(mine))
                {
                    s_normalField.SetValue(__instance, original.Normal);
                    s_hoverField.SetValue(__instance, original.Hover);
                    return;
                }
                // Keep the two values distinct so hover/selected state remains visible.
                s_normalField.SetValue(__instance, new ColorRgba(0xC56A4DFFu));
                s_hoverField.SetValue(__instance, new ColorRgba(0xF0A07AFFu));
            }
            catch
            {
                // Presentation is optional; never interfere with native pin updates.
            }
        }
    }
}
