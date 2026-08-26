// Taj's COI Mods | TweaksEfficiencyOverlayFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Factory;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiStatic.Toolbar;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;

namespace TajsCOI.Tweaks
{
    internal static class TweaksEfficiencyOverlayFeature
    {
        private static WeakReference<EfficiencyOverlayController>? s_controller;

        internal static void Install(DependencyResolver resolver)
        {
            if (!resolver.TryResolve(out IEntitiesManager entities) ||
                !resolver.TryResolve(out ToolbarHud toolbar))
            {
                throw new InvalidOperationException("IEntitiesManager or ToolbarHud unavailable");
            }

            var textMeshType = Type.GetType("UnityEngine.TextMesh, UnityEngine.TextRenderingModule", false);
            if (textMeshType is null)
            {
                throw new TypeLoadException("UnityEngine.TextMesh");
            }

            Dispose();
            var owner = new GameObject("Tajs efficiency overlay");
            try
            {
                var overlay = owner.AddComponent<EfficiencyOverlayRenderer>();
                overlay.Initialize(entities, textMeshType);
                var controller = new EfficiencyOverlayController(toolbar, overlay);
                s_controller = new WeakReference<EfficiencyOverlayController>(controller);
            }
            catch
            {
                UnityEngine.Object.Destroy(owner);
                throw;
            }
        }

        internal static void ApplySettings()
        {
            if (TryGetController(out EfficiencyOverlayController? controller) && controller is not null)
            {
                controller.ApplySettings();
            }
        }

        internal static void Dispose()
        {
            if (TryGetController(out EfficiencyOverlayController? controller) && controller is not null)
            {
                controller.Dispose();
            }

            s_controller = null;
        }

        private static bool TryGetController(out EfficiencyOverlayController? controller)
        {
            controller = null;
            if (s_controller is null || !s_controller.TryGetTarget(out controller) || controller is null)
            {
                s_controller = null;
                return false;
            }

            return true;
        }
    }

    internal sealed class EfficiencyOverlayController : IToolbarItemController
    {
        private readonly EfficiencyOverlayRenderer m_renderer;
        private readonly Toolbox m_toolbox;
        private readonly ToolboxItem m_percentage;
        private readonly ToolboxItem m_status;
        private readonly ToolboxItem m_compact;
        private readonly ToolboxItem m_buildings;
        private readonly ToolboxItem m_vehicles;
        private readonly Button m_toolbarButton;

        public bool IsVisible => true;

        public bool DeactivateShortcutsIfNotVisible => true;

        public ControllerConfig Config => ControllerConfig.Mode;

        public event Action<IToolbarItemController>? VisibilityChanged;

        internal EfficiencyOverlayController(ToolbarHud toolbar, EfficiencyOverlayRenderer renderer)
        {
            m_renderer = renderer;
            m_renderer.SetEnabled(TajsTweaksRuntimeState.EfficiencyOverlay);
            m_toolbarButton = toolbar.AddToolButton(
                Localize("Efficiency overlay"),
                this,
                "Assets/Unity/UserInterface/Toolbar/Stats.svg",
                930f);
            m_toolbox = toolbar.CreateToolbox();
            m_percentage = AddEntry("Percentage", () => SetMode("percentage"));
            m_status = AddEntry("Status", () => SetMode("status"));
            m_compact = AddEntry("Compact", () => SetMode("compact"));
            m_buildings = AddEntry("Buildings", ToggleBuildings);
            m_vehicles = AddEntry("Vehicles", ToggleVehicles);
            ApplySettings();
        }

        public void Activate()
        {
            SetActive(true);
            m_toolbox.Show();
        }

        public void Deactivate()
        {
            SetActive(false);
            m_toolbox.Hide();
        }

        public bool InputUpdate() => false;

