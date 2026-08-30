// Taj's COI Mods | RecipePickerLayoutFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    ///     Restricts presentation changes to RecipePicker. The native picker still owns recipe
    ///     enumeration, filtering, selection state, tooltips, and recipe-width calculation; this
    ///     feature only reapplies the resulting cards to a bounded column layout.
    /// </summary>
    internal static class RecipePickerLayoutFeature
    {
        private sealed class PickerState
        {
            internal bool Applied;
            internal float TileScale;
            internal double SpacingPoints;
            internal int Columns;
            internal int ScreenWidth;
            internal int ScreenHeight;
            internal float WindowWidth;
            internal float WindowHeight;
        }

        private static readonly object s_gate = new();
        private static readonly List<WeakReference<RecipePicker>> s_pickers = new();
        private static readonly ConditionalWeakTable<RecipePicker, PickerState> s_states = new();
        private static FieldInfo? s_recipesColumnField;

        internal static void Install(Harmony harmony)
        {
            Type type = typeof(RecipePicker);
            s_recipesColumnField = AccessTools.Field(type, "m_recipesColumn")
                                   ?? throw new MissingFieldException(type.FullName, "m_recipesColumn");
            MethodInfo attached = AccessTools.Method(type, "OnAttached", Type.EmptyTypes)
                                  ?? throw new MissingMethodException(type.FullName, "OnAttached");
            MethodInfo update = AccessTools.Method(type, "update", Type.EmptyTypes)
                                ?? throw new MissingMethodException(type.FullName, "update");
            if (attached.ReturnType != typeof(void) || update.ReturnType != typeof(Px))
            {
                throw new MissingMethodException(type.FullName, "RecipePicker layout methods changed");
            }

            harmony.Patch(
                attached,
                postfix: new HarmonyMethod(typeof(RecipePickerLayoutFeature), nameof(PickerAttachedPostfix)));
            harmony.Patch(
                update,
                postfix: new HarmonyMethod(typeof(RecipePickerLayoutFeature), nameof(PickerUpdatedPostfix)));
        }

        internal static void Tick()
        {
            WeakReference<RecipePicker>[] pickers;
            lock (s_gate)
            {
                s_pickers.RemoveAll(x => !x.TryGetTarget(out _));
                pickers = s_pickers.ToArray();
            }

            foreach (WeakReference<RecipePicker> reference in pickers)
            {
                if (!reference.TryGetTarget(out RecipePicker? picker) ||
                    !s_states.TryGetValue(picker, out PickerState? state))
                {
                    continue;
                }

                if (state.ScreenWidth != UnityEngine.Screen.width || state.ScreenHeight != UnityEngine.Screen.height ||
                    Math.Abs(state.WindowWidth - picker.ResolvedWidth) > 0.5f ||
                    Math.Abs(state.WindowHeight - picker.ResolvedHeight) > 0.5f)
                {
                    Apply(picker, force: true);
                }
            }
        }

        internal static void RefreshAll()
        {
            WeakReference<RecipePicker>[] pickers;
            lock (s_gate)
            {
                s_pickers.RemoveAll(x => !x.TryGetTarget(out _));
                pickers = s_pickers.ToArray();
            }

            foreach (WeakReference<RecipePicker> reference in pickers)
            {
                if (reference.TryGetTarget(out RecipePicker? picker))
                {
                    Apply(picker, force: true);
                }
            }
        }

        internal static void Reset()
        {
            lock (s_gate)
            {
                s_pickers.Clear();
            }
        }

        private static void PickerAttachedPostfix(RecipePicker __instance)
        {
            Track(__instance);
            Apply(__instance, force: true);
        }

        private static void PickerUpdatedPostfix(RecipePicker __instance)
        {
            Track(__instance);
            Apply(__instance, force: false);
        }

        private static void Track(RecipePicker picker)
        {
            lock (s_gate)
            {
                s_pickers.RemoveAll(x => !x.TryGetTarget(out _));
                if (!s_pickers.Any(x => x.TryGetTarget(out RecipePicker? value) && ReferenceEquals(value, picker)))
                {
                    s_pickers.Add(new WeakReference<RecipePicker>(picker));
                }
            }
        }

        private static void Apply(RecipePicker picker, bool force)
        {
            IReadOnlyList<SelectableStaticRecipeUi>? rowsForRecovery = null;
            try
            {
                if (s_recipesColumnField?.GetValue(picker) is not RecipesColumn recipesColumn)
                {
                    return;
                }

                RecipePickerLayoutPolicy policy = RecipePickerLayoutPolicy.Resolve(
                    TajsTweaksRuntimeState.RecipePickerDensity,
                    TajsTweaksRuntimeState.RecipePickerTileSize,
                    TajsTweaksRuntimeState.RecipePickerSpacing,
                    TajsTweaksRuntimeState.RecipePickerColumns);
                PickerState state;
                if (!s_states.TryGetValue(picker, out PickerState? existingState))
                {
                    state = new PickerState();
                    s_states.Add(picker, state);
                }
                else
                {
                    state = existingState!;
                }
                bool wrapped = recipesColumn.AllChildren.Any(child => child is Column column &&
                                                                      column.AllChildren.Any(nested => nested is SelectableStaticRecipeUi));
                if (policy.IsVanilla && !wrapped && !state.Applied)
                {
                    state.ScreenWidth = UnityEngine.Screen.width;
                    state.ScreenHeight = UnityEngine.Screen.height;
                    state.WindowWidth = picker.ResolvedWidth;
                    state.WindowHeight = picker.ResolvedHeight;
                    return;
                }
                if (!force && state.Applied && wrapped &&
                    Math.Abs(state.TileScale - policy.TileScale) < 0.0001f &&
                    Math.Abs(state.SpacingPoints - policy.SpacingPoints) < 0.0001 &&
                    state.Columns == policy.Columns)
                {
                    return;
                }

                List<SelectableStaticRecipeUi> rows = CollectRows(recipesColumn).ToList();
                rowsForRecovery = rows;
                if (rows.Count == 0)
                {
                    if (state.Applied)
                    {
                        recipesColumn.Direction(LayoutDirection.Column).Wrap(false).Gap(1.pt());
                        state.Applied = false;
                    }
                    return;
                }

                if (policy.IsVanilla)
                {
                    RestoreVanilla(recipesColumn, rows);
                    state.Applied = false;
                }
                else
                {
                    ApplyCompact(recipesColumn, rows, policy);
                    state.Applied = true;
                    state.TileScale = policy.TileScale;
                    state.SpacingPoints = policy.SpacingPoints;
                    state.Columns = policy.Columns;
                }

                state.ScreenWidth = UnityEngine.Screen.width;
                state.ScreenHeight = UnityEngine.Screen.height;
                state.WindowWidth = picker.ResolvedWidth;
                state.WindowHeight = picker.ResolvedHeight;
            }
            catch
            {
                // Presentation-only reflection must fail open and retain the native picker. If a
                // layout operation failed after detaching rows, put those same controls back into
                // the native column before returning.
                if (rowsForRecovery is not null)
                {
                    try
                    {
                        if (s_recipesColumnField?.GetValue(picker) is RecipesColumn recipesColumn)
                        {
                            RestoreVanilla(recipesColumn, rowsForRecovery);
                        }
                    }
                    catch
                    {
                        // A changed UI seam remains isolated to this optional feature.
                    }
                }
            }
        }

        private static IEnumerable<SelectableStaticRecipeUi> CollectRows(UiComponent component)
        {
            foreach (UiComponent child in component.AllChildren)
            {
                if (child is SelectableStaticRecipeUi recipe)
                {
                    yield return recipe;
                    continue;
                }

                // Keep the collector tolerant of harmless native wrapper changes while staying
                // scoped to this picker instance; no global product/grid component is patched.
                foreach (SelectableStaticRecipeUi nested in CollectRows(child))
                {
                    yield return nested;
                }
            }
        }

        private static void RestoreVanilla(RecipesColumn recipesColumn, IReadOnlyList<SelectableStaticRecipeUi> rows)
        {
            recipesColumn.Clear();
            recipesColumn.Direction(LayoutDirection.Column).Wrap(false).Gap(1.pt());
            foreach (SelectableStaticRecipeUi row in rows)
            {
                row.ScaleXy(1f).WidthAuto().NoShrink();
                recipesColumn.Add(row);
            }
            recipesColumn.FinalizeLayout();
        }

        private static void ApplyCompact(
            RecipesColumn recipesColumn,
            IReadOnlyList<SelectableStaticRecipeUi> rows,
            RecipePickerLayoutPolicy policy)
        {
            var spacing = new Px((float)policy.SpacingPoints * Px.POINTS_MULTIPLIER);
            Px nativeTileWidth = rows
                .Select(row => row.TotalWidth)
                .Aggregate(Px.Zero, (current, width) => current.Max(width));
            Px compactColumnWidth = nativeTileWidth * policy.TileScale;
            int columns = Math.Max(1, Math.Min(policy.Columns, rows.Count));
            int rowsPerColumn = (rows.Count + columns - 1) / columns;

            recipesColumn.Clear();
            recipesColumn.Direction(LayoutDirection.Row).Wrap(false).Gap(Px.Zero, Px.Zero);
            for (int columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                RecipesColumn column = new RecipesColumn(spacing).AlignItemsStretch().NoShrink().Width(compactColumnWidth);
                int start = columnIndex * rowsPerColumn;
                int end = Math.Min(rows.Count, start + rowsPerColumn);
                for (int rowIndex = start; rowIndex < end; rowIndex++)
                {
                    SelectableStaticRecipeUi row = rows[rowIndex];
                    // UI Toolkit transforms do not participate in flex measurement. Keep each
                    // row's native layout width, shrink its rendered/hit-test bounds once, and
                    // reserve the compact width on the containing column.
                    row.ScaleXy(policy.TileScale).Width(nativeTileWidth).NoShrink();
                    column.Add(row);
                }
                if (column.ChildrenCount > 0)
                {
                    column.FinalizeLayout();
                    if (columnIndex < columns - 1)
                    {
                        column.MarginRight(spacing);
                    }
                    recipesColumn.Add(column);
                }
            }
        }
    }
}
