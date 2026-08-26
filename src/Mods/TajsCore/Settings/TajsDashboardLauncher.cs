// Taj's COI Mods | TajsDashboardLauncher.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi;
using Mafi.Core.Console;
using Mafi.Unity;
using Mafi.Unity.UiToolkit.Library;

namespace TajsCOI.Core.Settings
{
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class TajsDashboardLauncher : IHotReloadUi
    {
        private readonly DependencyResolver m_resolver;
        private Option<TajsDashboardWindow> m_window;

        public TajsDashboardLauncher(DependencyResolver resolver)
        {
            m_resolver = resolver;
        }

        public void DisposeForHotReload()
        {
            if (m_window.HasValue)
            {
                TajsDashboardWindow window = m_window.Value;
                window.OnCloseStart -= OnWindowCloseStart;
                window.CloseNoFade();
                m_window = Option<TajsDashboardWindow>.None;
            }
        }

        [ConsoleCommand(
            documentation: "Shows the runtime dashboard for all loaded Taj's COI mods, component status, and settings.",
            customCommandName: "tajs_dashboard")]
        private string ToggleDashboard()
        {
            if (m_window.HasValue && m_window.Value.IsOpen)
            {
                m_window.Value.Close();
                m_window = Option<TajsDashboardWindow>.None;
                return "TajsCOI dashboard: hidden";
            }

            var window = m_resolver.Instantiate<TajsDashboardWindow>();
            window.OnCloseStart += OnWindowCloseStart;
            m_window = window;
            return "TajsCOI dashboard: shown";
        }

        private void OnWindowCloseStart(Window window)
        {
            if (!m_window.HasValue || !ReferenceEquals(m_window.Value, window))
            {
                return;
            }

            // Close-on-click-outside can close the window without going through the command
            // launcher. Remove the resolver-owned reference at the close boundary and detach
            // this handler so a stale UI object cannot retain the launcher/resolver graph.
            window.OnCloseStart -= OnWindowCloseStart;
            m_window = Option<TajsDashboardWindow>.None;
        }
    }
}