        internal void ApplySettings()
        {
            m_renderer.ApplySettings();
            m_renderer.SetEnabled(TajsTweaksRuntimeState.EfficiencyOverlay);
            m_percentage.Selected(TajsTweaksRuntimeState.EfficiencyOverlayMode == "percentage");
            m_status.Selected(TajsTweaksRuntimeState.EfficiencyOverlayMode == "status");
            m_compact.Selected(TajsTweaksRuntimeState.EfficiencyOverlayMode == "compact");
            m_buildings.Selected(TajsTweaksRuntimeState.EfficiencyOverlayBuildings);
            m_vehicles.Selected(TajsTweaksRuntimeState.EfficiencyOverlayVehicles);
            VisibilityChanged?.Invoke(this);
        }

        internal void Dispose()
        {
            m_toolbox.RemoveFromHierarchy();
            m_toolbarButton.RemoveFromHierarchy();
            if (m_renderer is not null)
            {
                UnityEngine.Object.Destroy(m_renderer.gameObject);
            }
        }

        private ToolboxItem AddEntry(string text, Action action)
        {
            return m_toolbox.AddEntry(
                Localize(text),
                _ => KeyBindings.EMPTY,
                action,
                Localize("Efficiency overlay controls"));
        }

        private void SetMode(string mode)
        {
            TajsTweaksRuntimeState.EfficiencyOverlayMode = mode;
            ApplySettings();
        }

        private void ToggleBuildings()
        {
            TajsTweaksRuntimeState.EfficiencyOverlayBuildings = !TajsTweaksRuntimeState.EfficiencyOverlayBuildings;
            ApplySettings();
        }

        private void ToggleVehicles()
        {
            TajsTweaksRuntimeState.EfficiencyOverlayVehicles = !TajsTweaksRuntimeState.EfficiencyOverlayVehicles;
            ApplySettings();
        }

        private void SetActive(bool active)
        {
            TajsTweaksRuntimeState.EfficiencyOverlay = active;
            m_renderer.SetEnabled(active);
        }

        private static LocStrFormatted Localize(string text) =>
            LocalizationManager.CreateAlreadyLocalizedStr(
                "TajsTweaksEfficiencyOverlay_" + text.GetHashCode().ToString("X", CultureInfo.InvariantCulture),
                text).AsFormatted;
    }

    internal static class EfficiencyOverlayPresentation
    {
        internal static int Percentage(ProductivityCounterHistory.Data data)
        {
            int total = data.CategoryA + data.CategoryB + data.CategoryC + data.CategoryD;
            return total <= 0 ? -1 : (int)Math.Round(data.CategoryA * 100d / total, MidpointRounding.AwayFromZero);
        }

        internal static Color ColorFor(int percentage)
        {
            if (percentage < 0)
            {
                return new Color(0.65f, 0.65f, 0.65f, 0.9f);
            }
            if (percentage >= 80)
            {
                return new Color(0.2f, 0.95f, 0.35f, 0.95f);
            }
            if (percentage >= 50)
            {
                return new Color(1f, 0.8f, 0.2f, 0.95f);
            }
            return new Color(1f, 0.25f, 0.2f, 0.95f);
        }

        internal static string Format(string mode, int percentage, string status)
        {
            return mode switch
            {
                "status" => status,
                "compact" => "●",
                _ => percentage < 0 ? "—" : percentage.ToString(CultureInfo.InvariantCulture) + "%",
            };
        }
    }

    internal sealed class EfficiencyOverlayRenderer : MonoBehaviour
    {
        private const int MaximumLabels = 1024;
        private const float MinimumViewportMargin = 0.05f;

        private const float LabelHeight = 2.25f;

        // TextMesh.characterSize is already expressed in world units. Keep the
        // transform scale in the same range as the working resource-overlay
        // labels; the previous 0.045-0.22 range made these labels effectively
        // microscopic at normal camera distances.
        // Counter perspective shrinkage so labels remain readable as the
        // camera zooms out. The bounded cap still protects against huge
        // labels during close-up inspection.
        private const float MinimumScale = 0.5f;
        private const float MaximumScale = 12f;
        private const float ReferenceDistance = 100f;

        private sealed class LabelSlot
        {
            internal readonly GameObject Object;
            internal readonly Component Text;

            internal LabelSlot(GameObject @object, Component text)
            {
                Object = @object;
                Text = text;
            }
        }

