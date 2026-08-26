// Taj's COI Mods | OverclockingInspectorPatch.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Factory.ComputingPower;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Factory.Machines;
using Mafi.Localization;
using Mafi.Core.Maintenance;
using Mafi.Core.Population;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using EntityId = Mafi.Core.EntityId;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    /// Small native-inspector panel. It intentionally exposes bounded +/- controls instead of
    /// duplicating the entire vanilla machine inspector; commands still go through the normal
    /// input scheduler and the label displays the native effective speed after the command lands.
    /// </summary>
    internal static class OverclockingInspectorPatch
    {
        private sealed class State
        {
            internal IEntity? Entity;
            internal Label Rate = null!;
            internal Label Mode = null!;
            internal Label Costs = null!;
        }

        private static readonly Dictionary<object, State> s_states = new();
        private static readonly Dictionary<Type, PropertyInfo> s_entityProperties = new();
        private static readonly Dictionary<Type, MethodInfo> s_addPanelMethods = new();

        internal static void Install(Harmony harmony)
        {
            Assembly assembly = typeof(PanelWithHeader).Assembly;
            string[] inspectorNames =
            {
                "MachineInspector",
                "OreSortingPlantInspector",
                "OfficeBuildingInspector",
                "WasteSortingPlantInspector",
            };
            int patched = 0;
            foreach (string inspectorName in inspectorNames)
            {
                Type? inspectorType = assembly.GetTypes().FirstOrDefault(type => type.Name == inspectorName && !type.IsAbstract);
                if (inspectorType is null)
                {
                    continue;
                }

                PropertyInfo? entityProperty = FindProperty(inspectorType, "Entity");
                MethodInfo? addPanel = FindAddPanelMethod(inspectorType);
                ConstructorInfo? constructor = inspectorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
                if (entityProperty is null || addPanel is null || constructor is null)
                {
                    continue;
                }

                s_entityProperties[inspectorType] = entityProperty;
                s_addPanelMethods[inspectorType] = addPanel;
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(OverclockingInspectorPatch), nameof(ConstructorPostfix)));
                MethodInfo? activate = FindLifecycleMethod(inspectorType, "Activate") ?? FindLifecycleMethod(inspectorType, "OnActivated");
                if (activate is not null)
                {
                    harmony.Patch(activate, postfix: new HarmonyMethod(typeof(OverclockingInspectorPatch), nameof(ActivatedPostfix)));
                }

                patched++;
            }

            if (patched == 0)
            {
                throw new TypeLoadException("No supported overclocking inspector was found.");
            }
        }

        internal static void ConstructorPostfix(object __instance)
        {
            EnsurePanel(__instance);
        }

        internal static void ActivatedPostfix(object __instance)
        {
            // BaseInspector assigns Entity in Activate(IEntity), after the inspector constructor.
            // Build lazily here as well so the panel is present on the first real inspection.
            EnsurePanel(__instance);
            Refresh(__instance);
        }

        internal static void RefreshAllForEntity(EntityId id)
        {
            foreach (KeyValuePair<object, State> pair in s_states.ToArray())
            {
                if (pair.Value.Entity is not null && pair.Value.Entity.Id == id)
                {
                    Refresh(pair.Key);
                }
            }
        }

        private static void QueueRelative(object inspector, int delta)
        {
            if (TajsOverclockingFeature.Current is null || !TryGetEntity(inspector, out IEntity? entity))
            {
                return;
            }

            int current = TajsOverclockingFeature.Current.GetPercent(entity!.Id);
            TajsOverclockingFeature.Current.QueueSetManual(entity.Id, current + delta, out _);
            Refresh(inspector);
        }

        private static void Reset(object inspector)
        {
            if (TajsOverclockingFeature.Current is not null && TryGetEntity(inspector, out IEntity? entity))
            {
                TajsOverclockingFeature.Current.Reset(entity!.Id, out _);
                Refresh(inspector);
            }
        }

        private static void ToggleAuto(object inspector)
        {
            if (TajsOverclockingFeature.Current is not null && TryGetEntity(inspector, out IEntity? entity))
            {
                bool enabled = !TajsOverclockingFeature.Current.IsAuto(entity!.Id);
                TajsOverclockingFeature.Current.SetAuto(entity.Id, enabled, null, null, out _);
                Refresh(inspector);
            }
        }

        private static void Refresh(object inspector)
        {
            if (!s_states.TryGetValue(inspector, out State? state) || state.Entity is null || TajsOverclockingFeature.Current is null)
            {
                return;
            }

            try
            {
                state.Rate.Value((TajsOverclockingFeature.Current.GetPercent(state.Entity.Id) + "%").AsLoc());
                OverclockEffectivePolicy policy = TajsOverclockingFeature.Current.GetEffectivePolicy(state.Entity.Id.Value);
                string group = policy.GroupId < 0 ? string.Empty : " / group " + policy.GroupId;
                state.Mode.Value(((policy.Auto ? "Auto" : "Manual") + group).AsLoc());
                state.Costs.Value(FormatCosts(state.Entity).AsLoc());
            }
            catch
            {
            }
        }

        private static void EnsurePanel(object inspector)
        {
            try
            {
                if (s_states.ContainsKey(inspector) || !TryGetEntity(inspector, out IEntity? entity) ||
                    TajsOverclockingFeature.Current is null || !TajsOverclockingFeature.Current.CanControl(entity!.Id))
                {
                    return;
                }

                var panel = new PanelWithHeader("Overclocking".AsLoc());
                var rate = new Label("100%".AsLoc()).FontBold();
                var mode = new Label(string.Empty.AsLoc());
                var costs = new Label(string.Empty.AsLoc());
                var row = new Row(4.pt()).Wrap();
                row.Add(new Label("Requested rate".AsLoc()));
                row.Add(rate);
                row.Add(new ButtonText(Button.General, "-5%".AsLoc(), () => QueueRelative(inspector, -5)));
                row.Add(new ButtonText(Button.General, "+5%".AsLoc(), () => QueueRelative(inspector, 5)));
                row.Add(new ButtonText(Button.General, "Default".AsLoc(), () => Reset(inspector)));
                row.Add(new ButtonText(Button.General, "Auto".AsLoc(), () => ToggleAuto(inspector)));
                panel.BodyAdd(row, mode, costs);
                s_states[inspector] = new State { Entity = entity, Rate = rate, Mode = mode, Costs = costs };
                s_addPanelMethods[inspector.GetType()].Invoke(inspector, new object[] { new UiComponent[] { panel } });
                Refresh(inspector);
            }
            catch
            {
                // The inspector is optional; a UI seam failure must not disable gameplay patches.
            }
        }

        private static string FormatCosts(IEntity entity)
        {
            var values = new List<string>();
            if (entity is IElectricityConsumingEntity electricity)
            {
                values.Add("power " + electricity.PowerRequired.Value + " kW");
            }

            if (entity is IComputingConsumingEntity computing)
            {
                values.Add("computing " + computing.ComputingRequired.Value);
            }

            if (entity is IEntityWithWorkers workers)
            {
                values.Add("workers " + workers.WorkersNeeded);
            }

            if (entity is IMaintainedEntity maintenance && maintenance.MaintenanceCosts.MaintenancePerMonth.IsPositive)
            {
                values.Add("maintenance " + maintenance.MaintenanceCosts.MaintenancePerMonth.Value + "/month");
            }

            return values.Count == 0 ? "Effective costs: none reported" : "Effective costs: " + string.Join(" | ", values);
        }

        private static bool TryGetEntity(object inspector, out IEntity? entity)
        {
            entity = null;
            if (!s_entityProperties.TryGetValue(inspector.GetType(), out PropertyInfo? property))
            {
                return false;
            }

            entity = property.GetValue(inspector) as IEntity;
            return entity is not null;
        }

        private static PropertyInfo? FindProperty(Type type, string name)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                PropertyInfo? property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is not null)
                {
                    return property;
                }
            }

            return null;
        }

        private static MethodInfo? FindAddPanelMethod(Type type)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                MethodInfo? method = current.GetMethod("AddPanelWithHeader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(UiComponent[]) }, null);
                if (method is not null)
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo? FindLifecycleMethod(Type type, string name)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                MethodInfo? method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (method is not null)
                {
                    return method;
                }
            }

            return null;
        }
    }
}
