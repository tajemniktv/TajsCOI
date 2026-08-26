// Taj's COI Mods | OverclockingInspectorPatch.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Factory.ComputingPower;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Maintenance;
using Mafi.Core.Population;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using EntityId = Mafi.Core.EntityId;
using UiButton = Mafi.Unity.UiToolkit.Library.Button;
using UiColumn = Mafi.Unity.UiToolkit.Library.Column;
using UiLabel = Mafi.Unity.UiToolkit.Library.Label;
using UiSlider = Mafi.Unity.UiToolkit.Library.Slider;
using UiTextField = Mafi.Unity.UiToolkit.Library.TextField;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    ///     Small native-inspector panel. It intentionally exposes bounded +/- controls instead of
    ///     duplicating the entire vanilla machine inspector; commands still go through the normal
    ///     input scheduler and the label displays the native effective speed after the command lands.
    /// </summary>
    internal static class OverclockingInspectorPatch
    {
        private sealed class State
        {
            internal IEntity? Entity;
            internal UiLabel Rate = null!;
            internal UiLabel Mode = null!;
            internal UiLabel Costs = null!;
            internal UiSlider Slider = null!;
            internal UiTextField Input = null!;
            internal int? PendingPercent;
        }

        private static readonly Dictionary<object, State> s_states = new();
        private static readonly Dictionary<Type, PropertyInfo> s_entityProperties = new();
        private static readonly Dictionary<Type, MethodInfo> s_addPanelMethods = new();
        private static readonly Dictionary<Type, FieldInfo> s_mainBodyFields = new();

        internal static void Install(Harmony harmony)
        {
            Assembly assembly = typeof(PanelWithHeader).Assembly;
            string[] inspectorNames =
            {
                "MachineInspector", "OreSortingPlantInspector", "OfficeBuildingInspector", "WasteSortingPlantInspector", "TransportInspector",
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
                ConstructorInfo? constructor = inspectorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault();
                if (entityProperty is null || addPanel is null || constructor is null)
                {
                    continue;
                }

                s_entityProperties[inspectorType] = entityProperty;
                s_addPanelMethods[inspectorType] = addPanel;
                FieldInfo? mainBody = FindField(inspectorType, "MainBody");
                if (mainBody is not null)
                {
                    s_mainBodyFields[inspectorType] = mainBody;
                }
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(OverclockingInspectorPatch), nameof(ConstructorPostfix)));
                MethodInfo? activate = FindLifecycleMethod(inspectorType, "Activate") ?? FindLifecycleMethod(inspectorType, "OnActivated");
                if (activate is not null)
                {
                    // Inspector implementations inherit the generic BaseInspector lifecycle
                    // method. Harmony must receive the declared/base definition, not the
                    // inherited dispatch view returned by reflection on the concrete inspector.
                    harmony.Patch(activate.GetBaseDefinition(), postfix: new HarmonyMethod(typeof(OverclockingInspectorPatch), nameof(ActivatedPostfix)));
                }

                patched++;
            }

            if (patched == 0)
            {
                throw new TypeLoadException("No supported overclocking inspector was found.");
            }
        }

        internal static void ConstructorPostfix(object __instance) => EnsurePanel(__instance);

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

            int current = s_states.TryGetValue(inspector, out State? state) && state.PendingPercent.HasValue
                ? state.PendingPercent.Value
                : TajsOverclockingFeature.Current.GetPercent(entity!.Id);
            QueueExact(inspector, current + delta);
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
                TajsOverclockingFeature feature = TajsOverclockingFeature.Current;
                int current = feature.GetPercent(state.Entity.Id);
                bool pending = state.PendingPercent.HasValue && state.PendingPercent.Value != current;
                if (!pending)
                {
                    state.PendingPercent = null;
                }

                int displayed = pending ? state.PendingPercent!.Value : current;
                OverclockEffectivePolicy policy = feature.GetEffectivePolicy(state.Entity.Id.Value);
                state.Rate.Value((displayed + "%").AsLoc());
                state.Slider.Range(policy.MinPercent, policy.MaxPercent).Value(displayed);
                state.Input.Text(displayed.ToString());
                string group = policy.GroupId < 0 ? string.Empty : " / group " + policy.GroupId;
                state.Mode.Value((pending ? "Pending" : (policy.Auto ? "Auto" : "Manual") + group).AsLoc());
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
                if (s_states.TryGetValue(inspector, out State? existing))
                {
                    IEntity? previous = existing.Entity;
                    existing.Entity = TryGetEntity(inspector, out IEntity? rebound) &&
                                      TajsOverclockingFeature.Current?.CanControl(rebound!.Id) == true
                        ? rebound
                        : null;
                    if (previous?.Id != existing.Entity?.Id)
                    {
                        existing.PendingPercent = null;
                    }
                    return;
                }

                if (!TryGetEntity(inspector, out IEntity? entity) ||
                    TajsOverclockingFeature.Current is null || !TajsOverclockingFeature.Current.CanControl(entity!.Id))
                {
                    return;
                }

                TajsOverclockingFeature feature = TajsOverclockingFeature.Current;
                OverclockEffectivePolicy policy = feature.GetEffectivePolicy(entity!.Id.Value);
                int current = feature.GetPercent(entity.Id);
                int step = Math.Max(1, TajsTweaksRuntimeState.OverclockAutoStepPercent);
                // Keep the panel at its intrinsic height. Ore sorting inspectors contain several
                // variable-height sections and shrink children when the inspector is constrained.
                PanelWithHeader panel = new PanelWithHeader("Overclocking".AsLoc()).FlexShrink(0f);
                UiLabel rate = new UiLabel((current + "%").AsLoc()).FontBold().Width(48.px());
                var mode = new UiLabel(string.Empty.AsLoc());
                var costs = new UiLabel(string.Empty.AsLoc());
                Slider slider = new UiSlider()
                    .Range(policy.MinPercent, policy.MaxPercent)
                    .ValueFormatter(Option<Func<Percent, LocStrFormatted>>.Create(_ => LocStrFormatted.Empty))
                    .Value(current);
                slider.RootElement.style.flexGrow = 1f;
                slider.RootElement.style.flexShrink = 1f;
                slider.RootElement.style.minWidth = 120f;

                TextField input = new UiTextField()
                    .Text(current.ToString())
                    .CharLimit(5)
                    .PositiveIntegersOnly();
                input.RootElement.style.width = 62f;
                input.RootElement.style.flexShrink = 0f;
                Row rateRow = new Row(3.pt()).AlignItemsCenter();
                rateRow.Add(new UiLabel("Requested rate".AsLoc()).Width(95.px()), rate, slider, input);

                Row buttonRow = new Row(3.pt()).Wrap().AlignItemsCenter();
                buttonRow.Add(new ButtonText(UiButton.General, ("-" + step + "%").AsLoc(), () => QueueRelative(inspector, -step)));
                buttonRow.Add(new ButtonText(UiButton.General, ("+" + step + "%").AsLoc(), () => QueueRelative(inspector, step)));
                buttonRow.Add(new ButtonText(UiButton.General, "Default".AsLoc(), () => Reset(inspector)));
                buttonRow.Add(new ButtonText(UiButton.General, "Auto".AsLoc(), () => ToggleAuto(inspector)));

                UiColumn content = new UiColumn(2.pt()).AlignItemsStretch();
                content.Add(rateRow, buttonRow, mode, costs);
                panel.BodyAdd(content);
                var state = new State
                {
                    Entity = entity,
                    Rate = rate,
                    Mode = mode,
                    Costs = costs,
                    Slider = slider,
                    Input = input,
                };
                s_states[inspector] = state;
                if (s_mainBodyFields.TryGetValue(inspector.GetType(), out FieldInfo? mainBodyField) &&
                    mainBodyField.GetValue(inspector) is UiColumn mainBody)
                {
                    // Native inspector panels are appended to MainBody; keeping the same order puts
                    // overclocking below the machine-specific controls instead of in the middle.
                    mainBody.Add(panel);
                }
                else
                {
                    s_addPanelMethods[inspector.GetType()].Invoke(inspector, new object[] { new UiComponent[] { panel } });
                }

                slider.OnValueChanged((_, newValue) =>
                {
                    int minimum = policy.MinPercent;
                    int maximum = policy.MaxPercent;
                    if (TajsOverclockingFeature.Current is not null &&
                        TryGetEntity(inspector, out IEntity? currentEntity))
                    {
                        OverclockEffectivePolicy currentPolicy =
                            TajsOverclockingFeature.Current.GetEffectivePolicy(currentEntity!.Id.Value);
                        minimum = currentPolicy.MinPercent;
                        maximum = currentPolicy.MaxPercent;
                    }

                    int value = Mathf.Clamp(Mathf.RoundToInt(newValue), minimum, maximum);
                    if (Mathf.Abs(newValue - value) > 0.001f)
                    {
                        slider.Value(value);
                    }

                    input.Text(value.ToString());
                    rate.Value((value + "%").AsLoc());
                    QueueExact(inspector, value);
                });
                input.OnEditEnd(text =>
                {
                    if (int.TryParse(text.Trim().TrimEnd('%'), out int value))
                    {
                        QueueExact(inspector, value);
                    }
                    else
                    {
                        Refresh(inspector);
                    }
                });
                Refresh(inspector);
                panel.RootElement.schedule.Execute(() => Refresh(inspector)).Every(250);
            }
            catch
            {
                // The inspector is optional; a UI seam failure must not disable gameplay patches.
            }
        }

        private static void QueueExact(object inspector, int percent)
        {
            if (TajsOverclockingFeature.Current is not TajsOverclockingFeature feature ||
                !TryGetEntity(inspector, out IEntity? entity))
            {
                return;
            }

            if (!feature.QueueSetManual(entity!.Id, percent, out _))
            {
                Refresh(inspector);
                return;
            }

            if (s_states.TryGetValue(inspector, out State? state))
            {
                OverclockEffectivePolicy policy = feature.GetEffectivePolicy(entity.Id.Value);
                int clamped = OverclockingMath.ClampPercent(percent, policy.MinPercent, policy.MaxPercent);
                state.PendingPercent = clamped;
                state.Rate.Value((clamped + "%").AsLoc());
                state.Slider.Range(policy.MinPercent, policy.MaxPercent).Value(clamped);
                state.Input.Text(clamped.ToString());
                state.Mode.Value("Pending".AsLoc());
            }
        }

        private static string FormatCosts(IEntity entity)
        {
            var values = new List<string>();
            if (entity is IElectricityConsumingEntity electricity)
            {
                values.Add("power " + electricity.PowerRequired.Format());
            }

            if (entity is IComputingConsumingEntity computing)
            {
                values.Add("computing " + computing.ComputingRequired.FormatShort());
            }

            if (entity is IEntityWithWorkers workers)
            {
                values.Add("workers " + workers.WorkersNeeded);
            }

            if (entity is Transport transport)
            {
                values.Add(TransportOverclockingPatches.DescribeCapacity(transport));
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

        private static FieldInfo? FindField(Type type, string name)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                FieldInfo? field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field is not null)
                {
                    return field;
                }
            }

            return null;
        }

        private static MethodInfo? FindAddPanelMethod(Type type)
        {
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                MethodInfo? method = current.GetMethod(
                    "AddPanelWithHeader",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(UiComponent[]) },
                    null);
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
