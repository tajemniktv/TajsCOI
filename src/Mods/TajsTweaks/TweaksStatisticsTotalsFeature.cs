// Taj's COI Mods | TweaksStatisticsTotalsFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Factory.ComputingPower;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Utils;
using Mafi.Localization;
using Mafi.Unity.Ui.Statistics;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using UnityEngine.UIElements;
using Label = Mafi.Unity.UiToolkit.Library.Label;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Adds current/maximum electricity and computing totals to the native statistics
    ///     breakdown. The aggregation runs only when vanilla refreshes that view and preserves
    ///     the native search filter.
    /// </summary>
    internal static class TweaksStatisticsTotalsFeature
    {
        private sealed class Widget
        {
            internal Row Container = null!;
            internal Mafi.Unity.Ui.Library.ProgressBar Bar = null!;
            internal Mafi.Unity.Ui.Library.Display Value = null!;
            internal Mafi.Unity.Ui.Library.Display Max = null!;
        }

        private static readonly Dictionary<object, Widget> s_electricityWidgets = new();
        private static readonly Dictionary<object, Widget> s_computingWidgets = new();
        private static FieldInfo? s_electricityShowConsumption;
        private static FieldInfo? s_electricityManager;
        private static FieldInfo? s_computingShowConsumption;
        private static FieldInfo? s_computingManager;
        private static PropertyInfo? s_searchText;
        private static FieldInfo? s_searchBar;
        private static FieldInfo? s_barField;

        private static readonly Color s_greenTop = new(0.28f, 0.72f, 0.38f, 1f);
        private static readonly Color s_greenBottom = new(0.12f, 0.42f, 0.2f, 1f);
        private static readonly Color s_yellowTop = new(0.95f, 0.85f, 0.2f, 1f);
        private static readonly Color s_yellowBottom = new(0.55f, 0.48f, 0.08f, 1f);
        private static readonly Color s_orangeTop = new(0.95f, 0.55f, 0.15f, 1f);
        private static readonly Color s_orangeBottom = new(0.55f, 0.28f, 0.05f, 1f);
        private static readonly Color s_redTop = new(0.9f, 0.3f, 0.25f, 1f);
        private static readonly Color s_redBottom = new(0.5f, 0.12f, 0.1f, 1f);
        private static readonly Dictionary<int, Texture2D> s_gradientCache = new();

        internal static void Install(Harmony harmony)
        {
            Type electricityView = typeof(ElectricityStatisticsTab).GetNestedType("BreakdownView", BindingFlags.NonPublic)
                                       ?.GetNestedType("BreakdownListView", BindingFlags.NonPublic)
                                   ?? throw new TypeLoadException("ElectricityStatisticsTab.BreakdownView.BreakdownListView");
            Type computingView = typeof(ComputingStatisticsTab).GetNestedType("BreakdownView", BindingFlags.NonPublic)
                                     ?.GetNestedType("BreakdownListView", BindingFlags.NonPublic)
                                 ?? throw new TypeLoadException("ComputingStatisticsTab.BreakdownView.BreakdownListView");

            s_electricityShowConsumption = electricityView.GetField("m_showConsumption", BindingFlags.Instance | BindingFlags.NonPublic);
            s_electricityManager = electricityView.GetField("m_electricityManager", BindingFlags.Instance | BindingFlags.NonPublic);
            s_computingShowConsumption = computingView.GetField("m_showConsumption", BindingFlags.Instance | BindingFlags.NonPublic);
            s_computingManager = computingView.GetField("m_computingManager", BindingFlags.Instance | BindingFlags.NonPublic);
            s_searchText = typeof(StatisticsBreakdownListView).GetProperty("SearchText", BindingFlags.Instance | BindingFlags.NonPublic);
            s_searchBar = typeof(StatisticsBreakdownListView).GetField("SearchBar", BindingFlags.Instance | BindingFlags.NonPublic);
            s_barField = typeof(Mafi.Unity.Ui.Library.ProgressBar).GetField("m_bar", BindingFlags.Instance | BindingFlags.NonPublic);
            if (s_electricityShowConsumption is null || s_electricityManager is null || s_computingShowConsumption is null ||
                s_computingManager is null || s_searchBar is null || s_barField is null)
            {
                throw new MissingMemberException("Native statistics totals fields");
            }

            Patch(harmony, electricityView, "OnAddView", nameof(ElectricityAddPostfix));
            Patch(harmony, electricityView, "OnRemoveView", nameof(ElectricityRemovePostfix));
            Patch(harmony, electricityView, "UpdateData", nameof(ElectricityUpdatePostfix));
            Patch(harmony, computingView, "OnAddView", nameof(ComputingAddPostfix));
            Patch(harmony, computingView, "OnRemoveView", nameof(ComputingRemovePostfix));
            Patch(harmony, computingView, "UpdateData", nameof(ComputingUpdatePostfix));
        }

        private static void Patch(Harmony harmony, Type type, string methodName, string postfixName)
        {
            MethodInfo method = AccessTools.Method(type, methodName) ?? throw new MissingMethodException(type.FullName, methodName);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(TweaksStatisticsTotalsFeature), postfixName));
        }

        private static Widget? EnsureWidget(object instance, Dictionary<object, Widget> store)
        {
            if (!TajsTweaksRuntimeState.ElectricityComputingTotals)
            {
                RemoveWidget(instance, store);
                return null;
            }
            if (store.TryGetValue(instance, out Widget? existing))
            {
                return existing;
            }
            if (s_searchBar?.GetValue(instance) is not Row row)
            {
                return null;
            }
            Widget widget = BuildWidget();
            row.InsertAt(0, widget.Container);
            store[instance] = widget;
            return widget;
        }

        private static Widget BuildWidget()
        {
            var bar = new Mafi.Unity.Ui.Library.ProgressBar();
            bar.Height(14.px()).Width(160.px());
            var value = new Mafi.Unity.Ui.Library.Display();
            var max = new Mafi.Unity.Ui.Library.Display();
            var row = new Row(1.pt()) { bar, value, new Label(new LocStrFormatted("/")), max };
            row.AlignItemsCenter().MarginRight(6.pt());
            return new Widget { Container = row, Bar = bar, Value = value, Max = max };
        }

        private static void RemoveWidget(object instance, Dictionary<object, Widget> store)
        {
            if (store.TryGetValue(instance, out Widget? widget))
            {
                widget.Container.RemoveFromHierarchy();
                store.Remove(instance);
            }
        }

        private static string[]? GetSearchTokens(object instance)
        {
            try
            {
                object? option = s_searchText?.GetValue(instance);
                PropertyInfo? hasValue = option?.GetType().GetProperty("HasValue");
                PropertyInfo? value = option?.GetType().GetProperty("Value");
                if (option is null || hasValue?.GetValue(option) is not true || value?.GetValue(option) is not string text || string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }
                return UiSearchUtils.SplitQuery(text);
            }
            catch
            {
                return null;
            }
        }

        private static (Color top, Color bottom) LerpColors(float value)
        {
            value = Mathf.Clamp01(value);
            if (value < 0.333f)
            {
                return (Color.Lerp(s_greenTop, s_yellowTop, value / 0.333f), Color.Lerp(s_greenBottom, s_yellowBottom, value / 0.333f));
            }
            if (value < 0.666f)
            {
                float t = (value - 0.333f) / 0.333f;
                return (Color.Lerp(s_yellowTop, s_orangeTop, t), Color.Lerp(s_yellowBottom, s_orangeBottom, t));
            }
            float final = (value - 0.666f) / 0.334f;
            return (Color.Lerp(s_orangeTop, s_redTop, final), Color.Lerp(s_orangeBottom, s_redBottom, final));
        }

        private static Texture2D Gradient(Color top, Color bottom)
        {
            int key = top.GetHashCode() ^ bottom.GetHashCode() << 16;
            if (s_gradientCache.TryGetValue(key, out Texture2D? cached))
            {
                return cached;
            }
            const int height = 16;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color middle = Color.Lerp(bottom, top, 0.5f);
            for (int index = 0; index < height; index++)
            {
                float t = (float)index / (height - 1);
                texture.SetPixel(0, index, t < 0.5f ? Color.Lerp(bottom, middle, t * 2f) : Color.Lerp(middle, top, (t - 0.5f) * 2f));
            }
            texture.Apply();
            s_gradientCache[key] = texture;
            return texture;
        }

        private static void UpdateVisual(Widget widget, float ratio, LocStrFormatted value, LocStrFormatted max)
        {
            widget.Value.SetValue(value);
            widget.Max.SetValue(max);
            widget.Bar.Value(Percent.FromFloat(Mathf.Clamp01(ratio)));
            if (s_barField?.GetValue(widget.Bar) is UiComponent component)
            {
                (Color top, Color bottom) colors = LerpColors(ratio);
                component.RootElement.style.backgroundImage = new StyleBackground(Gradient(colors.top, colors.bottom));
            }
        }

        private static void ElectricityAddPostfix(object __instance) => EnsureWidget(__instance, s_electricityWidgets);
        private static void ElectricityRemovePostfix(object __instance) => RemoveWidget(__instance, s_electricityWidgets);
        private static void ComputingAddPostfix(object __instance) => EnsureWidget(__instance, s_computingWidgets);
        private static void ComputingRemovePostfix(object __instance) => RemoveWidget(__instance, s_computingWidgets);

        private static void ElectricityUpdatePostfix(object __instance)
        {
            try
            {
                Widget? widget = EnsureWidget(__instance, s_electricityWidgets);
                if (widget is null || s_electricityManager?.GetValue(__instance) is not ElectricityManager manager)
                {
                    return;
                }
                bool consumption = (bool)s_electricityShowConsumption!.GetValue(__instance)!;
                string[]? tokens = GetSearchTokens(__instance);
                Electricity current = Electricity.Zero;
                Electricity maximum = Electricity.Zero;
                if (consumption)
                {
                    foreach (ElectricityManager.ConsumptionPerProto item in manager.GetConsumptionStatsPerProto())
                    {
                        if (item.EntitiesTotal != 0 && item.LastTick.MaxPossibleConsumption.Quantity != Quantity.Zero &&
                            (tokens is null || UiSearchUtils.ProtoMatches(item.ConsumerProto, tokens)))
                        {
                            current += item.LastTick.Consumed;
                            maximum += item.LastTick.MaxPossibleConsumption;
                        }
                    }
                }
                else
                {
                    foreach (ElectricityManager.ProductionPerProto item in manager.GetProductionStatsPerProto())
                    {
                        if (item.EntitiesTotal != 0 && (tokens is null || UiSearchUtils.ProtoMatches(item.ProducerProto, tokens)))
                        {
                            current += item.LastTick.Produced;
                            maximum += item.LastTick.MaxGenerationCapacity;
                        }
                    }
                }
                UpdateVisual(widget, maximum.Value > 0 ? (float)current.Value / (float)maximum.Value : 0f, current.Format(), maximum.Format());
            }
            catch
            {
                // Statistics are optional presentation; a changed private shape leaves native UI intact.
            }
        }

        private static void ComputingUpdatePostfix(object __instance)
        {
            try
            {
                Widget? widget = EnsureWidget(__instance, s_computingWidgets);
                if (widget is null || s_computingManager?.GetValue(__instance) is not ComputingManager manager)
                {
                    return;
                }
                bool consumption = (bool)s_computingShowConsumption!.GetValue(__instance)!;
                string[]? tokens = GetSearchTokens(__instance);
                Computing current = Computing.FromTFlops(0);
                Computing maximum = Computing.FromTFlops(0);
                if (consumption)
                {
                    foreach (ComputingManager.ConsumptionPerProto item in manager.GetConsumptionStatsPerProto())
                    {
                        if (item.EntitiesTotal != 0 && item.LastTick.MaxPossibleConsumption.Quantity != Quantity.Zero &&
                            (tokens is null || UiSearchUtils.ProtoMatches(item.ConsumerProto, tokens)))
                        {
                            current += item.LastTick.Consumed;
                            maximum += item.LastTick.MaxPossibleConsumption;
                        }
                    }
                }
                else
                {
                    foreach (ComputingManager.ProductionPerProto item in manager.GetProductionStatsPerProto())
                    {
                        if (item.EntitiesTotal != 0 && (tokens is null || UiSearchUtils.ProtoMatches(item.ProducerProto, tokens)))
                        {
                            current += item.LastTick.Produced;
                            maximum += item.LastTick.MaxGenerationCapacity;
                        }
                    }
                }
                UpdateVisual(widget, maximum.Value > 0 ? (float)current.Value / (float)maximum.Value : 0f, current.Format(), maximum.Format());
            }
            catch
            {
                // Statistics are optional presentation; a changed private shape leaves native UI intact.
            }
        }
    }
}
