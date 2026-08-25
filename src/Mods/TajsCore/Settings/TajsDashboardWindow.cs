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
        private readonly ScrollColumn m_pageContent;
        private readonly Dictionary<DashboardPage, ButtonText> m_pageButtons = new();
        private readonly Dictionary<string, ButtonText> m_categoryButtons = new(StringComparer.Ordinal);
        private string m_selectedCategory = AllCategories;
        private DashboardPage m_selectedPage = DashboardPage.Overview;
        private Label m_headerFeedback = null!;
        private bool m_refreshQueued;

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
            m_pageContent = new ScrollColumn().Fill().AlignItemsStretch().Gap(5.pt());

            BuildShell();
            WindowSize(1180.px(), 860.px());
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            OnCloseStart += _ => m_refreshQueued = false;
            RebuildPage();
            Open(uiRoot);
        }

        private void BuildShell()
        {
            Panel header = new Panel(true).ReducedPadding().BodyGap(2.pt());
            m_headerFeedback = new Label().FontSize(11).Hide();

            Column title = new Column(1.pt())
            {
                new Label("TajsCOI control center".AsLoc()).FontBold().FontSize(19),
                new Label("Live diagnostics, suite configuration, and safe maintenance tools for the active save.".AsLoc())
                    .FontSize(12),
            }.FlexGrow(1f);

            Row actions = new Row(2.pt()).AlignItemsCenter();
            actions.Add(
                new ButtonText(Button.Area, "Refresh".AsLoc(), QueueRefresh).Compact(),
                new ButtonText(Button.Area, "Export trace".AsLoc(), ExportTrace).Compact(),
                new ButtonText(Button.General, "Close".AsLoc(), Close).Compact());
            header.Body.Add(new Row(4.pt()) { title, actions }.AlignItemsCenter());
            header.Body.Add(m_headerFeedback);

            Row body = new Row(6.pt()).FlexGrow(1f).AlignItemsStretch();
            body.Add(BuildSidebar(), m_pageContent);

            Column shell = new Column(6.pt()).Fill().AlignItemsStretch();
            shell.Add(header, body);
            AddBodySingle(shell);
        }

        private Column BuildSidebar()
        {
            Column sidebar = new Column(2.pt()).Width(188.px()).FlexShrink(0f).AlignItemsStretch();
            Panel navigation = new Panel(true).ReducedPadding().BodyGap(2.pt()).FlexGrow(1f);
            navigation.Body.Add(new Label("Dashboard".AsLoc()).FontBold().FontSize(13));

            Column pageNavigation = new Column(1.pt()).AlignItemsStretch();
            AddPageButton(pageNavigation, DashboardPage.Overview, "Overview");
            AddPageButton(pageNavigation, DashboardPage.Profiler, "Profiler");
            AddPageButton(pageNavigation, DashboardPage.Performance, "Performance");
            AddPageButton(pageNavigation, DashboardPage.Tweaks, "Tweaks");
            AddPageButton(pageNavigation, DashboardPage.SaveLoad, "Save & Load");
            AddPageButton(pageNavigation, DashboardPage.Memory, "Memory");
            AddPageButton(pageNavigation, DashboardPage.Rendering, "Rendering");
            AddPageButton(pageNavigation, DashboardPage.Compatibility, "Compatibility");
            AddPageButton(pageNavigation, DashboardPage.Logs, "Logs");
            navigation.Body.Add(pageNavigation);

            navigation.Body.Add(new Label("Settings".AsLoc()).FontBold().FontSize(11).MarginTop(4.pt()));
            Column settingNavigation = new Column(1.pt()).AlignItemsStretch();
            AddPageButton(settingNavigation, DashboardPage.Settings, "All settings");

            foreach (IGrouping<string, SettingSnapshot> category in GetCategories(m_settings.GetSnapshot()))
            {
                ButtonText button = CreateCategoryButton(category.Key, category.Count(), settingNavigation);
                m_categoryButtons[category.Key] = button;
            }

            navigation.Body.Add(settingNavigation);
            sidebar.Add(navigation);
            UpdateNavigationSelection();
            return sidebar;
        }

        private void AddPageButton(Column container, DashboardPage page, string text)
        {
            ButtonText button = new ButtonText(
                Button.Area,
                text.AsLoc(),
                () => SelectPage(page)).Compact();
            container.Add(button);
            m_pageButtons[page] = button;
        }

        private ButtonText CreateCategoryButton(string category, int count, Column container)
        {
            ButtonText button = new ButtonText(
                Button.Area,
                ($"{category} ({count})").AsLoc(),
                () => SelectCategory(category)).Compact();
            container.Add(button);
            return button;
        }

        private void SelectPage(DashboardPage page)
        {
            m_selectedPage = page;
            UpdateNavigationSelection();
            QueueRefresh();
        }

        private void SelectCategory(string category)
        {
            m_selectedCategory = category;
            m_selectedPage = DashboardPage.Settings;
            UpdateNavigationSelection();
            QueueRefresh();
        }

        private void UpdateNavigationSelection()
        {
            foreach (KeyValuePair<DashboardPage, ButtonText> button in m_pageButtons)
            {
                button.Value.Selected(button.Key == m_selectedPage);
            }

            foreach (KeyValuePair<string, ButtonText> button in m_categoryButtons)
            {
                button.Value.Selected(
                    m_selectedPage == DashboardPage.Settings &&
                    string.Equals(button.Key, m_selectedCategory, StringComparison.Ordinal));
            }
        }

        private void QueueRefresh()
        {
            if (!IsOpen || m_refreshQueued)
            {
                return;
            }

            m_refreshQueued = true;
            m_pageContent.Schedule.Execute(() =>
            {
                m_refreshQueued = false;
                if (IsOpen)
                {
                    RebuildPage();
                }
            }).StartingIn(1L);
        }

        private void RebuildPage()
        {
            IReadOnlyList<SettingSnapshot> settings = m_settings.GetSnapshot();
            EnsureSelectedCategory(settings);
            IReadOnlyList<CompatibilityReport> reports = m_runtime.GetCompatibilitySnapshot();
            IReadOnlyList<LoadedModData> mods = GetTajsMods();

            m_pageContent.Clear();
            switch (m_selectedPage)
            {
                case DashboardPage.Overview:
                    AddOverview(settings, reports, mods, ReadProfilerSnapshot());
                    break;
                case DashboardPage.Profiler:
                    AddProfilerPage(settings, ReadProfilerSnapshot());
                    break;
                case DashboardPage.Performance:
                    AddDomainPage("Performance", "Behavior-neutral runtime diagnostics and explicitly opt-in maintenance operations.", settings, new[] { "Performance", "Diagnostics" }, includeMaintenance: true);
                    break;
                case DashboardPage.Tweaks:
                    AddDomainPage("Tweaks", "Settings registered by gameplay and quality-of-life features.", settings, new[] { "Tweak" }, includeMaintenance: false);
                    break;
                case DashboardPage.SaveLoad:
                    AddDomainPage("Save & Load", "Settings and lifecycle controls that apply at save reload or game restart.", settings, new[] { "Save", "Load" }, includeMaintenance: false);
                    break;
                case DashboardPage.Memory:
                    AddDomainPage("Memory", "Explicit memory-facing diagnostics and paused-only asset maintenance.", settings, new[] { "Memory", "Asset" }, includeMaintenance: true);
                    break;
                case DashboardPage.Rendering:
                    AddDomainPage("Rendering", "Rendering and visual-load settings exposed by the loaded suite.", settings, new[] { "Render", "Texture", "Graphics" }, includeMaintenance: false);
                    break;
                case DashboardPage.Compatibility:
                    AddCompatibilityPage(reports, mods);
                    break;
                case DashboardPage.Logs:
                    AddLogsPage(ReadProfilerSnapshot());
                    break;
                case DashboardPage.Settings:
                    AddSettingsPage(settings);
                    break;
                default:
                    AddOverview(settings, reports, mods, ReadProfilerSnapshot());
                    break;
            }
        }

        private void EnsureSelectedCategory(IReadOnlyList<SettingSnapshot> settings)
        {
            if (string.Equals(m_selectedCategory, AllCategories, StringComparison.Ordinal) ||
                settings.Any(snapshot => string.Equals(
                    snapshot.Descriptor.Category,
                    m_selectedCategory,
                    StringComparison.Ordinal)))
            {
                return;
            }

            m_selectedCategory = AllCategories;
            UpdateNavigationSelection();
        }

        private void AddOverview(
            IReadOnlyList<SettingSnapshot> settings,
            IReadOnlyList<CompatibilityReport> reports,
            IReadOnlyList<LoadedModData> mods,
            ProfilerSnapshot profiler)
        {
            int loadErrors = mods.Count(mod => mod.LoadError.HasValue);
            int unavailable = reports.Count(report => report.State == CompatibilityState.Disabled);
            int degraded = reports.Count(report => report.State == CompatibilityState.Degraded);
            int activeSettings = settings.Count(snapshot => !Equals(snapshot.Value, snapshot.Descriptor.DefaultValue));
            bool healthy = loadErrors == 0 && unavailable == 0 && degraded == 0;

            m_pageContent.Add(
                TajsDashboardUi.SectionHeader("Overview"),
                new Label("A compact operational view of the Taj's COI runtime in this gameplay scene.".AsLoc())
                    .FontSize(12));

            Row metrics = new Row(4.pt()).Wrap().AlignItemsStretch();
            metrics.Add(
                TajsDashboardUi.MetricTile("Mods loaded", mods.Count.ToString(CultureInfo.InvariantCulture), TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile("Active settings", activeSettings.ToString(CultureInfo.InvariantCulture), TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile("Compatibility", healthy ? "Healthy" : "Attention", healthy ? TajsDashboardUi.Green : TajsDashboardUi.Yellow),
                TajsDashboardUi.MetricTile("Runtime status", profiler.IsAvailable ? "Recording" : "Unavailable", profiler.IsAvailable ? TajsDashboardUi.Green : TajsDashboardUi.Red),
                TajsDashboardUi.MetricTile("Load errors", loadErrors.ToString(CultureInfo.InvariantCulture), loadErrors == 0 ? TajsDashboardUi.Green : TajsDashboardUi.Red));
            m_pageContent.Add(metrics);

            m_pageContent.Add(BuildLiveProfilerPanel(profiler));
            m_pageContent.Add(BuildQuickActionsPanel());
            m_pageContent.Add(BuildRuntimeSnapshotPanel(profiler));
            m_pageContent.Add(BuildCompatibilitySummary(reports));
            m_pageContent.Add(BuildLoadedModsPanel(mods));
        }

        private void AddProfilerPage(IReadOnlyList<SettingSnapshot> settings, ProfilerSnapshot profiler)
        {
            m_pageContent.Add(
                TajsDashboardUi.SectionHeader("Profiler"),
                new Label("Inspect the existing low-overhead flight recorder and arm bounded deep tracing when needed.".AsLoc())
                    .FontSize(12),
                BuildLiveProfilerPanel(profiler),
                BuildQuickActionsPanel(),
                BuildRuntimeSnapshotPanel(profiler));

            IReadOnlyList<SettingSnapshot> profilerSettings = settings
                .Where(snapshot => snapshot.Descriptor.ModId.IndexOf("Profiler", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   snapshot.Descriptor.Category.IndexOf("Profiler", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            AddSettingsList(profilerSettings, "Profiler settings");
        }

        private void AddDomainPage(
            string title,
            string subtitle,
            IReadOnlyList<SettingSnapshot> settings,
            IReadOnlyList<string> keywords,
            bool includeMaintenance)
        {
            m_pageContent.Add(TajsDashboardUi.SectionHeader(title), new Label(subtitle.AsLoc()).FontSize(12));
            if (includeMaintenance)
            {
                m_pageContent.Add(BuildMaintenancePanel());
            }

            IReadOnlyList<SettingSnapshot> filtered = settings
                .Where(snapshot => MatchesDomain(snapshot.Descriptor, keywords))
                .ToArray();
            if (filtered.Count == 0)
            {
                m_pageContent.Add(TajsDashboardUi.Card(
                    "No registered settings",
                    "This surface is ready for settings from the loaded suite; the current scene did not report any matching descriptors."));
                return;
            }

            AddSettingsList(filtered, $"{title} settings");
        }

        private void AddCompatibilityPage(
            IReadOnlyList<CompatibilityReport> reports,
            IReadOnlyList<LoadedModData> mods)
        {
            m_pageContent.Add(
                TajsDashboardUi.SectionHeader("Compatibility"),
                new Label("Expected-versus-observed seams reported by the loaded suite and its gameplay-scene services.".AsLoc())
                    .FontSize(12),
                BuildCompatibilitySummary(reports),
                BuildLoadedModsPanel(mods));

            Panel details = TajsDashboardUi.Card("Component reports", "Unavailable and degraded components remain visible with their owning mod and reason.");
            if (reports.Count == 0)
            {
                details.Body.Add(new Label("No component reports are available in this scene.".AsLoc()).FontSize(12));
            }
            else
            {
                foreach (CompatibilityReport report in reports)
                {
                    details.Body.Add(new Row(4.pt())
                    {
                        new Column(1.pt())
                        {
                            new Label($"{report.ModId} / {report.ComponentId}".AsLoc()).FontBold(),
                            new Label(report.Reason.AsLoc()).FontSize(11),
                            new Label($"Expected: {report.Expected} · Observed: {report.Observed}".AsLoc()).FontSize(11),
                        }.FlexGrow(1f),
                        TajsDashboardUi.StatusBadge(report.State.ToString(), CompatibilityColor(report.State)),
                    }.AlignItemsCenter());
                }
            }
            m_pageContent.Add(details);
        }

        private void AddLogsPage(ProfilerSnapshot profiler)
        {
            m_pageContent.Add(
                TajsDashboardUi.SectionHeader("Logs"),
                new Label("The dashboard keeps command output copyable and leaves the suite's structured log ownership with each service.".AsLoc())
                    .FontSize(12));

            Panel panel = TajsDashboardUi.Card("Latest diagnostics output", "Use the in-game console for the complete stream; these bounded snapshots are the most useful operational context.");
            panel.Body.Add(new Label("Flight recorder status".AsLoc()).FontBold());
            panel.Body.Add(new Label(Truncate(profiler.StatusText, 1200).AsLoc()).FontSize(11).Selectable(true));
            panel.Body.Add(new Label("Runtime summary".AsLoc()).FontBold().MarginTop(3.pt()));
            panel.Body.Add(new Label(Truncate(profiler.RuntimeText, 1800).AsLoc()).FontSize(11).Selectable(true));
            m_pageContent.Add(panel);
        }

        private void AddSettingsPage(IReadOnlyList<SettingSnapshot> settings)
        {
            IReadOnlyList<IGrouping<string, SettingSnapshot>> categories = GetCategories(settings);
            Row categoryButtons = new Row(2.pt()).Wrap().AlignItemsCenter();
            categoryButtons.Add(CreateInlineCategoryButton(AllCategories, settings.Count));
            foreach (IGrouping<string, SettingSnapshot> category in categories)
            {
                categoryButtons.Add(CreateInlineCategoryButton(category.Key, category.Count()));
            }

            m_pageContent.Add(
                TajsDashboardUi.SectionHeader("Settings"),
                TajsDashboardUi.Card(
                    "Settings index",
                    "All descriptors remain available here; use the sidebar or these compact filters to focus the list.",
                    categoryButtons));

            IReadOnlyList<SettingSnapshot> visible = string.Equals(m_selectedCategory, AllCategories, StringComparison.Ordinal)
                ? settings
                : settings.Where(snapshot => string.Equals(
                    snapshot.Descriptor.Category,
                    m_selectedCategory,
                    StringComparison.Ordinal)).ToArray();
            AddSettingsList(visible, string.Equals(m_selectedCategory, AllCategories, StringComparison.Ordinal)
                ? "All settings"
                : $"Settings · {m_selectedCategory}");
        }

        private ButtonText CreateInlineCategoryButton(string category, int count)
        {
            ButtonText button = new ButtonText(
                Button.Area,
                ($"{category} ({count})").AsLoc(),
                () => SelectCategory(category)).Compact();
            button.Selected(string.Equals(m_selectedCategory, category, StringComparison.Ordinal));
            return button;
        }

        private void AddSettingsList(IReadOnlyList<SettingSnapshot> settings, string heading)
        {
            m_pageContent.Add(TajsDashboardUi.SectionHeader($"{heading} ({settings.Count})"));
            if (settings.Count == 0)
            {
                m_pageContent.Add(new Label("No settings match this view.".AsLoc()).FontSize(12));
                return;
            }

            foreach (IGrouping<string, SettingSnapshot> modGroup in settings
                         .GroupBy(snapshot => snapshot.Descriptor.ModId)
                         .OrderBy(group => group.First().Descriptor.ModDisplayName, StringComparer.Ordinal))
            {
                SettingDescriptor first = modGroup.First().Descriptor;
                m_pageContent.Add(new Label(
                        $"{first.ModDisplayName} · {modGroup.Count()} setting{PluralSuffix(modGroup.Count())}".AsLoc())
                    .FontBold()
                    .FontSize(16)
                    .MarginTop(3.pt()));

                foreach (IGrouping<string, SettingSnapshot> category in modGroup
                             .GroupBy(snapshot => snapshot.Descriptor.Category)
                             .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    m_pageContent.Add(new Label($"{category.Key} · {category.Count()}".AsLoc())
                        .FontBold()
                        .FontSize(12)
                        .StyleChip()
                        .MarginTop(2.pt()));
                    foreach (SettingSnapshot setting in category.OrderBy(
                                 snapshot => snapshot.Descriptor.DisplayName,
                                 StringComparer.Ordinal))
                    {
                        m_pageContent.Add(CreateSettingControl(setting));
                    }
                }
            }
        }

        private UiComponent CreateSettingControl(SettingSnapshot snapshot)
        {
            SettingDescriptor descriptor = snapshot.Descriptor;
            Label feedback = new Label().FontSize(11).Hide();
            Column description = new Column(1.pt())
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

            Column body = new Column(2.pt())
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
            feedback.Value((result.Success ? ApplyModeText(result.ApplyMode) : result.Error).AsLoc()).Show();
            return result;
        }

        private Panel BuildLiveProfilerPanel(ProfilerSnapshot profiler)
        {
            Panel panel = TajsDashboardUi.Card(
                "Live profiler",
                "Read-only status from the existing runtime profiler; no profiler state is recreated in Core.");
            Row statuses = new Row(3.pt()).Wrap().AlignItemsCenter();
            statuses.Add(
                TajsDashboardUi.StatusBadge("Flight recorder: " + profiler.FlightRecorder, profiler.IsAvailable ? TajsDashboardUi.Green : TajsDashboardUi.Red),
                TajsDashboardUi.StatusBadge("Deep trace: " + profiler.DeepTrace, profiler.DeepTrace.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0 ? TajsDashboardUi.Yellow : TajsDashboardUi.Cyan),
                TajsDashboardUi.StatusBadge("Ring drops: " + profiler.RingDrops, profiler.RingDrops == "0" ? TajsDashboardUi.Green : TajsDashboardUi.Yellow),
                TajsDashboardUi.StatusBadge("GPU timing: " + profiler.GpuTiming, profiler.GpuTiming.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ? TajsDashboardUi.Yellow : TajsDashboardUi.Green));
            panel.Body.Add(statuses);
            panel.Body.Add(new Label(Truncate(profiler.StatusText, 900).AsLoc()).FontSize(11).Selectable(true));
            return panel;
        }

        private Panel BuildRuntimeSnapshotPanel(ProfilerSnapshot profiler)
        {
            Panel panel = TajsDashboardUi.Card(
                "Runtime snapshot",
                "Latest frame sample plus the rolling summary retained by the profiler service.");
            Row latest = new Row(3.pt()).Wrap().AlignItemsStretch();
            latest.Add(
                TajsDashboardUi.MetricTile("Frame", profiler.Frame, TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile("Wait for sim", profiler.WaitForSim, TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile("Sim update", profiler.SimUpdate, TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile("Render", profiler.Render, TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile("Classification", profiler.Classification, TajsDashboardUi.Green));
            panel.Body.Add(latest);
            panel.Body.Add(new Label("Rolling summary".AsLoc()).FontBold().MarginTop(2.pt()));
            panel.Body.Add(new Label(Truncate(profiler.RuntimeText, 1600).AsLoc()).FontSize(11).Selectable(true));
            return panel;
        }

        private Panel BuildQuickActionsPanel()
        {
            Panel panel = TajsDashboardUi.Card(
                "Quick actions",
                "Actions call the owning services through their existing console command contracts.");
            Row buttons = new Row(3.pt()).Wrap();
            Label feedback = new Label().FontSize(11).Hide();
            bool any = false;
            any |= AddCommandButton(buttons, Button.Warning, "Start deep trace", "tajs_profiler_deep_start 10", feedback);
            any |= AddCommandButton(buttons, Button.Area, "Stop deep trace", "tajs_profiler_deep_stop", feedback);
            any |= AddCommandButton(buttons, Button.Area, "Export trace", "tajs_profiler_trace_export runtime", feedback);
            any |= AddCommandButton(buttons, Button.Area, "Clear runtime history", "tajs_profiler_runtime_clear", feedback);
            any |= AddCommandButton(buttons, Button.Warning, "Trim unused assets", "trim_unused_assets", feedback);
            if (any)
            {
                panel.Body.Add(buttons);
            }
            else
            {
                panel.Body.Add(new Label("No profiler or maintenance commands are available in this scene.".AsLoc()).FontSize(12));
            }
            panel.Body.Add(new ButtonText(Button.Area, "Refresh diagnostics".AsLoc(), QueueRefresh).Compact());
            panel.Body.Add(feedback);
            return panel;
        }

        private Panel BuildMaintenancePanel()
        {
            Panel panel = TajsDashboardUi.Card(
                "Maintenance actions",
                "Manual asset trimming is still owned by TajsPerformance and remains paused-only and opt-in.");
            Row buttons = new Row(3.pt()).Wrap();
            Label feedback = new Label().FontSize(11).Hide();
            bool any = false;
            any |= AddCommandButton(buttons, Button.Warning, "Trim unused assets", "trim_unused_assets", feedback);
            any |= AddCommandButton(buttons, Button.Area, "Show last trim result", "trim_unused_assets_status", feedback);
            if (any)
            {
                panel.Body.Add(buttons);
            }
            else
            {
                panel.Body.Add(new Label("TajsPerformance maintenance commands are not loaded.".AsLoc()).FontSize(12));
            }
            panel.Body.Add(feedback);
            return panel;
        }

        private Panel BuildCompatibilitySummary(IReadOnlyList<CompatibilityReport> reports)
        {
            int compatible = reports.Count(report => report.State == CompatibilityState.Compatible);
            int degraded = reports.Count(report => report.State == CompatibilityState.Degraded);
            int disabled = reports.Count(report => report.State == CompatibilityState.Disabled);
            Panel panel = TajsDashboardUi.Card("Compatibility summary", "Missing components and load errors stay visible instead of silently disabling controls.");
            Row summary = new Row(3.pt()).Wrap();
            summary.Add(
                TajsDashboardUi.MetricTile("Healthy", compatible.ToString(CultureInfo.InvariantCulture), TajsDashboardUi.Green),
                TajsDashboardUi.MetricTile("Warnings", degraded.ToString(CultureInfo.InvariantCulture), TajsDashboardUi.Yellow),
                TajsDashboardUi.MetricTile("Unavailable", disabled.ToString(CultureInfo.InvariantCulture), disabled == 0 ? TajsDashboardUi.Green : TajsDashboardUi.Red));
            panel.Body.Add(summary);

            foreach (CompatibilityReport report in reports.Take(8))
            {
                panel.Body.Add(new Row(3.pt())
                {
                    new Label($"{report.ModId} / {report.ComponentId}".AsLoc()).FlexGrow(1f),
                    TajsDashboardUi.StatusBadge(report.State.ToString(), CompatibilityColor(report.State)),
                }.AlignItemsCenter());
            }
            if (reports.Count > 8)
            {
                panel.Body.Add(new Label($"{reports.Count - 8} more reports are available on the Compatibility page.".AsLoc()).FontSize(11));
            }
            return panel;
        }

        private Panel BuildLoadedModsPanel(IReadOnlyList<LoadedModData> mods)
        {
            Panel panel = TajsDashboardUi.Card("Loaded suite modules", "The dashboard only lists Taj's modules reported by the current gameplay resolver.");
            if (mods.Count == 0)
            {
                panel.Body.Add(new Label("No TajsCOI mods were reported by the loader.".AsLoc()).FontSize(12));
                return panel;
            }

            foreach (LoadedModData mod in mods)
            {
                bool failed = mod.LoadError.HasValue;
                string version = Convert.ToString(mod.Manifest.Version, CultureInfo.InvariantCulture) ?? "unknown";
                panel.Body.Add(new Row(4.pt())
                {
                    new Column(1.pt())
                    {
                        new Label(mod.Manifest.Id.AsLoc()).FontBold(),
                        new Label($"Version {version}".AsLoc()).FontSize(11),
                    }.FlexGrow(1f),
                    TajsDashboardUi.StatusBadge(failed ? "Load failed" : "Loaded", failed ? TajsDashboardUi.Red : TajsDashboardUi.Green),
                }.AlignItemsCenter());
                if (failed)
                {
                    panel.Body.Add(new Label(mod.LoadError.Value.ToString().AsLoc()).FontSize(11));
                }
            }
            return panel;
        }

        private bool AddCommandButton(Row buttons, ButtonVariant variant, string text, string command, Label feedback)
        {
            string commandName = command.Split(' ')[0];
            if (!m_consoleCommands.Executor.Commands.ContainsKey(commandName))
            {
                return false;
            }

            buttons.Add(new ButtonText(
                variant,
                text.AsLoc(),
                () => ExecuteConsoleCommand(command, feedback)).Compact());
            return true;
        }

        private void ExportTrace()
        {
            if (!TryReadCommand("tajs_profiler_trace_export runtime", out string output))
            {
                m_headerFeedback.Value(output.AsLoc()).Show();
                return;
            }
            m_headerFeedback.Value(output.AsLoc()).Show();
        }

        private void ExecuteConsoleCommand(string command, Label feedback)
        {
            TryReadCommand(command, out string output);
            feedback.Value(output.AsLoc()).Show();
        }

        private bool TryReadCommand(string command, out string output)
        {
            string commandName = command.Split(' ')[0];
            if (!m_consoleCommands.Executor.Commands.ContainsKey(commandName))
            {
                output = "Command unavailable: " + commandName;
                return false;
            }

            try
            {
                GameCommandResult result = m_consoleCommands.Executor.TryExecute(command);
                if (result.ErrorMessage.HasValue)
                {
                    output = result.ErrorMessage.Value;
                    return false;
                }

                output = result.Result.HasValue
                    ? Convert.ToString(result.Result.Value, CultureInfo.InvariantCulture) ?? "Command completed."
                    : "Command completed.";
                return true;
            }
            catch (Exception exception)
            {
                output = $"Command failed: {exception.Message}";
                return false;
            }
        }

        private ProfilerSnapshot ReadProfilerSnapshot()
        {
            bool statusAvailable = TryReadCommand("tajs_profiler_status", out string status);
            bool runtimeAvailable = TryReadCommand("tajs_profiler_runtime 10", out string runtime);
            if (!statusAvailable && !runtimeAvailable)
            {
                return ProfilerSnapshot.Unavailable(status, runtime);
            }

            return new ProfilerSnapshot(
                statusAvailable || runtimeAvailable,
                status,
                runtime,
                ReadField(status, "frames"),
                ReadField(status, "deep"),
                ReadField(status, "timing-ring-drops"),
                ReadField(status, "gpu-frame"),
                ReadField(status, "classification"),
                ReadField(status, "frame"),
                ReadField(status, "wait-for-sim"),
                ReadField(status, "sim"),
                ReadSummaryLine(runtime, "render"));
        }

        private static string ReadField(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "unavailable";
            }

            string needle = key + "=";
            int start = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return "unavailable";
            }

            start += needle.Length;
            int end = text.Length;
            foreach (char delimiter in new[] { ',', ';', '\n', '\r' })
            {
                int candidate = text.IndexOf(delimiter, start);
                if (candidate >= 0 && candidate < end)
                {
                    end = candidate;
                }
            }
            return text.Substring(start, end - start).Trim();
        }

        private static string ReadSummaryLine(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "unavailable";
            }

            foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(key.Length).Trim();
                }
            }
            return "unavailable";
        }

        private static IReadOnlyList<IGrouping<string, SettingSnapshot>> GetCategories(IReadOnlyList<SettingSnapshot> settings) =>
            settings
                .GroupBy(snapshot => snapshot.Descriptor.Category)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();

        private static bool MatchesDomain(SettingDescriptor descriptor, IReadOnlyList<string> keywords)
        {
            return keywords.Any(keyword =>
                descriptor.Category.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                descriptor.ModId.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                descriptor.DisplayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private IReadOnlyList<LoadedModData> GetTajsMods() =>
            ModsLoader.LoadedAndFailedMods
                .Where(mod => mod.Manifest.Id.StartsWith("Tajs", StringComparison.Ordinal))
                .OrderBy(mod => mod.Manifest.Id, StringComparer.Ordinal)
                .ToArray();

        private string FormatCurrent(SettingDescriptor descriptor) => FormatValue(GetCurrentValue(descriptor));

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

        private static ColorRgba CompatibilityColor(CompatibilityState state) =>
            state == CompatibilityState.Compatible ? TajsDashboardUi.Green :
            state == CompatibilityState.Degraded ? TajsDashboardUi.Yellow :
            TajsDashboardUi.Red;

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

        private static string Truncate(string text, int maximumLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maximumLength)
            {
                return text ?? string.Empty;
            }
            return text.Substring(0, maximumLength) + "...";
        }

        private static string PluralSuffix(int count) => count == 1 ? string.Empty : "s";

        private enum DashboardPage
        {
            Overview,
            Profiler,
            Performance,
            Tweaks,
            SaveLoad,
            Memory,
            Rendering,
            Compatibility,
            Logs,
            Settings,
        }

        private sealed class ProfilerSnapshot
        {
            internal ProfilerSnapshot(
                bool isAvailable,
                string statusText,
                string runtimeText,
                string flightRecorder,
                string deepTrace,
                string ringDrops,
                string gpuTiming,
                string classification,
                string frame,
                string waitForSim,
                string simUpdate,
                string render)
            {
                IsAvailable = isAvailable;
                StatusText = statusText;
                RuntimeText = runtimeText;
                FlightRecorder = ValueOrUnavailable(flightRecorder);
                DeepTrace = ValueOrUnavailable(deepTrace);
                RingDrops = ValueOrUnavailable(ringDrops);
                GpuTiming = ValueOrUnavailable(gpuTiming);
                Classification = ValueOrUnavailable(classification);
                Frame = ValueOrUnavailable(frame);
                WaitForSim = ValueOrUnavailable(waitForSim);
                SimUpdate = ValueOrUnavailable(simUpdate);
                Render = ValueOrUnavailable(render);
            }

            internal bool IsAvailable { get; }
            internal string StatusText { get; }
            internal string RuntimeText { get; }
            internal string FlightRecorder { get; }
            internal string DeepTrace { get; }
            internal string RingDrops { get; }
            internal string GpuTiming { get; }
            internal string Classification { get; }
            internal string Frame { get; }
            internal string WaitForSim { get; }
            internal string SimUpdate { get; }
            internal string Render { get; }

            internal static ProfilerSnapshot Unavailable(string status, string runtime) =>
                new(false, status, runtime, "unavailable", "unavailable", "unavailable", "unavailable", "unavailable", "unavailable", "unavailable", "unavailable", "unavailable");

            private static string ValueOrUnavailable(string value) =>
                string.IsNullOrWhiteSpace(value) ? "unavailable" : value;
        }
    }

    internal static class TajsDashboardUi
    {
        internal static readonly ColorRgba Cyan = new ColorRgba(130, 200, 255);
        internal static readonly ColorRgba Green = new ColorRgba(130, 220, 150);
        internal static readonly ColorRgba Yellow = new ColorRgba(245, 195, 90);
        internal static readonly ColorRgba Red = new ColorRgba(240, 105, 105);

        internal static Label SectionHeader(string text) =>
            new Label(text.AsLoc()).FontBold().FontSize(20).MarginTop(4.pt()).MarginBottom(1.pt());

        internal static Panel Card(string title, string subtitle, params UiComponent[] children)
        {
            Panel panel = new Panel(true)
                .ReducedPadding()
                .BodyGap(3.pt())
                .StyleGroupDark();
            panel.Body.Add(new Label(title.AsLoc()).FontBold().FontSize(15));
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                panel.Body.Add(new Label(subtitle.AsLoc()).FontSize(11));
            }
            panel.Body.Add(children);
            return panel;
        }

        internal static Panel MetricTile(string title, string value, ColorRgba color)
        {
            return new Panel(true)
                .ReducedPadding()
                .BodyGap(1.pt())
                .StyleGroupDark()
                .MinWidth(130.px())
                .FlexGrow(1f)
                .BodyAdd(
                    new Label(title.AsLoc()).FontSize(11),
                    new Label(value.AsLoc()).FontBold().FontSize(17).Color(color));
        }

        internal static Label StatusBadge(string text, ColorRgba color) =>
            new Label(text.AsLoc()).StyleChip().FontSize(11).Color(color);
    }
}
