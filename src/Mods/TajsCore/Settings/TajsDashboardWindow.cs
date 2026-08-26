// Taj's COI Mods | TajsDashboardWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Mods;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using UnityEngine.UIElements;
using Button = Mafi.Unity.UiToolkit.Library.Button;
using Column = Mafi.Unity.UiToolkit.Library.Column;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using TextField = Mafi.Unity.UiToolkit.Library.TextField;

namespace TajsCOI.Core.Settings
{
    internal sealed class TajsDashboardWindow : Window
    {
        private sealed class DashboardResizeManipulator : PointerManipulator
        {
            private readonly Action<PointerDownEvent> m_onPointerDown;
            private readonly Action<PointerMoveEvent> m_onPointerMove;
            private readonly Action<PointerUpEvent> m_onPointerUp;
            private readonly Action<PointerCaptureOutEvent> m_onPointerCaptureOut;

            internal DashboardResizeManipulator(
                Action<PointerDownEvent> onPointerDown,
                Action<PointerMoveEvent> onPointerMove,
                Action<PointerUpEvent> onPointerUp,
                Action<PointerCaptureOutEvent> onPointerCaptureOut)
            {
                m_onPointerDown = onPointerDown;
                m_onPointerMove = onPointerMove;
                m_onPointerUp = onPointerUp;
                m_onPointerCaptureOut = onPointerCaptureOut;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnPointerDown);
                target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                target.RegisterCallback<PointerUpEvent>(OnPointerUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0)
                {
                    return;
                }

                target.CapturePointer(evt.pointerId);
                m_onPointerDown(evt);
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (target.HasPointerCapture(evt.pointerId))
                {
                    m_onPointerMove(evt);
                }
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (evt.button == 0 && target.HasPointerCapture(evt.pointerId))
                {
                    m_onPointerUp(evt);
                    target.ReleasePointer(evt.pointerId);
                }
            }

            private void OnPointerCaptureOut(PointerCaptureOutEvent evt) => m_onPointerCaptureOut(evt);
        }

        private const string AllCategories = "All";
        private const float MinimumWindowWidth = 760f;
        private const float MinimumWindowHeight = 480f;

        private readonly ITajsSettings m_settings;
        private readonly ITajsRuntime m_runtime;
        private readonly GameConsoleCommandsExecutor m_consoleCommands;
        private readonly ScrollColumn m_pageContent;
        private readonly Column m_dashboardShell;
        private readonly ButtonIcon m_minimizeButton;
        private readonly ButtonIcon m_maximizeButton;
        private readonly UiComponent m_resizeHandle;
        private readonly Dictionary<DashboardPage, Column> m_pageContainers = new();
        private readonly HashSet<DashboardPage> m_builtPages = new();
        private readonly Dictionary<DashboardPage, Button> m_pageButtons = new();
        private readonly Dictionary<string, Button> m_categoryButtons = new(StringComparer.Ordinal);
        private string m_selectedCategory = AllCategories;
        private DashboardPage m_selectedPage = DashboardPage.Overview;
        private Label m_headerFeedback = null!;
        private bool m_refreshQueued;
        private bool m_isMinimized;
        private bool m_isMaximized;
        private bool m_isResizing;
        private float m_lastParentWidth = -1f;
        private ProfilerPageView? m_profilerPage;
        private LogsPageView? m_logsPage;

        private Column CurrentPage => m_pageContainers[m_selectedPage];

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
            m_dashboardShell = new Column(6.pt()).Fill().AlignItemsStretch();
            m_minimizeButton = new ButtonIcon(
                    Button.Header,
                    "Assets/Unity/UserInterface/General/Minimize.svg",
                    ToggleMinimized)
                .ObserveSelected(() => m_isMinimized);
            m_maximizeButton = new ButtonIcon(
                    Button.Header,
                    "Assets/Unity/UserInterface/General/Maximize.svg",
                    ToggleMaximized)
                .ObserveSelected(() => m_isMaximized);
            m_resizeHandle = new Icon("Assets/Unity/UserInterface/General/ResCornerBR32.png")
                .AbsolutePosition(null, 2.px(), 2.px(), null)
                .Width(16.px())
                .Height(16.px());
            m_resizeHandle.RootElement.AddManipulator(
                new DashboardResizeManipulator(
                    BeginResize,
                    ContinueResize,
                    EndResize,
                    _ => m_isResizing = false));

