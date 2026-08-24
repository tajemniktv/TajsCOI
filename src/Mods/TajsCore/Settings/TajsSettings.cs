// Taj's COI Mods | TajsSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Console;
using Mafi.Serialization;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;

namespace TajsCOI.Core.Settings
{
    [GlobalDependency(RegistrationMode.AsEverything)]
    internal sealed class TajsSettings : ITajsSettings
    {
        internal const int CurrentSchemaVersion = 1;

        private readonly object m_gate = new();
        private readonly object m_persistenceGate = new();
        private readonly string m_filePath;
        private readonly string m_backupPath;
        private readonly ITajsLogger m_log;
        private readonly Dictionary<string, SettingDescriptor> m_descriptors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> m_values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> m_persistedValues = new(StringComparer.Ordinal);

        public TajsSettings(ITajsRuntime runtime)
            : this(GetDefaultFilePath(), runtime.GetLogger("TajsCore", "Settings"))
        {
        }

        internal TajsSettings(string filePath, ITajsLogger log)
        {
            m_filePath = string.IsNullOrWhiteSpace(filePath)
                ? throw new ArgumentException("Settings file path cannot be empty.", nameof(filePath))
                : Path.GetFullPath(filePath);
            m_backupPath = m_filePath + ".bak";
            m_log = log ?? throw new ArgumentNullException(nameof(log));
            LoadPersistedValues();
        }

        public event EventHandler<SettingChangedEventArgs>? Changed;

        public void Register(SettingDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (descriptor.Scope != SettingScope.Global)
            {
                throw new NotSupportedException(
                    $"Setting '{descriptor.StableId}' uses {descriptor.Scope}, but TajsCore currently supports only global persistence.");
            }

            lock (m_gate)
            {
                if (m_descriptors.TryGetValue(descriptor.StableId, out SettingDescriptor existing))
                {
                    if (!DescriptorsMatch(existing, descriptor))
                    {
                        throw new InvalidOperationException(
                            $"Setting '{descriptor.StableId}' was registered more than once with different metadata.");
                    }
                    return;
                }

                object value = descriptor.DefaultValue;
                if (m_persistedValues.TryGetValue(descriptor.StableId, out object persisted))
                {
                    if (descriptor.TryNormalize(persisted, out object normalized, out string error))
                    {
                        value = normalized;
                    }
                    else
                    {
                        m_log.Warning(
                            $"Setting '{descriptor.StableId}' is invalid ({error}); using its default value.");
                    }
                }

                m_descriptors.Add(descriptor.StableId, descriptor);
                m_values.Add(descriptor.StableId, value);
            }
        }

        public T Get<T>(string modId, string key)
        {
            string stableId = MakeStableId(modId, key);
            lock (m_gate)
            {
                if (!m_values.TryGetValue(stableId, out object value))
                {
                    throw new KeyNotFoundException($"Setting '{stableId}' is not registered.");
                }
                if (value is T typed)
                {
                    return typed;
                }
                throw new InvalidCastException(
                    $"Setting '{stableId}' contains {value.GetType().Name}, not {typeof(T).Name}.");
            }
        }

        public SettingSetResult TrySet(string modId, string key, object? value)
        {
            string stableId;
            try
            {
                stableId = MakeStableId(modId, key);
            }
            catch (Exception exception)
            {
                return SettingSetResult.Rejected(exception.Message);
            }

            SettingChangedEventArgs change;
            SettingSetResult result;
            lock (m_persistenceGate)
            {
                SettingDescriptor descriptor;
                object normalized;
                object oldValue;
                string json;
                lock (m_gate)
                {
                    if (!m_descriptors.TryGetValue(stableId, out descriptor))
                    {
                        return SettingSetResult.Rejected($"Setting '{stableId}' is not registered.");
                    }
                    if (!descriptor.TryNormalize(value, out normalized, out string error))
                    {
                        return SettingSetResult.Rejected(error);
                    }

                    oldValue = m_values[stableId];
                    if (Equals(oldValue, normalized))
                    {
                        return SettingSetResult.Accepted(oldValue, descriptor.ApplyMode);
                    }
                    json = CreatePersistenceJson(stableId, normalized);
                }

                if (!TryPersist(json, out string persistError))
                {
                    return SettingSetResult.Rejected(persistError);
                }

                lock (m_gate)
                {
                    m_values[stableId] = normalized;
                    m_persistedValues[stableId] = normalized;
                }
                change = new SettingChangedEventArgs(descriptor, oldValue, normalized);
                result = SettingSetResult.Accepted(normalized, descriptor.ApplyMode);
            }

            EventHandler<SettingChangedEventArgs>? changed = Changed;
            if (changed is null)
            {
                return result;
            }

            foreach (Delegate handler in changed.GetInvocationList())
            {
                try
                {
                    var subscriber = (EventHandler<SettingChangedEventArgs>)handler;
                    subscriber(this, change);
                }
                catch (Exception exception)
                {
                    m_log.Exception(exception, $"Setting '{stableId}' was persisted, but a change subscriber failed.");
                }
            }
            return result;
        }

