// Taj's COI Mods | TajsMainMenuBridge.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.MainMenu;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Ui;
using TajsCOI.Common.Profiles;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using Button = Mafi.Unity.UiToolkit.Library.Button;

namespace TajsCOI.Core.Settings
{
    /// <summary>
    ///     Narrow 0.8.7b main-menu bridge. It adds only a global/profile-safe entry point and
    ///     deliberately does not resolve gameplay-scene services such as entity metadata.
    /// </summary>
    internal static class TajsMainMenuBridge
    {
        internal const string HarmonyId = "TajsCOI.Core.MainMenu";
        private static readonly object s_gate = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MainMenuScreen, object> s_attached = new();
        private static Harmony? s_harmony;

        internal static bool HasSupportedTarget()
        {
            Type? controller = FindControllerType();
            return controller is not null && FindConstructor(controller) is not null;
        }

        internal static bool TryInstall()
        {
            lock (s_gate)
            {
                if (s_harmony is not null)
                {
                    return true;
                }

                Type? controller = FindControllerType();
                ConstructorInfo? constructor = controller is null ? null : FindConstructor(controller);
                if (constructor is null)
                {
                    return false;
                }

                try
                {
                    var harmony = new Harmony(HarmonyId);
                    harmony.Patch(
                        constructor,
                        postfix: new HarmonyMethod(typeof(TajsMainMenuBridge), nameof(OnMainMenuConstructed)));
                    s_harmony = harmony;
                    return true;
                }
                catch
                {
                    // A changed private seam must leave the native menu and no-bootstrap path
                    // available. Do not retain a partially installed owner.
                    s_harmony?.UnpatchAll(HarmonyId);
                    s_harmony = null;
                    return false;
                }
            }
        }

        internal static void Uninstall()
        {
            lock (s_gate)
            {
                if (s_harmony is not null)
                {
                    s_harmony.UnpatchAll(HarmonyId);
                    s_harmony = null;
                }
            }
        }

        private static ConstructorInfo? FindConstructor(Type controller) =>
            controller.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(item => item.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[]
                {
                    "Mafi.Unity.IMain",
                    "Mafi.Unity.UiToolkit.UiRoot",
                    "Mafi.Core.IFileSystemHelper",
                    "Mafi.DependencyResolver",
                }));

        private static Type? FindControllerType() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Mafi.Unity.MainMenu.MainMenuController"))
                .FirstOrDefault(type => type is not null) ??
            AccessTools.TypeByName("Mafi.Unity.MainMenu.MainMenuController");

        private static void OnMainMenuConstructed(object __instance)
        {
            try
            {
                if (__instance is null)
                {
                    return;
                }

                FieldInfo? menuField = AccessTools.Field(__instance.GetType(), "m_mainMenu");
                FieldInfo? resolverField = AccessTools.Field(__instance.GetType(), "m_resolver");
                if (menuField?.GetValue(__instance) is not MainMenuScreen menu ||
                    resolverField?.GetValue(__instance) is not DependencyResolver resolver)
                {
                    return;
                }

                s_attached.GetValue(menu, _ =>
                {
                    menu.Add(new ButtonText(
                        Button.General,
                        "TajsCOI Control Center".AsLoc(),
                        () => OpenGlobalWindow(menu, resolver)).Compact());
                    return new object();
                });
            }
            catch
            {
                // Main-menu augmentation is optional; native construction must remain intact.
            }
        }

