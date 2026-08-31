// Taj's COI Mods | TajsLocalizationService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mafi;
using TajsCOI.Common.Localization;

namespace TajsCOI.Core.Localization
{
    /// <summary>
    ///     Namespaced, value-only catalog service for suite-owned UI. Native MaFi LocStr remains
    ///     authoritative for native strings; this service is only for Tajs-owned text.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsEverything)]
    public sealed class TajsLocalizationService : ILocalizationService
    {
        private readonly object m_gate = new();
        private readonly Dictionary<string, LocalizationCatalog> m_catalogs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> m_lookupCache = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_negativeLookupCache = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_missing = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_formattingFailures = new(StringComparer.Ordinal);
        private string m_activeLocale = "default";
        private bool m_debugKeys;

        public TajsLocalizationService()
        {
            // Keep Core's own fallback catalog available even when no optional language pack is
            // installed. Feature panels still resolve through this service, so a later regional
            // catalog can replace these values without another UI-specific string table.
            Register(
                new LocalizationCatalog(
                    "TajsCore",
                    "default",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["dashboard.profiles.title"] = "Settings profiles",
                        ["dashboard.profiles.description"] = "Profiles contain only settings explicitly marked profile-safe. Preview validates every entry before apply; unknown IDs are skipped and reported.",
                        ["dashboard.profiles.empty"] = "No profiles saved. Capture one with tajs_profile_capture <name> from the console.",
                        ["dashboard.profiles.column.name"] = "Name",
                        ["dashboard.profiles.column.values"] = "Values",
                        ["dashboard.profiles.column.schema"] = "Schema",
                        ["dashboard.profiles.column.scope"] = "Scope",
                        ["dashboard.profiles.scope.allCategories"] = "all categories",
                        ["dashboard.profiles.scope.allModules"] = "all modules",
                        ["dashboard.profiles.selected"] = "Selected profile: ",
                        ["dashboard.profiles.preview"] = "Preview",
                        ["dashboard.profiles.apply"] = "Apply",
                        ["dashboard.profiles.delete"] = "Delete",
                        ["dashboard.profiles.deleteFailed"] = "Profile could not be deleted: ",
                        ["dashboard.harmony.title"] = "Harmony ownership and collision diagnostics",
                        ["dashboard.harmony.description"] = "Read-only, on-demand metadata from the shared Core inspector. Detailed rows are bounded and expandable.",
                        ["dashboard.harmony.tajsTargets"] = "Tajs targets",
                        ["dashboard.harmony.sharedTargets"] = "Shared targets",
                        ["dashboard.harmony.attention"] = "Attention",
                        ["dashboard.harmony.tajsPatches"] = "Tajs patches",
                        ["dashboard.harmony.unavailable"] = "Inspector unavailable: ",
                        ["dashboard.harmony.none"] = "No shared targets or heuristic collision risks were detected.",
                        ["dashboard.harmony.column.target"] = "Target",
                        ["dashboard.harmony.column.risk"] = "Risk",
                        ["dashboard.harmony.column.shared"] = "Shared owners",
                        ["dashboard.harmony.column.patches"] = "Patches",
                        ["dashboard.harmony.column.reason"] = "Reason",
                        ["dashboard.harmony.details.title"] = "Patch details: ",
                        ["dashboard.harmony.details.omitted"] = "{0} more patch entries omitted from this bounded view.",
                        ["dashboard.harmony.additional"] = "Additional shared/risk targets are available in the profiler command and exported trace.",
                    },
                    version: "1"));
        }

        public string ActiveLocale
        {
            get
            {
                lock (m_gate)
                {
                    return m_activeLocale;
                }
            }
        }

        public bool DebugKeys
        {
            get => m_debugKeys;
            set
            {
                lock (m_gate)
                {
                    m_debugKeys = value;
                }
            }
        }

