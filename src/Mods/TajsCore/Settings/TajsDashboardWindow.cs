// Taj's COI Mods | TajsDashboardWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Console;
using Mafi.Core.Mods;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;

namespace TajsCOI.Core.Settings
{
    internal sealed class TajsDashboardWindow : Window
    {
        private const string AllCategories = "All";

        private readonly ITajsSettings m_settings;
        private readonly ITajsRuntime m_runtime;
        private readonly GameConsoleCommandsExecutor m_consoleCommands;
        private readonly ScrollColumn m_content;
        private string m_selectedCategory = AllCategories;

        public TajsDashboardWindow(
            ITajsSettings settings,
            ITajsRuntime runtime,
            GameConsoleCommandsExecutor consoleCommands,
            UiRoot uiRoot)
            : base("TajsCOI Dashboard".AsLoc())
        {
            m_settings = settings;
            m_runtime = runtime;
            m_consoleCommands = consoleCommands;
            m_content = new ScrollColumn().Fill().AlignItemsStretch().Gap(4.pt());

            AddBodySingle(
                new Row(2.pt())
                {
                    new Column(1.pt())
                    {
                        new Label("TajsCOI control center".AsLoc()).FontBold().FontSize(18),
                        new Label("Tune loaded mods, inspect compatibility, and run explicit maintenance actions.".AsLoc())
                            .FontSize(12),
                    }.FlexGrow(1f),
                    new ButtonText(Button.General, "Refresh".AsLoc(), Rebuild).Compact(),
                },
                m_content);
            WindowSize(980.px(), 820.px());
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            Rebuild();
            Open(uiRoot);
        }

        private void Rebuild()
        {
            m_content.Clear();

            IReadOnlyList<SettingSnapshot> settings = m_settings.GetSnapshot();
            IReadOnlyList<CompatibilityReport> reports = m_runtime.GetCompatibilitySnapshot();
            IReadOnlyList<LoadedModData> mods = GetTajsMods();

            AddOverview(mods, settings, reports);
            AddLoadedMods(mods);
            AddCategoryFilter(settings);
            AddActions();
            AddSettings(settings);
            AddCompatibility(reports);
        }

        private void AddOverview(
            IReadOnlyList<LoadedModData> mods,
            IReadOnlyList<SettingSnapshot> settings,
            IReadOnlyList<CompatibilityReport> reports)
        {
            int failedMods = mods.Count(mod => mod.LoadError.HasValue);
            int unavailableComponents = reports.Count(report => report.State == CompatibilityState.Disabled);
            var summary = new Panel(true)
                .ReducedPadding()
                .BodyGap(2.pt())
                .BodyAdd(
                    new Label("Everything important at a glance".AsLoc()).FontBold().FontSize(16),
                    new Label("Changes are persisted through the same settings service used by the console commands.".AsLoc())
                        .FontSize(12),
                    new Row(3.pt()).Wrap().AlignItemsCenter()
                        .AddStats(
                            $"{mods.Count} mod{PluralSuffix(mods.Count)}",
                            $"{settings.Count} setting{PluralSuffix(settings.Count)}",
                            $"{reports.Count} compatibility report{PluralSuffix(reports.Count)}",
                            failedMods == 0 ? "No mod load errors" : $"{failedMods} mod load error{PluralSuffix(failedMods)}",
                            unavailableComponents == 0 ? "All components available" :
                                $"{unavailableComponents} component{PluralSuffix(unavailableComponents)} unavailable"));
            m_content.Add(summary);
        }

        private void AddLoadedMods(IReadOnlyList<LoadedModData> mods)
        {
            m_content.Add(SectionTitle($"Mods in this game ({mods.Count})"));
            var panel = new Panel(true).ReducedPadding().BodyGap(1.pt());

            if (mods.Count == 0)
            {
                panel.Body.Add(new Label("No TajsCOI mods were reported by the loader.".AsLoc()).FontSize(12));
                m_content.Add(panel);
                return;
            }

            foreach (LoadedModData mod in mods)
            {
                bool failed = mod.LoadError.HasValue;
                string state = failed ? "Load failed" : "Loaded";
                string version = Convert.ToString(mod.Manifest.Version, CultureInfo.InvariantCulture) ?? "unknown";
                var details = new Column(1.pt())
                {
                    new Label(mod.Manifest.Id.AsLoc()).FontBold(),
                    new Label($"Version {version}".AsLoc()).FontSize(11),
                }.FlexGrow(1f);
                panel.Body.Add(new Row(4.pt())
                {
                    details,
                    new Label(state.AsLoc()).StyleChip(),
                }.AlignItemsCenter());

                if (failed)
                {
                    panel.Body.Add(new Label(mod.LoadError.Value.ToString().AsLoc()).FontSize(11));
                }
            }

            m_content.Add(panel);
        }

