// Taj's COI Mods | TajsDashboardMetadataPanel.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using TajsCOI.Common.Metadata;
using TajsCOI.Common.Ui;
using Button = Mafi.Unity.UiToolkit.Library.Button;
using Column = Mafi.Unity.UiToolkit.Library.Column;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using Panel = Mafi.Unity.UiToolkit.Library.Panel;
using Row = Mafi.Unity.UiToolkit.Library.Row;
using TextField = Mafi.Unity.UiToolkit.Library.TextField;

namespace TajsCOI.Core.Settings
{
    internal static class TajsDashboardMetadataPanel
    {
        internal static Panel Build(
            IEntityMetadataService metadata,
            Action<string, Label> executeCommand,
            Action queueRefresh)
        {
            Panel panel = TajsDashboardUi.Card(
                "Entity metadata groups",
                "Save-scoped groups organize aliases and notes. Use the rectangle action to assign a group to live entities; selection is disabled for locked groups.");
            IReadOnlyList<EntityMetadataGroup> groups = metadata.GetGroupSnapshot();
            IReadOnlyList<EntityMetadataRecord> records = metadata.GetEntityMetadataSnapshot();
            Dictionary<string, int> members = records
                .Where(record => record.GroupId is not null)
                .GroupBy(record => record.GroupId!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            TextField name = new TextField().Placeholder("New group name".AsLoc()).MaxWidth(220.px());
            TextField color = new TextField().Placeholder("Color #66C2A5".AsLoc()).MaxWidth(150.px());
            Label createFeedback = new Label().FontSize(11).Hide();
            Row createRow = new Row(3.pt()).AlignItemsCenter();
            createRow.Add(
                name,
                color,
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Create group",
                    "Assets/Unity/UserInterface/General/Configure.svg",
                    () =>
                    {
                        if (metadata.TryCreateGroup(name.GetText(), color.GetText(), out _, out string error))
                        {
                            createFeedback.Value("Metadata group created.".AsLoc()).Show();
                            name.Text(string.Empty);
                            color.Text(string.Empty);
                            queueRefresh();
                        }
                        else
                        {
                            createFeedback.Value(("Group was not created: " + error).AsLoc()).Show();
                        }
                    }),
                createFeedback);
            panel.Body.Add(createRow);

            if (groups.Count == 0)
            {
                panel.Body.Add(new Label("No metadata groups exist yet.".AsLoc()).FontSize(11));
            }
            else
            {
                var tableModel = new DataTableModel<EntityMetadataGroup>(
                    new[]
                    {
                        DataTableColumn<EntityMetadataGroup>.CreateText(
                            "name",
                            "Group",
                            group => group.Name,
                            width: DataTableColumnWidth.Constrained(180, 320),
                            visibilityPriority: 10),
                        DataTableColumn<EntityMetadataGroup>.CreateText(
                            "members",
                            "Members",
                            group => members.TryGetValue(group.GroupId, out int count)
                                ? count.ToString(CultureInfo.InvariantCulture)
                                : "0",
                            width: DataTableColumnWidth.Fixed(80),
                            alignment: DataTableColumnAlignment.End,
                            visibilityPriority: 9),
                        DataTableColumn<EntityMetadataGroup>.CreateText(
                            "color",
                            "Color",
                            group => group.Color,
                            width: DataTableColumnWidth.Fixed(110),
                            visibilityPriority: 8),
                        DataTableColumn<EntityMetadataGroup>.CreateText(
                            "lock",
                            "Lock",
                            group => group.Locked ? "Locked" : "Open",
                            width: DataTableColumnWidth.Fixed(90),
                            visibilityPriority: 7),
                    },
                    group => group.GroupId,
                    DataTableSelectionMode.Single);
                var table = new TajsDataTable<EntityMetadataGroup>(tableModel);
                table.Refresh(groups);
                table.SetAvailableWidth(760f);
                panel.Body.Add(table);

                foreach (EntityMetadataGroup group in groups)
                {
                    Label feedback = new Label().FontSize(11).Hide();
                    TextField groupName = new TextField().Text(group.Name).Placeholder("Group name".AsLoc()).MaxWidth(190.px());
                    TextField groupOrder = new TextField().Text(group.Order.ToString(CultureInfo.InvariantCulture)).Placeholder("Order".AsLoc()).MaxWidth(75.px());
                    TextField groupColor = new TextField().Text(group.Color).Placeholder("Color".AsLoc()).MaxWidth(105.px());
                    Row actions = new Row(2.pt()).AlignItemsCenter();
                    actions.Add(
                        groupName,
                        groupOrder,
                        groupColor,
                        TajsDashboardUi.ActionButton(
                            Button.Area,
                            "Save group",
                            "Assets/Unity/UserInterface/General/Save.svg",
                            () =>
                            {
                                if (!int.TryParse(groupOrder.GetText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) || order < 0)
                                {
                                    feedback.Value("Group order must be a non-negative integer.".AsLoc()).Show();
                                    return;
                                }

                                if (metadata.TryUpdateGroup(group.GroupId, groupName.GetText(), order, groupColor.GetText(), group.Locked, out string error))
                                {
                                    feedback.Value("Group updated.".AsLoc()).Show();
                                    queueRefresh();
                                }
                                else
                                {
                                    feedback.Value(("Group was not updated: " + error).AsLoc()).Show();
                                }
                            }),
                        TajsDashboardUi.ActionButton(
                            Button.Area,
                            "Pick rectangle",
                            "Assets/Unity/UserInterface/General/MapBounds.svg",
                            () => executeCommand("tajs_metadata_group_pick " + group.GroupId, feedback)),
                        TajsDashboardUi.ActionButton(
                            Button.Area,
                            group.Locked ? "Unlock" : "Lock",
                            "Assets/Unity/UserInterface/General/Configure.svg",
                            () =>
                            {
                                if (metadata.TryUpdateGroup(group.GroupId, null, group.Order, null, !group.Locked, out string error))
                                {
                                    feedback.Value((group.Name + ": " + (!group.Locked ? "locked" : "unlocked") + ".").AsLoc()).Show();
                                    queueRefresh();
                                }
                                else
                                {
                                    feedback.Value((group.Name + ": " + error).AsLoc()).Show();
                                }
                            }));
                    if (!group.Locked)
                    {
                        actions.Add(
                            TajsDashboardUi.ActionButton(
                                Button.Warning,
                                "Delete",
                                "Assets/Unity/UserInterface/General/Cancel.svg",
                                () =>
                                {
                                    if (metadata.TryDeleteGroup(group.GroupId))
                                    {
                                        queueRefresh();
                                    }
                                    else
                                    {
                                        feedback.Value((group.Name + ": delete failed.").AsLoc()).Show();
                                    }
                                }));
                    }

                    Row row = new Row(3.pt()).AlignItemsCenter();
                    row.Add(
                        new Label((group.Name + " · " + group.GroupId).AsLoc()).FontSize(11).FlexGrow(1f),
                        actions,
                        feedback);
                    panel.Body.Add(row);
                }
            }

            panel.Body.Add(new Label("Entity metadata records".AsLoc()).FontBold().FontSize(14).MarginTop(6.pt()));
            if (records.Count == 0)
            {
                panel.Body.Add(new Label("No aliases, notes, or memberships are saved yet.".AsLoc()).FontSize(11));
                return panel;
            }

            Dictionary<string, string> groupNames = groups.ToDictionary(group => group.GroupId, group => group.Name, StringComparer.Ordinal);
            foreach (EntityMetadataRecord record in records)
            {
                Label feedback = new Label().FontSize(11).Hide();
                string groupText = record.GroupId is not null && groupNames.TryGetValue(record.GroupId, out string? groupName)
                    ? groupName
                    : "Ungrouped";
                string aliasText = record.Alias.Length == 0 ? "(no alias)" : record.Alias;
                string noteText = record.Note.Length == 0 ? "(no note)" : record.Note;
                Column details = new Column(1.pt()).FlexGrow(1f).MinWidth(0.px());
                details.Add(
                    new Label((record.Identity + " · " + groupText).AsLoc()).FontBold(),
                    new Label(("Alias: " + aliasText).AsLoc()).FontSize(11),
                    new Label(("Note: " + noteText).AsLoc()).FontSize(11));
                Row actions = new Row(2.pt()).AlignItemsCenter();
                if (record.GroupId is not null)
                {
                    actions.Add(
                        TajsDashboardUi.ActionButton(
                            Button.Area,
                            "Ungroup",
                            "Assets/Unity/UserInterface/General/Cancel.svg",
                            () =>
                            {
                                if (metadata.TrySetEntityMetadata(record.Identity, record.Alias, record.Note, null, out string error))
                                {
                                    queueRefresh();
                                }
                                else
                                {
                                    feedback.Value(("Could not ungroup " + record.Identity + ": " + error).AsLoc()).Show();
                                }
                            }));
                }

                actions.Add(
                    TajsDashboardUi.ActionButton(
                        Button.Warning,
                        "Clear",
                        "Assets/Unity/UserInterface/General/Cancel.svg",
                        () =>
                        {
                            if (metadata.TryClearEntityMetadata(record.Identity))
                            {
                                queueRefresh();
                            }
                            else
                            {
                                feedback.Value(("Could not clear " + record.Identity + ".").AsLoc()).Show();
                            }
                        }));
                Row recordRow = new Row(3.pt()).AlignItemsCenter();
                recordRow.Add(details, actions, feedback);
                panel.Body.Add(recordRow);
            }
            return panel;
        }
    }
}
