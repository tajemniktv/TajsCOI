// Taj's COI Mods | TajsDashboardWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Core;
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
        private readonly ITajsSettings m_settings;
        private readonly ITajsRuntime m_runtime;
        private readonly ScrollColumn m_content;

        public TajsDashboardWindow(ITajsSettings settings, ITajsRuntime runtime, UiRoot uiRoot)
            : base("TajsCOI Dashboard".AsLoc())
        {
            m_settings = settings;
            m_runtime = runtime;
            m_content = new ScrollColumn().Fill().AlignItemsStretch().Gap(4.pt());

            AddBodySingle(
                new Row(2.pt())
                {
                    new Label("Runtime settings and component compatibility".AsLoc()).FlexGrow(1f),
                    new ButtonText("Refresh".AsLoc(), Rebuild),
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
            AddLoadedMods();
            AddSettings();
            AddCompatibility();
        }

        private void AddLoadedMods()
        {
            m_content.Add(SectionTitle("Loaded Taj's mods"));
            bool found = false;
            var tajsMods = new List<LoadedModData>();
            foreach (LoadedModData mod in ModsLoader.LoadedAndFailedMods)
            {
                if (mod.Manifest.Id.StartsWith("Tajs", StringComparison.Ordinal))
                {
                    tajsMods.Add(mod);
                }
            }
            foreach (LoadedModData mod in tajsMods.OrderBy(x => x.Manifest.Id, StringComparer.Ordinal))
            {
                found = true;
                string state = mod.LoadError.HasValue ? "FAILED: " + mod.LoadError.Value : "loaded";
                m_content.Add(new Label($"{mod.Manifest.Id} {mod.Manifest.Version} — {state}".AsLoc()));
            }
            if (!found)
            {
                m_content.Add(new Label("No loaded Taj's mods were reported.".AsLoc()));
            }
        }

        private void AddSettings()
        {
            IReadOnlyList<SettingSnapshot> settings = m_settings.GetSnapshot();
            m_content.Add(SectionTitle("Settings"));
            if (settings.Count == 0)
            {
                m_content.Add(new Label("No settings are registered in this scene.".AsLoc()));
                return;
            }

            foreach (IGrouping<string, SettingSnapshot> modGroup in settings.GroupBy(x => x.Descriptor.ModId))
            {
                m_content.Add(new Label(modGroup.First().Descriptor.ModDisplayName.AsLoc()).FontBold().FontSize(18));
                foreach (IGrouping<string, SettingSnapshot> category in modGroup.GroupBy(x => x.Descriptor.Category))
                {
                    m_content.Add(new Label(category.Key.AsLoc()).FontBold());
                    foreach (SettingSnapshot setting in category)
                    {
                        m_content.Add(CreateSettingControl(setting));
                    }
                }
            }
        }

        private UiComponent CreateSettingControl(SettingSnapshot snapshot)
        {
            SettingDescriptor descriptor = snapshot.Descriptor;
            Label error = new Label().Hide();
            var container = new Column(1.pt())
            {
                new Label(descriptor.DisplayName.AsLoc()).FontBold(),
                new Label(descriptor.Description.AsLoc()).FontSize(12),
                new Label($"{descriptor.StableId} · {ApplyModeText(descriptor.ApplyMode)}".AsLoc()).FontSize(11),
            };

            switch (descriptor.ValueType)
            {
                case SettingValueType.Boolean:
                    Toggle toggle = new Toggle().JustifyItemsStart().Value((bool)snapshot.Value);
                    toggle.OnValueChanged(value =>
                    {
                        SettingSetResult result = Set(descriptor, value, error);
                        if (!result.Success)
                        {
                            toggle.Value(m_settings.Get<bool>(descriptor.ModId, descriptor.Key));
                        }
                    });
                    container.Add(toggle);
                    break;

                case SettingValueType.Choice:
                    Dropdown<SettingChoice> dropdown = new Dropdown<SettingChoice>(
                            (choice, _, __) => new Label(choice.DisplayName.AsLoc()))
                        .SetOptions(descriptor.Choices)
                        .SetValue(descriptor.Choices.Single(x =>
                            string.Equals(x.Value, (string)snapshot.Value, StringComparison.Ordinal)));
                    dropdown.OnValueChanged((choice, _) => Set(descriptor, choice.Value, error));
                    container.Add(dropdown);
                    break;

                default:
                    TextField field = new TextField().Text(FormatValue(snapshot.Value));
                    field.OnEditEnd(text =>
                    {
                        SettingSetResult result = Set(descriptor, text, error);
                        if (!result.Success)
                        {
                            field.Text(FormatCurrent(descriptor));
                        }
                    });
                    container.Add(field);
                    break;
            }

            container.Add(error);
            return container;
        }

        private SettingSetResult Set(SettingDescriptor descriptor, object value, Label error)
        {
            SettingSetResult result = m_settings.TrySet(descriptor.ModId, descriptor.Key, value);
            if (result.Success)
            {
                error.Value(ApplyModeText(result.ApplyMode).AsLoc()).Show();
            }
            else
            {
                error.Value(result.Error.AsLoc()).Show();
            }
            return result;
        }

        private void AddCompatibility()
        {
            IReadOnlyList<CompatibilityReport> reports = m_runtime.GetCompatibilitySnapshot();
            m_content.Add(SectionTitle("Component compatibility"));
            if (reports.Count == 0)
            {
                m_content.Add(new Label("No component reports are available.".AsLoc()));
                return;
            }

            foreach (CompatibilityReport report in reports)
            {
                m_content.Add(new Column(1.pt())
                {
                    new Label($"{report.ModId} / {report.ComponentId}: {report.State}".AsLoc()).FontBold(),
                    new Label($"Expected: {report.Expected}".AsLoc()).FontSize(12),
                    new Label($"Observed: {report.Observed}".AsLoc()).FontSize(12),
                    new Label($"Reason: {report.Reason}".AsLoc()).FontSize(12),
                });
            }
        }

        private string FormatCurrent(SettingDescriptor descriptor)
        {
            SettingSnapshot current = m_settings.GetSnapshot().Single(x =>
                string.Equals(x.Descriptor.StableId, descriptor.StableId, StringComparison.Ordinal));
            return FormatValue(current.Value);
        }

        private static Label SectionTitle(string text) =>
            new Label(text.AsLoc()).FontBold().FontSize(22).MarginTop(4.pt());

        private static string FormatValue(object value) =>
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        private static string ApplyModeText(SettingApplyMode mode) =>
            mode == SettingApplyMode.Immediate ? "Applied immediately" :
            mode == SettingApplyMode.ReloadSave ? "Applies after reloading the save" :
            "Applies after restarting the game";
    }
}
