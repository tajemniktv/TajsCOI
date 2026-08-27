// Taj's COI Mods | LocalizationContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TajsCOI.Common.Localization
{
    public enum LocalizationRegistrationStatus
    {
        Added,
        Updated,
        AlreadyRegistered,
        Rejected,
    }

    public sealed class LocalizationCatalog
    {
        public LocalizationCatalog(
            string source,
            string locale,
            IReadOnlyDictionary<string, string> entries,
            string? version = null)
        {
            Source = Require(source, nameof(source));
            Locale = NormalizeLocale(locale);
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> item in entries)
            {
                string key = Require(item.Key, "entries key");
                copy[key] = item.Value ?? string.Empty;
            }

            Entries = new ReadOnlyDictionary<string, string>(copy);
            Version = version ?? string.Empty;
        }

        public string Source { get; }
        public string Locale { get; }
        public string Version { get; }
        public IReadOnlyDictionary<string, string> Entries { get; }

        public static LocalizationCatalog FromJson(
            string source,
            string locale,
            string json,
            string? version = null)
        {
            if (json is null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>));
            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                if (!(serializer.ReadObject(stream) is Dictionary<string, string> entries))
                {
                    throw new FormatException("Localization JSON must be an object of string values.");
                }

                return new LocalizationCatalog(source, locale, entries, version);
            }
        }

        public static string NormalizeLocale(string? locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                return "default";
            }

            string normalized = locale!.Trim().Replace('_', '-');
            string[] parts = normalized.Split('-');
            if (parts.Length == 1)
            {
                return parts[0].ToLowerInvariant();
            }

            return parts[0].ToLowerInvariant() + "-" + parts[1].ToUpperInvariant();
        }

        private static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Localization identifiers cannot be empty.", parameter);
            }

            return value.Trim();
        }
    }

    public sealed class LocalizationRegistrationResult
    {
        public LocalizationRegistrationResult(LocalizationRegistrationStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public LocalizationRegistrationStatus Status { get; }
        public string Message { get; }
        public bool IsSuccess => Status != LocalizationRegistrationStatus.Rejected;
    }

    public interface ILocalizationService
    {
        string ActiveLocale { get; }
        bool DebugKeys { get; set; }

        LocalizationRegistrationResult Register(LocalizationCatalog catalog);

        bool SetLocale(string locale);

        string Get(string source, string key, string? fallback = null, string? fallbackSource = null);

        bool TryGet(string source, string key, out string value, string? fallbackSource = null);

        IReadOnlyList<LocalizationCatalog> GetCatalogSnapshot();

        IReadOnlyList<string> GetMissingKeysSnapshot();
    }
}