        public LocalizationRegistrationResult Register(LocalizationCatalog catalog)
        {
            if (catalog is null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            string id = MakeId(catalog.Source, catalog.Locale);
            lock (m_gate)
            {
                if (!m_catalogs.TryGetValue(id, out LocalizationCatalog? previous))
                {
                    m_catalogs.Add(id, catalog);
                    m_lookupCache.Clear();
                    m_negativeLookupCache.Clear();
                    return new LocalizationRegistrationResult(
                        LocalizationRegistrationStatus.Added,
                        "Localization catalog registered: " + id);
                }

                if (CatalogsMatch(previous, catalog))
                {
                    return new LocalizationRegistrationResult(
                        LocalizationRegistrationStatus.AlreadyRegistered,
                        "Localization catalog already registered: " + id);
                }

                m_catalogs[id] = catalog;
                m_lookupCache.Clear();
                m_negativeLookupCache.Clear();
                return new LocalizationRegistrationResult(
                    LocalizationRegistrationStatus.Updated,
                    "Localization catalog updated: " + id);
            }
        }

        public bool SetLocale(string locale)
        {
            string normalized = LocalizationCatalog.NormalizeLocale(locale);
            lock (m_gate)
            {
                m_activeLocale = normalized;
                m_lookupCache.Clear();
                m_negativeLookupCache.Clear();
            }

            return true;
        }

        public string Get(string source, string key, string? fallback = null, string? fallbackSource = null)
        {
            if (TryGet(source, key, out string value, fallbackSource))
            {
                return value;
            }

            string missingId = MakeId(source, key);
            lock (m_gate)
            {
                m_missing.Add(missingId);
            }

            return DebugKeys ? "[" + missingId + "]" : fallback ?? key;
        }

        public string Format(string source, string key, string? fallback = null, params object[] arguments)
        {
            string template = Get(source, key, fallback);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, arguments ?? Array.Empty<object>());
            }
            catch (FormatException exception)
            {
                lock (m_gate)
                {
                    m_formattingFailures.Add(
                        MakeId(source, key) + ": " + exception.Message);
                }
                return template;
            }
            catch (ArgumentException exception)
            {
                lock (m_gate)
                {
                    m_formattingFailures.Add(
                        MakeId(source, key) + ": " + exception.Message);
                }
                return template;
            }
        }

        public bool TryGet(string source, string key, out string value, string? fallbackSource = null)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string normalizedSource = source.Trim();
            string normalizedKey = key.Trim();
            lock (m_gate)
            {
                string cacheKey = MakeId(normalizedSource, normalizedKey) + "|" + (fallbackSource ?? string.Empty).Trim();
                if (m_lookupCache.TryGetValue(cacheKey, out string? cached))
                {
                    value = cached;
                    return true;
                }
                if (m_negativeLookupCache.Contains(cacheKey))
                {
                    return false;
                }
                foreach (string locale in LocaleFallbacks(m_activeLocale))
                {
                    if (TryGetFromCatalog(normalizedSource, locale, normalizedKey, out value))
                    {
                        m_lookupCache[cacheKey] = value;
                        return true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(fallbackSource))
                {
                    foreach (string locale in LocaleFallbacks(m_activeLocale))
                    {
                        if (TryGetFromCatalog(fallbackSource!.Trim(), locale, normalizedKey, out value))
                        {
                            m_lookupCache[cacheKey] = value;
                            return true;
                        }
                    }
                }
            }

            lock (m_gate)
            {
                m_negativeLookupCache.Add(MakeId(normalizedSource, normalizedKey) + "|" + (fallbackSource ?? string.Empty).Trim());
            }
            return false;
        }

        public IReadOnlyList<LocalizationCatalog> GetCatalogSnapshot()
        {
            lock (m_gate)
            {
                return m_catalogs.Values
                    .OrderBy(catalog => catalog.Source, StringComparer.Ordinal)
                    .ThenBy(catalog => catalog.Locale, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        public IReadOnlyList<string> GetMissingKeysSnapshot()
        {
            lock (m_gate)
            {
                return m_missing.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }
        }

        public IReadOnlyList<string> GetFormattingFailuresSnapshot()
        {
            lock (m_gate)
            {
                return m_formattingFailures.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }
        }

        /// <summary>
        ///     Loads an optional development override. The resolved path is kept below root and
        ///     both source and locale are restricted to filename-safe identifiers.
        /// </summary>
        public bool TryLoadExternalOverride(
            string rootDirectory,
            string source,
            string locale,
            out string error)
        {
            error = string.Empty;
            try
            {
                string root = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));
                string normalizedSource = RequireFilePart(source, nameof(source));
                string normalizedLocale = LocalizationCatalog.NormalizeLocale(locale);
                string fileName = normalizedSource + "." + normalizedLocale + ".json";
                string candidate = Path.GetFullPath(Path.Combine(root, fileName));
                string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Localization override path escaped its root directory.";
                    return false;
                }
                if (!File.Exists(candidate))
                {
                    error = "Localization override file was not found.";
                    return false;
                }

                LocalizationCatalog catalog = LocalizationCatalog.FromJson(
                    normalizedSource,
                    normalizedLocale,
                    File.ReadAllText(candidate));
                Register(catalog);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is FormatException || exception is ArgumentException)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private bool TryGetFromCatalog(string source, string locale, string key, out string value)
        {
            if (m_catalogs.TryGetValue(MakeId(source, locale), out LocalizationCatalog? catalog) &&
                catalog.Entries.TryGetValue(key, out string? found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static IEnumerable<string> LocaleFallbacks(string locale)
        {
            string normalized = LocalizationCatalog.NormalizeLocale(locale);
            yield return normalized;
            int separator = normalized.IndexOf('-');
            if (separator > 0)
            {
                yield return normalized.Substring(0, separator);
            }
            if (!string.Equals(normalized, "default", StringComparison.Ordinal))
            {
                yield return "default";
            }
        }

        private static bool CatalogsMatch(LocalizationCatalog left, LocalizationCatalog right) =>
            string.Equals(left.Source, right.Source, StringComparison.Ordinal) &&
            string.Equals(left.Locale, right.Locale, StringComparison.Ordinal) &&
            string.Equals(left.Version, right.Version, StringComparison.Ordinal) &&
            left.Entries.Count == right.Entries.Count &&
            left.Entries.All(item => right.Entries.TryGetValue(item.Key, out string? value) &&
                                     string.Equals(item.Value, value, StringComparison.Ordinal));

        private static string MakeId(string source, string key) =>
            (source ?? string.Empty).Trim() + ":" + (key ?? string.Empty).Trim();

        private static string RequireFilePart(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                value.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("Localization source must be a safe file name.", parameter);
            }

            return value.Trim();
        }
    }
}