        private sealed class Snapshot
        {
            internal IEntityWithProductivityCounter Entity = null!;
            internal Vector3 Position;
            internal string Text = string.Empty;
            internal Color Color;
        }

        private readonly List<Snapshot> m_snapshots = new();
        private readonly List<LabelSlot> m_labels = new();
        private readonly Dictionary<Type, PropertyInfo?> m_statusProperties = new();
        private IEntitiesManager? m_entities;
        private Type? m_textMeshType;
        private PropertyInfo? m_textProperty;
        private PropertyInfo? m_colorProperty;
        private bool m_enabled;
        private bool m_dirty = true;
        private float m_nextRefresh;
        private float m_updateSeconds = 0.5f;
        private float m_renderDistance = 1500f;
        private float m_labelScale = 1f;

        internal void Initialize(IEntitiesManager entities, Type textMeshType)
        {
            m_entities = entities;
            m_textMeshType = textMeshType;
            m_textProperty = textMeshType.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            m_colorProperty = textMeshType.GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
            if (m_textProperty?.CanWrite != true || m_colorProperty?.CanWrite != true)
            {
                throw new MissingMemberException(textMeshType.FullName, "text/color");
            }
            m_enabled = TajsTweaksRuntimeState.EfficiencyOverlay;
        }

        internal void SetEnabled(bool enabled)
        {
            m_enabled = enabled;
            if (!enabled)
            {
                HideLabels();
            }
            else
            {
                m_dirty = true;
                m_nextRefresh = 0f;
            }
        }

        internal void ApplySettings()
        {
            m_updateSeconds = Mathf.Clamp((float)TajsTweaksRuntimeState.EfficiencyOverlayUpdateSeconds, 0.1f, 5f);
            m_renderDistance = Mathf.Clamp((float)TajsTweaksRuntimeState.EfficiencyOverlayRenderDistance, 100f, 2000f);
            m_labelScale = Mathf.Clamp((float)TajsTweaksRuntimeState.EfficiencyOverlayLabelScale, 0.5f, 2f);
            m_dirty = true;
        }

        private void Update()
        {
            if (!m_enabled || m_entities is null)
            {
                HideLabels();
                return;
            }

            if (m_dirty || Time.unscaledTime >= m_nextRefresh)
            {
                Refresh();
                m_nextRefresh = Time.unscaledTime + m_updateSeconds;
            }
        }

        private void LateUpdate()
        {
            if (!m_enabled || m_snapshots.Count == 0)
            {
                return;
            }

            Camera? camera = Camera.main;
            if (camera is null)
            {
                HideLabels();
                return;
            }

            float maxDistanceSquared = m_renderDistance * m_renderDistance;
            int count = Math.Min(m_snapshots.Count, m_labels.Count);
            for (int i = 0; i < count; i++)
            {
                Snapshot snapshot = m_snapshots[i];
                LabelSlot label = m_labels[i];
                Vector3 delta = snapshot.Position - camera.transform.position;
                if (delta.sqrMagnitude > maxDistanceSquared)
                {
                    label.Object.SetActive(false);
                    continue;
                }

                Vector3 screen = camera.WorldToViewportPoint(snapshot.Position);
                bool visible = screen.z > 0f && screen.x >= -MinimumViewportMargin && screen.x <= 1f + MinimumViewportMargin &&
                               screen.y >= -MinimumViewportMargin && screen.y <= 1f + MinimumViewportMargin;
                label.Object.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                label.Object.transform.position = snapshot.Position;
                label.Object.transform.rotation = camera.transform.rotation;
                float distance = delta.magnitude;
                float scale = Mathf.Clamp(m_labelScale * distance / ReferenceDistance, MinimumScale, MaximumScale);
                label.Object.transform.localScale = Vector3.one * scale;
            }

            for (int i = count; i < m_labels.Count; i++)
            {
                m_labels[i].Object.SetActive(false);
            }
        }