        public IReadOnlyList<SettingSnapshot> GetSnapshot()
        {
            lock (m_gate)
            {
                return m_descriptors.Values
                    .OrderBy(x => x.ModId, StringComparer.Ordinal)
                    .ThenBy(x => x.Category, StringComparer.Ordinal)
                    .ThenBy(x => x.DisplayName, StringComparer.Ordinal)
                    .Select(x => new SettingSnapshot(x, m_values[x.StableId]))
                    .ToArray();
            }
        }

        [ConsoleCommand(
            documentation: "Lists registered TajsCOI settings and their current values.",
            customCommandName: "tajs_settings_list")]
        public string ListSettings()
        {
            IReadOnlyList<SettingSnapshot> snapshot = GetSnapshot();
            return snapshot.Count == 0
                ? "TajsCOI settings: no feature settings are registered in this scene."
                : "TajsCOI settings:\n" + string.Join("\n", snapshot.Select(x =>
                    $"  {x.Descriptor.StableId}={FormatValue(x.Value)} [{x.Descriptor.ApplyMode}, {x.Descriptor.Scope}]"));
        }

        [ConsoleCommand(
            documentation: "Changes a registered TajsCOI setting by stable ID.",
            customCommandName: "tajs_settings_set")]
        public string SetSetting(string stableId, string value)
        {
            string normalizedId = stableId ?? string.Empty;
            int separator = normalizedId.IndexOf('.');
            if (separator <= 0 || separator == normalizedId.Length - 1)
            {
                return "Usage: tajs_settings_set <ModId.key> <value>";
            }

            SettingSetResult result = TrySet(
                normalizedId.Substring(0, separator),
                normalizedId.Substring(separator + 1),
                value);
            return result.Success
                ? $"{normalizedId}={FormatValue(result.Value!)} ({ApplyModeText(result.ApplyMode)})"
                : $"Failed to change {normalizedId}: {result.Error}";
        }

        private void LoadPersistedValues()
        {
            bool primaryLoaded = TryRead(m_filePath, out Dictionary<string, object> values, out Exception? primaryError);
            Exception? backupError = null;
            if (!primaryLoaded && !TryRead(m_backupPath, out values, out backupError))
            {
                if (primaryError is not null)
                {
                    m_log.Exception(primaryError, "TajsCOI settings JSON could not be parsed; defaults will be used.");
                }
                else if (backupError is not null)
                {
                    m_log.Exception(backupError, "TajsCOI settings backup could not be parsed; defaults will be used.");
                }
                return;
            }

            foreach (KeyValuePair<string, object> item in values)
            {
                m_persistedValues[item.Key] = item.Value;
            }
            if (!primaryLoaded)
            {
                m_log.Warning("Primary settings JSON was unavailable or invalid; recovered the previous valid backup.");
            }
        }

        private bool TryRead(string path, out Dictionary<string, object> values, out Exception? error)
        {
            values = new Dictionary<string, object>(StringComparer.Ordinal);
            error = null;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                object parsed;
                using (var reader = new StringReader(File.ReadAllText(path, Encoding.UTF8)))
                {
                    parsed = new JsonParser().Parse(reader);
                }
                if (parsed is not Dict<string, object> root)
                {
                    throw new FormatException("Settings root must be a JSON object.");
                }
                int version = root.TryGetValue("schema_version", out object rawVersion)
                    ? Convert.ToInt32(rawVersion, CultureInfo.InvariantCulture)
                    : 0;
                values = Migrate(root, version);
                return true;
            }
            catch (Exception exception)
            {
                error = exception;
                return false;
            }
        }

        private static Dictionary<string, object> Migrate(Dict<string, object> root, int version)
        {
            if (version < 0 || version > CurrentSchemaVersion)
            {
                throw new FormatException($"Unsupported settings schema version {version}.");
            }

            if (version == 0)
            {
                return root
                    .Where(x => !string.Equals(x.Key, "schema_version", StringComparison.Ordinal))
                    .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            }

            if (!root.TryGetValue("values", out object rawValues) || rawValues is not Dict<string, object> values)
            {
                throw new FormatException("Settings schema 1 requires a 'values' object.");
            }
            return values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        }

