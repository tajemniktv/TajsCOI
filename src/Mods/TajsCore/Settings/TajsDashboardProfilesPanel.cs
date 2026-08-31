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
using TextField = Mafi.Unity.UiToolkit.Library.TextField;

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

            Label managementFeedback = new Label().FontSize(11).Hide();
            TextField captureName = new TextField()
                .Placeholder(Text(localization, "profiles.capture.name", "Profile name").AsLoc())
                .MaxWidth(220.px());
            TextField captureCategories = new TextField()
                .Placeholder(Text(localization, "profiles.capture.categories", "Categories (optional)").AsLoc())
                .MaxWidth(220.px());
            TextField captureModules = new TextField()
                .Placeholder(Text(localization, "profiles.capture.modules", "Modules (optional)").AsLoc())
                .MaxWidth(220.px());
            Row captureRow = new Row(3.pt()).Wrap().AlignItemsCenter();
            captureRow.Add(
                captureName,
                captureCategories,
                captureModules,
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    Text(localization, "profiles.capture.save", "Save current"),
                    "Assets/Unity/UserInterface/General/Save.svg",
                    () =>
                    {
                        if (profiles.TryCapture(
                                captureName.GetText(),
                                SplitList(captureCategories.GetText()),
                                SplitList(captureModules.GetText()),
                                out SettingsProfile? profile,
                                out string error) && profile is not null)
                        {
                            managementFeedback.Value(
                                    (Text(localization, "profiles.capture.saved", "Saved profile: ") + profile.Name).AsLoc())
                                .Show();
                            captureName.Text(string.Empty);
                            queueRefresh();
                        }
                        else
                        {
                            managementFeedback.Value(
                                    (Text(localization, "profiles.capture.failed", "Profile was not saved: ") + error).AsLoc())
                                .Show();
                        }
                    }),
                managementFeedback);
            panel.Body.Add(captureRow);

            TextField importPath = new TextField()
                .Placeholder(Text(localization, "profiles.import.path", "Import JSON path").AsLoc())
                .MaxWidth(420.px());
            TextField importName = new TextField()
                .Placeholder(Text(localization, "profiles.import.name", "Name override (optional)").AsLoc())
                .MaxWidth(220.px());
            Row importRow = new Row(3.pt()).Wrap().AlignItemsCenter();
            importRow.Add(
                importPath,
                importName,
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    Text(localization, "profiles.import.action", "Import"),
                    "Assets/Unity/UserInterface/General/Configure.svg",
                    () =>
                    {
                        if (profiles.TryImport(importPath.GetText(), importName.GetText(), out SettingsProfile? profile, out string error) && profile is not null)
                        {
                            managementFeedback.Value(
                                    (Text(localization, "profiles.imported", "Imported profile: ") + profile.Name).AsLoc())
                                .Show();
                            importPath.Text(string.Empty);
                            importName.Text(string.Empty);
                            queueRefresh();
                        }
                        else
                        {
                            managementFeedback.Value(
                                    (Text(localization, "profiles.import.failed", "Profile was not imported: ") + error).AsLoc())
                                .Show();
                        }
                    }));
            panel.Body.Add(importRow);

            TextField restoreConfirmation = new TextField()
                .Placeholder(Text(localization, "profiles.restore.confirmation", "Type RESET to restore defaults").AsLoc())
                .MaxWidth(260.px());
            Label restoreFeedback = new Label().FontSize(11).Hide();
            Row restoreRow = new Row(3.pt()).Wrap().AlignItemsCenter();
            restoreRow.Add(
                restoreConfirmation,
                TajsDashboardUi.ActionButton(
                    Button.Warning,
                    Text(localization, "profiles.restore.action", "Restore profile-safe defaults"),
                    "Assets/Unity/UserInterface/General/Reset.svg",
                    () =>
                    {
                        if (!string.Equals(restoreConfirmation.GetText().Trim(), "RESET", StringComparison.Ordinal))
                        {
                            restoreFeedback.Value(Text(localization, "profiles.restore.required", "Type RESET to confirm.").AsLoc()).Show();
                            return;
                        }

                        SettingsProfileApplyResult result = profiles.RestoreDefaults(null);
                        restoreFeedback.Value(
                                (Text(localization, "profiles.restore.result", "Defaults restored: applied=") +
                                 result.AppliedCount + ", errors=" + result.Errors.Count +
                                 (result.Errors.Count == 0 ? string.Empty : " (" + string.Join(" | ", result.Errors.Take(4)) + ")")).AsLoc())
                            .Show();
                        restoreConfirmation.Text(string.Empty);
                        queueRefresh();
                    }),
                restoreFeedback);
            panel.Body.Add(restoreRow);

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

                SettingsProfile profile = selected;
                TextField renameName = new TextField()
                    .Text(profile.Name)
                    .Placeholder(Text(localization, "profiles.rename.name", "New name").AsLoc())
                    .MaxWidth(220.px());
                TextField duplicateName = new TextField()
                    .Placeholder(Text(localization, "profiles.duplicate.name", "Duplicate name").AsLoc())
                    .MaxWidth(220.px());
                TextField exportPath = new TextField()
                    .Placeholder(Text(localization, "profiles.export.path", "Export JSON path").AsLoc())
                    .MaxWidth(420.px());
                TextField deleteConfirmation = new TextField()
                    .Placeholder(Text(localization, "profiles.delete.confirmation", "Type DELETE to remove").AsLoc())
                    .MaxWidth(240.px());
                Row profileFields = new Row(2.pt()).Wrap().AlignItemsCenter();
                profileFields.Add(renameName, duplicateName, exportPath, deleteConfirmation);
                Row buttons = new Row(2.pt()).Wrap().AlignItemsCenter();
                buttons.Add(
                    TajsDashboardUi.ActionButton(
                        Button.Area,
                        Text(localization, "profiles.preview", "Preview"),
                        "Assets/Unity/UserInterface/General/Configure.svg",
                        () =>
                        {
                            SettingsProfilePreview preview = profiles.Preview(profile);
                            feedback.Value(PreviewText(preview).AsLoc())
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
                                     ", errors=" + result.Errors.Count +
                                     (result.Errors.Count == 0 ? string.Empty : " (" + string.Join(" | ", result.Errors.Take(4)) + ")")).AsLoc())
                                .Show();
                            queueRefresh();
                        }),
                    TajsDashboardUi.ActionButton(
                        Button.Warning,
                        Text(localization, "profiles.delete", "Delete"),
                        "Assets/Unity/UserInterface/General/Delete.svg",
                        () =>
                        {
                            if (!string.Equals(deleteConfirmation.GetText().Trim(), "DELETE", StringComparison.Ordinal))
                            {
                                feedback.Value(Text(localization, "profiles.delete.required", "Type DELETE to confirm.").AsLoc()).Show();
                                return;
                            }
                            if (profiles.TryDelete(profile.Name, out string error))
                            {
                                deleteConfirmation.Text(string.Empty);
                                queueRefresh();
                            }
                            else
                            {
                                feedback.Value((Text(localization, "profiles.deleteFailed", "Profile could not be deleted: ") + error).AsLoc()).Show();
                            }
                        }));
                actions.Add(
                    new Label((Text(localization, "profiles.selected", "Selected profile: ") + profile.Name).AsLoc()).FontBold(),
                    profileFields,
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

        private static IReadOnlyList<string> SplitList(string text) =>
            (text ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static string PreviewText(SettingsProfilePreview preview)
        {
            string summary = string.Join(", ", preview.Entries
                .GroupBy(entry => entry.State)
                .Select(group => group.Key + "=" + group.Count()));
            string details = string.Join("; ", preview.Entries.Take(12).Select(entry =>
                entry.StableId + " [" + entry.State + "]" +
                (entry.State == SettingsProfilePreviewState.Proposed
                    ? " " + FormatValue(entry.CurrentValue) + " -> " + FormatValue(entry.ProposedValue)
                    : string.Empty)));
            string omitted = preview.Entries.Count > 12
                ? "; ... " + (preview.Entries.Count - 12) + " more"
                : string.Empty;
            string skipped = preview.SkippedIds.Count == 0 ? string.Empty : "; skipped=" + preview.SkippedIds.Count;
            return preview.Profile.Name + ": " + summary + skipped +
                   (details.Length == 0 ? string.Empty : "\n" + details + omitted);
        }

        private static string FormatValue(object? value) =>
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "<null>";

        private static string Text(ILocalizationService localization, string key, string fallback) =>
            localization.Get("TajsCore", "dashboard." + key, fallback);
    }
}