        private void AddCategoryFilter(IReadOnlyList<SettingSnapshot> settings)
        {
            IReadOnlyList<IGrouping<string, SettingSnapshot>> categories = settings
                .GroupBy(snapshot => snapshot.Descriptor.Category)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();

            if (!string.Equals(m_selectedCategory, AllCategories, StringComparison.Ordinal) &&
                !categories.Any(group => string.Equals(group.Key, m_selectedCategory, StringComparison.Ordinal)))
            {
                m_selectedCategory = AllCategories;
            }

            var categoryButtons = new Row(2.pt()).Wrap().AlignItemsCenter();
            categoryButtons.Add(CreateCategoryButton(AllCategories, settings.Count));
            foreach (IGrouping<string, SettingSnapshot> category in categories)
            {
                categoryButtons.Add(CreateCategoryButton(category.Key, category.Count()));
            }

            m_content.Add(
                new Panel(true)
                    .ReducedPadding()
                    .BodyGap(2.pt())
                    .BodyAdd(
                        new Label("Browse by category".AsLoc()).FontBold().FontSize(14),
                        new Label("Use a category to keep the settings list focused on the kind of change you want to make.".AsLoc())
                            .FontSize(12),
                        categoryButtons));
        }

        private ButtonText CreateCategoryButton(string category, int count)
        {
            bool selected = string.Equals(m_selectedCategory, category, StringComparison.Ordinal);
            var button = new ButtonText(
                Button.Area,
                $"{category} ({count})".AsLoc(),
                () =>
                {
                    m_selectedCategory = category;
                    Rebuild();
                });
            return button.Selected(selected).Compact();
        }

        private void AddActions()
        {
            bool canTrimAssets = m_consoleCommands.Executor.Commands.ContainsKey("trim_unused_assets");
            bool canReadTrimStatus = m_consoleCommands.Executor.Commands.ContainsKey("trim_unused_assets_status");
            if (!canTrimAssets && !canReadTrimStatus)
            {
                return;
            }

            var feedback = new Label().FontSize(11).Hide();
            var buttons = new Row(3.pt()).Wrap();
            if (canTrimAssets)
            {
                buttons.Add(new ButtonText(
                    Button.Warning,
                    "Trim unused assets".AsLoc(),
                    () => ExecuteConsoleCommand("trim_unused_assets", feedback)).Compact());
            }
            if (canReadTrimStatus)
            {
                buttons.Add(new ButtonText(
                    Button.Area,
                    "Show last trim result".AsLoc(),
                    () => ExecuteConsoleCommand("trim_unused_assets_status", feedback)).Compact());
            }

            m_content.Add(SectionTitle("Maintenance actions"));
            m_content.Add(
                new Panel(true)
                    .ReducedPadding()
                    .BodyGap(2.pt())
                    .BodyAdd(
                        new Label("Manual memory maintenance".AsLoc()).FontBold(),
                        new Label("These actions are explicit and follow the same paused-only safety checks as the console commands. They never run automatically.".AsLoc())
                            .FontSize(12),
                        buttons,
                        feedback));
        }

        private void ExecuteConsoleCommand(string command, Label feedback)
        {
            try
            {
                GameCommandResult result = m_consoleCommands.Executor.TryExecute(command);
                string message = result.ErrorMessage.HasValue
                    ? result.ErrorMessage.Value
                    : result.Result.HasValue
                        ? Convert.ToString(result.Result.Value, CultureInfo.InvariantCulture) ?? "Command completed."
                        : "Command completed.";
                feedback.Value(message.AsLoc()).Show();
            }
            catch (Exception exception)
            {
                feedback.Value($"Command failed: {exception.Message}".AsLoc()).Show();
            }
        }

