// Taj's COI Mods | TajsDashboardLauncher.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi;
using Mafi.Core.Console;
using Mafi.Unity;

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
                m_window.Value.CloseNoFade();
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

            m_window = m_resolver.Instantiate<TajsDashboardWindow>();
            return "TajsCOI dashboard: shown";
        }
    }
}