        private static void OpenGlobalWindow(MainMenuScreen menu, DependencyResolver resolver)
        {
            try
            {
                resolver.Instantiate<TajsMainMenuWindow>().OpenIn(menu);
            }
            catch
            {
                // Do not turn an optional button into a startup or menu failure.
            }
        }
    }

    internal sealed class TajsMainMenuWindow : Window
    {
        private readonly ISettingsProfileService m_profiles;
        private readonly ITajsSettings m_settings;
        private readonly ITajsRuntime m_runtime;

        public TajsMainMenuWindow(
            ISettingsProfileService profiles,
            ITajsSettings settings,
            ITajsRuntime runtime)
            : base("TajsCOI Control Center".AsLoc())
        {
            m_profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Build();
        }

        private void Build()
        {
            var body = new ScrollColumn().Fill().AlignItemsStretch().Gap(4.pt());
            body.Add(new Label(
                "Main-menu controls are limited to global settings, profile-safe values, and diagnostics."
                    .AsLoc()).FontSize(12));
            body.Add(BuildSettingsSummary(), BuildProfiles(), BuildDiagnostics());
            AddBodySingle(body);
        }

        private Panel BuildSettingsSummary()
        {
            Panel panel = TajsDashboardUi.Card("Global settings", "Current values exposed by Core's ordinary settings service.");
            foreach (SettingSnapshot snapshot in m_settings.GetSnapshot()
                         .Where(item => item.Descriptor.Scope == SettingScope.Global)
                         .OrderBy(item => item.Descriptor.StableId, StringComparer.Ordinal))
            {
                panel.Body.Add(new Label(
                    (snapshot.Descriptor.StableId + " = " + Convert.ToString(snapshot.Value, System.Globalization.CultureInfo.InvariantCulture)).AsLoc())
                    .FontSize(11));
            }
            return panel;
        }

        private Panel BuildProfiles()
        {
            Panel panel = TajsDashboardUi.Card("Settings profiles", "Preview or apply profile-safe values before entering a save.");
            IReadOnlyList<SettingsProfile> profiles = m_profiles.List();
            if (profiles.Count == 0)
            {
                panel.Body.Add(new Label("No saved profiles.".AsLoc()).FontSize(11));
                return panel;
            }

            foreach (SettingsProfile profile in profiles)
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
                            SettingsProfilePreview preview = m_profiles.Preview(profile);
                            feedback.Value((profile.Name + ": " + string.Join(", ", preview.Entries
                                    .GroupBy(entry => entry.State)
                                    .Select(group => group.Key + "=" + group.Count()))).AsLoc()).Show();
                        }),
                    TajsDashboardUi.ActionButton(
                        Button.Area,
                        "Apply",
                        "Assets/Unity/UserInterface/General/Configure.svg",
                        () =>
                        {
                            SettingsProfileApplyResult result = m_profiles.Apply(profile);
                            feedback.Value((profile.Name + ": applied=" + result.AppliedCount +
                                    ", skipped=" + result.SkippedIds.Count + ", errors=" + result.Errors.Count).AsLoc()).Show();
                        }));
                panel.Body.Add(new Row(3.pt())
                {
                    new Column(1.pt())
                    {
                        new Label(profile.Name.AsLoc()).FontBold(),
                        new Label((profile.Values.Count + " saved value(s)").AsLoc()).FontSize(11),
                        feedback,
                    }.FlexGrow(1f),
                    actions,
                }.AlignItemsCenter());
            }
            return panel;
        }

        private Panel BuildDiagnostics()
        {
            Panel panel = TajsDashboardUi.Card("Diagnostics", "Read-only process and compatibility status.");
            panel.Body.Add(new Label(("Loaded mods: " + m_runtime.GetLoadedModSnapshot().Count).AsLoc()).FontSize(11));
            panel.Body.Add(new Label(("Compatibility reports: " + m_runtime.GetCompatibilitySnapshot().Count).AsLoc()).FontSize(11));
            panel.Body.Add(new Label(("Registered capabilities: " + m_runtime.GetCapabilitySnapshot().Count).AsLoc()).FontSize(11));
            panel.Body.Add(new Label(("Registered components: " + m_runtime.GetComponentSnapshot().Count).AsLoc()).FontSize(11));
            panel.Body.Add(new ButtonText(Button.Area, "Close".AsLoc(), Close));
            return panel;
        }
    }
}
