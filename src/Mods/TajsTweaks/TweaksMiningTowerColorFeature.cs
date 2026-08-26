// Taj's COI Mods | TweaksMiningTowerColorFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Settings;
using UnityEngine;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Adds the per-mine-tower palette selector from Tweaks++ to the native mine-tower
    ///     inspector and applies it to the native area outline renderer. Configuration is kept
    ///     in the normal Tajs settings store, bounded to 256 tower entries.
    /// </summary>
    internal static class TweaksMiningTowerColorFeature
    {
        private static readonly string[] s_names = { "Default", "Green", "Yellow", "Blue", "Orange", "Red", "Purple", "Cyan", "White" };

        private static readonly Color[] s_palette =
        {
            new(0f, 0f, 0f, 0f),
            new(0.31f, 0.86f, 0.31f, 1f),
            new(1f, 0.86f, 0.2f, 1f),
            new(0.31f, 0.55f, 1f, 1f),
            new(1f, 0.65f, 0f, 1f),
            new(1f, 0.31f, 0.31f, 1f),
            new(0.71f, 0.39f, 1f, 1f),
            new(0.2f, 0.85f, 0.9f, 1f),
            new(1f, 1f, 1f, 1f),
        };

        private static WeakReference<ITajsSettings>? s_settings;
        private static Type? s_rendererType;
        private static FieldInfo? s_towerField;
        private static FieldInfo? s_outlineField;
        private static FieldInfo? s_towersDataField;
        private static MethodInfo? s_updateTower;
        private static MethodInfo? s_renderUpdate;
        private static FieldInfo? s_entityField;
        private static PropertyInfo? s_entityProperty;
        private static FieldInfo? s_topRightButtons;
        private static FieldInfo? s_rendererField;
        private static readonly ConditionalWeakTable<object, DropdownState> s_dropdowns = new();
        private static string s_lastColorData = string.Empty;
        private static WeakReference<object>? s_lastRenderer;

        private sealed class DropdownState
        {
            internal Dropdown<int> Dropdown = null!;
        }

        internal static void Install(Harmony harmony, ITajsSettings settings)
        {
            s_settings = new WeakReference<ITajsSettings>(settings);
            InstallRendererHooks(harmony);
            InstallInspectorHooks(harmony);
        }

        private static void InstallRendererHooks(Harmony harmony)
        {
            s_rendererType = AccessTools.TypeByName("Mafi.Unity.Mine.TowerAreasRenderer")
                             ?? throw new TypeLoadException("Mafi.Unity.Mine.TowerAreasRenderer");
            Type nested = s_rendererType.GetNestedType("TowerAreaData", BindingFlags.Public | BindingFlags.NonPublic)
                          ?? throw new MissingMemberException(s_rendererType.FullName, "TowerAreaData");
            s_towerField = nested.GetField("Tower", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                           ?? throw new MissingFieldException(nested.FullName, "Tower");
            s_outlineField = nested.GetField("AreaOutline", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             ?? throw new MissingFieldException(nested.FullName, "AreaOutline");
            s_towersDataField = s_rendererType.GetField("m_towersData", BindingFlags.Instance | BindingFlags.NonPublic);
            if (s_towersDataField is null)
            {
                throw new MissingFieldException(s_rendererType.FullName, "m_towersData");
            }

            foreach (MethodInfo method in s_rendererType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "updateTower" && parameters.Length == 1 && parameters[0].ParameterType == nested)
                {
                    s_updateTower = method;
                }
            }
            if (s_updateTower is null)
            {
                throw new MissingMethodException(s_rendererType.FullName, "updateTower");
            }
            harmony.Patch(s_updateTower, postfix: new HarmonyMethod(typeof(TweaksMiningTowerColorFeature), nameof(UpdateTowerPostfix)));
            s_renderUpdate = AccessTools.Method(s_rendererType, "renderUpdate");
            if (s_renderUpdate is not null)
            {
                harmony.Patch(s_renderUpdate, postfix: new HarmonyMethod(typeof(TweaksMiningTowerColorFeature), nameof(RenderUpdatePostfix)));
            }
        }

        private static void InstallInspectorHooks(Harmony harmony)
        {
            Type inspector = typeof(PanelWithHeader).Assembly.GetTypes().FirstOrDefault(x => x.Name == "MineTowerInspector")
                             ?? throw new TypeLoadException("MineTowerInspector");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type? type = inspector; type is not null; type = type.BaseType)
            {
                s_entityProperty ??= type.GetProperty("Entity", flags);
                s_entityField ??= type.GetField("m_entity", flags);
                s_topRightButtons ??= type.GetField("TopRightButtons", flags);
                s_rendererField ??= type.GetField("m_towerAreasRenderer", flags);
            }
            if (s_entityProperty is null && s_entityField is null || s_topRightButtons is null)
            {
                throw new MissingMemberException(inspector.FullName, "Entity/TopRightButtons");
            }

            foreach (ConstructorInfo constructor in inspector.GetConstructors(flags))
            {
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TweaksMiningTowerColorFeature), nameof(InspectorCtorPostfix)));
            }
            MethodInfo? activated = FindNoArgMethod(inspector, "OnActivated", flags);
            if (activated is not null)
            {
                harmony.Patch(activated, postfix: new HarmonyMethod(typeof(TweaksMiningTowerColorFeature), nameof(InspectorActivatedPostfix)));
            }
        }

        private static MethodInfo? FindNoArgMethod(Type type, string name, BindingFlags flags)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                MethodInfo? method = current.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (method is not null)
                {
                    return method;
                }
            }
            return null;
        }

        private static void UpdateTowerPostfix(object __0) => ApplyToData(__0);

        private static void RenderUpdatePostfix(object __instance)
        {
            string current = TajsTweaksRuntimeState.ResourceTowerColors;
            bool rendererChanged = s_lastRenderer is null || !s_lastRenderer.TryGetTarget(out object? previous) || !ReferenceEquals(previous, __instance);
            if (rendererChanged || !string.Equals(current, s_lastColorData, StringComparison.Ordinal))
            {
                s_lastColorData = current;
                s_lastRenderer = new WeakReference<object>(__instance);
                ApplyAll(__instance);
            }
        }

        private static void InspectorCtorPostfix(object __instance)
        {
            try
            {
                if (__instance is null || s_topRightButtons?.GetValue(__instance) is not Row row || s_dropdowns.TryGetValue(__instance, out _))
                {
                    return;
                }
                var dropdown = new Dropdown<int>((index, _, _) =>
                {
                    var option = new Row(4.pt());
                    option.AlignItemsCenter();
                    option.Add(new Label(Localize(s_names[Mathf.Clamp(index, 0, s_names.Length - 1)])));
                    return option;
                });
                dropdown.SetOptions(Enumerable.Range(0, s_names.Length).ToArray());
                dropdown.OnValueChanged((index, _) => SetCurrentIndex(__instance, index));
                dropdown.SetValue(CurrentIndex(__instance));
                dropdown.Tooltip(Localize("Sets a personal color for this mine tower's area boundary."));
                row.InsertAt(0, dropdown);
                s_dropdowns.Add(__instance, new DropdownState { Dropdown = dropdown });
            }
            catch
            {
                // Inspector UI is optional; native tower controls remain usable.
            }
        }

        private static void InspectorActivatedPostfix(object __instance)
        {
            if (s_dropdowns.TryGetValue(__instance, out DropdownState? state))
            {
                state.Dropdown.SetValue(CurrentIndex(__instance));
            }
        }

        private static int CurrentIndex(object inspector)
        {
            return TryGetEntity(inspector, out IEntity? entity) && entity is not null &&
                   TajsTweaksRuntimeState.ParseTowerColors(TajsTweaksRuntimeState.ResourceTowerColors).TryGetValue(entity.Id.Value, out int index)
                ? index
                : 0;
        }

        private static void SetCurrentIndex(object inspector, int index)
        {
            if (!TryGetEntity(inspector, out IEntity? entity) || entity is null || s_settings is null ||
                !s_settings.TryGetTarget(out ITajsSettings? settings))
            {
                return;
            }
            Dictionary<int, int> colors = TajsTweaksRuntimeState.ParseTowerColors(TajsTweaksRuntimeState.ResourceTowerColors)
                .ToDictionary(x => x.Key, x => x.Value);
            if (index == 0)
            {
                colors.Remove(entity.Id.Value);
            }
            else
            {
                colors[entity.Id.Value] = Mathf.Clamp(index, 0, 8);
            }
            settings.TrySet(
                TajsTweaksSettingsCatalog.ModId,
                TajsTweaksSettingsCatalog.ResourceTowerColors,
                TajsTweaksRuntimeState.FormatTowerColors(colors));
            if (s_rendererField?.GetValue(inspector) is object renderer)
            {
                ApplyColorNow(renderer, entity.Id.Value);
            }
        }

        private static bool TryGetEntity(object inspector, out IEntity? entity)
        {
            entity = s_entityProperty?.GetValue(inspector) as IEntity ?? s_entityField?.GetValue(inspector) as IEntity;
            return entity is not null;
        }

        private static void ApplyToData(object? towerData)
        {
            if (towerData is null || s_towerField?.GetValue(towerData) is not IEntity entity ||
                s_outlineField?.GetValue(towerData) is not object outline)
            {
                return;
            }
            IReadOnlyDictionary<int, int> colors = TajsTweaksRuntimeState.ParseTowerColors(TajsTweaksRuntimeState.ResourceTowerColors);
            if (!colors.TryGetValue(entity.Id.Value, out int index) || index <= 0 || index >= s_palette.Length)
            {
                return;
            }
            AccessTools.Method(outline.GetType(), "SetColor", new[] { typeof(Color) })?.Invoke(outline, new object[] { s_palette[index] });
        }

        private static void ApplyAll(object renderer)
        {
            if (s_towersDataField?.GetValue(renderer) is not object data)
            {
                return;
            }
            PropertyInfo? count = data.GetType().GetProperty("Count");
            PropertyInfo? item = data.GetType().GetProperty("Item");
            if (count?.GetValue(data) is not int length || item is null)
            {
                return;
            }
            for (int index = 0; index < length; index++)
            {
                object? towerData = item.GetValue(data, new object[] { index });
                if (towerData is not null && s_towerField?.GetValue(towerData) is IEntity entity &&
                    TajsTweaksRuntimeState.ParseTowerColors(TajsTweaksRuntimeState.ResourceTowerColors).ContainsKey(entity.Id.Value))
                {
                    ApplyToData(towerData);
                }
                else if (towerData is not null && s_updateTower is not null)
                {
                    // Re-run the native update for entries whose override was removed.
                    s_updateTower.Invoke(renderer, new[] { towerData });
                }
            }
        }

        private static void ApplyColorNow(object renderer, int towerId) => ApplyAll(renderer);

        private static LocStrFormatted Localize(string text) =>
            LocalizationManager.CreateAlreadyLocalizedStr("TajsTweaksTowerColor_" + text.GetHashCode().ToString("X"), text).AsFormatted;
    }
}
