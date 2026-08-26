// Taj's COI Mods | TweaksStackerDesignationFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Terrain.Designation;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Displays only terrain designations in a bounded rectangle around the selected
    ///     stacker. It deliberately uses its own line pool and never activates or mutates the
    ///     global TerrainDesignationsRenderer.
    /// </summary>
    internal static class TweaksStackerDesignationFeature
    {
        private static WeakReference<StackerTower>? s_active;
        private static StackerDesignationOverlay? s_overlay;
        private static PropertyInfo? s_entityProperty;
        private static FieldInfo? s_topRightField;
        private static MethodInfo? s_addPanelMethod;
        private static readonly ConditionalWeakTable<object, ToggleState> s_toggles = new();

        private sealed class ToggleState
        {
            internal Toggle Toggle = null!;
        }

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            if (!resolver.TryResolve(out ITerrainDesignationsManager manager))
            {
                throw new InvalidOperationException("ITerrainDesignationsManager unavailable");
            }
            Type inspector = typeof(PanelWithHeader).Assembly.GetTypes().FirstOrDefault(x => x.Name == "StackerTowerInspector")
                             ?? throw new TypeLoadException("StackerTowerInspector");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type? type = inspector; type is not null; type = type.BaseType)
            {
                s_entityProperty ??= type.GetProperty("Entity", flags);
                s_topRightField ??= type.GetField("TopRightButtons", flags);
                s_addPanelMethod ??= type.GetMethod("AddPanelWithHeader", flags, null, new[] { typeof(UiComponent[]) }, null);
            }
            if (s_entityProperty is null || s_addPanelMethod is null)
            {
                throw new MissingMemberException(inspector.FullName, "Entity/AddPanelWithHeader");
            }
            foreach (ConstructorInfo constructor in inspector.GetConstructors(flags))
            {
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TweaksStackerDesignationFeature), nameof(ConstructorPostfix)));
            }
            PatchNoArg(harmony, inspector, "OnActivated", nameof(ActivatedPostfix));
            PatchNoArg(harmony, inspector, "OnDeactivated", nameof(DeactivatedPostfix));
            MethodInfo? renderUpdate = FindDeclaredByName(inspector, "RenderUpdate", flags);
            if (renderUpdate is not null)
            {
                harmony.Patch(renderUpdate, postfix: new HarmonyMethod(typeof(TweaksStackerDesignationFeature), nameof(ActivatedPostfix)));
            }

            // Create the scene-owned overlay only after every private seam has been validated and
            // patched. If installation fails, the host can roll back this Harmony owner without
            // leaving an orphaned GameObject behind.
            var owner = new GameObject("Tajs stacker designation overlay");
            s_overlay = owner.AddComponent<StackerDesignationOverlay>();
            s_overlay.Initialize(manager);
        }

        internal static void Dispose()
        {
            s_active = null;
            if (s_overlay is not null)
            {
                UnityEngine.Object.Destroy(s_overlay.gameObject);
                s_overlay = null;
            }
        }

        private static void PatchNoArg(Harmony harmony, Type type, string name, string postfix)
        {
            MethodInfo? method = FindNoArg(type, name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is not null)
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(TweaksStackerDesignationFeature), postfix));
            }
        }

        private static MethodInfo? FindNoArg(Type type, string name, BindingFlags flags)
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

        private static MethodInfo? FindDeclaredByName(Type type, string name, BindingFlags flags)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                MethodInfo? method = current.GetMethods(flags | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name == name);
                if (method is not null)
                {
                    return method;
                }
            }
            return null;
        }

        private static void ConstructorPostfix(object __instance)
        {
            try
            {
                if (__instance is null || s_topRightField?.GetValue(__instance) is not Row row || s_toggles.TryGetValue(__instance, out _))
                {
                    return;
                }
                Row line = new Row(6.pt()).AlignItemsCenter();
                var toggle = new Toggle(standalone: true);
                toggle.Value(TajsTweaksRuntimeState.StackerDesignationOverlay);
                toggle.OnValueChanged(value =>
                {
                    TajsTweaksRuntimeState.StackerDesignationOverlay = value;
                    if (value)
                    {
                        RefreshActive();
                    }
                    else
                    {
                        s_overlay?.Hide();
                    }
                });
                line.Add(toggle);
                var label = new Label(Localize("Designations on/off"));
                label.Tooltip(Localize("Shows only dig, level and dump designations in this stacker tower's working area."));
                line.Add(label);
                s_addPanelMethod!.Invoke(__instance, new object[] { new UiComponent[] { line } });
                s_toggles.Add(__instance, new ToggleState { Toggle = toggle });
            }
            catch
            {
                // This is optional inspector presentation; stacker operation remains native.
            }
        }

        private static void ActivatedPostfix(object __instance)
        {
            if (s_entityProperty?.GetValue(__instance) is StackerTower tower)
            {
                s_active = new WeakReference<StackerTower>(tower);
                RefreshActive();
            }
            if (s_toggles.TryGetValue(__instance, out ToggleState? state))
            {
                state.Toggle.Value(TajsTweaksRuntimeState.StackerDesignationOverlay);
            }
        }

        private static void DeactivatedPostfix()
        {
            s_active = null;
            s_overlay?.Hide();
        }

        internal static bool TryGetActive(out StackerTower? tower)
        {
            tower = null;
            return s_active is not null && s_active.TryGetTarget(out tower) && tower is not null;
        }

        private static void RefreshActive() => s_overlay?.MarkDirty();

        private static LocStrFormatted Localize(string text) =>
            LocalizationManager.CreateAlreadyLocalizedStr("TajsTweaksStacker_" + text.GetHashCode().ToString("X"), text).AsFormatted;
    }

    internal sealed class StackerDesignationOverlay : MonoBehaviour
    {
        private const int MaximumDesignations = 512;
        private readonly List<LineRenderer> m_lines = new();
        private ITerrainDesignationsManager? m_manager;
        private Material? m_material;
        private bool m_dirty = true;
        private int m_lastTowerId = -1;
        private float m_nextRefresh;

        internal void Initialize(ITerrainDesignationsManager manager) => m_manager = manager;

        internal void MarkDirty() => m_dirty = true;

        internal void Hide()
        {
            foreach (LineRenderer line in m_lines)
            {
                line.enabled = false;
            }
        }

        private void Update()
        {
            if (!TajsTweaksRuntimeState.StackerDesignationOverlay || m_manager is null ||
                TweaksStackerDesignationFeature.TryGetActive(out StackerTower? tower) is false || tower is null)
            {
                Hide();
                return;
            }

            if (m_dirty || tower.Id.Value != m_lastTowerId || Time.unscaledTime >= m_nextRefresh)
            {
                Refresh(tower);
                m_nextRefresh = Time.unscaledTime + 0.5f;
            }
        }

        private void Refresh(StackerTower tower)
        {
            m_dirty = false;
            m_lastTowerId = tower.Id.Value;
            int radius = Math.Min(
                512,
                Math.Max(
                    8,
                    tower.Prototype.MaxDumpRadius.Value +
                    tower.ConnectedRailSegmentsCount * tower.Prototype.RailProto.SegmentLength.Value + 4));
            Tile2i center = tower.CenterTile.Xy;
            IEnumerable<TerrainDesignation> designations;
            try
            {
                designations = m_manager!.SelectDesignationsInArea(
                    new Tile2i(center.X - radius, center.Y - radius),
                    new Tile2i(center.X + radius, center.Y + radius));
            }
            catch
            {
                Hide();
                return;
            }

            int index = 0;
            foreach (TerrainDesignation designation in designations.Where(IsStackerDesignation).Take(MaximumDesignations))
            {
                if (designation.IsDestroyed)
                {
                    continue;
                }
                LineRenderer line = GetLine(index++);
                line.positionCount = 5;
                line.SetPosition(0, designation.Origin3i.ToCenterVector3());
                line.SetPosition(1, designation.PlusX3i.ToCenterVector3());
                line.SetPosition(2, designation.PlusXy3i.ToCenterVector3());
                line.SetPosition(3, designation.PlusY3i.ToCenterVector3());
                line.SetPosition(4, designation.Origin3i.ToCenterVector3());
                line.startColor = line.endColor = ColorFor(designation);
                line.enabled = true;
            }
            for (int hidden = index; hidden < m_lines.Count; hidden++)
            {
                m_lines[hidden].enabled = false;
            }
        }

        private static bool IsStackerDesignation(TerrainDesignation designation)
        {
            string id = designation.ProtoId.Value;
            return id.IndexOf("Dump", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   id.IndexOf("Mine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   id.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private LineRenderer GetLine(int index)
        {
            while (m_lines.Count <= index)
            {
                var lineObject = new GameObject("Tajs stacker designation");
                lineObject.transform.SetParent(transform, false);
                var line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = false;
                line.widthMultiplier = 0.1f;
                line.numCapVertices = 1;
                line.material = m_material ??= new Material(Shader.Find("Sprites/Default"));
                m_lines.Add(line);
            }
            return m_lines[index];
        }

        private static Color ColorFor(TerrainDesignation designation)
        {
            string id = designation.ProtoId.Value;
            if (id.IndexOf("Dump", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(1f, 0.55f, 0.15f, 0.9f);
            }
            if (id.IndexOf("Mine", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(0.3f, 0.8f, 1f, 0.9f);
            }
            return new Color(0.7f, 0.4f, 1f, 0.9f);
        }

        private void OnDestroy()
        {
            if (m_material is not null)
            {
                Destroy(m_material);
                m_material = null;
            }
        }
    }
}
