// Taj's COI Mods | TweaksHudLayoutFeature.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Unity.Ui.Hud;
using TajsCOI.Common.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Provides a small, stable-key HUD layout surface over the game's existing HudController.
    ///     Positions are normalized to the current root size, so saved layouts survive resolution
    ///     changes without taking ownership of vanilla HUD construction.
    /// </summary>
    internal static class TweaksHudLayoutFeature
    {
        private sealed class HudElementState
        {
            internal string Key = string.Empty;
            internal VisualElement Root = null!;
            internal StyleLength OriginalMarginLeft;
            internal StyleLength OriginalMarginTop;
            internal StyleScale OriginalScale;
            internal StyleEnum<DisplayStyle> OriginalDisplay;
            internal PickingMode OriginalPickingMode;
            internal Vector2 Position;
            internal HudDragManipulator? Manipulator;
        }

        private sealed class HudDragManipulator : PointerManipulator
        {
            private readonly Action<Vector2> m_onDelta;
            private readonly Action m_onCommit;
            private bool m_locked;
            private bool m_dragging;
            private int m_pointerId;
            private Vector2 m_start;

            internal HudDragManipulator(VisualElement target, Action<Vector2> onDelta, Action onCommit)
            {
                this.target = target;
                m_onDelta = onDelta;
                m_onCommit = onCommit;
            }

            internal void SetLocked(bool locked)
            {
                m_locked = locked;
                if (locked && m_dragging)
                {
                    m_dragging = false;
                    if (target.HasPointerCapture(m_pointerId))
                    {
                        target.ReleasePointer(m_pointerId);
                    }
                }
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnPointerDown);
                target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                target.RegisterCallback<PointerUpEvent>(OnPointerUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (m_locked || evt.button != 0)
                {
                    return;
                }
                m_dragging = true;
                m_pointerId = evt.pointerId;
                m_start = new Vector2(evt.position.x, evt.position.y);
                target.CapturePointer(m_pointerId);
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (!m_dragging || evt.pointerId != m_pointerId || !target.HasPointerCapture(m_pointerId))
                {
                    return;
                }
                Vector2 current = new Vector2(evt.position.x, evt.position.y);
                m_onDelta(current - m_start);
                m_start = current;
                evt.StopPropagation();
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (!m_dragging || evt.pointerId != m_pointerId || evt.button != 0)
                {
                    return;
                }
                m_dragging = false;
                if (target.HasPointerCapture(m_pointerId))
                {
                    target.ReleasePointer(m_pointerId);
                }
                m_onCommit();
                evt.StopPropagation();
            }

            private void OnPointerCaptureOut(PointerCaptureOutEvent _)
            {
                if (m_dragging)
                {
                    m_dragging = false;
                    m_onCommit();
                }
            }
        }

        private static readonly string[] s_fieldKeys =
        {
            "m_topBarContainer=topBar",
            "m_topLeftContainer=topLeft",
            "m_pinnedProductsContainer=pinnedProducts",
            "m_statusBar=statusBar",
            "m_notificationsButtons=notifications",
            "m_researchPanel=research",
            "m_calendarControls=calendar",
            "m_priceDisplaysContainer=priceDisplays",
        };

        private static readonly Dictionary<string, HudElementState> s_elements = new Dictionary<string, HudElementState>(StringComparer.Ordinal);
        private static readonly BindingFlags s_instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static FieldInfo[]? s_hudFields;
        private static ITajsSettings? s_settings;

        internal static void Install(DependencyResolver resolver, ITajsSettings settings)
        {
            // TajsTweaksFeatureHost is itself being resolved when this method runs. Resolving
            // HudController here would recurse through the gameplay dependency graph. The host
            // already calls Apply after construction and on render updates, so defer discovery
            // until the resolver is fully unlocked.
            s_settings = settings;
        }

        internal static void Apply(DependencyResolver resolver, ITajsSettings settings)
        {
            s_settings = settings;
            if (!resolver.TryResolve(out HudController hud))
            {
                return;
            }
            if (s_hudFields is null)
            {
                s_hudFields = typeof(HudController).GetFields(s_instanceFlags);
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string mapping in s_fieldKeys)
            {
                string[] parts = mapping.Split(new[] { '=' }, 2);
                if (parts.Length != 2)
                {
                    continue;
                }
                FieldInfo? field = s_hudFields.FirstOrDefault(x => x.Name == parts[0]);
                if (field?.GetValue(hud) is not object component || GetRoot(component) is not VisualElement root)
                {
                    continue;
                }
                string key = parts[1];
                seen.Add(key);
                if (!s_elements.TryGetValue(key, out HudElementState? state) || state.Root != root)
                {
                    if (state is not null)
                    {
                        Restore(state);
                    }
                    state = new HudElementState
                    {
                        Key = key,
                        Root = root,
                        OriginalMarginLeft = root.style.marginLeft,
                        OriginalMarginTop = root.style.marginTop,
                        OriginalScale = root.style.scale,
                        OriginalDisplay = root.style.display,
                        OriginalPickingMode = root.pickingMode,
                        Position = ParsePosition(key, TajsTweaksRuntimeState.HudPositions),
                    };
                    s_elements[key] = state;
                }

                if (!TajsTweaksRuntimeState.HudLayout)
                {
                    Restore(state);
                    continue;
                }
                ApplyState(state);
            }

            foreach (string stale in s_elements.Keys.Where(x => !seen.Contains(x)).ToArray())
            {
                Restore(s_elements[stale]);
                s_elements.Remove(stale);
            }
        }

        internal static string Reset(DependencyResolver resolver, ITajsSettings settings)
        {
            if (!settings.TrySet(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudPositions, string.Empty).Success)
            {
                return "HUD positions could not be reset through the settings store.";
            }
            Apply(resolver, settings);
            return "HUD positions reset; visibility and scale settings were left unchanged.";
        }

        internal static string Status()
        {
            return "HUD layout=" + TajsTweaksRuntimeState.HudLayout + "; locked=" + TajsTweaksRuntimeState.HudDragLocked +
                "; supported elements=" + s_elements.Count + "; drag positions are normalized to the current resolution.";
        }

        private static void ApplyState(HudElementState state)
        {
            VisualElement root = state.Root;
            root.pickingMode = TajsTweaksRuntimeState.HudDragLocked ? PickingMode.Ignore : PickingMode.Position;
            root.style.scale = new StyleScale(new Scale(Vector2.one * Mathf.Clamp(TajsTweaksRuntimeState.HudScale, 75, 150) / 100f));
            bool hidden = TajsTweaksRuntimeState.ParseIds(TajsTweaksRuntimeState.HudHidden).Contains(state.Key, StringComparer.Ordinal);
            root.style.display = hidden ? DisplayStyle.None : StyleKeyword.Null;
            if (state.Manipulator is null)
            {
                state.Manipulator = new HudDragManipulator(root, delta => Move(state, delta), SavePositions);
                root.AddManipulator(state.Manipulator);
            }
            state.Manipulator.SetLocked(TajsTweaksRuntimeState.HudDragLocked);
            ApplyPosition(state);
        }

        private static void Move(HudElementState state, Vector2 delta)
        {
            Rect viewport = state.Root.panel?.visualTree.worldBound ?? default;
            if (viewport.width <= 1f || viewport.height <= 1f)
            {
                return;
            }
            state.Position = ClampPosition(state.Position + new Vector2(delta.x / viewport.width, delta.y / viewport.height));
            ApplyPosition(state);
        }

        private static void ApplyPosition(HudElementState state)
        {
            Rect viewport = state.Root.panel?.visualTree.worldBound ?? default;
            if (viewport.width > 1f && viewport.height > 1f)
            {
                state.Root.style.marginLeft = state.Position.x * viewport.width;
                state.Root.style.marginTop = state.Position.y * viewport.height;
            }
        }

        private static void SavePositions()
        {
            if (s_settings is null)
            {
                return;
            }
            string value = string.Join(";", s_elements.Values.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.Key + "=" + x.Position.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    x.Position.y.ToString("R", CultureInfo.InvariantCulture)));
            s_settings.TrySet(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudPositions, value);
        }

        private static Vector2 ParsePosition(string key, string text)
        {
            foreach (string entry in (text ?? string.Empty).Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = entry.Split(new[] { '=' }, 2);
                if (pair.Length != 2 || pair[0].Trim() != key)
                {
                    continue;
                }
                string[] values = pair[1].Split(new[] { ',' }, 2);
                if (values.Length == 2 && float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                {
                    return ClampPosition(new Vector2(x, y));
                }
            }
            return Vector2.zero;
        }

        private static Vector2 ClampPosition(Vector2 value) => new Vector2(Mathf.Clamp(value.x, -1f, 1f), Mathf.Clamp(value.y, -1f, 1f));

        private static VisualElement? GetRoot(object component)
        {
            PropertyInfo? property = component.GetType().GetProperty("RootElement", s_instanceFlags);
            return property?.GetValue(component) as VisualElement;
        }

        private static void Restore(HudElementState state)
        {
            if (state.Manipulator is not null)
            {
                state.Root.RemoveManipulator(state.Manipulator);
                state.Manipulator = null;
            }
            state.Root.style.marginLeft = state.OriginalMarginLeft;
            state.Root.style.marginTop = state.OriginalMarginTop;
            state.Root.style.scale = state.OriginalScale;
            state.Root.style.display = state.OriginalDisplay;
            state.Root.pickingMode = state.OriginalPickingMode;
        }
    }
}
