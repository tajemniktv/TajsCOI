// Taj's COI Mods | ShortcutPersistenceService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Text;
using Mafi;
using Mafi.Core.Console;
using TajsCOI.Common.Shortcuts;

namespace TajsCOI.Core.Shortcuts
{
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class ShortcutPersistenceService
    {
        private readonly IShortcutRegistry m_registry;
        private readonly string m_filePath;

        public ShortcutPersistenceService(IShortcutRegistry registry)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsCOI",
                "shortcuts.txt");
            // Missing files are normal on first run. A malformed file is reported by the
            // explicit command so startup cannot disable the rest of Core.
            if (File.Exists(m_filePath))
            {
                m_registry.TryLoad(m_filePath, out _);
            }
        }

        [ConsoleCommand(
            documentation: "Lists effective Taj's COI shortcut bindings.",
            customCommandName: "tajs_shortcuts")]
        public string GetStatus()
        {
            var builder = new StringBuilder(256);
            foreach (ShortcutBindingSnapshot binding in m_registry.GetSnapshot())
            {
                string primary = binding.Primary.IsEmpty ? "unbound" : binding.Primary.Serialized;
                string secondary = binding.Secondary.IsEmpty ? string.Empty : ", " + binding.Secondary.Serialized;
                builder.Append(binding.Descriptor.ActionId).Append(" = ").Append(primary).Append(secondary).AppendLine();
            }

            return builder.Length == 0 ? "No Taj's COI shortcuts are registered." : builder.ToString().TrimEnd();
        }

        [ConsoleCommand(
            documentation: "Persists effective Taj's COI shortcut bindings.",
            customCommandName: "tajs_shortcuts_save")]
        public string Save()
        {
            return m_registry.TrySave(m_filePath, out string error)
                ? "TajsCOI shortcuts saved."
                : "TajsCOI shortcuts could not be saved: " + error;
        }

        [ConsoleCommand(
            documentation: "Reloads Taj's COI shortcut bindings from disk.",
            customCommandName: "tajs_shortcuts_reload")]
        public string Reload()
        {
            return m_registry.TryLoad(m_filePath, out string error)
                ? "TajsCOI shortcuts reloaded."
                : "TajsCOI shortcuts could not be reloaded: " + error;
        }
    }
}
