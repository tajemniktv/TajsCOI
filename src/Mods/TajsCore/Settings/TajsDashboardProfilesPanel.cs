// Taj's COI Mods | TajsDashboardProfilesPanel.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Profiles;
using Button = Mafi.Unity.UiToolkit.Library.Button;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using Panel = Mafi.Unity.UiToolkit.Library.Panel;
using Row = Mafi.Unity.UiToolkit.Library.Row;

namespace TajsCOI.Core.Settings
{
    internal static class TajsDashboardProfilesPanel
    {
        internal static Panel Build(ISettingsProfileService profiles, Action queueRefresh)
        {
            Panel panel = TajsDashboardUi.Card(
                "Settings profiles",
                "Profiles contain only settings explicitly marked profile-safe. Preview validates every entry before apply; unknown IDs are skipped and reported.");
            IReadOnlyList<SettingsProfile> savedProfiles = profiles.List();
            if (savedProfiles.Count == 0)
            {
                panel.Body.Add(new Label("No profiles saved. Capture one with tajs_profile_capture <name> from the console.".AsLoc()).FontSize(11));
                return panel;
            }

            foreach (SettingsProfile profile in savedProfiles)
            {
                Label feedback = new Label().FontSize(11).Hide();
                Row actions = new Row(2.pt()).AlignItemsCenter();
                actions.Add(
                    TajsDashboardUi.ActionButton(
                        Button.Area,
                        "Preview",
                        "Assets/Unity/UserInterface/General/Configure.svg",
                        () =>
                        {
                            SettingsProfilePreview preview = profiles.Preview(profile);
                            feedback.Value(
                                    (preview.Profile.Name + ": " +
                                     string.Join(", ", preview.Entries.GroupBy(entry => entry.State).Select(group => group.Key + "=" + group.Count())) +
                                     (preview.SkippedIds.Count == 0 ? string.Empty : "; skipped=" + preview.SkippedIds.Count)).AsLoc())
                                .Show();
                        }),
                    TajsDashboardUi.ActionButton(
                        Button.Area,
                        "Apply",
                        "Assets/Unity/UserInterface/General/Configure.svg",
                        () =>
                        {
                            SettingsProfileApplyResult result = profiles.Apply(profile);
                            feedback.Value(
                                    (profile.Name + ": applied=" + result.AppliedCount + ", skipped=" + result.SkippedIds.Count +
                                     ", errors=" + result.Errors.Count).AsLoc())
                                .Show();
                            queueRefresh();
                        }));
                Row row = new Row(4.pt()).AlignItemsCenter();
                var description = new Column(1.pt())
                {
                    new Label(profile.Name.AsLoc()).FontBold(),
                    new Label((profile.Values.Count + " saved value(s) · schema " + profile.Schema).AsLoc()).FontSize(11),
                    feedback,
                };
                row.Add(description.FlexGrow(1f), actions);
                panel.Body.Add(row);
            }
            return panel;
        }
    }
}