        private void AddSettings(IReadOnlyList<SettingSnapshot> settings)
        {
            IReadOnlyList<SettingSnapshot> visibleSettings = string.Equals(m_selectedCategory, AllCategories, StringComparison.Ordinal)
                ? settings
                : settings.Where(snapshot => string.Equals(
                    snapshot.Descriptor.Category,
                    m_selectedCategory,
                    StringComparison.Ordinal)).ToArray();

            string heading = string.Equals(m_selectedCategory, AllCategories, StringComparison.Ordinal)
                ? "Settings"
                : $"Settings · {m_selectedCategory}";
            m_content.Add(SectionTitle($"{heading} ({visibleSettings.Count})"));
            if (visibleSettings.Count == 0)
            {
                m_content.Add(new Label("No settings match this category.".AsLoc()).FontSize(12));
                return;
            }

            foreach (IGrouping<string, SettingSnapshot> modGroup in visibleSettings
                         .GroupBy(snapshot => snapshot.Descriptor.ModId)
                         .OrderBy(group => group.First().Descriptor.ModDisplayName, StringComparer.Ordinal))
            {
                SettingDescriptor firstDescriptor = modGroup.First().Descriptor;
                m_content.Add(
                    new Label($"{firstDescriptor.ModDisplayName} · {modGroup.Count()} setting{PluralSuffix(modGroup.Count())}".AsLoc())
                        .FontBold()
                        .FontSize(17)
                        .MarginTop(4.pt()));

                foreach (IGrouping<string, SettingSnapshot> category in modGroup
                             .GroupBy(snapshot => snapshot.Descriptor.Category)
                             .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    m_content.Add(
                        new Label($"{category.Key} · {category.Count()}".AsLoc())
                            .FontBold()
                            .FontSize(13)
                            .StyleChip()
                            .MarginTop(2.pt()));
                    foreach (SettingSnapshot setting in category.OrderBy(
                                 snapshot => snapshot.Descriptor.DisplayName,
                                 StringComparer.Ordinal))
                    {
                        m_content.Add(CreateSettingControl(setting));
                    }
                }
            }
        }

        private UiComponent CreateSettingControl(SettingSnapshot snapshot)
        {
            SettingDescriptor descriptor = snapshot.Descriptor;
            Label feedback = new Label().FontSize(11).Hide();
            var description = new Column(1.pt())
            {
                new Label(descriptor.DisplayName.AsLoc()).FontBold(),
                new Label(descriptor.Description.AsLoc()).FontSize(12),
                new Label(FormatSettingMeta(descriptor).AsLoc()).FontSize(11),
            }.FlexGrow(1f);

            UiComponent editor;
            switch (descriptor.ValueType)
            {
                case SettingValueType.Boolean:
                    bool enabled = (bool)snapshot.Value;
                    ButtonText stateButton = null!;
                    stateButton = new ButtonText(
                        Button.ToggleGroup,
                        BooleanStateText(enabled).AsLoc(),
                        () =>
                        {
                            bool nextValue = !m_settings.Get<bool>(descriptor.ModId, descriptor.Key);
                            SettingSetResult result = Set(descriptor, nextValue, feedback);
                            if (result.Success)
                            {
                                bool currentValue = (bool)result.Value!;
                                stateButton.Value(BooleanStateText(currentValue).AsLoc()).Selected(currentValue);
                            }
                        })
                        .Toggleable(true)
                        .Selected(enabled)
                        .Compact();
                    editor = stateButton;
                    break;

                case SettingValueType.Choice:
                    SettingChoice currentChoice = FindChoice(descriptor, snapshot.Value);
                    Dropdown<SettingChoice> dropdown = new Dropdown<SettingChoice>(
                            (choice, _, __) => new Label(choice.DisplayName.AsLoc()))
                        .SetOptions(descriptor.Choices)
                        .SetValue(currentChoice);
                    dropdown.OnValueChanged((choice, _) =>
                    {
                        SettingSetResult result = Set(descriptor, choice.Value, feedback);
                        if (!result.Success)
                        {
                            dropdown.SetValue(FindChoice(descriptor, GetCurrentValue(descriptor)));
                        }
                    });
                    editor = dropdown;
                    break;

                default:
                    TextField field = new TextField().Text(FormatValue(snapshot.Value));
                    field.OnEditEnd(text =>
                    {
                        SettingSetResult result = Set(descriptor, text, feedback);
                        if (!result.Success)
                        {
                            field.Text(FormatCurrent(descriptor));
                        }
                    });
                    editor = field;
                    break;
            }

            var body = new Column(2.pt())
            {
                new Row(4.pt())
                {
                    description,
                    editor,
                }.AlignItemsCenter(),
                feedback,
            };
            return new Panel(true).ReducedPadding().BodyGap(2.pt()).BodyAdd(body).StyleGroupDark();
        }

