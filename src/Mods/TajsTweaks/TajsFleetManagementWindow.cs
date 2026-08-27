// Taj's COI Mods | TajsFleetManagementWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Ui;
using UnityEngine;
using UnityEngine.UIElements;
using Column = Mafi.Unity.UiToolkit.Library.Column;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using TextField = Mafi.Unity.UiToolkit.Library.TextField;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Native fleet window. It only reads live vehicle/depot state and delegates every action
    ///     to the same normal input commands exposed by TajsTweaksFeatureHost.
    /// </summary>
    internal sealed class TajsFleetManagementWindow : Window
    {
        private sealed class VehicleGroup
        {
            internal string PrototypeId = string.Empty;
            internal int Total;
            internal int Assigned;
            internal int Scrap;
            internal int Replacement;
            internal int Queued;
            internal int Problem;
            internal readonly List<Vehicle> Vehicles = new();
        }

        private readonly TajsTweaksFeatureHost m_host;
        private readonly IEntitiesManager m_entities;
        private readonly UiRoot m_uiRoot;
        private readonly Column m_groups;
        private readonly Label m_status;
        private readonly DataTableModel<VehicleGroup> m_groupTable;
        private readonly TextField m_source;
        private readonly TextField m_target;
        private readonly TextField m_count;
        private readonly TextField m_policy;
        private readonly IVisualElementScheduledItem? m_refreshSchedule;
        private float m_lastActivityTime;
        private float m_nextRefreshTime;
        private string m_confirmationKey = string.Empty;

        private static readonly object s_memberAccessorGate = new();
        private static readonly Dictionary<Type, Dictionary<string, MemberAccessor>> s_memberAccessors = new();

        internal TajsFleetManagementWindow(
            TajsTweaksFeatureHost host,
            IEntitiesManager entities,
            UiRoot uiRoot)
            : base("Fleet management".AsLoc())
        {
            m_host = host;
            m_entities = entities;
            m_uiRoot = uiRoot;
            m_groupTable = new DataTableModel<VehicleGroup>(
                new[]
                {
                    DataTableColumn<VehicleGroup>.CreateText(
                        "prototype",
                        "Prototype",
                        group => group.PrototypeId,
                        sortable: false),
                    DataTableColumn<VehicleGroup>.Create(
                        "total",
                        "Total",
                        group => group.Total,
                        sortable: false),
                    DataTableColumn<VehicleGroup>.Create(
                        "assigned",
                        "Assigned",
                        group => group.Assigned,
                        sortable: false),
                },
                group => group.PrototypeId,
                DataTableSelectionMode.None);
            WindowSize(new Px(940f), new Px(650f));

            Panel panel = new Panel().BodyGap(new Px(5f));
            Row header = new Row(5.pt()).AlignItemsCenter();
            header.Add(new Label("Bulk operation".AsLoc()).FontBold());
            m_source = new TextField().Placeholder("Source prototype ID".AsLoc()).MaxWidth(new Px(180f));
            m_target = new TextField().Placeholder("Replacement prototype ID".AsLoc()).MaxWidth(new Px(210f));
            m_count = new TextField().Text("1").MaxWidth(new Px(90f));
            m_policy = new TextField().Text("unassigned-first").Placeholder("Filter: any/assigned/unassigned-first".AsLoc()).MaxWidth(new Px(220f));
            m_source.OnEditEnd(_ => MarkActivity());
            m_target.OnEditEnd(_ => MarkActivity());
            m_count.OnEditEnd(_ => MarkActivity());
            m_policy.OnEditEnd(_ => MarkActivity());
            header.Add(m_source);
            header.Add(m_target);
            header.Add(m_count);
            header.Add(m_policy);
            header.Add(
                MakeButton(
                    "Order",
                    () => ConfirmAndRun(
                        "order",
                        () =>
                            m_host.FleetOrder(m_source.GetText(), m_count.GetText(), "CONFIRM"))));
            header.Add(
                MakeButton(
                    "Scrap",
                    () => ConfirmAndRun(
                        "scrap",
                        () =>
                            m_host.FleetScrapType(m_source.GetText(), m_count.GetText(), "CONFIRM", m_policy.GetText()))));
            header.Add(
                MakeButton(
                    "Replace",
                    () => ConfirmAndRun(
                        "replace",
                        () =>
                            m_host.FleetReplaceType(m_source.GetText(), m_target.GetText(), m_count.GetText(), "CONFIRM", m_policy.GetText()))));
            header.Add(MakeButton("Refresh", ManualRefresh));
            panel.Add(header);

            m_status = new Label(string.Empty.AsLoc());
            panel.Add(m_status);
            m_groups = new Column(3.pt()).AlignItemsStretch();
            var scroll = new ScrollColumn();
            scroll.Add(m_groups);
            panel.Add(scroll);
            Body.Add(panel);

            MarkActivity();
            m_refreshSchedule = RootElement.schedule.Execute(RefreshIfDue).Every(1000L);
            RefreshNow();
            OnCloseStart += _ => m_refreshSchedule?.Pause();
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            Open(m_uiRoot);
        }

        private ButtonText MakeButton(string text, Action action)
        {
            var button = new ButtonText(
                Mafi.Unity.UiToolkit.Library.Button.General,
                text.AsLoc(),
                () =>
                {
                    MarkActivity();
                    action();
                });
            button.Width(new Px(90f));
            return button;
        }

        private void MarkActivity() => m_lastActivityTime = Time.realtimeSinceStartup;

        private void ManualRefresh()
        {
            MarkActivity();
            RefreshNow();
        }

        private void RefreshIfDue()
        {
            if (!IsOpen)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < m_nextRefreshTime)
            {
                return;
            }

            Refresh();
            bool active = now - m_lastActivityTime <= 10f;
            m_nextRefreshTime = now + (active ? 1f : 5f);
        }

        private void RefreshNow()
        {
            Refresh();
            m_nextRefreshTime = Time.realtimeSinceStartup + 1f;
        }

        private void ConfirmAndRun(string key, Func<string> action)
        {
            if (!string.Equals(m_confirmationKey, key, StringComparison.Ordinal))
            {
                m_confirmationKey = key;
                m_status.Value(("Review the selected fleet operation, then click " + key + " again to confirm.").AsLoc());
                return;
            }

            m_confirmationKey = string.Empty;
            m_status.Value(action().AsLoc());
            RefreshNow();
        }

        private void Refresh()
        {
            try
            {
                m_groups.Clear();
                Vehicle[] vehicles = m_entities.GetAllEntitiesOfType<Vehicle>()
                    .Where(vehicle => vehicle is not null && !vehicle.IsDestroyed)
                    .ToArray();
                var groups = new Dictionary<string, VehicleGroup>(StringComparer.Ordinal);
                foreach (Vehicle vehicle in vehicles)
                {
                    string id = vehicle.Prototype.Id.Value;
                    if (!groups.TryGetValue(id, out VehicleGroup? group))
                    {
                        group = new VehicleGroup { PrototypeId = id };
                        groups[id] = group;
                    }

                    group.Total++;
                    group.Assigned += vehicle.AssignedTo.HasValue ? 1 : 0;
                    group.Scrap += vehicle.IsOnWayToDepotForScrap ? 1 : 0;
                    group.Replacement += vehicle.IsOnWayToDepotForReplacement || vehicle.ReplaceQueued ? 1 : 0;
                    group.Problem += vehicle is Truck truck && truck.IsCannotDeliverNotificationActive ? 1 : 0;
                    group.Vehicles.Add(vehicle);
                }

                foreach (VehicleDepotBase depot in m_entities.GetAllEntitiesOfType<VehicleDepotBase>())
                {
                    AddQueueCounts(depot.BuildQueue, groups, queued: true);
                    AddQueueCounts(depot.ReplaceQueue, groups, queued: false);
                }

                VehicleGroup[] orderedGroups = groups.Values
                    .OrderByDescending(value => value.Total)
                    .ThenBy(value => value.PrototypeId, StringComparer.Ordinal)
                    .ToArray();
                m_groupTable.SetRows(orderedGroups);
                foreach (DataTableRow<VehicleGroup> tableRow in m_groupTable.GetVisibleRows())
                {
                    m_groups.Add(BuildGroupRow(tableRow.Value));
                }

                int assigned = vehicles.Count(vehicle => vehicle.AssignedTo.HasValue);
                int scrap = vehicles.Count(vehicle => vehicle.IsOnWayToDepotForScrap);
                int replacements = vehicles.Count(vehicle => vehicle.IsOnWayToDepotForReplacement || vehicle.ReplaceQueued);
                int problems = vehicles.Count(vehicle => vehicle is Truck truck && truck.IsCannotDeliverNotificationActive);
                int queued = groups.Values.Sum(group => group.Queued);
                m_status.Value(
                    (vehicles.Length + " vehicles | assigned " + assigned + " | scrap " + scrap +
                     " | replacement " + replacements + " | queued " + queued + " | problems " + problems +
                     " | batch limit " + TajsTweaksRuntimeState.FleetBatchLimit).AsLoc());
            }
            catch
            {
                m_status.Value("Fleet state is unavailable in this scene.".AsLoc());
            }
        }

        private static void AddQueueCounts(object queue, Dictionary<string, VehicleGroup> groups, bool queued)
        {
            if (queue is not IEnumerable entries)
            {
                return;
            }

            foreach (object entry in entries)
            {
                DrivingEntityProto? proto = entry as DrivingEntityProto ?? ReadMember(entry, "Proto") as DrivingEntityProto;
                if (proto is null)
                {
                    continue;
                }

                if (!groups.TryGetValue(proto.Id.Value, out VehicleGroup? group))
                {
                    group = new VehicleGroup { PrototypeId = proto.Id.Value };
                    groups[proto.Id.Value] = group;
                }

                if (queued)
                {
                    group.Queued++;
                }
                else
                {
                    group.Replacement++;
                }
            }
        }

        private Row BuildGroupRow(VehicleGroup group)
        {
            Row row = new Row(4.pt()).AlignItemsCenter();
            row.RootElement.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            row.Add(new Label(group.PrototypeId.AsLoc()).Width(new Px(220f)));
            row.Add(new Label(("total " + group.Total + "  assigned " + group.Assigned).AsLoc()).Width(new Px(180f)));
            row.Add(new Label(("scrap " + group.Scrap + "  replacement " + group.Replacement).AsLoc()).Width(new Px(220f)));
            row.Add(new Label(("queued " + group.Queued + "  problem " + group.Problem).AsLoc()).Width(new Px(180f)));

            ButtonText scrap = MakeButton(
                "Scrap",
                () => ConfirmAndRun(
                    "scrap:" + group.PrototypeId,
                    () => m_host.FleetScrapType(group.PrototypeId, GetBatchText(group), "CONFIRM")));
            row.Add(scrap);
            ButtonText replace = MakeButton(
                "Replace",
                () => ConfirmAndRun(
                    "replace:" + group.PrototypeId,
                    () => m_host.FleetReplaceType(group.PrototypeId, m_target.GetText(), GetBatchText(group), "CONFIRM", m_policy.GetText())));
            row.Add(replace);
            ButtonText cancelScrap = MakeButton(
                "Cancel scrap",
                () => ConfirmAndRun(
                    "cancel-scrap:" + group.PrototypeId,
                    () => CancelGroup(group, "scrap")));
            row.Add(cancelScrap);
            ButtonText cancelReplace = MakeButton(
                "Cancel replace",
                () => ConfirmAndRun(
                    "cancel-replace:" + group.PrototypeId,
                    () => CancelGroup(group, "replace")));
            row.Add(cancelReplace);
            return row;
        }

        private static string GetBatchText(VehicleGroup group) => Math.Max(1, group.Total).ToString(CultureInfo.InvariantCulture);

        private string CancelGroup(VehicleGroup group, string operation)
        {
            string ids = string.Join(
                ",",
                group.Vehicles
                    .Where(vehicle => operation == "scrap" ? vehicle.IsOnWayToDepotForScrap : vehicle.IsOnWayToDepotForReplacement || vehicle.ReplaceQueued)
                    .Take(TajsTweaksRuntimeState.FleetBatchLimit)
                    .Select(vehicle => vehicle.Id.Value.ToString(CultureInfo.InvariantCulture)));
            return string.IsNullOrEmpty(ids)
                ? "No pending " + operation + " requests for " + group.PrototypeId + "."
                : m_host.FleetCancel(operation, ids, "CONFIRM");
        }

        private sealed class MemberAccessor
        {
            private readonly PropertyInfo? m_property;
            private readonly FieldInfo? m_field;

            internal MemberAccessor(Type type, string name)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                m_property = type.GetProperty(name, flags);
                m_field = type.GetField(name, flags);
            }

            internal object? GetValue(object instance) =>
                m_property?.GetValue(instance) ?? m_field?.GetValue(instance);
        }

        private static object? ReadMember(object value, string name)
        {
            Type type = value.GetType();
            MemberAccessor accessor;
            lock (s_memberAccessorGate)
            {
                if (!s_memberAccessors.TryGetValue(type, out Dictionary<string, MemberAccessor>? members))
                {
                    members = new Dictionary<string, MemberAccessor>(StringComparer.Ordinal);
                    s_memberAccessors[type] = members;
                }

                if (!members.TryGetValue(name, out accessor!))
                {
                    // Resolve each queue-entry shape once. The null result remains the
                    // compatibility fallback when a supported COI version exposes neither a
                    // Proto property nor a Proto field.
                    accessor = new MemberAccessor(type, name);
                    members[name] = accessor;
                }
            }
            return accessor.GetValue(value);
        }
    }
}
