// Taj's COI Mods | TajsOverclockingWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine.UIElements;
using UiColumn = Mafi.Unity.UiToolkit.Library.Column;
using UiLabel = Mafi.Unity.UiToolkit.Library.Label;
using UiTextField = Mafi.Unity.UiToolkit.Library.TextField;
using UiButton = Mafi.Unity.UiToolkit.Library.Button;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    ///     Small gameplay-scoped management surface for named overclock groups.  It deliberately
    ///     delegates policy changes to the feature's Queue* methods; the window never mutates a
    ///     live entity or group policy directly.  The per-entity inspector remains the fastest
    ///     path for one machine, while this window supplies the missing named-group/bulk surface.
    /// </summary>
    internal sealed class TajsOverclockingWindow : Window
    {
        private readonly TajsOverclockingFeature m_feature;
        private readonly UiColumn m_groups;
        private readonly UiLabel m_status;
        private readonly UiTextField m_groupName;
        private readonly UiTextField m_rate;
        private readonly IVisualElementScheduledItem? m_refreshSchedule;

        internal TajsOverclockingWindow(TajsOverclockingFeature feature, UiRoot uiRoot)
            : base("Overclocking groups".AsLoc())
        {
            m_feature = feature;
            WindowSize(new Px(820f), new Px(560f));

            Panel panel = new Panel().BodyGap(new Px(5f));
            Row header = new Row(5.pt()).AlignItemsCenter();
            header.Add(new UiLabel("Global default: 100%".AsLoc()).FontBold());
            m_groupName = new UiTextField().Placeholder("New group name".AsLoc()).MaxWidth(new Px(220f));
            header.Add(m_groupName);
            m_rate = new UiTextField().Text("100").CharLimit(5).PositiveIntegersOnly().MaxWidth(new Px(72f));
            header.Add(new UiLabel("Rate".AsLoc()));
            header.Add(m_rate);
            header.Add(MakeButton("Create group", CreateGroup));
            header.Add(MakeButton("Refresh", RefreshNow));
            panel.Add(header);

            m_status = new UiLabel(string.Empty.AsLoc());
            panel.Add(m_status);
            m_groups = new UiColumn(4.pt()).AlignItemsStretch();
            var scroll = new ScrollColumn();
            scroll.Add(m_groups);
            panel.Add(scroll);
            Body.Add(panel);

            m_refreshSchedule = RootElement.schedule.Execute(RefreshNow).Every(500L);
            OnCloseStart += _ => m_refreshSchedule?.Pause();
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            Open(uiRoot);
            RefreshNow();
        }

        private ButtonText MakeButton(string text, Action action)
        {
            var button = new ButtonText(
                UiButton.General,
                text.AsLoc(),
                () =>
                {
                    action();
                    RefreshNow();
                });
            button.Width(new Px(108f));
            return button;
        }

        private void CreateGroup()
        {
            string name = m_groupName.GetText().Trim();
            OverclockGroup group = m_feature.CreateGroup(name);
            m_groupName.Text(string.Empty);
            m_status.Value(("Created group " + group.Id + ".").AsLoc());
        }

        private void RefreshNow()
        {
            if (!IsOpen)
            {
                return;
            }

            m_groups.Clear();
            m_status.Value(
                (m_feature.Groups.Count == 0
                    ? "No named groups. Global default is 100%."
                    : m_feature.Groups.Count + " named group(s); global default is 100%.").AsLoc());

            foreach (OverclockGroup group in m_feature.Groups.OrderBy(value => value.Id))
            {
                Row row = new Row(4.pt()).AlignItemsCenter();
                string bounds = group.MinPercent + "-" + group.MaxPercent + "%";
                row.Add(new UiLabel((group.Id + ": " + group.Name).AsLoc()).Width(new Px(190f)));
                row.Add(new UiLabel((group.Members.Count + " member(s), " +
                                   (group.ManualDefault == 0 ? "global" : group.ManualDefault + "%") +
                                   ", " + (group.Auto ? "Auto" : "Manual") + " " + bounds).AsLoc())
                    .Width(new Px(250f)));
                row.Add(MakeButton("Pick", () => m_feature.StartGroupSelection(group.Id)));
                row.Add(MakeButton("Set default", () =>
                    m_feature.QueueSetGroupDefault(group.Id, ReadRate(), out _)));
                row.Add(MakeButton("Apply override", () =>
                    m_feature.QueueApplyGroupToMembers(group.Id, ReadRate(), out _)));
                row.Add(MakeButton(group.Auto ? "Manual" : "Auto", () =>
                    m_feature.QueueSetGroupAuto(group.Id, !group.Auto, null, null, out _)));
                row.Add(MakeButton("Delete", () => m_feature.QueueDeleteGroup(group.Id, out _)));
                m_groups.Add(row);
            }
        }

        private int ReadRate()
        {
            return int.TryParse(m_rate.GetText().Trim().TrimEnd('%'), out int value)
                ? value
                : 100;
        }
    }
}