        private SettingSetResult Set(SettingDescriptor descriptor, object value, Label feedback)
        {
            SettingSetResult result = m_settings.TrySet(descriptor.ModId, descriptor.Key, value);
            if (result.Success)
            {
                feedback.Value(ApplyModeText(result.ApplyMode).AsLoc()).Show();
            }
            else
            {
                feedback.Value(result.Error.AsLoc()).Show();
            }
            return result;
        }

        private void AddCompatibility(IReadOnlyList<CompatibilityReport> reports)
        {
            m_content.Add(SectionTitle("Compatibility"));
            var panel = new Panel(true).ReducedPadding().BodyGap(2.pt());
            if (reports.Count == 0)
            {
                panel.Body.Add(new Label("No component reports are available in this scene.".AsLoc()).FontSize(12));
                m_content.Add(panel);
                return;
            }

            foreach (CompatibilityReport report in reports)
            {
                panel.Body.Add(new Row(4.pt())
                {
                    new Column(1.pt())
                    {
                        new Label($"{report.ModId} / {report.ComponentId}".AsLoc()).FontBold(),
                        new Label(report.Reason.AsLoc()).FontSize(11),
                    }.FlexGrow(1f),
                    new Label(report.State.ToString().AsLoc()).StyleChip(),
                }.AlignItemsCenter());
                panel.Body.Add(new Label($"Expected: {report.Expected} · Observed: {report.Observed}".AsLoc()).FontSize(11));
            }

            m_content.Add(panel);
        }

        private IReadOnlyList<LoadedModData> GetTajsMods() =>
            ModsLoader.LoadedAndFailedMods
                .Where(mod => mod.Manifest.Id.StartsWith("Tajs", StringComparison.Ordinal))
                .OrderBy(mod => mod.Manifest.Id, StringComparer.Ordinal)
                .ToArray();

        private string FormatCurrent(SettingDescriptor descriptor)
        {
            return FormatValue(GetCurrentValue(descriptor));
        }

        private object GetCurrentValue(SettingDescriptor descriptor) =>
            m_settings.GetSnapshot().Single(snapshot =>
                string.Equals(snapshot.Descriptor.StableId, descriptor.StableId, StringComparison.Ordinal)).Value;

        private static SettingChoice FindChoice(SettingDescriptor descriptor, object value)
        {
            string currentValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return descriptor.Choices.FirstOrDefault(choice =>
                       string.Equals(choice.Value, currentValue, StringComparison.Ordinal))
                   ?? descriptor.Choices[0];
        }

        private static Label SectionTitle(string text) =>
            new Label(text.AsLoc()).FontBold().FontSize(20).MarginTop(8.pt()).MarginBottom(2.pt());

        private static string FormatValue(object value) =>
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        private static string BooleanStateText(bool value) => value ? "Enabled" : "Disabled";

        private static string FormatSettingMeta(SettingDescriptor descriptor)
        {
            var parts = new List<string>
            {
                descriptor.StableId,
                ApplyModeText(descriptor.ApplyMode),
            };
            if (descriptor.Scope == SettingScope.PerSave)
            {
                parts.Add("Per save");
            }
            if (descriptor.Flags != SettingFlags.None)
            {
                parts.Add(descriptor.Flags.ToString());
            }
            if (!string.IsNullOrEmpty(descriptor.ComponentRequirement))
            {
                parts.Add("Requires " + descriptor.ComponentRequirement);
            }
            return string.Join(" · ", parts);
        }

        private static string ApplyModeText(SettingApplyMode mode) =>
            mode == SettingApplyMode.Immediate ? "Live" :
            mode == SettingApplyMode.ReloadSave ? "After save reload" :
            "After game restart";

        private static string PluralSuffix(int count) => count == 1 ? string.Empty : "s";
    }

    internal static class TajsDashboardUiExtensions
    {
        internal static Row AddStats(this Row row, params string[] values)
        {
            foreach (string value in values)
            {
                row.Add(new Label(value.AsLoc()).StyleChip().FontSize(11));
            }
            return row;
        }
    }
}