        private void Refresh()
        {
            m_dirty = false;
            m_snapshots.Clear();
            Camera? camera = Camera.main;
            float maxDistanceSquared = m_renderDistance * m_renderDistance;
            foreach (IEntityWithProductivityCounter entity in m_entities!.GetAllEntitiesOfType<IEntityWithProductivityCounter>())
            {
                if (m_snapshots.Count >= MaximumLabels || entity.IsDestroyed || entity is not IEntityWithPosition positioned ||
                    !IsIncluded(entity) ||
                    camera is not null && (positioned.Position3f.ToVector3() - camera.transform.position).sqrMagnitude > maxDistanceSquared)
                {
                    continue;
                }

                int percentage = EfficiencyOverlayPresentation.Percentage(entity.OngoingMonthlyData);
                string status = GetStatus(entity);
                m_snapshots.Add(
                    new Snapshot
                    {
                        Entity = entity,
                        Position = positioned.Position3f.ToVector3() + Vector3.up * LabelHeight,
                        Text = EfficiencyOverlayPresentation.Format(TajsTweaksRuntimeState.EfficiencyOverlayMode, percentage, status),
                        Color = EfficiencyOverlayPresentation.ColorFor(percentage),
                    });
            }

            for (int i = 0; i < m_snapshots.Count; i++)
            {
                LabelSlot label = GetLabel(i);
                m_textProperty!.SetValue(label.Text, m_snapshots[i].Text);
                m_colorProperty!.SetValue(label.Text, m_snapshots[i].Color);
                label.Object.SetActive(true);
            }
            for (int i = m_snapshots.Count; i < m_labels.Count; i++)
            {
                m_labels[i].Object.SetActive(false);
            }
        }

        private bool IsIncluded(IEntityWithProductivityCounter entity)
        {
            bool vehicle = entity is Vehicle;
            return vehicle ? TajsTweaksRuntimeState.EfficiencyOverlayVehicles : TajsTweaksRuntimeState.EfficiencyOverlayBuildings;
        }

        private string GetStatus(IEntityWithProductivityCounter entity)
        {
            if (entity.IsPaused)
            {
                return "Paused";
            }

            Type type = entity.GetType();
            if (!m_statusProperties.TryGetValue(type, out PropertyInfo? property))
            {
                property = type.GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public) ??
                           type.GetProperty("Status", BindingFlags.Instance | BindingFlags.Public) ??
                           type.GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
                m_statusProperties[type] = property;
            }

            try
            {
                return property?.GetValue(entity)?.ToString() ?? (entity.IsEnabled ? "Idle" : "Disabled");
            }
            catch
            {
                return "Unavailable";
            }
        }

        private LabelSlot GetLabel(int index)
        {
            while (m_labels.Count <= index)
            {
                var labelObject = new GameObject("Tajs efficiency label");
                labelObject.transform.SetParent(transform, false);
                Component text = labelObject.AddComponent(m_textMeshType!);
                SetTextProperty(text, "fontSize", 42);
                SetTextProperty(text, "characterSize", 0.12f);
                SetTextProperty(text, "fontStyle", ParseEnum(text, "fontStyle", "Bold"));
                SetTextProperty(text, "alignment", ParseEnum(text, "alignment", "Center"));
                SetTextProperty(text, "anchor", ParseEnum(text, "anchor", "MiddleCenter"));
                m_labels.Add(new LabelSlot(labelObject, text));
            }

            return m_labels[index];
        }

        private static object? ParseEnum(Component component, string propertyName, string value)
        {
            PropertyInfo? property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property is null ? null : Enum.Parse(property.PropertyType, value);
        }

        private static void SetTextProperty(Component component, string propertyName, object? value)
        {
            if (value is null)
            {
                return;
            }
            PropertyInfo? property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.CanWrite == true)
            {
                property.SetValue(component, value);
            }
        }

        private void HideLabels()
        {
            for (int i = 0; i < m_labels.Count; i++)
            {
                m_labels[i].Object.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < m_labels.Count; i++)
            {
                if (m_labels[i].Object is not null)
                {
                    Destroy(m_labels[i].Object);
                }
            }
            m_labels.Clear();
        }
    }
}
