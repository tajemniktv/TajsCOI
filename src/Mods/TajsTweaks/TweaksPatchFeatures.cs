// Taj's COI Mods | TweaksPatchFeatures.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Notifications;
using Mafi.Core.PathFinding.Goals;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Unity.Camera;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Controllers.LayoutEntityPlacing;
using Mafi.Unity.Ui.Hud;
using UnityEngine;
using UnityEngine.UIElements;

namespace TajsCOI.Tweaks
{
    internal static class TweaksLinePlacementFeature
    {
        private static FieldInfo? s_parentField;
        private static FieldInfo? s_shortcutsField;
        private static MethodInfo? s_axisMethod;

        internal static void Install(Harmony harmony)
        {
            Type helper = typeof(StaticEntityMassPlacer).GetNestedType("DragPlacementHelper", BindingFlags.NonPublic)
                          ?? throw new MissingMemberException(typeof(StaticEntityMassPlacer).FullName, "DragPlacementHelper");
            s_parentField = AccessTools.Field(helper, "m_parent");
            s_shortcutsField = AccessTools.Field(typeof(StaticEntityMassPlacer), "m_shortcutsManager");
            if (s_parentField is null || s_shortcutsField is null)
            {
                throw new MissingMemberException(helper.FullName, "placement shortcut fields");
            }
            MethodInfo allowed = AccessTools.Method(helper, "isDragStartAllowed")
                                 ?? throw new MissingMethodException(helper.FullName, "isDragStartAllowed");
            MethodInfo target = AccessTools.Method(helper, "computeAxisAlignedPositions")
                                ?? throw new MissingMethodException(helper.FullName, "computeAxisAlignedPositions");
            s_axisMethod = target;
            harmony.Patch(allowed, prefix: new HarmonyMethod(typeof(TweaksLinePlacementFeature), nameof(AllowConfiguredShortcut)));
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(TweaksLinePlacementFeature), nameof(TrimLinePositions)));
            PatchAlternatePlacementPath(harmony, helper, "computeFreeAnglePositions");
            PatchAlternatePlacementPath(harmony, helper, "computeGridPositions");
        }

        private static void PatchAlternatePlacementPath(Harmony harmony, Type helper, string name)
        {
            MethodInfo alternate = AccessTools.Method(helper, name)
                                   ?? throw new MissingMethodException(helper.FullName, name);
            harmony.Patch(
                alternate,
                prefix: new HarmonyMethod(typeof(TweaksLinePlacementFeature), nameof(ForceAxisAlignedPath)),
                postfix: new HarmonyMethod(typeof(TweaksLinePlacementFeature), nameof(TrimLinePositions)));
        }

        private static bool ForceAxisAlignedPath(object __instance, object[] __args)
        {
            if (!TajsTweaksRuntimeState.LinePlacement)
            {
                return true;
            }

            if (__args.Length != 3 ||
                __args[0] is not Tile3i start ||
                __args[1] is not Tile3i cursor ||
                __args[2] is not Lyst<Tile3i> results ||
                s_axisMethod is null)
            {
                // The supported 0.8.7b paths all have the same three argument types, but the
                // second parameter is named "end" on the free-angle path and "cursor" on the
                // other paths. __args deliberately binds by position so both remain compatible.
                return true;
            }

            s_axisMethod.Invoke(__instance, new object[] { start, cursor, results });
            return false;
        }

        private static bool AllowConfiguredShortcut(object __instance, ref bool __result)
        {
            if (!TajsTweaksRuntimeState.LinePlacement)
            {
                return true;
            }

            // The native helper remains responsible for the drag state machine, validation,
            // preview pooling and the final batch command. This gate only makes the opt-in
            // mode require its configured key; ordinary clicks continue through vanilla input.
            __result = false;
            if (TryParseShortcut(TajsTweaksRuntimeState.LinePlacementShortcut, out KeyCode shortcut) &&
                s_parentField?.GetValue(__instance) is StaticEntityMassPlacer parent &&
                s_shortcutsField?.GetValue(parent) is ShortcutsManager shortcuts)
            {
                __result = shortcuts.IsOn(KeyBindings.FromKey(KbCategory.DragPlacement, ShortcutMode.Game, shortcut));
            }
            return false;
        }

        private static bool TryParseShortcut(string? value, out KeyCode shortcut)
        {
            if (Enum.TryParse(value ?? string.Empty, ignoreCase: true, out shortcut) && shortcut != KeyCode.None)
            {
                return true;
            }
            shortcut = KeyCode.None;
            return false;
        }

        private static void TrimLinePositions(Lyst<Tile3i> results)
        {
            if (!TajsTweaksRuntimeState.LinePlacement)
            {
                return;
            }
            int maximum = Math.Max(1, TajsTweaksRuntimeState.LinePlacementLength);
            while (results.Count > maximum)
            {
                results.RemoveAt(results.Count - 1);
            }
        }
    }

    internal static class TweaksPinnedProductsFeature
    {
        private static readonly object s_gate = new();
        private static readonly List<WeakReference<object>> s_huds = new();
        private static FieldInfo? s_rowsField;
        private static FieldInfo? s_columnField;
        private static FieldInfo? s_childrenField;
        private static MethodInfo? s_clearMethod;
        private static MethodInfo? s_addMethod;
        private static MethodInfo? s_setColumnsMethod;
        private static MethodInfo? s_alternateBackgroundMethod;
        private static MethodInfo? s_getStoredMethod;
        private static MethodInfo? s_getCapacityMethod;

        private sealed class BarColorState
        {
            internal StyleColor Original;
            internal bool Captured;
        }

        private static readonly ConditionalWeakTable<VisualElement, BarColorState> s_barColors = new();
        private static bool s_wasBarColorsEnabled;

        internal static void Install(Harmony harmony)
        {
            Type type = typeof(PinnedProductsHud);
            s_rowsField = type.GetField("m_productRows", BindingFlags.Instance | BindingFlags.NonPublic);
            s_columnField = type.GetField("m_productsColumn", BindingFlags.Instance | BindingFlags.NonPublic);
            s_alternateBackgroundMethod = AccessTools.Method(type, "alternateChildrenBackground");
            Type? rowType = type.GetNestedType("PinnedProductRow", BindingFlags.Public | BindingFlags.NonPublic);
            s_getStoredMethod = rowType is null ? null : AccessTools.Method(rowType, "GetStoredQuantity");
            s_getCapacityMethod = rowType is null ? null : AccessTools.Method(rowType, "GetStorageCapacity");
            if (s_columnField is not null)
            {
                Type columnType = s_columnField.FieldType;
                s_childrenField = columnType.GetField("m_children", BindingFlags.Instance | BindingFlags.NonPublic);
                s_clearMethod = AccessTools.Method(columnType, "Clear", Type.EmptyTypes);
                s_setColumnsMethod = AccessTools.Method(columnType, "SetColumns", new[] { typeof(int) });
                s_addMethod = columnType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(x => x.Name == "Add" && x.GetParameters().Length == 1 &&
                                         x.GetParameters()[0].ParameterType.IsGenericType &&
                                         x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            }
            if (s_rowsField is null || s_columnField is null || s_childrenField is null || s_clearMethod is null ||
                s_addMethod is null || s_getStoredMethod is null || s_getCapacityMethod is null)
            {
                throw new MissingMemberException("PinnedProductsHud reflection surface changed.");
            }

            PatchIfPresent(harmony, AccessTools.Method(type, "validateLayout"));
            PatchIfPresent(harmony, AccessTools.Method(type, "onGeometryChanged"));
        }

        private static void PatchIfPresent(Harmony harmony, MethodInfo? method)
        {
            if (method is not null)
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(TweaksPinnedProductsFeature), nameof(RefreshPostfix)));
            }
        }

        private static void RefreshPostfix(object __instance)
        {
            lock (s_gate)
            {
                s_huds.RemoveAll(x => !x.TryGetTarget(out _));
                if (!s_huds.Any(x => x.TryGetTarget(out object? hud) && ReferenceEquals(hud, __instance)))
                {
                    s_huds.Add(new WeakReference<object>(__instance));
                }
            }
            Apply(__instance);
        }

        internal static void Tick()
        {
            WeakReference<object>[] huds;
            lock (s_gate)
            {
                s_huds.RemoveAll(x => !x.TryGetTarget(out _));
                huds = s_huds.ToArray();
            }
            foreach (WeakReference<object> reference in huds)
            {
                if (reference.TryGetTarget(out object? hud))
                {
                    Apply(hud);
                }
            }
        }

        private static void Apply(object hud)
        {
            if (!TajsTweaksRuntimeState.PinnedSort && !TajsTweaksRuntimeState.PinnedLowOnly && !TajsTweaksRuntimeState.PinnedCompact &&
                !TajsTweaksRuntimeState.PinnedBarColors && !s_wasBarColorsEnabled && TajsTweaksRuntimeState.PinnedColumns == 1)
            {
                return;
            }
            try
            {
                object? column = s_columnField?.GetValue(hud);
                if (column is null || s_rowsField?.GetValue(hud) is not IEnumerable rows)
                {
                    return;
                }

                s_setColumnsMethod?.Invoke(column, new object[] { Math.Max(1, Math.Min(4, TajsTweaksRuntimeState.PinnedColumns)) });

                var rowList = new List<object>();
                foreach (object row in rows)
                {
                    if (row is not null)
                    {
                        rowList.Add(row);
                        ApplyBarColor(row);
                    }
                }
                if (rowList.Count == 0)
                {
                    return;
                }

                IEnumerable<object> selected = rowList;
                if (TajsTweaksRuntimeState.PinnedLowOnly)
                {
                    selected = rowList.Where(x => GetFillPercent(x) <= TajsTweaksRuntimeState.PinnedLowThreshold)
                        .Take(Math.Max(1, TajsTweaksRuntimeState.PinnedLowLimit));
                }
                if (TajsTweaksRuntimeState.PinnedSort)
                {
                    bool ascending = string.Equals(TajsTweaksRuntimeState.PinnedSortDirection, "ascending", StringComparison.Ordinal);
                    List<object> sortable = selected.ToList();
                    Dictionary<object, long> values = sortable.ToDictionary(x => x, GetSortValue);
                    double hysteresis = Math.Max(0, TajsTweaksRuntimeState.PinnedHysteresisPercent) / 100d;
                    sortable.Sort((left, right) =>
                    {
                        long leftValue = values[left];
                        long rightValue = values[right];
                        long maximum = Math.Max(1, Math.Max(Math.Abs(leftValue), Math.Abs(rightValue)));
                        if (hysteresis > 0 && Math.Abs(leftValue - rightValue) <= maximum * hysteresis)
                        {
                            return rowList.IndexOf(left).CompareTo(rowList.IndexOf(right));
                        }
                        int result = leftValue.CompareTo(rightValue);
                        return ascending ? result : -result;
                    });
                    selected = sortable;
                }

                Type itemType = s_addMethod!.GetParameters()[0].ParameterType.GetGenericArguments()[0];
                var typed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
                foreach (object row in selected)
                {
                    if (itemType.IsInstanceOfType(row))
                    {
                        typed.Add(row);
                        ApplyCompactStyle(row);
                    }
                }
                s_clearMethod!.Invoke(column, null);
                s_addMethod.Invoke(column, new object[] { typed });
                s_alternateBackgroundMethod?.Invoke(hud, null);
                s_wasBarColorsEnabled = TajsTweaksRuntimeState.PinnedBarColors;
            }
            catch
            {
                // HUD reflection is presentation-only; a changed private field must restore vanilla UI.
            }
        }

        private static long GetSortValue(object row)
        {
            if (string.Equals(TajsTweaksRuntimeState.PinnedSortMode, "fill", StringComparison.Ordinal))
            {
                return GetFillPercent(row);
            }
            return ReadQuantityValue(s_getStoredMethod!.Invoke(row, null));
        }

        private static long GetFillPercent(object row)
        {
            long stored = ReadQuantityValue(s_getStoredMethod!.Invoke(row, null));
            long capacity = ReadQuantityValue(s_getCapacityMethod!.Invoke(row, null));
            return capacity <= 0 ? 0 : Math.Min(100, Math.Max(0, stored * 100 / capacity));
        }

        private static long ReadQuantityValue(object? quantity)
        {
            if (quantity is null)
            {
                return 0;
            }
            if (quantity is IConvertible convertible)
            {
                return convertible.ToInt64(System.Globalization.CultureInfo.InvariantCulture);
            }

            Type type = quantity.GetType();
            PropertyInfo? property = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? value = property?.GetValue(quantity);
            if (value is null)
            {
                FieldInfo? field = type.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                value = field?.GetValue(quantity);
            }
            if (value is IConvertible direct)
            {
                return direct.ToInt64(System.Globalization.CultureInfo.InvariantCulture);
            }
            return value is null || ReferenceEquals(value, quantity) ? 0 : ReadQuantityValue(value);
        }

        private static void ApplyCompactStyle(object row)
        {
            PropertyInfo? rootProperty = row.GetType().GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (rootProperty?.GetValue(row) is not VisualElement root)
            {
                return;
            }
            if (TajsTweaksRuntimeState.PinnedCompact)
            {
                // PinnedRowBase has 1 px top and 2 px bottom padding. Removing both
                // completely clips the chart/text on the compact layout in 0.8.7b;
                // retain a bounded five-pixel breathing room for the row content.
                root.style.paddingTop = 2f;
                root.style.paddingBottom = 3f;
            }
            else
            {
                root.style.paddingTop = StyleKeyword.Null;
                root.style.paddingBottom = StyleKeyword.Null;
            }
        }

        private static void ApplyBarColor(object row)
        {
            PropertyInfo? rootProperty = row.GetType().GetProperty("RootElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (rootProperty?.GetValue(row) is not VisualElement root)
            {
                return;
            }
            VisualElement? bar = FindBar(root, 0);
            if (bar is null)
            {
                return;
            }
            BarColorState state = s_barColors.GetValue(bar, _ => new BarColorState());
            if (!state.Captured)
            {
                state.Original = bar.style.backgroundColor;
                state.Captured = true;
            }
            if (!TajsTweaksRuntimeState.PinnedBarColors)
            {
                bar.style.backgroundColor = state.Original;
                return;
            }
            double fill = GetFillPercent(row);
            bar.style.backgroundColor = fill <= 20 ? new Color(0.8f, 0.12f, 0.1f, 1f) :
                fill <= 40 ? new Color(0.9f, 0.45f, 0.08f, 1f) :
                fill <= 60 ? new Color(0.9f, 0.78f, 0.12f, 1f) :
                new Color(0.3f, 0.75f, 0.28f, 1f);
        }

        private static VisualElement? FindBar(VisualElement? node, int depth)
        {
            if (node is null || depth > 8)
            {
                return null;
            }
            StyleLength height = node.style.height;
            if (height.keyword == StyleKeyword.Undefined && height.value.unit == LengthUnit.Pixel &&
                Math.Abs(height.value.value - 4f) < 0.5f && node.childCount > 0)
            {
                return node[0];
            }
            for (int index = 0; index < node.childCount; index++)
            {
                VisualElement? found = FindBar(node[index], depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }
            return null;
        }
    }

    internal static class TweaksCameraFeature
    {
        private static WeakReference<OrbitalCameraModel>? s_lastCamera;
        private static FieldInfo? s_minPivotField;
        private static FieldInfo? s_maxPivotField;
        private static MethodInfo? s_recomputePivotHeightMethod;

        internal static void Install(Harmony harmony)
        {
            Patch(harmony, AccessTools.Method(typeof(OrbitalCameraModel), "GetMinGroundClearance"), nameof(MinGroundClearancePostfix));
            Patch(harmony, AccessTools.Method(typeof(OrbitalCameraModel), "computePivotHeight"), nameof(PivotHeightPostfix));
            Patch(harmony, AccessTools.Method(typeof(OrbitalCameraModel), "SetMode"), nameof(SetModePostfix));
            Patch(harmony, AccessTools.Method(typeof(CameraController), "adjustEyePositionToAvoidTerrainClipping"), nameof(TerrainClipPostfix));
        }

        private static void Patch(Harmony harmony, MethodInfo? target, string postfix)
        {
            if (target is not null)
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(TweaksCameraFeature), postfix));
            }
        }

        private static void MinGroundClearancePostfix(ref HeightTilesF __result)
        {
            if (TajsTweaksRuntimeState.GroundClipping)
            {
                __result = new HeightTilesF(-500);
            }
            else if (TajsTweaksRuntimeState.FreeCamera)
            {
                __result = HeightTilesF.Zero;
            }
        }

        private static void PivotHeightPostfix(ref HeightTilesF __result)
        {
            if (TajsTweaksRuntimeState.GroundClipping)
            {
                __result = new HeightTilesF(__result.Value - 15);
            }
        }

        private static void TerrainClipPostfix(ref Vector3 __result, Vector3 eyePosition)
        {
            if (TajsTweaksRuntimeState.GroundClipping || TajsTweaksRuntimeState.FreeCamera)
            {
                // CameraController applies Mathf.Max(clearance, nearClip * 1.25f), so a
                // negative GetMinGroundClearance result alone can never permit clipping.
                __result = eyePosition;
            }
        }

        private static void SetModePostfix(OrbitalCameraModel __instance)
        {
            s_lastCamera = new WeakReference<OrbitalCameraModel>(__instance);
            ApplyZoom();
        }

        internal static void ApplyZoom()
        {
            if (s_lastCamera is null || !s_lastCamera.TryGetTarget(out OrbitalCameraModel? camera))
            {
                return;
            }
            try
            {
                s_minPivotField ??= typeof(OrbitalCameraModel).GetField("m_minPivotDistance", BindingFlags.Instance | BindingFlags.NonPublic);
                s_maxPivotField ??= typeof(OrbitalCameraModel).GetField("m_maxPivotDistance", BindingFlags.Instance | BindingFlags.NonPublic);
                if (s_minPivotField is null || s_maxPivotField is null)
                {
                    return;
                }
                s_minPivotField.SetValue(camera, new RelTile1f(TajsTweaksRuntimeState.UnlimitedZoom ? 1 : 16));
                s_maxPivotField.SetValue(camera, new RelTile1f(TajsTweaksRuntimeState.UnlimitedZoom ? 10000 : 400));
            }
            catch
            {
                // Camera state is restored by vanilla when this private seam is unavailable.
            }
        }

        internal static void RefreshGroundClipping()
        {
            if (s_lastCamera is null || !s_lastCamera.TryGetTarget(out OrbitalCameraModel? camera))
            {
                return;
            }

            try
            {
                s_recomputePivotHeightMethod ??= AccessTools.Method(typeof(OrbitalCameraModel), "recomputePivotHeight");
                s_recomputePivotHeightMethod?.Invoke(camera, null);
            }
            catch
            {
                // The camera will recompute its pivot on the next movement/mode change.
            }
        }
    }

    internal static class TweaksDesignationFeature
    {
        private static FieldInfo? s_upgradeAreaSelectionField;
        private static MethodInfo? s_upgradeSetEdgeSizeLimit;
        private static RelTile1i? s_upgradeVanillaLimit;
        private static MethodInfo? s_polygonMaxEdgeSizeSetter;

        private static readonly string[] s_types =
        {
            "Mafi.Unity.Ui.Controllers.Designations.TerrainDesignationController, Mafi.Unity",
            "Mafi.Unity.Ui.Controllers.DecalDesignationController, Mafi.Unity",
            "Mafi.Unity.Ui.Controllers.SurfaceDesignationController, Mafi.Unity",
            "Mafi.Unity.Ui.Controllers.Tools.AreaRemovalHandler, Mafi.Unity",
            "Mafi.Unity.Ui.Controllers.Tools.PropsRemovalInputController, Mafi.Unity",
            "Mafi.Unity.Ui.Controllers.Tools.FulfillDesignationsInputController, Mafi.Unity",
            "Mafi.Unity.Ui.Controllers.Tools.BaseEntityCursorInputController, Mafi.Unity",
            "Mafi.Unity.Ui.Blueprints.BlueprintCreationController, Mafi.Unity",
            "Mafi.Unity.Ui.Controllers.TreeHarvestingDesignatorController, Mafi.Unity",
        };

        internal static void Install(Harmony harmony)
        {
            foreach (string typeName in s_types)
            {
                var type = Type.GetType(typeName, false);
                if (type is null)
                {
                    continue;
                }

                FieldInfo[] fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(x => x.Name.Contains("MAX_AREA", StringComparison.Ordinal))
                    .ToArray();
                if (fields.Length == 0)
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(
                             BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                    {
                        continue;
                    }
                    if (UsesAreaLimit(method, fields))
                    {
                        harmony.Patch(method, transpiler: new HarmonyMethod(typeof(TweaksDesignationFeature), nameof(ReplaceLimits)));
                    }
                }
            }

            InstallUpgradeAreaSupport(harmony);
            InstallPolygonAreaSupport(harmony);

            var renderer = Type.GetType("Mafi.Unity.Terrain.Designation.TerrainDesignationsRenderer, Mafi.Unity", false);
            // Gate the renderer's single per-frame update rather than each private chunk. This
            // leaves the native draw path untouched when hiding is off and avoids depending on
            // the private nested chunk type, which changed shape between game builds.
            MethodInfo? render = renderer is null ? null : AccessTools.Method(renderer, "renderUpdate");
            if (render is not null)
            {
                harmony.Patch(render, prefix: new HarmonyMethod(typeof(TweaksDesignationFeature), nameof(RenderPrefix)));
            }

            InstallTweaksPlusPlusCompatibility(harmony);
        }

        private static void InstallUpgradeAreaSupport(Harmony harmony)
        {
            Type? controller = Type.GetType(
                "Mafi.Unity.Ui.Controllers.Tools.UpgradeToolInputController, Mafi.Unity",
                false);
            MethodInfo? activate = controller is null ? null : AccessTools.Method(controller, "Activate", Type.EmptyTypes);
            Type? baseType = controller?.BaseType;
            FieldInfo? areaSelection = FindField(baseType, "m_areaSelectionTool", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? setEdgeSizeLimit = baseType is null
                ? null
                : AccessTools.Method(baseType, "SetEdgeSizeLimit", new[] { typeof(RelTile1i) });
            FieldInfo? vanillaLimit = FindField(
                baseType,
                "MAX_AREA_EDGE_SIZE",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (activate is null || areaSelection is null || setEdgeSizeLimit is null ||
                vanillaLimit?.GetValue(null) is not RelTile1i limit)
            {
                // This is an optional version-specific extension. The existing designation
                // patches remain usable when the upgrade controller shape changes.
                return;
            }

            s_upgradeAreaSelectionField = areaSelection;
            s_upgradeSetEdgeSizeLimit = setEdgeSizeLimit;
            s_upgradeVanillaLimit = limit;
            harmony.Patch(
                activate,
                postfix: new HarmonyMethod(typeof(TweaksDesignationFeature), nameof(UpgradeActivatePostfix)));
        }

        private static void InstallPolygonAreaSupport(Harmony harmony)
        {
            Type? polygonState = Type.GetType("Mafi.Unity.Ui.Controllers.PolygonEditState, Mafi.Unity", false);
            MethodInfo? initialize = polygonState is null ? null : AccessTools.Method(polygonState, "Initialize");
            PropertyInfo? maxEdgeSize = polygonState?.GetProperty(
                "MaxEdgeSize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? setter = maxEdgeSize?.GetSetMethod(true);
            ParameterInfo[] setterParameters = setter?.GetParameters() ?? Array.Empty<ParameterInfo>();
            if (initialize is null || setter is null || setter.IsStatic || setter.ReturnType != typeof(void) ||
                setterParameters.Length != 1 || setterParameters[0].ParameterType != typeof(Fix32))
            {
                // Mine and forestry inspectors continue using their native polygon bounds if
                // this shared editor changes in a future game build.
                return;
            }

            s_polygonMaxEdgeSizeSetter = setter;
            harmony.Patch(
                initialize,
                postfix: new HarmonyMethod(typeof(TweaksDesignationFeature), nameof(PolygonInitializePostfix)));
        }

        private static FieldInfo? FindField(Type? type, string name, BindingFlags flags)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                FieldInfo? field = current.GetField(flags.HasFlag(BindingFlags.Static) ? name : name,
                    flags | BindingFlags.DeclaredOnly);
                if (field is not null)
                {
                    return field;
                }
            }
            return null;
        }

        private static void UpgradeActivatePostfix(object __instance)
        {
            if (s_upgradeAreaSelectionField is null || s_upgradeSetEdgeSizeLimit is null ||
                s_upgradeVanillaLimit is not RelTile1i vanillaLimit)
            {
                return;
            }

            try
            {
                object? areaSelection = s_upgradeAreaSelectionField.GetValue(__instance);
                if (areaSelection is null)
                {
                    return;
                }
                RelTile1i limit = TajsTweaksRuntimeState.DesignationControls
                    ? new RelTile1i(TajsTweaksRuntimeState.DesignationLimit)
                    : vanillaLimit;
                s_upgradeSetEdgeSizeLimit.Invoke(__instance, new object[] { limit });
            }
            catch
            {
                // A changed private UI seam must never prevent the vanilla upgrade tool from
                // activating. Its constructor already supplied the native limit.
            }
        }

        private static void PolygonInitializePostfix(object __instance)
        {
            if (!TajsTweaksRuntimeState.DesignationControls || s_polygonMaxEdgeSizeSetter is null)
            {
                return;
            }

            try
            {
                s_polygonMaxEdgeSizeSetter.Invoke(
                    __instance,
                    new object[] { Fix32.FromInt(TajsTweaksRuntimeState.DesignationLimit) });
            }
            catch
            {
                // The native polygon bounds remain in effect when the setter changes shape.
            }
        }

        internal static bool HasTweaksPlusPlusDesignationVisualIntegration()
        {
            try
            {
                Type? patchType = FindTweaksPlusPlusPatchType();
                return patchType is not null && AccessTools.Method(patchType, "RenderPrefix") is not null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool HasExpectedHarmonyOwner(string ownerId)
        {
            try
            {
                foreach (MethodBase original in Harmony.GetAllPatchedMethods())
                {
                    Patches? patches = Harmony.GetPatchInfo(original);
                    if (patches is null)
                    {
                        continue;
                    }

                    if (HasExpectedHarmonyOwner(patches.Prefixes, ownerId) ||
                        HasExpectedHarmonyOwner(patches.Postfixes, ownerId) ||
                        HasExpectedHarmonyOwner(patches.Transpilers, ownerId) ||
                        HasExpectedHarmonyOwner(patches.Finalizers, ownerId))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        private static void InstallTweaksPlusPlusCompatibility(Harmony harmony)
        {
            try
            {
                // Tweaks++ installs its own prefix on the same renderer and its persisted
                // DesignationsVisualEnabled value can otherwise veto this method entirely.
                // Patch that prefix's result instead of depending on Harmony prefix ordering:
                // any false prefix skips the original, regardless of which prefix runs first.
                Type? patchType = FindTweaksPlusPlusPatchType();
                MethodInfo? renderPrefix = patchType is null ? null : AccessTools.Method(patchType, "RenderPrefix");
                if (renderPrefix is not null)
                {
                    harmony.Patch(
                        renderPrefix,
                        postfix: new HarmonyMethod(typeof(TweaksDesignationFeature), nameof(TweaksPlusPlusRenderPostfix)));
                }
            }
            catch
            {
                // Tweaks++ is an optional compatibility seam. A missing or changed type must
                // not prevent the independent designation-area patches from installing.
            }
        }

        private static Type? FindTweaksPlusPlusPatchType() =>
            Type.GetType("TweaksPP.DesignationVisualPatch, Tweaks++", false) ??
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(x => string.Equals(x.GetName().Name, "Tweaks++", StringComparison.Ordinal))
                ?.GetType("TweaksPP.DesignationVisualPatch", false);

        private static bool HasExpectedHarmonyOwner(IEnumerable<Patch> patches, string ownerId) =>
            patches.Any(
                patch => string.Equals(patch.owner, ownerId, StringComparison.Ordinal) &&
                    patch.PatchMethod?.DeclaringType == typeof(TweaksDesignationFeature) &&
                    (string.Equals(patch.PatchMethod.Name, nameof(ReplaceLimits), StringComparison.Ordinal) ||
                     string.Equals(patch.PatchMethod.Name, nameof(RenderPrefix), StringComparison.Ordinal)));

        private static bool UsesAreaLimit(MethodInfo method, IReadOnlyCollection<FieldInfo> fields)
        {
            // ReSharper disable once RedundantArgumentDefaultValue
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method, null))
            {
                if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && fields.Contains(field))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<CodeInstruction> ReplaceLimits(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo rel = AccessTools.Method(typeof(TweaksDesignationFeature), nameof(GetRelLimit))!;
            MethodInfo integer = AccessTools.Method(typeof(TweaksDesignationFeature), nameof(GetIntLimit))!;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field.Name.Contains("MAX_AREA", StringComparison.Ordinal))
                {
                    yield return instruction;
                    if (field.FieldType == typeof(int))
                    {
                        yield return new CodeInstruction(OpCodes.Call, integer);
                    }
                    else if (field.FieldType == typeof(RelTile1i))
                    {
                        yield return new CodeInstruction(OpCodes.Call, rel);
                    }
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static RelTile1i GetRelLimit(RelTile1i vanilla) =>
            TajsTweaksRuntimeState.DesignationControls ? new RelTile1i(TajsTweaksRuntimeState.DesignationLimit) : vanilla;

        private static int GetIntLimit(int vanilla) =>
            TajsTweaksRuntimeState.DesignationControls ? TajsTweaksRuntimeState.DesignationLimit : vanilla;

        private static bool ShouldHideDesignations() =>
            TajsTweaksRuntimeState.DesignationControls && TajsTweaksRuntimeState.HideDesignations;

        private static bool RenderPrefix() => !ShouldHideDesignations();

        private static void TweaksPlusPlusRenderPostfix(ref bool __result)
        {
            if (!ShouldHideDesignations())
            {
                __result = true;
            }
        }
    }

    internal static class TweaksNotificationFeature
    {
        internal static void Install(Harmony harmony)
        {
            MethodInfo target =
                AccessTools.Method(
                    typeof(NotificationsManager),
                    "AddNotification",
                    new[] { typeof(NotificationProto), typeof(Option<IObjectWithTitle>), typeof(Option<object>) }) ??
                throw new MissingMethodException(typeof(NotificationsManager).FullName, "AddNotification");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TweaksNotificationFeature), nameof(AddPrefix)));
        }

        private static bool AddPrefix(NotificationProto? proto)
        {
            if (!TajsTweaksRuntimeState.NotificationFilter || proto is null)
            {
                return true;
            }
            string id = proto.Id.Value;
            return !TajsTweaksRuntimeState.IsNotificationMuted(id);
        }
    }

    internal static class TweaksBuildDefaultsFeature
    {
        private static FieldInfo? s_dumpableField;
        private static FieldInfo? s_warningField;

        internal static void Install(Harmony harmony)
        {
            MethodInfo? setDefault = AccessTools.Method(typeof(DefaultLogisticsModeManager), "SetDefault", new[] { typeof(IEntity) });
            MethodInfo? setIfMissing = AccessTools.Method(
                typeof(DefaultLogisticsModeManager),
                "SetDefaultIfNotSetInConfig",
                new[] { typeof(IEntity), typeof(EntityConfigData) });
            if (setDefault is null || setIfMissing is null)
            {
                throw new MissingMethodException(typeof(DefaultLogisticsModeManager).FullName, "SetDefault methods");
            }
            harmony.Patch(setDefault, postfix: new HarmonyMethod(typeof(TweaksBuildDefaultsFeature), nameof(DefaultPostfix)));
            harmony.Patch(setIfMissing, postfix: new HarmonyMethod(typeof(TweaksBuildDefaultsFeature), nameof(DefaultIfMissingPostfix)));

            ConstructorInfo? mineConstructor = typeof(MineTower).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.GetParameters().Any(p => p.ParameterType == typeof(ProtosDb)));
            if (mineConstructor is not null)
            {
                harmony.Patch(mineConstructor, postfix: new HarmonyMethod(typeof(TweaksBuildDefaultsFeature), nameof(MineTowerPostfix)));
            }
            s_dumpableField = typeof(MineTower).GetField("m_dumpableProducts", BindingFlags.Instance | BindingFlags.NonPublic);
            s_warningField = typeof(MineTower).GetField("m_productsToNotifyIfCannotGetRidOf", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static void DefaultPostfix(IEntity entity) => ApplyStorageMode(entity, null, isConfigAware: false);

        private static void DefaultIfMissingPostfix(IEntity entity, EntityConfigData configData) => ApplyStorageMode(entity, configData, isConfigAware: true);

        private static void ApplyStorageMode(IEntity entity, EntityConfigData? config, bool isConfigAware)
        {
            var simple = entity as IEntityWithSimpleLogisticsControl;
            var advanced = entity as IEntityWithLogisticsControl;
            if (simple is null && advanced is null)
            {
                return;
            }
            string mode = GetMode(entity);
            if (mode == "vanilla")
            {
                return;
            }
            bool setInput = !isConfigAware || !config!.LogisticsInputMode.HasValue;
            bool setOutput = !isConfigAware || !config!.LogisticsOutputMode.HasValue;
            bool importEnabled = mode == "import" || mode == "both";
            bool exportEnabled = mode == "export" || mode == "both";

            if (advanced is not null)
            {
                if (setInput && advanced.CanDisableLogisticsInput)
                {
                    advanced.SetLogisticsInputMode(importEnabled ? EntityLogisticsMode.On : EntityLogisticsMode.Off);
                }
                if (setOutput && advanced.CanDisableLogisticsOutput)
                {
                    advanced.SetLogisticsOutputMode(exportEnabled ? EntityLogisticsMode.On : EntityLogisticsMode.Off);
                }
            }
            else if (simple is not null)
            {
                if (setInput)
                {
                    simple.SetLogisticsInputDisabled(!importEnabled);
                }
                if (setOutput)
                {
                    simple.SetLogisticsOutputDisabled(!exportEnabled);
                }
            }
        }

        private static string GetMode(IEntity entity)
        {
            if (entity is not StorageBase storage)
            {
                return "vanilla";
            }
            return storage.Prototype is UnitStorageProto ? TajsTweaksRuntimeState.DefaultsUnit :
                storage.Prototype is FluidStorageProto ? TajsTweaksRuntimeState.DefaultsFluid :
                storage.Prototype is LooseStorageProto ? TajsTweaksRuntimeState.DefaultsLoose :
                TajsTweaksRuntimeState.DefaultsWarehouse;
        }

        private static void MineTowerPostfix(MineTower? __instance, ProtosDb? protosDb)
        {
            if (__instance is null || protosDb is null)
            {
                return;
            }
            ApplyProducts(__instance, protosDb, s_dumpableField, TajsTweaksRuntimeState.DefaultsMineDump);
            ApplyProducts(__instance, protosDb, s_warningField, TajsTweaksRuntimeState.DefaultsMineWarn);
        }

        private static void ApplyProducts(MineTower tower, ProtosDb protosDb, FieldInfo? field, string ids)
        {
            if (field is null || string.IsNullOrWhiteSpace(ids))
            {
                return;
            }
            object? set = field.GetValue(tower);
            MethodInfo? add = set?.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            if (set is null || add is null)
            {
                return;
            }
            foreach (string id in TajsTweaksRuntimeState.ParseIds(ids))
            {
                ProductProto? product = protosDb.All<ProductProto>().FirstOrDefault(x => x.Id.Value == id);
                if (product is not null)
                {
                    try
                    {
                        add.Invoke(set, new object[] { product });
                    }
                    catch (Exception)
                    {
                        // A changed private collection shape disables this optional product entry;
                        // leave the remaining vanilla/default behavior untouched.
                        break;
                    }
                }
            }
        }
    }

    internal static class TweaksMineTruckStagingFeature
    {
        private static WeakReference<DependencyResolver>? s_resolver;
        private static readonly Dictionary<int, float> s_nextChecks = new();

        internal static void SetResolver(DependencyResolver resolver) => s_resolver = new WeakReference<DependencyResolver>(resolver);

        internal static void Install(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(ParkAndWaitJobFactory), "TryEnqueueParkingJobIfNeeded")
                                ?? throw new MissingMethodException(typeof(ParkAndWaitJobFactory).FullName, "TryEnqueueParkingJobIfNeeded");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TweaksMineTruckStagingFeature), nameof(Prefix)));
        }

        private static void Prefix(Vehicle vehicle, ref ILayoutEntity staticEntity)
        {
            if (!TajsTweaksRuntimeState.StageMineTrucks || vehicle is not Truck truck || truck.Cargo.IsEmpty || staticEntity is not MineTower mine ||
                !TryAcquireCheck(truck.Id.Value, TajsTweaksRuntimeState.StageMineTrucksScan))
            {
                return;
            }
            try
            {
                OreSortingPlant? best = null;
                var bestDistance = Fix64.MaxValue;
                foreach (OreSortingPlant plant in mine.AssignedInputOreSorters)
                {
                    if (!IsReachable(truck, plant) || !plant.CanAcceptTruck(truck, out bool hadMatchingProducts, out _) || !hadMatchingProducts)
                    {
                        continue;
                    }
                    Fix64 distance = truck.Position2f.DistanceSqrTo(plant.Position2f);
                    if (distance < bestDistance)
                    {
                        best = plant;
                        bestDistance = distance;
                    }
                }
                if (best is not null)
                {
                    staticEntity = best;
                }
            }
            catch
            {
                // The ordinary mine parking target remains active when assignment data is unavailable.
            }
        }

        private static bool IsReachable(Truck truck, OreSortingPlant plant)
        {
            if (s_resolver is null || !s_resolver.TryGetTarget(out DependencyResolver? resolver) ||
                !resolver.TryResolve(out UnreachableTerrainDesignationsManager unreachables))
            {
                return false;
            }
            return !unreachables.HasUnreachableEntity(truck, plant);
        }

        private static bool TryAcquireCheck(int vehicleId, int periodSeconds)
        {
            float now = Time.realtimeSinceStartup;
            lock (s_nextChecks)
            {
                if (s_nextChecks.TryGetValue(vehicleId, out float next) && next > now)
                {
                    return false;
                }
                s_nextChecks[vehicleId] = now + Math.Max(2, periodSeconds);
                if (s_nextChecks.Count > 2048)
                {
                    s_nextChecks.Clear();
                }
                return true;
            }
        }
    }

    internal static class TweaksStuckTruckRecoveryFeature
    {
        private static WeakReference<DependencyResolver>? s_resolver;
        private static readonly Dictionary<int, float> s_nextChecks = new();
        private static readonly Dictionary<int, int> s_observations = new();
        private static readonly Dictionary<int, int> s_failures = new();

        internal static void Install(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(ParkAndWaitJobFactory), "TryEnqueueParkingJobIfNeeded")
                                ?? throw new MissingMethodException(typeof(ParkAndWaitJobFactory).FullName, "TryEnqueueParkingJobIfNeeded");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TweaksStuckTruckRecoveryFeature), nameof(Prefix)));
        }

        internal static void SetResolver(DependencyResolver resolver) => s_resolver = new WeakReference<DependencyResolver>(resolver);

        private static bool Prefix(Vehicle vehicle, ref ILayoutEntity staticEntity, ref bool __result)
        {
            if (!TajsTweaksRuntimeState.RecoverTrucks || vehicle is not Truck truck)
            {
                return true;
            }
            if (truck.AssignedTo.HasValue || truck.Cargo.IsEmpty || !truck.IsCannotDeliverNotificationActive)
            {
                Forget(truck.Id.Value);
                return true;
            }
            if (!TryAcquireCheck(truck.Id.Value, TajsTweaksRuntimeState.RecoverPeriod))
            {
                return true;
            }
            try
            {
                if (s_resolver is null || !s_resolver.TryGetTarget(out DependencyResolver? resolver) ||
                    !resolver.TryResolve(out IEntitiesManager entities) ||
                    !resolver.TryResolve(out VehicleGoalsFactory goals) ||
                    !resolver.TryResolve(out NavigateToJob.Factory navigation))
                {
                    BackOff(truck.Id.Value, TajsTweaksRuntimeState.RecoverPeriod);
                    return true;
                }
                if (TryQueueStorageRecovery(truck, resolver, out ILayoutEntity? storage))
                {
                    truck.DeactivateCannotDeliver();
                    staticEntity = storage!;
                    __result = true;
                    ClearFailure(truck.Id.Value);
                    return false;
                }
                Shipyard? shipyard = entities.GetAllEntitiesOfType<Shipyard>().FirstOrDefault();
                if (shipyard is null)
                {
                    BackOff(truck.Id.Value, TajsTweaksRuntimeState.RecoverPeriod);
                    return true;
                }
                truck.CancelAllJobsAndResetState();
                StaticEntityVehicleGoal goal = goals.CreateGoal(shipyard);
                navigation.EnqueueJob(truck, goal, navigateClosebyIsSufficient: true);
                truck.DeactivateCannotDeliver();
                staticEntity = shipyard;
                __result = true;
                ClearFailure(truck.Id.Value);
                return false;
            }
            catch
            {
                BackOff(truck.Id.Value, TajsTweaksRuntimeState.RecoverPeriod);
                return true;
            }
        }

        private static bool TryQueueStorageRecovery(Truck truck, DependencyResolver resolver, out ILayoutEntity? target)
        {
            target = null;
            if (!resolver.TryResolve(out IVehicleBuffersRegistry buffers) ||
                !resolver.TryResolve(out CargoDeliveryJob.Factory delivery) ||
                !resolver.TryResolve(out UnreachableTerrainDesignationsManager unreachables) ||
                !resolver.TryResolve(out IEntitiesManager entities))
            {
                return false;
            }

            var cargo = new Lyst<ProductQuantity>();
            truck.Cargo.GetCargoProducts(cargo);
            if (cargo.Count == 0)
            {
                return false;
            }
            RegisteredInputBuffer? primaryBuffer = null;
            ProductQuantity primaryQuantity = default;
            StorageBase? primaryStorage = null;
            var secondary = new Lyst<SecondaryInputBufferSpec>();
            foreach (ProductQuantity productQuantity in cargo)
            {
                StorageBase? bestStorage = null;
                RegisteredInputBuffer? bestBuffer = null;
                var bestDistance = Fix64.MaxValue;
                foreach (StorageBase storage in entities.GetAllEntitiesOfType<StorageBase>())
                {
                    if (storage.IsDestroyed || unreachables.HasUnreachableEntity(truck, storage))
                    {
                        continue;
                    }
                    Option<RegisteredInputBuffer> candidate = buffers.TryGetInputBuffer(storage, productQuantity.Product);
                    if (candidate.IsNone || !candidate.Value.IsEnabled || candidate.Value.RemainingCapacity.IsNotPositive)
                    {
                        continue;
                    }
                    Fix64 distance = truck.Position2f.DistanceSqrTo(storage.Position2f);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestStorage = storage;
                        bestBuffer = candidate.Value;
                    }
                }
                if (bestStorage is null || bestBuffer is null)
                {
                    continue;
                }
                Quantity amount = productQuantity.Quantity.Min(bestBuffer.RemainingCapacity);
                if (amount.IsNotPositive)
                {
                    continue;
                }
                ProductQuantity deliver = productQuantity.Product.WithQuantity(amount);
                if (primaryBuffer is null)
                {
                    primaryBuffer = bestBuffer;
                    primaryStorage = bestStorage;
                    primaryQuantity = deliver;
                }
                else
                {
                    secondary.Add(new SecondaryInputBufferSpec(bestBuffer, amount));
                }
            }
            if (primaryBuffer is null || primaryStorage is null)
            {
                return false;
            }
            truck.CancelAllJobsAndResetState();
            delivery.EnqueueJob(truck, primaryQuantity, primaryBuffer, secondary.IsEmpty ? null : secondary);
            target = primaryStorage;
            return true;
        }

        private static bool TryAcquireCheck(int vehicleId, int periodSeconds)
        {
            float now = Time.realtimeSinceStartup;
            lock (s_nextChecks)
            {
                if (s_nextChecks.TryGetValue(vehicleId, out float next) && next > now)
                {
                    return false;
                }
                s_nextChecks[vehicleId] = now + Math.Max(2, periodSeconds);
                int observations = s_observations.TryGetValue(vehicleId, out int previous) ? previous + 1 : 1;
                s_observations[vehicleId] = Math.Min(observations, 3);
                if (s_nextChecks.Count > 2048)
                {
                    s_nextChecks.Clear();
                    s_observations.Clear();
                    s_failures.Clear();
                }
                return observations >= 2;
            }
        }

        private static void BackOff(int vehicleId, int periodSeconds)
        {
            lock (s_nextChecks)
            {
                int failures = s_failures.TryGetValue(vehicleId, out int previous) ? Math.Min(previous + 1, 6) : 1;
                s_failures[vehicleId] = failures;
                double multiplier = Math.Pow(2, failures - 1);
                s_nextChecks[vehicleId] = Time.realtimeSinceStartup + (float)Math.Min(120, Math.Max(2, periodSeconds) * multiplier);
            }
        }

        private static void ClearFailure(int vehicleId)
        {
            lock (s_nextChecks)
            {
                s_failures.Remove(vehicleId);
            }
        }

        private static void Forget(int vehicleId)
        {
            lock (s_nextChecks)
            {
                s_nextChecks.Remove(vehicleId);
                s_observations.Remove(vehicleId);
                s_failures.Remove(vehicleId);
            }
        }
    }

    internal static class TweaksStorageFeature
    {
        private static readonly object s_gate = new();
        private static bool s_applied;

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            if (!TajsTweaksRuntimeState.StorageOverrides || s_applied || !resolver.TryResolve(out ProtosDb protosDb))
            {
                return;
            }
            IReadOnlyDictionary<string, double> perPrototype = TajsTweaksRuntimeState.GetStorageOverrides();
            foreach (StorageBaseProto proto in protosDb.All<StorageBaseProto>())
            {
                double capacityMultiplier = perPrototype.TryGetValue(proto.Id.Value, out double overrideMultiplier)
                    ? overrideMultiplier
                    : TajsTweaksRuntimeState.StorageMultiplier;
                Apply(proto, capacityMultiplier, TajsTweaksRuntimeState.StorageThroughputMultiplier);
            }
            lock (s_gate)
            {
                s_applied = true;
            }
        }

        private static void Apply(StorageBaseProto proto, double capacityMultiplier, double throughputMultiplier)
        {
            if (capacityMultiplier.Equals(1d) && throughputMultiplier.Equals(1d))
            {
                return;
            }
            int capacity = Scale(proto.Capacity.Value, capacityMultiplier);
            int transfer = Scale(proto.TransferLimit.Value, throughputMultiplier);
            FieldInfo? capacityField = typeof(StorageBaseProto).GetField("Capacity", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo? transferField = typeof(StorageBaseProto).GetField("TransferLimit", BindingFlags.Instance | BindingFlags.Public);
            capacityField?.SetValue(proto, new Quantity(capacity));
            transferField?.SetValue(proto, new Quantity(transfer));
            if (proto is StorageProto storage)
            {
                FieldInfo? throughput = typeof(StorageProto).GetField("<ThroughputPerTick>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (throughput is not null)
                {
                    int ticks = Math.Max(1, storage.TransferLimitDuration.Ticks);
                    throughput.SetValue(storage, new PartialQuantity(Math.Max(1, transfer / ticks)));
                }
            }
        }

        private static int Scale(int value, double multiplier)
        {
            double scaled = Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
            return (int)Math.Min(int.MaxValue, Math.Max(1, scaled));
        }
    }

    internal static class TweaksResourceOverlayFeature
    {
        private sealed class TowerLabelState
        {
            internal readonly List<GameObject> Objects = new();
        }

        private static readonly ConditionalWeakTable<Component, TowerLabelState> s_towerLabels = new();

        private static readonly Color[] s_towerColors =
        {
            new(0.25f, 0.75f, 1f, 0.85f),
            new(1f, 0.75f, 0.2f, 0.85f),
            new(0.55f, 1f, 0.35f, 0.85f),
            new(1f, 0.35f, 0.35f, 0.85f),
            new(0.8f, 0.45f, 1f, 0.85f),
        };

        private static FieldInfo? s_overlayDataField;
        private static PropertyInfo? s_overlayDataCount;
        private static PropertyInfo? s_overlayDataItem;
        private static FieldInfo? s_overlayTowerField;
        private static FieldInfo? s_overlayLineField;
        private static MethodInfo? s_lineSetColor;
        private static Type? s_textMeshType;

        internal static void Install(Harmony harmony)
        {
            var overlay = Type.GetType("Mafi.Unity.Mine.TowerAreaStatusOverlay, Mafi.Unity", false);
            MethodInfo? render = overlay is null ? null : AccessTools.Method(overlay, "renderUpdate");
            if (render is not null)
            {
                harmony.Patch(render, postfix: new HarmonyMethod(typeof(TweaksResourceOverlayFeature), nameof(RenderPostfix)));
                Type? data = overlay!.GetNestedType("TowerOverlayData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                s_overlayDataField = overlay.GetField("m_overlayData", BindingFlags.Instance | BindingFlags.NonPublic);
                s_overlayTowerField = data?.GetField("Tower", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                s_overlayLineField = data?.GetField("Line", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (s_overlayDataField is not null)
                {
                    s_overlayDataCount = s_overlayDataField.FieldType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                    s_overlayDataItem = s_overlayDataField.FieldType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
                }
                s_lineSetColor = Type.GetType("Mafi.Unity.LineMb, Mafi.Unity", false)?.GetMethod("SetColor", new[] { typeof(Color) });
            }

            // Deposit labels are owned by TweaksResourceDepositFeature. Keeping them outside
            // ResVisBarsMb avoids one label per sampled bar and lets the cluster cache reuse
            // unaffected regions when the native renderer applies dirty chunks.
        }

        private static void RenderPostfix(object __instance)
        {
            if (!TajsTweaksRuntimeState.ResourceOverlay || !TajsTweaksRuntimeState.ResourceOverlayTowerAreas ||
                s_overlayDataField is null || s_overlayDataCount is null || s_overlayDataItem is null ||
                s_overlayTowerField is null || s_overlayLineField is null || s_lineSetColor is null)
            {
                if (__instance is Component disabledOverlay)
                {
                    ClearTowerLabels(disabledOverlay);
                }
                return;
            }
            try
            {
                object? data = s_overlayDataField.GetValue(__instance);
                if (data is null || s_overlayDataCount.GetValue(data) is not int count || s_overlayDataItem.GetMethod is null)
                {
                    return;
                }
                TowerLabelState? labels = __instance is Component overlayComponent
                    ? s_towerLabels.GetValue(overlayComponent, _ => new TowerLabelState())
                    : null;
                s_textMeshType ??= Type.GetType("UnityEngine.TextMesh, UnityEngine.TextRenderingModule", false);
                int labelIndex = 0;
                for (int index = 0; index < count; index++)
                {
                    object? item = s_overlayDataItem.GetValue(data, new object[] { index });
                    object? tower = item is null ? null : s_overlayTowerField.GetValue(item);
                    object? line = item is null ? null : s_overlayLineField.GetValue(item);
                    if (tower is null || line is null)
                    {
                        continue;
                    }
                    int id = tower.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(tower) is Mafi.Core.EntityId entityId
                        ? entityId.Value
                        : index;
                    s_lineSetColor.Invoke(line, new object[] { s_towerColors[Math.Abs(id) % s_towerColors.Length] });
                    if (TajsTweaksRuntimeState.ResourceOverlayTowerLabels && labels is not null && s_textMeshType is not null &&
                        line is Component lineComponent)
                    {
                        GameObject label = GetOrCreateLabel(labels, labelIndex++);
                        if (__instance is Component parent)
                        {
                            label.transform.SetParent(parent.transform, true);
                        }
                        label.transform.position = lineComponent.transform.position + Vector3.up * (float)TajsTweaksRuntimeState.ResourceOverlayLabelHeight;
                        SetLabelText(label, "tower " + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                if (labels is not null)
                {
                    TrimLabels(labels, labelIndex);
                }
            }
            catch
            {
                // The native tower overlay remains authoritative if its private list changes.
            }
        }

        private static void SetTextProperty(Component component, string name, object value)
        {
            PropertyInfo? property = component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.CanWrite == true)
            {
                property.SetValue(component, value);
            }
        }

        private static void ClearTowerLabels(Component overlay)
        {
            if (!s_towerLabels.TryGetValue(overlay, out TowerLabelState? state))
            {
                return;
            }
            TrimLabels(state, 0);
        }

        private static GameObject GetOrCreateLabel(TowerLabelState state, int index)
        {
            while (state.Objects.Count <= index)
            {
                var label = new GameObject("Tajs mining tower label");
                label.transform.localScale = Vector3.one;
                Component text = label.AddComponent(s_textMeshType ??= Type.GetType("UnityEngine.TextMesh, UnityEngine.TextRenderingModule", false)!);
                SetTextProperty(text, "characterSize", 0.08f * Mathf.Clamp(TajsTweaksRuntimeState.ResourceOverlayLabelScale, 50, 200) / 100f);
                SetTextProperty(text, "fontSize", 24);
                SetTextProperty(text, "color", WithAlpha(Color.white));
                state.Objects.Add(label);
            }
            return state.Objects[index];
        }

        private static void SetLabelText(GameObject label, string text)
        {
            if (label.GetComponent(s_textMeshType ??= Type.GetType("UnityEngine.TextMesh, UnityEngine.TextRenderingModule", false)!) is Component component)
            {
                SetTextProperty(component, "text", text);
                SetTextProperty(component, "characterSize", 0.08f * Mathf.Clamp(TajsTweaksRuntimeState.ResourceOverlayLabelScale, 50, 200) / 100f);
                SetTextProperty(component, "color", WithAlpha(Color.white));
                Camera? camera = Camera.main;
                if (camera is not null)
                {
                    label.transform.rotation = camera.transform.rotation;
                }
            }
        }

        private static void TrimLabels(TowerLabelState state, int count)
        {
            for (int index = state.Objects.Count - 1; index >= count; index--)
            {
                if (state.Objects[index] is not null)
                {
                    UnityEngine.Object.Destroy(state.Objects[index]);
                }
                state.Objects.RemoveAt(index);
            }
        }

        private static Color WithAlpha(Color color)
        {
            color.a = Mathf.Clamp(TajsTweaksRuntimeState.ResourceOverlayLabelAlpha, 0, 100) / 100f;
            return color;
        }
    }
}
