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
using Mafi.Core.Factory.Transports;
using Mafi.Localization;
using Mafi.Core.Maintenance;
using Mafi.Core.Population;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Mafi.Core.EntityId;
using UiButton = Mafi.Unity.UiToolkit.Library.Button;
using UiColumn = Mafi.Unity.UiToolkit.Library.Column;
using UiLabel = Mafi.Unity.UiToolkit.Library.Label;
using UiSlider = UnityEngine.UIElements.Slider;
using NativeTextField = UnityEngine.UIElements.TextField;

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
            internal UiLabel Rate = null!;
            internal UiLabel Mode = null!;
            internal UiLabel Costs = null!;
            internal UiSlider Slider = null!;
            internal NativeTextField Input = null!;
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
                "MachineInspector",
                "OreSortingPlantInspector",
                "OfficeBuildingInspector",
                "WasteSortingPlantInspector",
                "TransportInspector",
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
                OverclockEffectivePolicy policy = feature.GetEffectivePolicy(state.Entity.Id.Value);
                state.Rate.Value((current + "%").AsLoc());
                state.Slider.lowValue = policy.MinPercent;
                state.Slider.highValue = policy.MaxPercent;
                state.Slider.SetValueWithoutNotify(current);
                state.Input.SetValueWithoutNotify(current.ToString());
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

                TajsOverclockingFeature feature = TajsOverclockingFeature.Current;
                OverclockEffectivePolicy policy = feature.GetEffectivePolicy(entity!.Id.Value);
                int current = feature.GetPercent(entity.Id);
                int step = Math.Max(1, TajsTweaksRuntimeState.OverclockAutoStepPercent);
                var panel = new PanelWithHeader("Overclocking".AsLoc());
                var rate = new UiLabel((current + "%").AsLoc()).FontBold().Width(48.px());
                var mode = new UiLabel(string.Empty.AsLoc());
                var costs = new UiLabel(string.Empty.AsLoc());
                var slider = new UiSlider(policy.MinPercent, policy.MaxPercent)
                {
                    value = current,
                    pageSize = step,
                };
                slider.style.flexGrow = 1f;
                slider.style.flexShrink = 1f;
                slider.style.minWidth = 80f;
                slider.style.height = 22f;
                var sliderHost = new UiComponent();
                sliderHost.RootElement.style.flexGrow = 1f;
                sliderHost.RootElement.style.flexShrink = 1f;
                sliderHost.RootElement.style.minWidth = 80f;
                sliderHost.RootElement.Add(slider);

                var input = new NativeTextField
                {
                    value = current.ToString(),
                    maxLength = 5,
                };
                input.style.width = 62f;
                input.style.flexShrink = 0f;
                input.style.height = 24f;
                var rateRow = new Row(3.pt()).AlignItemsCenter();
                rateRow.Add(new UiLabel("Requested rate".AsLoc()).Width(95.px()));
                rateRow.Add(rate);
                rateRow.RootElement.Add(sliderHost.RootElement);
                rateRow.RootElement.Add(input);

                var buttonRow = new Row(3.pt()).Wrap().AlignItemsCenter();
                buttonRow.Add(new ButtonText(UiButton.General, ("-" + step + "%").AsLoc(), () => QueueRelative(inspector, -step)));
                buttonRow.Add(new ButtonText(UiButton.General, ("+" + step + "%").AsLoc(), () => QueueRelative(inspector, step)));
                buttonRow.Add(new ButtonText(UiButton.General, "Default".AsLoc(), () => Reset(inspector)));
                buttonRow.Add(new ButtonText(UiButton.General, "Auto".AsLoc(), () => ToggleAuto(inspector)));

                var content = new UiColumn(2.pt()).AlignItemsStretch();
                content.Add(rateRow, buttonRow, mode, costs);
                panel.BodyAdd(content);
                var state = new State { Entity = entity, Rate = rate, Mode = mode, Costs = costs, Slider = slider, Input = input };
                s_states[inspector] = state;
                if (s_mainBodyFields.TryGetValue(inspector.GetType(), out FieldInfo? mainBodyField) &&
                    mainBodyField.GetValue(inspector) is UiColumn mainBody)
                {
                    mainBody.InsertAt(0, panel);
                }
                else
                {
                    s_addPanelMethods[inspector.GetType()].Invoke(inspector, new object[] { new UiComponent[] { panel } });
                }

                slider.RegisterValueChangedCallback(change =>
                {
                    int value = Mathf.RoundToInt(change.newValue);
                    input.SetValueWithoutNotify(value.ToString());
                    rate.Value((value + "%").AsLoc());
                });
                slider.RegisterCallback<MouseUpEvent>(_ => ApplyExact(inspector, Mathf.RoundToInt(slider.value)));
                input.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        if (int.TryParse(input.value.Trim().TrimEnd('%'), out int value))
                        {
                            ApplyExact(inspector, value);
                        }

                        evt.StopPropagation();
                    }
                }, TrickleDown.TrickleDown);
                input.RegisterCallback<FocusOutEvent>(_ =>
                {
                    if (int.TryParse(input.value.Trim().TrimEnd('%'), out int value))
                    {
                        ApplyExact(inspector, value);
                    }
                });
                Refresh(inspector);
            }
            catch
            {
                // The inspector is optional; a UI seam failure must not disable gameplay patches.
            }
        }

        private static void ApplyExact(object inspector, int percent)
        {
            if (TajsOverclockingFeature.Current is not null && TryGetEntity(inspector, out IEntity? entity))
            {
                TajsOverclockingFeature.Current.QueueSetManual(entity!.Id, percent, out _);
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
