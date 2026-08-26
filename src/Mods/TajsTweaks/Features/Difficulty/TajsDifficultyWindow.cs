// Taj's COI Mods | TajsDifficultyWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Tweaks.Features.Difficulty;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Dedicated advanced difficulty dashboard. The controls intentionally show the native
    ///     current value, the captured original-save value, the preset vanilla value, and the
    ///     apply classification side by side.
    /// </summary>
    internal sealed class TajsDifficultyWindow : Window
    {
        private readonly TajsDifficultyFeature m_feature;
        private readonly Column m_content;
        private readonly Label m_status;
        private readonly HashSet<string> m_confirmPending = new(StringComparer.Ordinal);

        internal TajsDifficultyWindow(TajsDifficultyFeature feature, UiRoot uiRoot)
            : base("TajsDifficulty · Advanced settings".AsLoc())
        {
            m_feature = feature ?? throw new ArgumentNullException(nameof(feature));
            WindowSize(new Px(1120f), new Px(760f));

            m_status = new Label(string.Empty.AsLoc()).FontSize(12);
            m_content = new Column(4.pt()).AlignItemsStretch();
            ScrollColumn scroll = new ScrollColumn().Fill().AlignItemsStretch();
            scroll.Add(m_content);

            Row actions = new Row(3.pt()).AlignItemsCenter();
            actions.Add(
                new ButtonText(Button.Area, "Restore original save".AsLoc(), () => SetStatus(m_feature.Reset("original", true))).Compact(),
                new ButtonText(Button.Area, "Restore vanilla".AsLoc(), () => SetStatus(m_feature.Reset("vanilla", true))).Compact(),
                new ButtonText(Button.Area, "Refresh".AsLoc(), Refresh).Compact());

            Panel panel = new Panel().BodyGap(4.pt());
            panel.Body.Add(
                new Label("Advanced difficulty editor".AsLoc()).FontBold().FontSize(18),
                new Label(
                        "Values are submitted through the native difficulty command. New-game-only and reload-classified settings are shown for audit but are never force-written to an active save."
                            .AsLoc())
                    .FontSize(12),
                actions,
                m_status,
                scroll);
            Body.Add(panel);
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            Refresh();
            Open(uiRoot);
        }

        private void Refresh()
        {
            m_content.Clear();
            IReadOnlyList<TajsDifficultyRow> rows = m_feature.Snapshot();
            m_status.Value(m_feature.Status().AsLoc());

            foreach (IGrouping<string, TajsDifficultyRow> category in rows.GroupBy(row => row.Definition.Category))
            {
                m_content.Add(new Label((category.Key + " · " + category.Count()).AsLoc()).FontBold().FontSize(15).StyleChip());
                foreach (TajsDifficultyRow row in category)
                {
                    m_content.Add(BuildRow(row));
                }
            }
        }

        private UiComponent BuildRow(TajsDifficultyRow row)
        {
            Column details = new Column(1.pt()).FlexGrow(1f).FlexShrink(1f).MinWidth(0.px());
            details.Add(
                new Label(row.Definition.DisplayName.AsLoc()).FontBold(),
                new Label(row.Definition.Description.AsLoc()).FontSize(11),
                new Label(
                        ("Current: " + row.Current + " · Original: " + row.Original + " · Vanilla: " + row.Vanilla +
                         " · " + m_feature.ApplyModeText(row.Definition.ApplyMode)).AsLoc())
                    .FontSize(11));

            string pending = row.Current;
            TextField input = new TextField().Text(row.Current).MaxWidth(210.px());
            input.OnEditEnd(value => pending = value);
            ButtonText apply = new ButtonText(
                Button.Area,
                "Apply".AsLoc(),
                () =>
                {
                    string confirmationKey = row.Definition.MemberName + "=" + pending;
                    bool confirmed = m_confirmPending.Remove(confirmationKey);
                    string result = m_feature.Set(row.Definition.MemberName, pending, confirmed);
                    if (result.IndexOf("Repeat with CONFIRM", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        m_confirmPending.Add(confirmationKey);
                        result = "This is an extreme value. Click Apply again to confirm.";
                    }
                    SetStatus(result);
                    Refresh();
                }).Compact();
            ButtonText original = new ButtonText(
                Button.Area,
                "Original".AsLoc(),
                () =>
                {
                    SetStatus(m_feature.RestoreMember(row.Definition.MemberName, false, true));
                    Refresh();
                }).Compact();
            ButtonText vanilla = new ButtonText(
                Button.Area,
                "Vanilla".AsLoc(),
                () =>
                {
                    SetStatus(m_feature.RestoreMember(row.Definition.MemberName, true, true));
                    Refresh();
                }).Compact();

            Row controls = new Row(2.pt()).AlignItemsCenter();
            controls.Add(input, apply, original, vanilla);
            Row body = new Row(5.pt()).AlignItemsStart();
            body.Add(details, controls);
            return new Panel(true).ReducedPadding().BodyAdd(body).StyleGroupDark();
        }

        private void SetStatus(string message) => m_status.Value(message.AsLoc());
    }
}
