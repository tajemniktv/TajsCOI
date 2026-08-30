// Taj's COI Mods | AdaptiveTowerInspectorFeature.cs
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
using Mafi.Core.Entities.Dynamic;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Settings;
using UnityEngine;
using ClickEvent = UnityEngine.UIElements.ClickEvent;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    ///     Optional extension point for integrations that want to contribute a tower-inspector
    ///     section without coupling this feature to another mod's private UI types.
    /// </summary>
    public interface IAdaptiveInspectorSectionProvider
    {
        public string SectionId { get; }

        public bool TryAttach(object inspector, UiComponent mainBody);
    }

    public static class AdaptiveInspectorSectionRegistry
    {
        private static readonly object s_gate = new();
        private static readonly List<WeakReference<IAdaptiveInspectorSectionProvider>> s_providers = new();

        public static void Register(IAdaptiveInspectorSectionProvider provider)
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.SectionId))
            {
                return;
            }

            lock (s_gate)
            {
                s_providers.RemoveAll(reference => !reference.TryGetTarget(out _));
                if (!s_providers.Any(reference => reference.TryGetTarget(out IAdaptiveInspectorSectionProvider? current) &&
                                                  string.Equals(current.SectionId, provider.SectionId, StringComparison.Ordinal)))
                {
                    s_providers.Add(new WeakReference<IAdaptiveInspectorSectionProvider>(provider));
                }
            }
        }

        public static void Unregister(IAdaptiveInspectorSectionProvider provider)
        {
            lock (s_gate)
            {
                s_providers.RemoveAll(reference => !reference.TryGetTarget(out IAdaptiveInspectorSectionProvider? current) ||
                                                   ReferenceEquals(current, provider));
            }
        }

        internal static IAdaptiveInspectorSectionProvider[] Snapshot()
        {
            lock (s_gate)
            {
                var result = new List<IAdaptiveInspectorSectionProvider>();
                s_providers.RemoveAll(reference => !reference.TryGetTarget(out IAdaptiveInspectorSectionProvider? provider));
                foreach (WeakReference<IAdaptiveInspectorSectionProvider> reference in s_providers)
                {
                    if (reference.TryGetTarget(out IAdaptiveInspectorSectionProvider? provider))
                    {
                        result.Add(provider);
                    }
                }

                return result.ToArray();
            }
        }
    }

    /// <summary>
    ///     Reusable presentation wrapper around the game's native collapsible panel. It changes
    ///     only the header/body presentation and never replaces the native section or its listeners.
    /// </summary>
    internal sealed class AdaptiveCollapsibleSection
    {
        internal AdaptiveCollapsibleSection(PanelWithHeader panel, string id, Action<bool> onChanged)
        {
            Panel = panel;
            Id = id;
            panel.Collapsed(false);
            // Register a ClickEvent callback instead of the fluent Action overload: the latter
            // replaces PanelWithHeader's native Clickable manipulator and would break collapse.
            panel.Header.OnClick((ClickEvent _) => onChanged(panel.IsCollapsed));
        }

        internal PanelWithHeader Panel { get; }
        internal string Id { get; }

        internal void SetCollapsed(bool collapsed) => Panel.Collapsed(collapsed);
    }

    internal static class AdaptiveTowerInspectorFeature
    {
        private sealed class SectionState
        {
            internal string Id = string.Empty;
            internal string VehicleClass = string.Empty;
            internal PanelWithHeader Panel = null!;
            internal AdaptiveCollapsibleSection Wrapper = null!;
            internal ScrollColumn? Scroll;
            internal UiComponent? VehicleAssigner;
            internal UiComponent? OriginalParent;
            internal int OriginalIndex;
            internal bool OriginalVisible;
            internal bool OriginalCollapsed;
        }

        private sealed class InspectorState
        {
            internal object Inspector = null!;
            internal UiComponent MainBody = null!;
            internal PanelWithHeader FilterPanel = null!;
            internal Label Summary = null!;
            internal readonly List<SectionState> Sections = new();
            internal readonly Dictionary<string, Toggle> Filters = new(StringComparer.Ordinal);
            internal bool Restored;
        }

        private static readonly ConditionalWeakTable<object, InspectorState> s_states = new();
        private static readonly List<WeakReference<object>> s_inspectors = new();
        private static readonly BindingFlags s_flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }

            Assembly assembly = typeof(PanelWithHeader).Assembly;
            foreach (string typeName in new[] { "Mafi.Unity.Ui.Inspectors.MineTowerInspector", "Mafi.Unity.Ui.Inspectors.ForestryTowerInspector" })
            {
                Type inspectorType = assembly.GetType(typeName, throwOnError: true)!;
                foreach (ConstructorInfo constructor in inspectorType.GetConstructors(s_flags))
                {
                    harmony.Patch(
                        constructor,
                        postfix: new HarmonyMethod(typeof(AdaptiveTowerInspectorFeature), nameof(InspectorConstructedPostfix)));
                }

                MethodInfo? activated = inspectorType.GetMethod(
                    "OnActivated",
                    s_flags | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (activated is not null)
                {
                    harmony.Patch(
                        activated,
                        postfix: new HarmonyMethod(typeof(AdaptiveTowerInspectorFeature), nameof(InspectorActivatedPostfix)));
                }
            }

            s_installed = true;
        }

        internal static void RefreshAll()
        {
            foreach (WeakReference<object> reference in s_inspectors.ToArray())
            {
                if (!reference.TryGetTarget(out object? inspector))
                {
                    s_inspectors.Remove(reference);
                    continue;
                }

                if (s_states.TryGetValue(inspector, out InspectorState? state))
                {
                    Apply(state);
                }
                else if (TajsTweaksRuntimeState.AdaptiveTowerInspector)
                {
                    TryBuild(inspector);
                }
            }
        }

        internal static void Reset()
        {
            foreach (WeakReference<object> reference in s_inspectors.ToArray())
            {
                if (reference.TryGetTarget(out object? inspector) && s_states.TryGetValue(inspector, out InspectorState? state))
                {
                    Restore(state);
                }
            }

            s_inspectors.Clear();
        }

        private static void InspectorConstructedPostfix(object __instance)
        {
            Track(__instance);
            if (TajsTweaksRuntimeState.AdaptiveTowerInspector)
            {
                TryBuild(__instance);
            }
        }

        private static void InspectorActivatedPostfix(object __instance)
        {
            Track(__instance);
            if (!TajsTweaksRuntimeState.AdaptiveTowerInspector)
            {
                if (s_states.TryGetValue(__instance, out InspectorState? disabledState))
                {
                    Restore(disabledState);
                }

                return;
            }

            if (!s_states.TryGetValue(__instance, out InspectorState? state))
            {
                TryBuild(__instance);
            }
            else if (!IsAttached(state))
            {
                // Inspector instances can be reused after the vanilla tree is rebuilt. Drop the
                // old presentation state and build against the freshly opened native sections.
                try
                {
                    Restore(state);
                }
                catch
                {
                    // A partially rebuilt tree is still safe to replace below.
                }
                s_states.Remove(__instance);
                TryBuild(__instance);
            }
            else
            {
                Apply(state);
            }
        }

        private static bool IsAttached(InspectorState state)
        {
            if (state.FilterPanel.Parent.ValueOrNull != state.MainBody)
            {
                return false;
            }

            return state.Sections.All(section => section.Panel.Parent.ValueOrNull == state.MainBody);
        }

        private static void Track(object inspector)
        {
            if (inspector is null || s_inspectors.Any(reference => reference.TryGetTarget(out object? current) && ReferenceEquals(current, inspector)))
            {
                return;
            }

            s_inspectors.Add(new WeakReference<object>(inspector));
        }

        private static void TryBuild(object inspector)
        {
            if (inspector is null || s_states.TryGetValue(inspector, out _))
            {
                return;
            }

            try
            {
                if (AccessTools.Field(inspector.GetType(), "MainBody")?.GetValue(inspector) is not UiComponent mainBody)
                {
                    return;
                }

                var state = new InspectorState
                {
                    Inspector = inspector,
                    MainBody = mainBody,
                    FilterPanel = new PanelWithHeader(Localize("Vehicle filters")),
                    Summary = new Label().FontBold(),
                };
                state.FilterPanel.BodyAdd(new Row(2.pt()), state.Summary);
                mainBody.InsertAt(0, state.FilterPanel);

                BuildFilters(state);
                BuildSections(state);
                foreach (IAdaptiveInspectorSectionProvider provider in AdaptiveInspectorSectionRegistry.Snapshot())
                {
                    try
                    {
                        provider.TryAttach(inspector, mainBody);
                    }
                    catch
                    {
                        // Optional integrations must never affect the native inspector.
                    }
                }

                s_states.Add(inspector, state);
                Apply(state);
            }
            catch
            {
                // A future UI tree must leave the native tower inspector usable.
            }
        }

        private static void BuildFilters(InspectorState state)
        {
            string[] classes = state.Inspector.GetType().Name == "MineTowerInspector"
                ? new[] { "excavator", "truck" }
                : new[] { "tree_planter", "tree_harvester" };
            Row row = new Row(3.pt()).Wrap();
            foreach (string vehicleClass in classes)
            {
                string captured = vehicleClass;
                Toggle toggle = new Toggle(standalone: true)
                    .Label(Localize(FormatClassName(vehicleClass)))
                    .Value(IsVehicleClassVisible(vehicleClass))
                    .OnValueChanged(_ => SaveFilters(state));
                state.Filters[vehicleClass] = toggle;
                row.Add(toggle);
            }

            state.FilterPanel.Body.InsertAt(0, row);
        }

        private static void BuildSections(InspectorState state)
        {
            // 0.8.7b MineTowerInspector builds the dump/notification panel followed by
            // excavator and truck VehicleAssignerUi panels. ForestryTowerInspector similarly
            // builds harvesting options followed by tree-planter and tree-harvester panels.
            // We intentionally patch only those concrete inspector types and identify the
            // assignment sections by their native VehicleAssignerUi child.
            string[] vehicleClasses = state.Inspector.GetType().Name == "MineTowerInspector"
                ? new[] { "excavator", "truck" }
                : new[] { "tree_planter", "tree_harvester" };
            int vehicleIndex = 0;
            int sectionIndex = 0;
            foreach (PanelWithHeader panel in state.MainBody.AllChildren.OfType<PanelWithHeader>().ToArray())
            {
                if (ReferenceEquals(panel, state.FilterPanel))
                {
                    continue;
                }

                UiComponent? vehicleUi = panel.Body.AllChildren.FirstOrDefault(child =>
                    string.Equals(child.GetType().Name, "VehicleAssignerUi", StringComparison.Ordinal));
                string vehicleClass = vehicleUi is null || vehicleIndex >= vehicleClasses.Length
                    ? string.Empty
                    : vehicleClasses[vehicleIndex++];
                string id = vehicleClass.Length == 0 ? "section." + sectionIndex.ToString() : "vehicle." + vehicleClass;
                sectionIndex++;
                var section = new SectionState
                {
                    Id = id,
                    VehicleClass = vehicleClass,
                    Panel = panel,
                    VehicleAssigner = vehicleUi,
                    OriginalParent = vehicleUi?.Parent.ValueOrNull,
                    OriginalIndex = vehicleUi is null
                        ? -1
                        : panel.Body.AllChildren.TakeWhile(child => !ReferenceEquals(child, vehicleUi)).Count(),
                    OriginalVisible = panel.IsVisible(),
                    OriginalCollapsed = panel.IsCollapsed,
                };
                state.Sections.Add(section);
                section.Wrapper = new AdaptiveCollapsibleSection(panel, id, _ => SaveSectionCollapse(section));
                section.Wrapper.SetCollapsed(IsSectionCollapsed(id));

                if (vehicleUi is not null)
                {
                    ScrollColumn scroll = new ScrollColumn().MaxHeight(8 * 28f.px()).FlexShrink(0f);
                    vehicleUi.RemoveFromHierarchy();
                    scroll.Add(vehicleUi);
                    panel.Body.Add(scroll);
                    section.Scroll = scroll;
                }
            }
        }

        private static void Apply(InspectorState state)
        {
            if (!TajsTweaksRuntimeState.AdaptiveTowerInspector)
            {
                Restore(state);
                return;
            }

            state.Restored = false;
            state.FilterPanel.Show();
            foreach (KeyValuePair<string, Toggle> pair in state.Filters)
            {
                pair.Value.Value(IsVehicleClassVisible(pair.Key));
            }

            int hiddenAssigned = 0;
            foreach (SectionState section in state.Sections)
            {
                bool visible = section.VehicleClass.Length == 0 || IsVehicleClassVisible(section.VehicleClass);
                section.Panel.Visible(visible);
                if (visible)
                {
                    section.Wrapper.SetCollapsed(IsSectionCollapsed(section.Id));
                }
                if (!visible)
                {
                    hiddenAssigned += CountAssignedVehicles(state.Inspector, section.VehicleClass);
                }

                if (section.Scroll is not null)
                {
                    if (section.VehicleAssigner is not null && section.VehicleAssigner.Parent.ValueOrNull != section.Scroll)
                    {
                        section.VehicleAssigner.RemoveFromHierarchy();
                        section.Scroll.Add(section.VehicleAssigner);
                    }
                    section.Scroll.MaxHeight((Mathf.Clamp(TajsTweaksRuntimeState.InspectorVehicleVisibleRows, 3, 24) * 28f).px());
                }
            }

            state.Summary.Value(
                hiddenAssigned == 0
                    ? Localize("All assigned vehicles are visible.")
                    : Localize(hiddenAssigned + " assigned vehicle(s) hidden by filters."));
            state.Summary.Visible(hiddenAssigned > 0);
        }

        private static void Restore(InspectorState state)
        {
            if (state.Restored)
            {
                return;
            }

            state.FilterPanel.Hide();
            foreach (SectionState section in state.Sections)
            {
                section.Panel.Visible(section.OriginalVisible);
                section.Wrapper.SetCollapsed(section.OriginalCollapsed);
                if (section.Scroll is not null && section.VehicleAssigner is not null)
                {
                    section.VehicleAssigner.RemoveFromHierarchy();
                    section.Scroll.RemoveFromHierarchy();
                    if (section.OriginalParent is not null)
                    {
                        section.OriginalParent.InsertAt(
                            Math.Min(Math.Max(section.OriginalIndex, 0), section.OriginalParent.ChildrenCount),
                            section.VehicleAssigner);
                    }
                }
            }

            state.Restored = true;
        }

        private static int CountAssignedVehicles(object inspector, string vehicleClass)
        {
            if (vehicleClass.Length == 0 || AccessTools.Property(inspector.GetType(), "Entity")?.GetValue(inspector) is not IEntityAssignedWithVehicles entity)
            {
                return 0;
            }

            int count = 0;
            foreach (Vehicle vehicle in entity.AllVehicles)
            {
                if (string.Equals(ClassifyVehicle(vehicle), vehicleClass, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ClassifyVehicle(Vehicle vehicle)
        {
            string value = (vehicle.GetType().Name + " " + vehicle.Prototype.GetType().Name).ToLowerInvariant();
            if (value.Contains("excavator", StringComparison.Ordinal))
            {
                return "excavator";
            }
            if (value.Contains("treeplanter", StringComparison.Ordinal) || value.Contains("tree_planter", StringComparison.Ordinal))
            {
                return "tree_planter";
            }
            if (value.Contains("treeharvester", StringComparison.Ordinal) || value.Contains("tree_harvester", StringComparison.Ordinal))
            {
                return "tree_harvester";
            }
            return value.Contains("truck", StringComparison.Ordinal) ? "truck" : string.Empty;
        }

        private static bool IsVehicleClassVisible(string vehicleClass)
        {
            IReadOnlyList<string> configured = TajsTweaksRuntimeState.ParseIds(TajsTweaksRuntimeState.InspectorVehicleFilters);
            return configured.Contains(vehicleClass, StringComparer.Ordinal);
        }

        private static void SaveFilters(InspectorState state)
        {
            string value = string.Join(
                ",",
                state.Filters.Where(pair => pair.Value.GetValue()).Select(pair => pair.Key).OrderBy(x => x, StringComparer.Ordinal));
            TajsTweaksFeatureSettings.TrySet(TajsTweaksSettingsCatalog.InspectorVehicleFilters, value);
            Apply(state);
        }

        private static void SaveSectionCollapse(SectionState section)
        {
            HashSet<string> collapsed = new(TajsTweaksRuntimeState.ParseIds(TajsTweaksRuntimeState.InspectorSectionCollapsed), StringComparer.Ordinal);
            if (section.Panel.IsCollapsed)
            {
                collapsed.Add(section.Id);
            }
            else
            {
                collapsed.Remove(section.Id);
            }
            TajsTweaksFeatureSettings.TrySet(
                TajsTweaksSettingsCatalog.InspectorSectionCollapsed,
                string.Join(",", collapsed.OrderBy(x => x, StringComparer.Ordinal)));
        }

        private static bool IsSectionCollapsed(string id) =>
            TajsTweaksRuntimeState.ParseIds(TajsTweaksRuntimeState.InspectorSectionCollapsed).Contains(id, StringComparer.Ordinal);

        private static string FormatClassName(string id) => id.Replace('_', ' ');

        private static LocStrFormatted Localize(string value) =>
            LocalizationManager.CreateAlreadyLocalizedStr("TajsTweaksAdaptiveInspector_" + value.GetHashCode().ToString("X"), value).AsFormatted;

        private static class TajsTweaksFeatureSettings
        {
            private static WeakReference<ITajsSettings>? s_settings;

            internal static void TrySet(string key, string value)
            {
                if (s_settings is not null && s_settings.TryGetTarget(out ITajsSettings? settings))
                {
                    settings.TrySet(TajsTweaksSettingsCatalog.ModId, key, value);
                }
            }

            internal static void Bind(ITajsSettings settings) => s_settings = new WeakReference<ITajsSettings>(settings);
        }

        internal static void BindSettings(ITajsSettings settings) => TajsTweaksFeatureSettings.Bind(settings);
    }
}
