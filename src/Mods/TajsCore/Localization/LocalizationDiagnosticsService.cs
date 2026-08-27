// Taj's COI Mods | LocalizationDiagnosticsService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Text;
using Mafi;
using Mafi.Core.Console;
using TajsCOI.Common.Localization;

namespace TajsCOI.Core.Localization
{
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class LocalizationDiagnosticsService
    {
        private readonly ILocalizationService m_localization;

        public LocalizationDiagnosticsService(ILocalizationService localization)
        {
            m_localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        [ConsoleCommand(
            documentation: "Shows registered TajsCOI localization catalogs and missing keys.",
            customCommandName: "tajs_localization_status")]
        public string GetStatus()
        {
            var builder = new StringBuilder(256);
            builder.Append("locale=").Append(m_localization.ActiveLocale)
                .Append(", debug_keys=").Append(m_localization.DebugKeys).AppendLine();
            foreach (LocalizationCatalog catalog in m_localization.GetCatalogSnapshot())
            {
                builder.Append("  ").Append(catalog.Source).Append('/').Append(catalog.Locale)
                    .Append(" keys=").Append(catalog.Entries.Count).AppendLine();
            }

            string[] missing = m_localization.GetMissingKeysSnapshot().ToArray();
            builder.Append("missing=").Append(missing.Length);
            if (missing.Length > 0)
            {
                builder.Append(" [").Append(string.Join(", ", missing.Take(20))).Append(']');
            }

            return builder.ToString();
        }

        [ConsoleCommand(
            documentation: "Sets the active TajsCOI localization locale.",
            customCommandName: "tajs_localization_locale")]
        public string SetLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                return "A locale is required.";
            }

            m_localization.SetLocale(locale);
            return "TajsCOI locale set to " + m_localization.ActiveLocale + ".";
        }
    }
}
