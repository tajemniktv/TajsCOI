// Taj's COI Mods | TweaksHudActionFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Settings;
using UnityEngine.UIElements;
using UiLabel = Mafi.Unity.UiToolkit.Library.Label;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    ///     Presentation policy for a discovered status/calendar action. The policy is deliberately
    ///     keyed by stable semantic IDs and contains no references to live UI objects.
    /// </summary>
    internal readonly struct HudActionPreference
    {
        internal readonly int? Order;
        internal readonly bool? Visible;
        internal readonly bool Core;

        internal HudActionPreference(int? order, bool? visible, bool core)
        {
            Order = order;
            Visible = visible;
            Core = core;
        }
    }

    internal static class HudActionPolicyCodec
    {
        internal static IReadOnlyDictionary<string, HudActionPreference> Parse(string? text)
        {
            var result = new Dictionary<string, HudActionPreference>(StringComparer.Ordinal);
            foreach (string entry in (text ?? string.Empty).Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = entry.Split(new[] { '=' }, 2);
                if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
                {
                    continue;
                }

                string[] values = pair[1].Split(':');
                int? order = null;
                bool? visible = null;
                bool core = false;
                if (values.Length > 0 && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedOrder))
                {
                    order = parsedOrder;
                }
                if (values.Length > 1 && bool.TryParse(values[1], out bool parsedVisible))
                {
                    visible = parsedVisible;
                }
                if (values.Length > 2)
                {
                    bool.TryParse(values[2], out core);
                }

                result[pair[0].Trim()] = new HudActionPreference(order, visible, core);
            }

            return result;
        }
    }

    internal static class TweaksHudActionFeature
    {
        private sealed class ActionState
        {
            internal string Id = string.Empty;
            internal UiComponent Component = null!;
            internal UiComponent Parent = null!;
            internal int OriginalIndex;
            internal StyleEnum<DisplayStyle> OriginalDisplay;
            internal bool OriginalVisible;
            internal bool IsCore;
        }

        private sealed class RootState
        {
            internal UiComponent Root = null!;
            internal readonly List<ActionState> Actions = new();
            internal readonly HashSet<UiComponent> SeenComponents = new();
            internal bool Hovered;
            internal UiLabel? Clock;
            internal IVisualElementScheduledItem? ClockSchedule;
            internal string StructureFingerprint = string.Empty;
            internal string PolicyFingerprint = string.Empty;
            internal bool Enabled;
            internal bool InitialCaptureComplete;
        }

        private static readonly ConditionalWeakTable<UiComponent, RootState> s_states = new();
        private static readonly List<WeakReference<UiComponent>> s_roots = new();

        internal static void Apply(HudController hud, ITajsSettings settings, bool enabled)
        {
            if (!enabled)
            {
                Reset(hud);
                return;
            }

            foreach (UiComponent root in GetRoots(hud))
            {
                RootState state = EnsureState(root);
                state.Enabled = true;
                string structure = ComputeStructureFingerprint(root);
                string policy = TajsTweaksRuntimeState.HudActionPolicy + "|" +
                                 TajsTweaksRuntimeState.HudActionCollapsed + "|" +
                                 TajsTweaksRuntimeState.HudActionHoverReveal + "|" +
                                 TajsTweaksRuntimeState.HudRealWorldClock + "|" +
                                 TajsTweaksRuntimeState.HudClock24Hour;
                bool structureChanged = !string.Equals(structure, state.StructureFingerprint, StringComparison.Ordinal);
                if (structureChanged)
                {
                    DiscoverActions(state);
                    state.StructureFingerprint = structure;
                }

                if (structureChanged || !string.Equals(policy, state.PolicyFingerprint, StringComparison.Ordinal))
                {
                    state.PolicyFingerprint = policy;
                    ApplyActions(state);
                    ApplyClock(state);
                }
            }
        }

        internal static void Reset(HudController hud)
        {
            foreach (UiComponent root in GetRoots(hud))
            {
                if (!s_states.TryGetValue(root, out RootState? state))
                {
                    continue;
                }

                RestoreActions(state);
                if (state.Clock is not null)
                {
                    state.ClockSchedule?.Pause();
                    state.Clock.RemoveFromHierarchy();
                    state.Clock = null;
                    state.ClockSchedule = null;
                }

                state.Enabled = false;
                state.StructureFingerprint = string.Empty;
                state.PolicyFingerprint = string.Empty;
            }
        }

        internal static void ResetAll()
        {
            foreach (WeakReference<UiComponent> reference in s_roots.ToArray())
            {
                if (reference.TryGetTarget(out UiComponent? root) && s_states.TryGetValue(root, out RootState? state))
                {
                    RestoreActions(state);
                    state.Enabled = false;
                    state.ClockSchedule?.Pause();
                    state.Clock?.RemoveFromHierarchy();
                    state.Clock = null;
                    state.ClockSchedule = null;
                }
            }

            s_roots.Clear();
        }

        private static UiComponent[] GetRoots(HudController hud)
        {
            var result = new List<UiComponent>();
            foreach (System.Reflection.FieldInfo field in hud.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                if (field.GetValue(hud) is CalendarControlsHud calendar)
                {
                    result.Add(calendar);
                }
                else if (field.GetValue(hud) is StatusBarHud status)
                {
                    result.Add(status);
                }
            }

            return result.ToArray();
        }

        private static RootState EnsureState(UiComponent root)
        {
            if (s_states.TryGetValue(root, out RootState? existing))
            {
                return existing;
            }

            var state = new RootState { Root = root };
            root.OnMouseEnterLeave(
                () =>
                {
                    state.Hovered = true;
                    ApplyActions(state);
                },
                () =>
                {
                    state.Hovered = false;
                    ApplyActions(state);
                });
            s_states.Add(root, state);
            s_roots.Add(new WeakReference<UiComponent>(root));
            return state;
        }

        private static void DiscoverActions(RootState state)
        {
            var candidates = new List<(UiComponent Component, UiComponent Parent, int Index)>();
            Visit(state.Root, state.Root, candidates);
            bool initialCapture = !state.InitialCaptureComplete;
            HashSet<UiComponent> currentComponents = candidates.Select(candidate => candidate.Component).ToHashSet();
            state.Actions.RemoveAll(action => !currentComponents.Contains(action.Component));
            state.SeenComponents.RemoveWhere(component => !currentComponents.Contains(component));
            foreach ((UiComponent component, UiComponent parent, int index) in candidates)
            {
                if (!state.SeenComponents.Add(component))
                {
                    continue;
                }

                string prefix = state.Root is CalendarControlsHud ? "calendar" : "status";
                string semantic = ResolveSemanticName(component);
                string parentName = string.IsNullOrWhiteSpace(parent.RootElement.name) ? parent.GetType().Name : parent.RootElement.name;
                int duplicate = state.Actions.Count(action => action.Parent == parent && action.Component.GetType() == component.GetType());
                string id = prefix + "." + Sanitize(parentName) + "." + Sanitize(semantic) + "." + duplicate.ToString(CultureInfo.InvariantCulture);
                state.Actions.Add(new ActionState
                {
                    Id = id,
                    Component = component,
                    Parent = parent,
                    OriginalIndex = index,
                    OriginalDisplay = component.RootElement.style.display,
                    OriginalVisible = initialCapture ? component.IsVisible() : true,
                });
            }

            state.InitialCaptureComplete = true;
        }

        private static string ResolveSemanticName(UiComponent component)
        {
            if (!string.IsNullOrWhiteSpace(component.RootElement.name))
            {
                return component.RootElement.name;
            }

            // ButtonIcon does not expose its asset path, but the loaded sprite name is stable
            // across instances and gives speed/menu actions meaningful IDs when available.
            if (component is ButtonIcon button)
            {
                try
                {
                    object style = button.Icon.RootElement.style;
                    object? background = style.GetType().GetProperty("backgroundImage")?.GetValue(style);
                    object? value = background?.GetType().GetProperty("value")?.GetValue(background);
                    object? sprite = value?.GetType().GetProperty("sprite")?.GetValue(value);
                    string? spriteName = sprite?.GetType().GetProperty("name")?.GetValue(sprite) as string;
                    if (!string.IsNullOrWhiteSpace(spriteName))
                    {
                        return spriteName!;
                    }
                }
                catch
                {
                    // Metadata discovery is advisory; type names remain a stable fallback.
                }
            }

            return component.GetType().Name;
        }

        private static void Visit(UiComponent parent, UiComponent root, List<(UiComponent, UiComponent, int)> result)
        {
            int index = 0;
            foreach (UiComponent child in parent.AllChildren.ToArray())
            {
                if (IsAction(child))
                {
                    result.Add((child, parent, index));
                }
                Visit(child, root, result);
                index++;
            }
        }

        private static bool IsAction(UiComponent component)
        {
            string name = component.GetType().Name;
            return name.StartsWith("ButtonIcon", StringComparison.Ordinal) ||
                   name.IndexOf("StatusBarDisplay", StringComparison.Ordinal) >= 0;
        }

        private static string ComputeStructureFingerprint(UiComponent root)
        {
            var parts = new List<string>();
            VisitFingerprint(root, parts);
            return string.Join("|", parts);
        }

        private static void VisitFingerprint(UiComponent parent, List<string> parts)
        {
            parts.Add(parent.GetType().FullName ?? parent.GetType().Name);
            foreach (UiComponent child in parent.AllChildren)
            {
                if (IsAction(child))
                {
                    parts.Add(child.GetType().FullName ?? child.GetType().Name);
                }
                VisitFingerprint(child, parts);
            }
        }

        private static void ApplyActions(RootState state)
        {
            if (!state.Enabled)
            {
                RestoreActions(state);
                return;
            }

            IReadOnlyDictionary<string, HudActionPreference> preferences = HudActionPolicyCodec.Parse(TajsTweaksRuntimeState.HudActionPolicy);
            bool collapse = TajsTweaksRuntimeState.HudActionCollapsed && (!TajsTweaksRuntimeState.HudActionHoverReveal || !state.Hovered);
            foreach (ActionState action in state.Actions)
            {
                HudActionPreference preference = preferences.TryGetValue(action.Id, out HudActionPreference configured)
                    ? configured
                    : new HudActionPreference(null, action.OriginalVisible, false);
                action.IsCore = preference.Core;
                bool visible = preference.Visible ?? true;
                if (collapse && !preference.Core)
                {
                    visible = false;
                }
                action.Component.Visible(visible);
            }

            foreach (IGrouping<UiComponent, ActionState> group in state.Actions.GroupBy(action => action.Parent))
            {
                ActionState[] ordered = group.OrderBy(action => preferences.TryGetValue(action.Id, out HudActionPreference preference) && preference.Order.HasValue
                        ? preference.Order.Value
                        : action.OriginalIndex)
                    .ThenBy(action => action.OriginalIndex)
                    .ToArray();
                UiComponent[] current = group.Key.AllChildren
                    .Where(child => group.Any(action => ReferenceEquals(action.Component, child)))
                    .ToArray();
                if (current.SequenceEqual(ordered.Select(action => action.Component)))
                {
                    continue;
                }

                int[] slots = group.Key.AllChildren.Select((child, index) => (child, index))
                    .Where(item => group.Any(action => ReferenceEquals(action.Component, item.child)))
                    .Select(item => item.index)
                    .ToArray();
                foreach (ActionState action in ordered)
                {
                    action.Component.RemoveFromHierarchy();
                }
                for (int index = 0; index < ordered.Length; index++)
                {
                    ordered[index].Parent.InsertAt(Math.Min(slots[index], ordered[index].Parent.ChildrenCount), ordered[index].Component);
                }
            }
        }

        private static void RestoreActions(RootState state)
        {
            foreach (ActionState action in state.Actions.OrderBy(x => x.OriginalIndex))
            {
                action.Component.RootElement.style.display = action.OriginalDisplay;
            }
            foreach (IGrouping<UiComponent, ActionState> group in state.Actions.GroupBy(action => action.Parent))
            {
                ActionState[] ordered = group.OrderBy(x => x.OriginalIndex).ToArray();
                UiComponent[] current = group.Key.AllChildren
                    .Where(child => group.Any(action => ReferenceEquals(action.Component, child)))
                    .ToArray();
                if (current.SequenceEqual(ordered.Select(action => action.Component)))
                {
                    continue;
                }

                int[] slots = group.Key.AllChildren.Select((child, index) => (child, index))
                    .Where(item => group.Any(action => ReferenceEquals(action.Component, item.child)))
                    .Select(item => item.index)
                    .ToArray();
                foreach (ActionState action in ordered)
                {
                    action.Component.RemoveFromHierarchy();
                }
                for (int index = 0; index < ordered.Length; index++)
                {
                    int slot = index < slots.Length ? slots[index] : ordered[index].OriginalIndex;
                    ordered[index].Parent.InsertAt(Math.Min(slot, ordered[index].Parent.ChildrenCount), ordered[index].Component);
                }
            }
        }

        private static void ApplyClock(RootState state)
        {
            if (!TajsTweaksRuntimeState.HudRealWorldClock || state.Root is not CalendarControlsHud)
            {
                if (state.Clock is not null)
                {
                    state.ClockSchedule?.Pause();
                    state.Clock.RemoveFromHierarchy();
                    state.Clock = null;
                    state.ClockSchedule = null;
                }

                return;
            }

            if (state.Clock is null)
            {
                state.Clock = new UiLabel().Name("TajsRealWorldClock").FontBold().TextCenterMiddle().MinWidth(72f.px());
                state.Root.Add(state.Clock);
                UiLabel clock = state.Clock;
                state.ClockSchedule = clock.Schedule.Execute(() => UpdateClock(clock)).Every(1000L);
            }

            UpdateClock(state.Clock);
        }

        private static void UpdateClock(UiLabel clock)
        {
            string format = TajsTweaksRuntimeState.HudClock24Hour ? "HH:mm:ss" : "hh:mm:ss tt";
            clock.Value(DateTime.Now.ToString(format, CultureInfo.CurrentCulture).AsLoc());
        }

        private static string Sanitize(string value)
        {
            return new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        }
    }
}
