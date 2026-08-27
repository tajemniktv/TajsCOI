// Taj's COI Mods | TajsDashboardBootstrapPanel.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using HarmonyLib;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Bootstrap;
using TajsCOI.Common.Ui;
using Button = Mafi.Unity.UiToolkit.Library.Button;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using Panel = Mafi.Unity.UiToolkit.Library.Panel;
using Row = Mafi.Unity.UiToolkit.Library.Row;

namespace TajsCOI.Core.Settings
{
    /// <summary>
    ///     The explicit, in-game custody surface for the optional early bootstrap payload.
    ///     All file ownership and safety decisions remain in TajsBootstrap; this panel only
    ///     supplies runtime-discovered paths and reports the primitive result to the user.
    /// </summary>
    internal static class TajsDashboardBootstrapPanel
    {
        internal static Panel Build(Action queueRefresh)
        {
            Panel panel = TajsDashboardUi.Card(
                "Optional bootstrap installer",
                "Install, verify, repair, disable, or uninstall only the Tajs-owned payload. UnityDoorstop files such as root winhttp.dll are never managed, and no elevation is attempted.");
            Label status = new Label().FontSize(11).Selectable(true);
            Label feedback = new Label().FontSize(11).Hide().Selectable(true);

            void RefreshStatus()
            {
                string? root = BootstrapInstaller.DiscoverGameRoot();
                if (root is null)
                {
                    status.Value("Game root could not be discovered from the running process.".AsLoc());
                    return;
                }

                BootstrapInstallResult result = BootstrapInstaller.Verify(root);
                status.Value(("Root: " + root + "\n" + result).AsLoc());
            }

            void Report(string operation, BootstrapInstallResult result)
            {
                feedback.Value((operation + ": " + result).AsLoc()).Show();
                RefreshStatus();
                queueRefresh();
            }

            bool TryGetRoot(out string root)
            {
                root = BootstrapInstaller.DiscoverGameRoot() ?? string.Empty;
                if (root.Length > 0)
                {
                    return true;
                }

                feedback.Value("Bootstrap operation refused: the game root was not discovered from the running process.".AsLoc()).Show();
                return false;
            }

            bool TryGetSources(out string bootstrapPath, out string harmonyPath)
            {
                bootstrapPath = string.Empty;
                harmonyPath = string.Empty;
                try
                {
                    bootstrapPath = typeof(BootstrapInstaller).Assembly.Location;
                    harmonyPath = typeof(Harmony).Assembly.Location;
                }
                catch (Exception exception)
                {
                    feedback.Value(("Bootstrap source discovery failed: " + exception.Message).AsLoc()).Show();
                    return false;
                }

                if (File.Exists(bootstrapPath) && File.Exists(harmonyPath))
                {
                    return true;
                }

                feedback.Value(("Bootstrap source discovery failed: expected payload or canonical 0Harmony.dll was not found.\n" +
                                "payload=" + bootstrapPath + "\nharmony=" + harmonyPath).AsLoc()).Show();
                return false;
            }

            Row actions = new Row(3.pt()).Wrap().AlignItemsCenter();
            actions.Add(
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Install",
                    "Assets/Unity/UserInterface/General/Configure.svg",
                    () =>
                    {
                        if (TryGetRoot(out string root) && TryGetSources(out string bootstrapPath, out string harmonyPath))
                        {
                            Report("Install", BootstrapInstaller.Install(new BootstrapInstallRequest(root, bootstrapPath, harmonyPath)));
                        }
                    }),
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Verify",
                    "Assets/Unity/UserInterface/General/Repeat.svg",
                    () =>
                    {
                        if (TryGetRoot(out string root))
                        {
                            Report("Verify", BootstrapInstaller.Verify(root));
                        }
                    }),
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Repair",
                    "Assets/Unity/UserInterface/General/Repeat.svg",
                    () =>
                    {
                        if (TryGetRoot(out string root) && TryGetSources(out string bootstrapPath, out string harmonyPath))
                        {
                            Report("Repair", BootstrapInstaller.Repair(new BootstrapInstallRequest(root, bootstrapPath, harmonyPath)));
                        }
                    }),
                TajsDashboardUi.ActionButton(
                    Button.Area,
                    "Disable",
                    "Assets/Unity/UserInterface/General/Cancel.svg",
                    () =>
                    {
                        if (TryGetRoot(out string root))
                        {
                            Report("Disable", BootstrapInstaller.Disable(root));
                        }
                    }),
                TajsDashboardUi.ActionButton(
                    Button.Warning,
                    "Uninstall",
                    "Assets/Unity/UserInterface/General/Cancel.svg",
                    () =>
                    {
                        if (TryGetRoot(out string root))
                        {
                            Report("Uninstall", BootstrapInstaller.Uninstall(root));
                        }
                    }));

            panel.Body.Add(actions, status, feedback);
            RefreshStatus();
            return panel;
        }
    }
}
