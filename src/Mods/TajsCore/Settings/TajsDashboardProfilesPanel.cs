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
using TajsCOI.Common.Localization;
using TajsCOI.Common.Ui;
using Button = Mafi.Unity.UiToolkit.Library.Button;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using Panel = Mafi.Unity.UiToolkit.Library.Panel;
using Row = Mafi.Unity.UiToolkit.Library.Row;

namespace TajsCOI.Core.Settings
{
    internal static class TajsDashboardProfilesPanel
    {
        private sealed class ProfileRow : IEquatable<ProfileRow>
        {
            internal ProfileRow(SettingsProfile profile)
            {
                Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            }

            internal SettingsProfile Profile { get; }

            public bool Equals(ProfileRow? other) =>
                other is not null &&
                string.Equals(Profile.Name, other.Profile.Name, StringComparison.OrdinalIgnoreCase) &&
                Profile.Values.Count == other.Profile.Values.Count &&
                Profile.Schema == other.Profile.Schema;

            public override bool Equals(object? obj) => Equals(obj as ProfileRow);

            public override int GetHashCode() =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(Profile.Name) * 397 ^
                Profile.Values.Count * 17 ^ Profile.Schema;
        }

        internal static Panel Build(
            ISettingsProfileService profiles,
            ILocalizationService localization,
            Action queueRefresh)
        {
            if (profiles is null)
            {
                throw new ArgumentNullException(nameof(profiles));
            }
            if (localization is null)
            {
                throw new ArgumentNullException(nameof(localization));
            }

            Panel panel = TajsDashboardUi.Card(
                Text(localization, "profiles.title", "Settings profiles"),
                Text(localization, "profiles.description", "Profiles contain only settings explicitly marked profile-safe. Preview validates every entry before apply; unknown IDs are skipped and reported."));
            IReadOnlyList<ProfileRow> savedProfiles = profiles.List().Select(profile => new ProfileRow(profile)).ToArray();
            if (savedProfiles.Count == 0)
            {
                panel.Body.Add(
                    new Label(
                            Text(localization, "profiles.empty", "No profiles saved. Capture one with tajs_profile_capture <name> from the console.").AsLoc())
                        .FontSize(11));
                return panel;
            }

            var model = new DataTableModel<ProfileRow>(
                new[]
                {
                    DataTableColumn<ProfileRow>.CreateText(
                        "name",
                        Text(localization, "profiles.column.name", "Name"),
                        row => row.Profile.Name,
                        width: DataTableColumnWidth.Constrained(180f, 360f),
                        visibilityPriority: 10),
                    DataTableColumn<ProfileRow>.Create(
                        "values",
                        Text(localization, "profiles.column.values", "Values"),
                        row => row.Profile.Values.Count,
                        value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        width: DataTableColumnWidth.Fixed(90f),
                        alignment: DataTableColumnAlignment.End,
                        visibilityPriority: 8),
                    DataTableColumn<ProfileRow>.Create(
                        "schema",
                        Text(localization, "profiles.column.schema", "Schema"),
                        row => row.Profile.Schema,
                        value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        width: DataTableColumnWidth.Fixed(90f),
                        alignment: DataTableColumnAlignment.End,
                        visibilityPriority: 7),
                    DataTableColumn<ProfileRow>.CreateText(
                        "scope",
                        Text(localization, "profiles.column.scope", "Scope"),
                        row => ScopeText(row.Profile, localization),
                        width: DataTableColumnWidth.Flex(),
                        visibilityPriority: 1),
                },
                row => row.Profile.Name,
                DataTableSelectionMode.Single);

            var actions = new Column(2.pt()).AlignItemsStretch();
            SettingsProfile? selected = null;
            Label feedback = new Label().FontSize(11).Hide();

            void RenderActions(IReadOnlyCollection<string> selectedIds)
            {
                selected = savedProfiles
                    .Where(row => selectedIds.Contains(row.Profile.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(row => row.Profile)
                    .FirstOrDefault();
                actions.Clear();
                if (selected is null)
                {
                    return;
                }

                Row buttons = new Row(2.pt()).Wrap().AlignItemsCenter();
                SettingsProfile profile = selected;
                buttons.Add(
                    TajsDashboardUi.ActionButton(
                        Button.Area,
                        Text(localization, "profiles.preview", "Preview"),
                        "Assets/Unity/UserInterface/General/Configure.svg",
                        () =>
                        {
                            SettingsProfilePreview preview = profiles.Preview(profile);
                            feedback.Value(
                                    (profile.Name + ": " +
                                     string.Join(", ", preview.Entries.GroupBy(entry => entry.State).Select(group => group.Key + "=" + group.Count())) +
                                     (preview.SkippedIds.Count == 0 ? string.Empty : "; skipped=" + preview.SkippedIds.Count)).AsLoc())
                                .Show();
                        }),
                    TajsDashboardUi.ActionButton(
                        Button.Area,
                        Text(localization, "profiles.apply", "Apply"),
                        "Assets/Unity/UserInterface/General/Configure.svg",
                        () =>
                        {
                            SettingsProfileApplyResult result = profiles.Apply(profile);
                            feedback.Value(
                                    (profile.Name + ": applied=" + result.AppliedCount + ", skipped=" + result.SkippedIds.Count +
                                     ", errors=" + result.Errors.Count).AsLoc())
                                .Show();
                            queueRefresh();
                        }),
                    TajsDashboardUi.ActionButton(
                        Button.Warning,
                        Text(localization, "profiles.delete", "Delete"),
                        "Assets/Unity/UserInterface/General/Delete.svg",
                        () =>
                        {
                            if (profiles.TryDelete(profile.Name, out string error))
                            {
                                queueRefresh();
                            }
                            else
                            {
                                feedback.Value((Text(localization, "profiles.deleteFailed", "Profile could not be deleted: ") + error).AsLoc()).Show();
                            }
                        }));
                actions.Add(
                    new Label((Text(localization, "profiles.selected", "Selected profile: ") + profile.Name).AsLoc()).FontBold(),
                    buttons,
                    feedback);
            }

            var table = new TajsDataTable<ProfileRow>(model, RenderActions);
            table.Refresh(savedProfiles);
            table.SetAvailableWidth(760f);
            panel.Body.Add(table, actions);
            table.SelectRow(savedProfiles[0].Profile.Name);
            return panel;
        }

        private static string ScopeText(SettingsProfile profile, ILocalizationService localization)
        {
            string categories = profile.Categories.Count == 0
                ? Text(localization, "profiles.scope.allCategories", "all categories")
                : string.Join(", ", profile.Categories);
            string modules = profile.Modules.Count == 0
                ? Text(localization, "profiles.scope.allModules", "all modules")
                : string.Join(", ", profile.Modules);
            return categories + " / " + modules;
        }

        private static string Text(ILocalizationService localization, string key, string fallback) =>
            localization.Get("TajsCore", "dashboard." + key, fallback);
    }
}