            BuildShell();
            Frame.Add(m_resizeHandle);
            WindowSize(1180.px(), 860.px());
            MakeMovableAndEnablePositionSaving();
            EnablePinning();
            AddHeaderButton(m_minimizeButton);
            AddHeaderButton(m_maximizeButton);
            CloseOnClickOutside();
            OnCloseStart += _ => m_refreshQueued = false;
            Schedule.Execute(UpdateResponsiveWindowSize).Every(20L);
            RebuildPage();
            Open(uiRoot);
        }

        private void BuildShell()
        {
            Panel header = new Panel(true).ReducedPadding().BodyGap(2.pt());
            m_headerFeedback = new Label().FontSize(11).Hide();

            Column titleText = new Column(1.pt())
            {
                new Label("TajsCOI Control Center".AsLoc()).FontBold().FontSize(19),
                new Label("Live diagnostics, suite configuration, and safe maintenance tools for the active save.".AsLoc())
                    .FontSize(12),
            };

            Row title = new Row(4.pt()).AlignItemsCenter().FlexGrow(1f);
            title.Add(
                new Icon("Assets/Unity/UserInterface/General/ModLarge.svg")
                    .Width(30.px())
                    .Height(30.px()),
                titleText);

            Row actions = new Row(2.pt()).AlignItemsCenter();
            actions.Add(
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Refresh",
                    "Assets/Unity/UserInterface/General/Repeat.svg",
                    QueueRefresh),
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Export trace",
                    "Assets/Unity/UserInterface/General/ExportToString.svg",
                    ExportTrace));
            header.Body.Add(new Row(4.pt()) { title, actions }.AlignItemsCenter());
            header.Body.Add(m_headerFeedback);

            Row body = new Row(6.pt()).FlexGrow(1f).AlignItemsStretch();
            BuildPageContainers();
            body.Add(BuildSidebar(), m_pageContent);

            m_dashboardShell.Add(header, body);
            AddBodySingle(m_dashboardShell);
        }

        private void ToggleMinimized()
        {
            m_isMinimized = !m_isMinimized;
            m_isResizing = false;
            if (m_isMinimized)
            {
                m_dashboardShell.Hide();
                m_resizeHandle.Hide();
                WindowSize(GetCurrentWindowWidth().px(), 76.px());
            }
            else
            {
                m_dashboardShell.Show();
                UpdateResizeHandleVisibility();
                m_lastParentWidth = -1f;
                UpdateResponsiveWindowSize();
            }
        }

        private void ToggleMaximized()
        {
            if (m_isMinimized)
            {
                m_isMinimized = false;
                m_dashboardShell.Show();
            }

            m_isMaximized = !m_isMaximized;
            m_isResizing = false;
            UpdateResizeHandleVisibility();
            m_lastParentWidth = -1f;
            UpdateResponsiveWindowSize();
        }

        private void UpdateResponsiveWindowSize()
        {
            if (m_isMinimized || Parent.IsNone || Parent.Value.ResolvedWidth <= 1f)
            {
                return;
            }

            float parentWidth = Parent.Value.ResolvedWidth;
            if (m_lastParentWidth > 0f && Math.Abs(parentWidth - m_lastParentWidth) < 1f)
            {
                return;
            }

            m_lastParentWidth = parentWidth;
            float width = Math.Min(m_isMaximized ? 2000f : 1180f, parentWidth * 0.9f);
            WindowSize(Math.Max(MinimumWindowWidth, width).px(), (m_isMaximized ? 90 : 80).Percent());
        }

        private float GetCurrentWindowWidth() => Frame.ResolvedWidth > 1f ? Frame.ResolvedWidth : 1180f;

        private void UpdateResizeHandleVisibility()
        {
            if (m_isMaximized || m_isMinimized)
            {
                m_resizeHandle.Hide();
            }
            else
            {
                m_resizeHandle.Show();
            }
        }

        private void BeginResize(PointerDownEvent evt)
        {
            if (evt.button != 0 || m_isMaximized || m_isMinimized || Frame.ResolvedWidth <= 1f)
            {
                return;
            }

            m_isResizing = true;
            evt.StopPropagation();
        }

        private void ContinueResize(PointerMoveEvent evt)
        {
            if (!m_isResizing || Parent.IsNone)
            {
                return;
            }

            float maxWidth = Math.Max(MinimumWindowWidth, Parent.Value.ResolvedWidth * 0.96f);
            float maxHeight = Math.Max(MinimumWindowHeight, Parent.Value.ResolvedHeight * 0.96f);
            float width = Math.Max(MinimumWindowWidth, Math.Min(maxWidth, Frame.ResolvedWidth + evt.deltaPosition.x));
            float height = Math.Max(MinimumWindowHeight, Math.Min(maxHeight, Frame.ResolvedHeight + evt.deltaPosition.y));
            WindowSize(width.px(), height.px());
            m_lastParentWidth = Parent.Value.ResolvedWidth;
            evt.StopPropagation();
        }

        private void EndResize(PointerUpEvent evt)
        {
            if (evt.button == 0 && m_isResizing)
            {
                m_isResizing = false;
                evt.StopPropagation();
            }
        }

        private void BuildPageContainers()
        {
            foreach (DashboardPage page in Enum.GetValues(typeof(DashboardPage)))
            {
                Column container = new Column(5.pt()).Fill().AlignItemsStretch();
                container.Hide();
                m_pageContainers[page] = container;
                m_pageContent.Add(container);
            }
            CurrentPage.Show();
            m_pageContent.ScrollToStart();
        }

        private Column BuildSidebar()
        {
            Column sidebar = new Column(2.pt()).Width(214.px()).FlexShrink(0f).AlignItemsStretch();
            Panel navigation = new Panel(true).ReducedPadding().FlexGrow(1f).AlignItemsStretch();
            ScrollColumn navigationScroll = new ScrollColumn().Fill().AlignItemsStretch().Gap(2.pt());

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

            Column settingNavigation = new Column(1.pt()).AlignItemsStretch();
            IReadOnlyList<SettingSnapshot> settings = m_settings.GetSnapshot();
            AddPageButton(settingNavigation, DashboardPage.Settings, "All settings", settings.Count);

            foreach (IGrouping<string, SettingSnapshot> category in GetCategories(settings))
            {
                Button button = CreateCategoryButton(category.Key, category.Count(), settingNavigation);
                m_categoryButtons[category.Key] = button;
            }

            navigationScroll.Add(
                TajsDashboardUi.NavigationSectionLabel("Dashboard"),
                pageNavigation,
                TajsDashboardUi.NavigationSectionLabel("Settings"),
                settingNavigation);
            navigation.Body.Add(navigationScroll);
            sidebar.Add(navigation);
            UpdateNavigationSelection();
            return sidebar;
        }

        private void AddPageButton(Column container, DashboardPage page, string text, int? count = null)
        {
            Button button = TajsDashboardUi.NavigationButton(
                text,
                PageIcon(page),
                () => SelectPage(page),
                count);
            container.Add(button);
            m_pageButtons[page] = button;
        }

        private Button CreateCategoryButton(string category, int count, Column container)
        {
            Button button = TajsDashboardUi.NavigationButton(
                category,
                CategoryIcon(category),
                () => SelectCategory(category),
                count);
            container.Add(button);
            return button;
        }

        private static string PageIcon(DashboardPage page) =>
            page switch
            {
                DashboardPage.Overview => "Assets/Unity/UserInterface/General/Home.svg",
                DashboardPage.Profiler => "Assets/Unity/UserInterface/Toolbar/Stats.svg",
                DashboardPage.Performance => "Assets/Unity/UserInterface/General/UptimeStats.svg",
                DashboardPage.Tweaks => "Assets/Unity/UserInterface/General/Configure.svg",
                DashboardPage.SaveLoad => "Assets/Unity/UserInterface/General/Save.svg",
                DashboardPage.Memory => "Assets/Unity/UserInterface/General/Package.svg",
                DashboardPage.Rendering => "Assets/Unity/UserInterface/General/Layers.svg",
                DashboardPage.Compatibility => "Assets/Unity/UserInterface/General/Handshake.svg",
                DashboardPage.Logs => "Assets/Unity/UserInterface/General/Message.svg",
                DashboardPage.Settings => "Assets/Unity/UserInterface/General/Mod.svg",
                _ => "Assets/Unity/UserInterface/General/Mod.svg",
            };

        private static string CategoryIcon(string category)
        {
            if (category.IndexOf("Building", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/Build.svg";
            }
            if (category.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/FocusPoint.svg";
            }
            if (category.IndexOf("Designation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/EntityIcons/Designation.png";
            }
            if (category.IndexOf("Fleet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                category.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/VehicleFilterGlobal.svg";
            }
            if (category.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                category.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/Package.svg";
            }
            if (category.IndexOf("Notification", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/Message.svg";
            }
            if (category.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                category.IndexOf("Rendering", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/Layers.svg";
            }
            if (category.IndexOf("Profiler", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/Toolbar/Stats.svg";
            }
            if (category.IndexOf("World", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/MapBounds.svg";
            }
            return "Assets/Unity/UserInterface/General/Configure.svg";
        }

        private void SelectPage(DashboardPage page)
        {
            m_selectedPage = page;
            // Each page shares one ScrollColumn. Reset its viewport when navigation changes so
            // a previously deep settings scroll cannot clip the new page's heading.
            m_pageContent.ScrollToStart();
            UpdateNavigationSelection();
            QueueRefresh();
        }

        private void SelectCategory(string category)
        {
            m_selectedCategory = category;
            m_selectedPage = DashboardPage.Settings;
            m_pageContent.ScrollToStart();
            UpdateNavigationSelection();
            QueueRefresh();
        }

        private void UpdateNavigationSelection()
        {
            foreach (KeyValuePair<DashboardPage, Button> button in m_pageButtons)
            {
                button.Value.Selected(button.Key == m_selectedPage);
            }

            foreach (KeyValuePair<string, Button> button in m_categoryButtons)
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
            IReadOnlyList<SettingSnapshot>? settings = null;
            IReadOnlyList<CompatibilityReport>? reports = null;
            IReadOnlyList<LoadedModData>? mods = null;

            IReadOnlyList<SettingSnapshot> LoadSettings()
            {
                if (settings is null)
                {
                    settings = m_settings.GetSnapshot();
                    EnsureSelectedCategory(settings);
                }
                return settings;
            }

            IReadOnlyList<CompatibilityReport> LoadReports() => reports ??= m_runtime.GetCompatibilitySnapshot();
            IReadOnlyList<LoadedModData> LoadMods() => mods ??= GetTajsMods();

            foreach (Column page in m_pageContainers.Values)
            {
                page.Hide();
            }
            CurrentPage.Show();

            switch (m_selectedPage)
            {
                case DashboardPage.Overview:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddOverview(LoadSettings(), LoadReports(), LoadMods(), ReadProfilerSnapshot());
                    }
                    break;
                case DashboardPage.Profiler:
                    if (m_profilerPage is null)
                    {
                        EnsureProfilerPage(LoadSettings());
                    }
                    m_profilerPage!.Update(ReadProfilerSnapshot());
                    break;
                case DashboardPage.Performance:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddDomainPage(
                            "Performance",
                            "Behavior-neutral runtime diagnostics and explicitly opt-in maintenance operations.",
                            LoadSettings(),
                            new[] { "Performance", "Diagnostics" },
                            includeMaintenance: true);
                    }
                    break;
                case DashboardPage.Tweaks:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddDomainPage(
                            "Tweaks",
                            "Settings registered by gameplay and quality-of-life features.",
                            LoadSettings(),
                            new[] { "Tweak" },
                            includeMaintenance: false);
                    }
                    break;
                case DashboardPage.SaveLoad:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddSaveLoadPage(LoadSettings());
                    }
                    break;
                case DashboardPage.Memory:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddDomainPage(
                            "Memory",
                            "Explicit memory-facing diagnostics and paused-only asset maintenance.",
                            LoadSettings(),
                            new[] { "Memory", "Asset" },
                            includeMaintenance: true);
                    }
                    break;
                case DashboardPage.Rendering:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddDomainPage(
                            "Rendering",
                            "Rendering and visual-load settings exposed by the loaded suite.",
                            LoadSettings(),
                            new[] { "Render", "Texture", "Graphics" },
                            includeMaintenance: false);
                    }
                    break;
                case DashboardPage.Compatibility:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddCompatibilityPage(LoadReports(), LoadMods());
                    }
                    break;
                case DashboardPage.Logs:
                    EnsureLogsPage();
                    m_logsPage!.Update(ReadProfilerSnapshot());
                    break;
                case DashboardPage.Settings:
                    // Settings controls are dynamic and may change shape when descriptors are
                    // registered by another mod, so this is the one intentionally rebuildable
                    // page. Diagnostic pages update cached labels below instead.
                    CurrentPage.Clear();
                    AddSettingsPage(LoadSettings());
                    break;
                default:
                    if (m_builtPages.Add(m_selectedPage))
                    {
                        AddOverview(LoadSettings(), LoadReports(), LoadMods(), ReadProfilerSnapshot());
                    }
                    break;
            }
        }

        private void EnsureProfilerPage(IReadOnlyList<SettingSnapshot> settings)
        {
            if (m_profilerPage is not null)
            {
                return;
            }

            m_profilerPage = new ProfilerPageView(this, settings);
            CurrentPage.Add(m_profilerPage.Root);
            m_builtPages.Add(DashboardPage.Profiler);
        }

        private void EnsureLogsPage()
        {
            if (m_logsPage is not null)
            {
                return;
            }

            m_logsPage = new LogsPageView();
            CurrentPage.Add(m_logsPage.Root);
            m_builtPages.Add(DashboardPage.Logs);
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

            CurrentPage.Add(
                TajsDashboardUi.SectionHeader("Overview"),
                new Label("A compact operational view of the Taj's COI runtime in this gameplay scene.".AsLoc())
                    .FontSize(12));

            Row metrics = new Row(4.pt()).Wrap().AlignItemsStretch();
            metrics.Add(
                TajsDashboardUi.MetricTile(
                    "Mods loaded",
                    mods.Count.ToString(CultureInfo.InvariantCulture),
                    "Active",
                    TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile(
                    "Active settings",
                    activeSettings.ToString(CultureInfo.InvariantCulture),
                    "In use",
                    TajsDashboardUi.Cyan),
                TajsDashboardUi.MetricTile(
                    "Compatibility",
                    healthy ? "Healthy" : "Attention",
                    "See details",
                    healthy ? TajsDashboardUi.Green : TajsDashboardUi.Yellow),
                TajsDashboardUi.MetricTile(
                    "Runtime status",
                    profiler.IsAvailable ? "Recording" : "Unavailable",
                    "Live",
                    profiler.IsAvailable ? TajsDashboardUi.Green : TajsDashboardUi.Red),
                TajsDashboardUi.MetricTile(
                    "Load errors",
                    loadErrors.ToString(CultureInfo.InvariantCulture),
                    "Issues",
                    loadErrors == 0 ? TajsDashboardUi.Green : TajsDashboardUi.Red));
            CurrentPage.Add(metrics);

            Panel runtime = TajsDashboardUi.Card(
                "Runtime health",
                "Detailed profiler controls and trace actions remain on the Profiler tab.");
            Row runtimeStatuses = new Row(3.pt()).Wrap().AlignItemsCenter();
            runtimeStatuses.Add(
                TajsDashboardUi.StatusBadge(
                    profiler.IsAvailable ? "Recording" : "Unavailable",
                    profiler.IsAvailable ? TajsDashboardUi.Green : TajsDashboardUi.Red),
                TajsDashboardUi.StatusBadge(
                    "Deep trace: " + Truncate(profiler.DeepTrace, 32),
                    profiler.DeepTrace.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0
                        ? TajsDashboardUi.Yellow
                        : TajsDashboardUi.Cyan),
                TajsDashboardUi.StatusBadge(
                    "GPU: " + Truncate(profiler.GpuTiming, 32),
                    profiler.GpuTiming.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                        ? TajsDashboardUi.Yellow
                        : TajsDashboardUi.Green));
            Row runtimeRow = new Row(4.pt())
            {
                runtimeStatuses.FlexGrow(1f),
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "View profiler",
                    "Assets/Unity/UserInterface/Toolbar/Stats.svg",
                    () => SelectPage(DashboardPage.Profiler)),
            }.AlignItemsCenter();
            runtime.Body.Add(runtimeRow);
            CurrentPage.Add(runtime);

            Row summaries = new Row(5.pt()).AlignItemsStretch();
            summaries.Add(
                BuildCompatibilitySummary(reports).FlexGrow(1f),
                BuildLoadedModsPanel(mods).FlexGrow(1f));
            CurrentPage.Add(summaries);
        }

        private void AddDomainPage(
            string title,
            string subtitle,
            IReadOnlyList<SettingSnapshot> settings,
            IReadOnlyList<string> keywords,
            bool includeMaintenance)
        {
            CurrentPage.Add(TajsDashboardUi.SectionHeader(title), new Label(subtitle.AsLoc()).FontSize(12));
            if (includeMaintenance)
            {
                CurrentPage.Add(BuildMaintenancePanel());
            }

            IReadOnlyList<SettingSnapshot> filtered = settings
                .Where(snapshot => MatchesDomain(snapshot.Descriptor, keywords))
                .ToArray();
            if (filtered.Count == 0)
            {
                CurrentPage.Add(
                    TajsDashboardUi.Card(
                        "No registered settings",
                        "This surface is ready for settings from the loaded suite; the current scene did not report any matching descriptors."));
                return;
            }

            AddSettingsList(filtered, $"{title} settings");
        }

        private void AddSaveLoadPage(IReadOnlyList<SettingSnapshot> settings)
        {
            CurrentPage.Add(
                TajsDashboardUi.SectionHeader("Save & Load"),
                new Label("Settings and lifecycle controls that apply at save reload or game restart.".AsLoc())
                    .FontSize(12),
                BuildSaveRepairPanel());

            IReadOnlyList<SettingSnapshot> filtered = settings
                .Where(snapshot => MatchesDomain(snapshot.Descriptor, new[] { "Save", "Load" }))
                .ToArray();
            if (filtered.Count == 0)
            {
                CurrentPage.Add(
                    TajsDashboardUi.Card(
                        "No registered settings",
                        "This surface is ready for settings from the loaded suite; the current scene did not report any matching descriptors."));
                return;
            }

            AddSettingsList(filtered, $"Save & Load settings");
        }

        private Panel BuildSaveRepairPanel()
        {
            Panel panel = TajsDashboardUi.Card(
                "Save sanitizer",
                "Core reports only audited, type-specific findings. Unknown or uncertain save data is left untouched.");
            Row buttons = new Row(3.pt()).Wrap();
            Label feedback = new Label().FontSize(11).Hide();
            if (!AddCommandButton(
                    buttons,
                    Button.Area,
                    "Run dry-run report",
                    "tajs_save_sanitize_report",
                    feedback))
            {
                panel.Body.Add(new Label("The Core save sanitizer is unavailable in this scene.".AsLoc()).FontSize(12));
                return panel;
            }

            panel.Body.Add(buttons);
            panel.Body.Add(
                new Label(
                        "For a repair, use tajs_save_sanitize_repair <target> CONFIRM <new-save-name>. The command refuses to overwrite an existing or currently loaded save."
                            .AsLoc())
                    .FontSize(11));
            panel.Body.Add(feedback);
            return panel;
        }

        private void AddCompatibilityPage(
            IReadOnlyList<CompatibilityReport> reports,
            IReadOnlyList<LoadedModData> mods)
        {
            CurrentPage.Add(
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
                    details.Body.Add(
                        new Row(4.pt())
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
            CurrentPage.Add(details);
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

            CurrentPage.Add(
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
            AddSettingsList(
                visible,
                string.Equals(m_selectedCategory, AllCategories, StringComparison.Ordinal)
                    ? "All settings"
                    : $"Settings · {m_selectedCategory}");
        }

        private ButtonText CreateInlineCategoryButton(string category, int count)
        {
            ButtonText button = new ButtonText(
                Button.Area,
                $"{category} ({count})".AsLoc(),
                () => SelectCategory(category)).Compact();
            button.Selected(string.Equals(m_selectedCategory, category, StringComparison.Ordinal));
            return button;
        }

        private void AddSettingsList(IReadOnlyList<SettingSnapshot> settings, string heading) => AddSettingsListTo(CurrentPage, settings, heading);

        private void AddSettingsListTo(Column target, IReadOnlyList<SettingSnapshot> settings, string heading)
        {
            target.Add(TajsDashboardUi.SectionHeader($"{heading} ({settings.Count})"));
            if (settings.Count == 0)
            {
                target.Add(new Label("No settings match this view.".AsLoc()).FontSize(12));
                return;
            }

            foreach (IGrouping<string, SettingSnapshot> modGroup in settings
                         .GroupBy(snapshot => snapshot.Descriptor.ModId)
                         .OrderBy(group => group.First().Descriptor.ModDisplayName, StringComparer.Ordinal))
            {
                SettingDescriptor first = modGroup.First().Descriptor;
                target.Add(
                    new Label(
                            $"{first.ModDisplayName} · {modGroup.Count()} setting{PluralSuffix(modGroup.Count())}".AsLoc())
                        .FontBold()
                        .FontSize(16)
                        .MarginTop(3.pt()));

                foreach (IGrouping<string, SettingSnapshot> category in modGroup
                             .GroupBy(snapshot => snapshot.Descriptor.Category)
                             .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    target.Add(
                        new Label($"{category.Key} · {category.Count()}".AsLoc())
                            .FontBold()
                            .FontSize(12)
                            .StyleChip()
                            .MarginTop(2.pt()));
                    foreach (SettingSnapshot setting in category.OrderBy(
                                 snapshot => snapshot.Descriptor.DisplayName,
                                 StringComparer.Ordinal))
                    {
                        target.Add(CreateSettingControl(setting));
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
            }.MinWidth(0.px()).FlexGrow(1f).FlexShrink(1f);

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
                                    // ReSharper disable once AccessToModifiedClosure
                                    stateButton.Value(BooleanStateText(currentValue).AsLoc()).Selected(currentValue);
                                }
                            })
                        // ReSharper disable once RedundantArgumentDefaultValue
                        .Toggleable(true)
                        .Selected(enabled)
                        .Compact();
                    editor = stateButton;
                    break;

                case SettingValueType.Choice:
                    SettingChoice currentChoice = FindChoice(descriptor, snapshot.Value);
                    // ReSharper disable once UnusedParameter.Local
                    Dropdown<SettingChoice> dropdown = new Dropdown<SettingChoice>((choice, _, __) => new Label(choice.DisplayName.AsLoc()))
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

            editor.FlexShrink(0f).MaxWidth(280.px());

            Row settingRow = new Row(4.pt()).Width(100.Percent()).AlignItemsStart();
            settingRow.Add(description, editor);
            Column body = new Column(2.pt()).Width(100.Percent()).AlignItemsStretch();
            body.Add(settingRow, feedback);
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
                TajsDashboardUi.StatusBadge(
                    "Deep trace: " + profiler.DeepTrace,
                    profiler.DeepTrace.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0 ? TajsDashboardUi.Yellow : TajsDashboardUi.Cyan),
                TajsDashboardUi.StatusBadge("Ring drops: " + profiler.RingDrops, profiler.RingDrops == "0" ? TajsDashboardUi.Green : TajsDashboardUi.Yellow),
                TajsDashboardUi.StatusBadge(
                    "GPU timing: " + profiler.GpuTiming,
                    profiler.GpuTiming.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ? TajsDashboardUi.Yellow : TajsDashboardUi.Green));
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
            panel.Body.Add(
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Refresh diagnostics",
                    "Assets/Unity/UserInterface/General/Repeat.svg",
                    QueueRefresh));
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
            Panel panel = TajsDashboardUi.Card(
                "Compatibility summary",
                "Missing components and load errors stay visible instead of silently disabling controls.");
            Row summary = new Row(3.pt()).Wrap();
            summary.Add(
                TajsDashboardUi.MetricTile("Healthy", compatible.ToString(CultureInfo.InvariantCulture), TajsDashboardUi.Green),
                TajsDashboardUi.MetricTile("Warnings", degraded.ToString(CultureInfo.InvariantCulture), TajsDashboardUi.Yellow),
                TajsDashboardUi.MetricTile(
                    "Unavailable",
                    disabled.ToString(CultureInfo.InvariantCulture),
                    disabled == 0 ? TajsDashboardUi.Green : TajsDashboardUi.Red));
            panel.Body.Add(summary);

            foreach (CompatibilityReport report in reports.Take(8))
            {
                panel.Body.Add(
                    new Row(3.pt())
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
                panel.Body.Add(
                    new Row(4.pt())
                    {
                        new Column(1.pt()) { new Label(mod.Manifest.Id.AsLoc()).FontBold(), new Label($"Version {version}".AsLoc()).FontSize(11) }
                            .FlexGrow(1f),
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

            buttons.Add(
                TajsDashboardUi.ActionButton(
                    variant,
                    text,
                    CommandIcon(command),
                    () => ExecuteConsoleCommand(command, feedback)));
            return true;
        }

        private static string CommandIcon(string command)
        {
            if (command.IndexOf("trace", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/ExportToString.svg";
            }
            if (command.IndexOf("clear", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/Reset.svg";
            }
            if (command.IndexOf("trim", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/General/Maintenance.svg";
            }
            if (command.IndexOf("deep", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Assets/Unity/UserInterface/Toolbar/Stats.svg";
            }
            return "Assets/Unity/UserInterface/General/Configure.svg";
        }

        private void ExportTrace()
        {
            TryReadCommand("tajs_profiler_trace_export runtime", out string output);
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
            var parts = new List<string> { descriptor.StableId, ApplyModeText(descriptor.ApplyMode) };
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

        private static string Truncate(string? text, int maximumLength)
        {
            if (text is null)
            {
                return string.Empty;
            }
            string nonNullText = text;
            if (nonNullText.Length == 0 || nonNullText.Length <= maximumLength)
            {
                return nonNullText;
            }
            return nonNullText.Substring(0, maximumLength) + "...";
        }

        private static string PluralSuffix(int count) => count == 1 ? string.Empty : "s";

        private sealed class ProfilerPageView
        {
            internal readonly Column Root;
            private readonly Label m_flightRecorder;
            private readonly Label m_deepTrace;
            private readonly Label m_ringDrops;
            private readonly Label m_gpuTiming;
            private readonly Label m_statusText;
            private readonly Label m_frame;
            private readonly Label m_waitForSim;
            private readonly Label m_simUpdate;
            private readonly Label m_render;
            private readonly Label m_classification;
            private readonly Label m_runtimeText;

            internal ProfilerPageView(TajsDashboardWindow owner, IReadOnlyList<SettingSnapshot> settings)
            {
                Root = new Column(5.pt()).AlignItemsStretch();
                Root.Add(
                    TajsDashboardUi.SectionHeader("Profiler"),
                    new Label("Inspect the existing low-overhead flight recorder and arm bounded deep tracing when needed.".AsLoc())
                        .FontSize(12));

                Panel live = TajsDashboardUi.Card(
                    "Live profiler",
                    "Read-only status from the existing runtime profiler; Core only consumes its command contract.");
                Row statuses = new Row(3.pt()).Wrap().AlignItemsCenter();
                m_flightRecorder = TajsDashboardUi.StatusBadge("Flight recorder: unavailable", TajsDashboardUi.Red);
                m_deepTrace = TajsDashboardUi.StatusBadge("Deep trace: unavailable", TajsDashboardUi.Cyan);
                m_ringDrops = TajsDashboardUi.StatusBadge("Ring drops: unavailable", TajsDashboardUi.Yellow);
                m_gpuTiming = TajsDashboardUi.StatusBadge("GPU timing: unavailable", TajsDashboardUi.Yellow);
                statuses.Add(m_flightRecorder, m_deepTrace, m_ringDrops, m_gpuTiming);
                m_statusText = new Label().FontSize(11).Selectable(true);
                live.Body.Add(statuses, m_statusText);

                Panel runtime = TajsDashboardUi.Card(
                    "Runtime snapshot",
                    "Latest frame sample plus the rolling summary retained by the profiler service.");
                Row latest = new Row(3.pt()).Wrap().AlignItemsStretch();
                m_frame = new Label("unavailable".AsLoc()).FontBold().FontSize(17).Color(TajsDashboardUi.Cyan);
                m_waitForSim = new Label("unavailable".AsLoc()).FontBold().FontSize(17).Color(TajsDashboardUi.Cyan);
                m_simUpdate = new Label("unavailable".AsLoc()).FontBold().FontSize(17).Color(TajsDashboardUi.Cyan);
                m_render = new Label("unavailable".AsLoc()).FontBold().FontSize(17).Color(TajsDashboardUi.Cyan);
                m_classification = new Label("unavailable".AsLoc()).FontBold().FontSize(17).Color(TajsDashboardUi.Green);
                latest.Add(
                    TajsDashboardUi.MetricTile("Frame", m_frame, TajsDashboardUi.Cyan),
                    TajsDashboardUi.MetricTile("Wait for sim", m_waitForSim, TajsDashboardUi.Cyan),
                    TajsDashboardUi.MetricTile("Sim update", m_simUpdate, TajsDashboardUi.Cyan),
                    TajsDashboardUi.MetricTile("Render", m_render, TajsDashboardUi.Cyan),
                    TajsDashboardUi.MetricTile("Classification", m_classification, TajsDashboardUi.Green));
                m_runtimeText = new Label().FontSize(11).Selectable(true);
                runtime.Body.Add(latest, new Label("Rolling summary".AsLoc()).FontBold().MarginTop(2.pt()), m_runtimeText);

                Root.Add(live, owner.BuildQuickActionsPanel(), runtime);
                IReadOnlyList<SettingSnapshot> profilerSettings = settings
                    .Where(snapshot => snapshot.Descriptor.ModId.IndexOf("Profiler", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       snapshot.Descriptor.Category.IndexOf("Profiler", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();
                owner.AddSettingsListTo(Root, profilerSettings, "Profiler settings");
            }

            internal void Update(ProfilerSnapshot profiler)
            {
                m_flightRecorder.Value(("Flight recorder: " + profiler.FlightRecorder).AsLoc())
                    .Color(profiler.IsAvailable ? TajsDashboardUi.Green : TajsDashboardUi.Red);
                m_deepTrace.Value(("Deep trace: " + profiler.DeepTrace).AsLoc())
                    .Color(profiler.DeepTrace.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0 ? TajsDashboardUi.Yellow : TajsDashboardUi.Cyan);
                m_ringDrops.Value(("Ring drops: " + profiler.RingDrops).AsLoc())
                    .Color(profiler.RingDrops == "0" ? TajsDashboardUi.Green : TajsDashboardUi.Yellow);
                m_gpuTiming.Value(("GPU timing: " + profiler.GpuTiming).AsLoc())
                    .Color(profiler.GpuTiming.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ? TajsDashboardUi.Yellow : TajsDashboardUi.Green);
                m_statusText.Value(Truncate(profiler.StatusText, 900).AsLoc());
                m_frame.Value(profiler.Frame.AsLoc());
                m_waitForSim.Value(profiler.WaitForSim.AsLoc());
                m_simUpdate.Value(profiler.SimUpdate.AsLoc());
                m_render.Value(profiler.Render.AsLoc());
                m_classification.Value(profiler.Classification.AsLoc());
                m_runtimeText.Value(Truncate(profiler.RuntimeText, 1600).AsLoc());
            }
        }

        private sealed class LogsPageView
        {
            internal readonly Column Root;
            private readonly Label m_statusText;
            private readonly Label m_runtimeText;

            internal LogsPageView()
            {
                Root = new Column(5.pt()).AlignItemsStretch();
                Root.Add(
                    TajsDashboardUi.SectionHeader("Logs"),
                    new Label("The dashboard keeps command output copyable and leaves structured-log ownership with each service.".AsLoc())
                        .FontSize(12));

                Panel panel = TajsDashboardUi.Card(
                    "Latest diagnostics output",
                    "Use the in-game console for the complete stream; these bounded snapshots are the most useful operational context.");
                panel.Body.Add(new Label("Flight recorder status".AsLoc()).FontBold());
                m_statusText = new Label().FontSize(11).Selectable(true);
                panel.Body.Add(m_statusText);
                panel.Body.Add(new Label("Runtime summary".AsLoc()).FontBold().MarginTop(3.pt()));
                m_runtimeText = new Label().FontSize(11).Selectable(true);
                panel.Body.Add(m_runtimeText);
                Root.Add(panel);
            }

            internal void Update(ProfilerSnapshot profiler)
            {
                m_statusText.Value(Truncate(profiler.StatusText, 1200).AsLoc());
                m_runtimeText.Value(Truncate(profiler.RuntimeText, 1800).AsLoc());
            }
        }

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
                new(
                    false,
                    status,
                    runtime,
                    "unavailable",
                    "unavailable",
                    "unavailable",
                    "unavailable",
                    "unavailable",
                    "unavailable",
                    "unavailable",
                    "unavailable",
                    "unavailable");

            private static string ValueOrUnavailable(string value) =>
                string.IsNullOrWhiteSpace(value) ? "unavailable" : value;
        }
    }

    internal static class TajsDashboardUi
    {
        internal static readonly ColorRgba Cyan = new(130, 200, 255);
        internal static readonly ColorRgba Green = new(130, 220, 150);
        internal static readonly ColorRgba Yellow = new(245, 195, 90);
        internal static readonly ColorRgba Red = new(240, 105, 105);
        internal static readonly ColorRgba Muted = new(170, 180, 195);

        internal static Label SectionHeader(string text) =>
            new Label(text.AsLoc()).FontBold().FontSize(18).MarginTop(3.pt()).MarginBottom(1.pt());

        internal static Label NavigationSectionLabel(string text) =>
            new Label(text.ToUpperInvariant().AsLoc())
                .FontBold()
                .FontSize(10)
                .Color(Muted)
                .MarginTop(3.pt())
                .MarginBottom(1.pt());

        internal static Button NavigationButton(
            string text,
            string iconPath,
            Action onClick,
            int? count = null)
        {
            Button button = new Button(Button.Area, onClick).Compact();
            Row content = new Row(3.pt()).Width(100.Percent()).AlignItemsCenter();
            content.Add(
                new Icon(iconPath).Width(16.px()).Height(16.px()),
                new Label(text.AsLoc()).FontSize(12).FlexGrow(1f));
            if (count.HasValue)
            {
                content.Add(CountBadge(count.Value));
            }
            button.Add(content);
            return button;
        }

        internal static Button ActionButton(
            ButtonVariant variant,
            string text,
            string iconPath,
            Action onClick)
        {
            Button button = new Button(variant, onClick).Compact();
            Row content = new Row(3.pt()).AlignItemsCenter();
            content.Add(
                new Icon(iconPath).Width(14.px()).Height(14.px()),
                new Label(text.AsLoc()).FontSize(11));
            button.Add(content);
            return button;
        }

        internal static Label CountBadge(int count) =>
            new Label(count.ToString(CultureInfo.InvariantCulture).AsLoc())
                .StyleChip()
                .FontSize(10)
                .Color(Muted);

        internal static Panel Card(string title, string subtitle, params UiComponent[] children)
        {
            Panel panel = new Panel(true)
                .ReducedPadding()
                .BodyGap(3.pt())
                .StyleGroupDark();
            panel.Body.Add(new Label(title.AsLoc()).FontBold().FontSize(14));
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                panel.Body.Add(new Label(subtitle.AsLoc()).FontSize(11).Color(Muted));
            }
            panel.Body.Add(children);
            return panel;
        }

        internal static Panel MetricTile(string title, string value, ColorRgba color) => MetricTile(
            title,
            new Label(value.AsLoc()).FontBold().FontSize(17).Color(color),
            color);

        internal static Panel MetricTile(string title, string value, string detail, ColorRgba color) => MetricTile(
            title,
            new Label(value.AsLoc()).FontBold().FontSize(17).Color(color),
            detail,
            color);

        internal static Panel MetricTile(string title, Label value, ColorRgba color)
            => MetricTile(title, value, string.Empty, color);

        internal static Panel MetricTile(string title, Label value, string detail, ColorRgba color)
        {
            Panel panel = new Panel(true)
                .ReducedPadding()
                .BodyGap(1.pt())
                .StyleGroupDark()
                .MinWidth(130.px())
                .FlexGrow(1f)
                .BodyAdd(new Label(title.AsLoc()).FontSize(11), value.Color(color));
            if (!string.IsNullOrWhiteSpace(detail))
            {
                panel.Body.Add(new Label(detail.AsLoc()).FontSize(10).Color(Muted));
            }
            return panel;
        }

        internal static Label StatusBadge(string text, ColorRgba color) =>
            new Label(text.AsLoc()).StyleChip().FontSize(11).Color(color);
    }
}
