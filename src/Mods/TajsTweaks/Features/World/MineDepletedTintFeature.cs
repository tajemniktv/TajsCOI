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
        private static bool s_enabled = true;
        private static readonly object s_gate = new();
        private static readonly List<WeakReference<object>> s_seenPins = new();

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
            s_enabled = TajsTweaksRuntimeState.MineDepletedTint;
            s_installed = true;
        }

        internal static void SetEnabled(bool enabled)
        {
            lock (s_gate)
            {
                s_enabled = enabled;
                ReconcilePins();
            }
        }

        internal static void Reset()
        {
            lock (s_gate)
            {
                s_enabled = false;
                ReconcilePins();
                s_seenPins.Clear();
            }
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

        internal static bool ShouldTint(bool enabled, bool ownedByPlayer, double? quantityAvailable) =>
            enabled && MineDepletionClassifier.IsDepleted(ownedByPlayer, quantityAvailable);

        private static void SetLocationColorPostfix(object __instance)
        {
            if (s_normalField is null || s_hoverField is null || s_locationProperty is null)
            {
                return;
            }
            try
            {
                lock (s_gate)
                {
                    TrackPin(__instance);
                    ApplyPin(__instance);
                }
            }
            catch
            {
                // Presentation is optional; never interfere with native pin updates.
            }
        }

        private static void ReconcilePins()
        {
            for (int index = s_seenPins.Count - 1; index >= 0; index--)
            {
                if (!s_seenPins[index].TryGetTarget(out object? pin))
                {
                    s_seenPins.RemoveAt(index);
                    continue;
                }
                try
                {
                    ApplyPin(pin);
                }
                catch
                {
                    // A pin can be torn down while a scene is changing; leave it for GC.
                }
            }
        }

        private static void TrackPin(object pin)
        {
            if (s_original.TryGetValue(pin, out _))
            {
                return;
            }
            // The conditional table is weak and gives duplicate detection without an O(n) scan
            // on every native color update; the list is only the bounded reconciliation index.
            s_original.GetValue(pin, _ => new OriginalColors());
            s_seenPins.Add(new WeakReference<object>(pin));
        }

        private static void ApplyPin(object pin)
        {
            if (s_normalField is null || s_hoverField is null || s_locationProperty is null)
            {
                return;
            }
            OriginalColors original = s_original.GetValue(pin, _ => new OriginalColors());
            object? normal = s_normalField.GetValue(pin);
            object? hover = s_hoverField.GetValue(pin);
            if (original.Normal is null)
            {
                original.Normal = normal;
                original.Hover = hover;
            }

            var location = s_locationProperty.GetValue(pin) as WorldMapLocation;
            WorldMapMine? mine = location?.Entity.HasValue == true ? location.Entity.Value as WorldMapMine : null;
            if (mine is null || !ShouldTint(
                    s_enabled,
                    mine.IsOwnedByPlayer,
                    mine.QuantityAvailable.HasValue ? mine.QuantityAvailable.Value.Value : (double?)null))
            {
                if (original.Normal is not null)
                {
                    s_normalField.SetValue(pin, original.Normal);
                    s_hoverField.SetValue(pin, original.Hover);
                }
                return;
            }
            // Keep the two values distinct so hover/selected state remains visible.
            s_normalField.SetValue(pin, new ColorRgba(0xC56A4DFFu));
            s_hoverField.SetValue(pin, new ColorRgba(0xF0A07AFFu));
        }
    }
}