        private string CreatePersistenceJson(string changedId, object changedValue)
        {
            var values = new Dictionary<string, object>(m_persistedValues, StringComparer.Ordinal)
            {
                [changedId] = changedValue,
            };
            foreach (SettingDescriptor descriptor in m_descriptors.Values.Where(x => x.Scope == SettingScope.Global))
            {
                if (!values.ContainsKey(descriptor.StableId))
                {
                    values[descriptor.StableId] = m_values[descriptor.StableId];
                }
            }

            var root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["schema_version"] = CurrentSchemaVersion,
                ["values"] = values,
            };
            return SerializeSettings(root);
        }

        private bool TryPersist(string json, out string error)
        {
            string? directory = Path.GetDirectoryName(m_filePath);
            string tempPath = m_filePath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(m_filePath))
                {
                    File.Replace(tempPath, m_filePath, m_backupPath, true);
                }
                else
                {
                    File.Move(tempPath, m_filePath);
                }
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                TryDelete(tempPath);
                m_log.Exception(exception, "TajsCOI settings could not be written; the previous file and runtime value were kept.");
                error = "Settings could not be persisted; the previous value remains active.";
                return false;
            }
        }

        private static bool DescriptorsMatch(SettingDescriptor left, SettingDescriptor right) =>
            string.Equals(left.StableId, right.StableId, StringComparison.Ordinal) &&
            string.Equals(left.ModDisplayName, right.ModDisplayName, StringComparison.Ordinal) &&
            string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
            string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
            string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
            string.Equals(left.ComponentRequirement, right.ComponentRequirement, StringComparison.Ordinal) &&
            left.ValueType == right.ValueType &&
            Equals(left.DefaultValue, right.DefaultValue) && left.Minimum == right.Minimum &&
            left.Maximum == right.Maximum && left.Step == right.Step && left.Scope == right.Scope &&
            left.ApplyMode == right.ApplyMode && left.Flags == right.Flags &&
            ChoicesMatch(left, right);

        private static bool ChoicesMatch(SettingDescriptor left, SettingDescriptor right)
        {
            if (left.Choices.Count != right.Choices.Count)
            {
                return false;
            }
            for (int index = 0; index < left.Choices.Count; index++)
            {
                if (!string.Equals(left.Choices[index].Value, right.Choices[index].Value, StringComparison.Ordinal) ||
                    !string.Equals(left.Choices[index].DisplayName, right.Choices[index].DisplayName, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static string MakeStableId(string modId, string key)
        {
            if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Setting mod ID and key are required.");
            }
            return modId.Trim() + "." + key.Trim();
        }

        private static string GetDefaultFilePath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsCOI",
                "settings.json");

        private static string ApplyModeText(SettingApplyMode mode) =>
            mode == SettingApplyMode.Immediate ? "applied immediately" :
            mode == SettingApplyMode.ReloadSave ? "takes effect after reloading the save" :
            "requires a game restart";

        private static string FormatValue(object value) =>
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        private static string SerializeSettings(IReadOnlyDictionary<string, object> root)
        {
            var builder = new StringBuilder(256);
            AppendJsonObject(builder, root);
            return builder.ToString();
        }

        private static void AppendJsonObject(StringBuilder builder, IEnumerable<KeyValuePair<string, object>> values)
        {
            builder.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> item in values.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                AppendJsonString(builder, item.Key);
                builder.Append(':');
                AppendJsonValue(builder, item.Value);
            }
            builder.Append('}');
        }

        private static void AppendJsonValue(StringBuilder builder, object? value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    return;
                case string text:
                    AppendJsonString(builder, text);
                    return;
                case bool boolean:
                    builder.Append(boolean ? "true" : "false");
                    return;
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case IReadOnlyDictionary<string, object> dictionary:
                    AppendJsonObject(builder, dictionary);
                    return;
                case IEnumerable<object> array:
                    builder.Append('[');
                    bool first = true;
                    foreach (object? item in array)
                    {
                        if (!first)
                        {
                            builder.Append(',');
                        }
                        first = false;
                        AppendJsonValue(builder, item);
                    }
                    builder.Append(']');
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Settings value type '{value.GetType().FullName}' cannot be serialized as JSON.");
            }
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            JsonWriter.JsonEscapeString(value, builder);
            builder.Append('"');
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
