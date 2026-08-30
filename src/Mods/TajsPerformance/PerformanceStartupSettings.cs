// Taj's COI Mods | PerformanceStartupSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mafi.Collections;
using Mafi.Serialization;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Reads only the two process-lifetime performance switches that must be known before
    ///     dependency resolution. The normal TajsSettings service remains the source of truth
    ///     once the gameplay scene exists; this reader is a fail-closed startup bridge.
    /// </summary>
    internal static class PerformanceStartupSettings
    {
        internal static bool TryReadPersistedBoolean(string stableId, out bool value)
        {
            value = false;
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Captain of Industry",
                    "TajsCOI",
                    "settings.json");
                return File.Exists(path) && TryReadBoolean(File.ReadAllText(path), stableId, out value);
            }
            catch
            {
                // A missing, unreadable, or malformed startup file must never opt a candidate in.
                value = false;
                return false;
            }
        }

        internal static bool TryReadBoolean(string json, string stableId, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(stableId))
            {
                return false;
            }

            try
            {
                object parsed = new JsonParser().Parse(new StringReader(json));
                if (parsed is not Dict<string, object> root)
                {
                    return false;
                }

                int schemaVersion = root.TryGetValue("schema_version", out object rawVersion)
                    ? Convert.ToInt32(rawVersion, CultureInfo.InvariantCulture)
                    : 0;
                if (schemaVersion < 0 || schemaVersion > 1)
                {
                    return false;
                }

                IReadOnlyDictionary<string, object> values = schemaVersion == 0
                    ? root
                        .Where(x => !string.Equals(x.Key, "schema_version", StringComparison.Ordinal))
                        .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
                    : root.TryGetValue("values", out object rawValues) && rawValues is Dict<string, object> nested
                        ? nested
                        : throw new FormatException("Settings schema 1 requires a 'values' object.");

                if (!values.TryGetValue(stableId, out object rawValue) || rawValue is not bool boolean)
                {
                    return false;
                }

                value = boolean;
                return true;
            }
            catch
            {
                value = false;
                return false;
            }
        }
    }
}
