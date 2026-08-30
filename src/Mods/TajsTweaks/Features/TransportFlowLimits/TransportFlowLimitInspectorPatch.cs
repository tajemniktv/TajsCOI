// Taj's COI Mods | TransportFlowLimitInspectorPatch.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Factory.Transports;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UiButton = Mafi.Unity.UiToolkit.Library.Button;
using UiColumn = Mafi.Unity.UiToolkit.Library.Column;
using UiLabel = Mafi.Unity.UiToolkit.Library.Label;
using UiTextField = Mafi.Unity.UiToolkit.Library.TextField;

namespace TajsCOI.Tweaks.Features.TransportFlowLimits
{
    /// <summary>
    /// Adds a small value editor to the native transport inspector. Input is queued as a
    /// value-only command and applied on the simulation thread; unsupported inspector shapes fail
    /// open without affecting the receive patch.
    /// </summary>
    internal static class TransportFlowLimitInspectorPatch
    {
        private sealed class State
        {
            internal Transport Transport = null!;
            internal UiLabel Value = null!;
            internal UiTextField Input = null!;
            internal bool InputEditing;
        }

        private static readonly Dictionary<object, State> s_states = new();
        private static readonly Dictionary<Type, PropertyInfo> s_entityProperties = new();
        private static readonly Dictionary<Type, MethodInfo> s_addPanelMethods = new();
        private static readonly Dictionary<Type, FieldInfo> s_mainBodyFields = new();

        internal static bool IsInstalled { get; private set; }

        internal static void Install(Harmony harmony)
        {
            Assembly assembly = typeof(PanelWithHeader).Assembly;
            Type? inspectorType = assembly.GetTypes().FirstOrDefault(type => type.Name == "TransportInspector" && !type.IsAbstract);
            if (inspectorType is null)
            {
                throw new TypeLoadException("No supported transport inspector was found.");
            }

            PropertyInfo? entityProperty = FindProperty(inspectorType, "Entity");
            MethodInfo? addPanel = FindAddPanelMethod(inspectorType);
            ConstructorInfo? constructor = inspectorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
            if (entityProperty is null || addPanel is null || constructor is null)
            {
                throw new MissingMemberException(inspectorType.FullName, "Entity/AddPanelWithHeader");
            }

            s_entityProperties[inspectorType] = entityProperty;
            s_addPanelMethods[inspectorType] = addPanel;
            FieldInfo? mainBody = FindField(inspectorType, "MainBody");
            if (mainBody is not null)
            {
                s_mainBodyFields[inspectorType] = mainBody;
            }

            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TransportFlowLimitInspectorPatch), nameof(ConstructorPostfix)));
            MethodInfo? activate = FindLifecycleMethod(inspectorType, "Activate") ?? FindLifecycleMethod(inspectorType, "OnActivated");
            if (activate is not null)
            {
                harmony.Patch(activate.GetBaseDefinition(), postfix: new HarmonyMethod(typeof(TransportFlowLimitInspectorPatch), nameof(ActivatedPostfix)));
            }

            IsInstalled = true;
        }

        internal static void Reset()
        {
            s_states.Clear();
        }

        internal static void ConstructorPostfix(object __instance) => EnsurePanel(__instance);

        internal static void ActivatedPostfix(object __instance)
        {
            EnsurePanel(__instance);
            Refresh(__instance);
        }

        private static void EnsurePanel(object inspector)
        {
            try
            {
                if (s_states.TryGetValue(inspector, out State? existing))
                {
                    if (TryGetTransport(inspector, out Transport? rebound))
                    {
                        existing.Transport = rebound!;
                    }
                    return;
                }

                if (!TryGetTransport(inspector, out Transport? transport))
                {
                    return;
                }

                PanelWithHeader panel = new PanelWithHeader("Flow limit".AsLoc()).FlexShrink(0f);
                UiLabel value = new UiLabel(string.Empty.AsLoc()).FontBold();
                UiTextField input = new UiTextField().Text(CurrentText(transport!)).CharLimit(12);
                UiColumn content = new UiColumn(2.pt()).AlignItemsStretch();
                Row row = new Row(3.pt()).AlignItemsCenter();
                row.Add(new UiLabel("units/s".AsLoc()).Width(70.px()), input, value);
                row.Add(new ButtonText(UiButton.General, "Apply".AsLoc(), () => Queue(inspector)));
                row.Add(new ButtonText(UiButton.General, "Unlimited".AsLoc(), () => QueueUnlimited(inspector)));
                content.Add(row);
                panel.BodyAdd(content);

                var state = new State { Transport = transport!, Value = value, Input = input };
                s_states[inspector] = state;
                if (s_mainBodyFields.TryGetValue(inspector.GetType(), out FieldInfo? mainBodyField) &&
                    mainBodyField.GetValue(inspector) is UiColumn mainBody)
                {
                    mainBody.Add(panel);
                }
                else
                {
                    s_addPanelMethods[inspector.GetType()].Invoke(inspector, new object[] { new UiComponent[] { panel } });
                }

                input.OnFocus(() => state.InputEditing = true);
                input.OnEditEnd(_ =>
                {
                    state.InputEditing = false;
                    Queue(inspector);
                });
                Refresh(inspector);
                panel.RootElement.schedule.Execute(() => Refresh(inspector)).Every(250);
            }
            catch
            {
                // Optional UI seam: native inspection remains usable if a toolkit member changes.
            }
        }

        private static void Queue(object inspector)
        {
            if (!s_states.TryGetValue(inspector, out State? state) ||
                !TryReadLimit(state.Input.GetText(), out double limit) ||
                !TransportFlowLimitFeature.QueueSetConfiguredLimit(state.Transport.Id.Value, limit))
            {
                Refresh(inspector);
            }
        }

        private static void QueueUnlimited(object inspector)
        {
            if (s_states.TryGetValue(inspector, out State? state) &&
                TransportFlowLimitFeature.QueueSetConfiguredLimit(state.Transport.Id.Value, 0d))
            {
                state.Input.Text("0");
            }
            Refresh(inspector);
        }

        private static void Refresh(object inspector)
        {
            if (!s_states.TryGetValue(inspector, out State? state))
            {
                return;
            }

            string text = CurrentText(state.Transport);
            state.Value.Value(text.AsLoc());
            if (!state.InputEditing)
            {
                state.Input.Text(text == "unlimited" ? "0" : text.Replace(" units/s", string.Empty));
            }
        }

        private static string CurrentText(Transport transport) =>
            TransportFlowLimitFeature.TryGetConfiguredLimit(transport.Id.Value, out double value)
                ? value.ToString("0.###", CultureInfo.CurrentCulture) + " units/s"
                : "unlimited";

        private static bool TryReadLimit(string text, out double limit)
        {
            string input = (text ?? string.Empty).Trim();
            bool parsed = double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out limit) ||
                          double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out limit);
            return parsed && !double.IsNaN(limit) && !double.IsInfinity(limit) && limit >= 0d &&
                   limit <= TransportFlowLimitState.MaxLimitUnitsPerSecond;
        }

        private static bool TryGetTransport(object inspector, out Transport? transport)
        {
            transport = null;
            PropertyInfo? property = FindProperty(inspector.GetType(), "Entity");
            transport = property?.GetValue(inspector) as Transport;
            return transport is not null;
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
                MethodInfo? method = current.GetMethod("AddPanelWithHeader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(UiComponent[]) }, null);
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
