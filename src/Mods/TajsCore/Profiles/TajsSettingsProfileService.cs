// Taj's COI Mods | TajsSettingsProfileService.cs
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
using TajsCOI.Common.Diagnostics;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Profiles;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;

namespace TajsCOI.Core.Profiles
{
    [GlobalDependency(RegistrationMode.AsEverything)]
    internal sealed class TajsSettingsProfileService : ISettingsProfileService
    {
        internal const int CurrentSchema = 1;
        private readonly object m_gate = new();
        private readonly ITajsSettings m_settings;
        private readonly ITajsLogger m_log;
        private readonly string m_rootDirectory;
        private readonly Dictionary<string, SettingsProfile> m_profiles = new(StringComparer.OrdinalIgnoreCase);

        public TajsSettingsProfileService(ITajsSettings settings, ITajsRuntime runtime)
            : this(
                settings,
                runtime,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Captain of Industry",
                    "TajsCOI",
                    "Profiles"))
        {
        }

        internal TajsSettingsProfileService(ITajsSettings settings, ITajsRuntime runtime, string rootDirectory)
        {
            m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            m_log = runtime.GetLogger("TajsCore", "SettingsProfiles");
            m_rootDirectory = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));
            LoadExisting();
            runtime.RegisterComponent(
                new RuntimeComponentDescriptor(
                    "TajsCore",
                    "SettingsProfiles",
                    RuntimeComponentLifetime.Process,
                    "ITajsSettings profile preview/apply and atomic JSON persistence",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()));
        }

        public IReadOnlyList<SettingsProfile> List()
        {
            lock (m_gate)
            {
                return m_profiles.Values.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        public bool TryGet(string name, out SettingsProfile? profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            lock (m_gate)
            {
                return m_profiles.TryGetValue(name.Trim(), out profile);
            }
        }

        public SettingsProfilePreview Preview(SettingsProfile profile)
        {
            if (profile is null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            SettingSnapshot[] snapshots = m_settings.GetSnapshot().ToArray();
            Dictionary<string, SettingSnapshot> byId = snapshots.ToDictionary(snapshot => snapshot.Descriptor.StableId, StringComparer.Ordinal);
            var entries = new List<SettingsProfilePreviewEntry>();
            var skipped = new List<string>();
            foreach (KeyValuePair<string, object> item in profile.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!byId.TryGetValue(item.Key, out SettingSnapshot? snapshot))
                {
                    entries.Add(
                        new SettingsProfilePreviewEntry(
                            item.Key,
                            SettingsProfilePreviewState.Unavailable,
                            null,
                            item.Value,
                            "Setting ID is not registered in this installation."));
                    skipped.Add(item.Key);
                    continue;
                }

                if (!snapshot.Descriptor.Flags.HasFlag(SettingFlags.ProfileSafe))
                {
                    entries.Add(
                        new SettingsProfilePreviewEntry(
                            item.Key,
                            SettingsProfilePreviewState.Unavailable,
                            snapshot.Value,
                            item.Value,
                            "The setting is not explicitly marked profile-safe."));
                    skipped.Add(item.Key);
                    continue;
                }

                if (!snapshot.Descriptor.TryNormalize(item.Value, out object normalized, out string error))
                {
                    entries.Add(
                        new SettingsProfilePreviewEntry(
                            item.Key,
                            SettingsProfilePreviewState.Invalid,
                            snapshot.Value,
                            item.Value,
                            error));
                    continue;
                }

                bool unchanged = Equals(snapshot.Value, normalized);
                entries.Add(
                    new SettingsProfilePreviewEntry(
                        item.Key,
                        unchanged ? SettingsProfilePreviewState.Unchanged : SettingsProfilePreviewState.Proposed,
                        snapshot.Value,
                        normalized,
                        unchanged ? "Already active." : "Validated profile value."));
            }

            // A profile can intentionally omit values. Expose those selected, safe settings as
            // current rather than silently implying that they will be reset.
            foreach (SettingSnapshot snapshot in snapshots.Where(snapshot =>
                         snapshot.Descriptor.Flags.HasFlag(SettingFlags.ProfileSafe) &&
                         !profile.Values.ContainsKey(snapshot.Descriptor.StableId) &&
                         IsSelected(profile, snapshot.Descriptor)))
            {
                entries.Add(
                    new SettingsProfilePreviewEntry(
                        snapshot.Descriptor.StableId,
                        SettingsProfilePreviewState.Current,
                        snapshot.Value,
                        snapshot.Value,
                        "Profile does not override this selected setting."));
            }

            return new SettingsProfilePreview(profile, entries, skipped);
        }

        public SettingsProfileApplyResult Apply(SettingsProfile profile)
        {
            SettingsProfilePreview preview = Preview(profile ?? throw new ArgumentNullException(nameof(profile)));
            List<string> errors = preview.Entries
                .Where(entry => entry.State == SettingsProfilePreviewState.Invalid)
                .Select(entry => entry.StableId + ": " + entry.Message)
                .ToList();
            if (errors.Count != 0)
            {
                return new SettingsProfileApplyResult(0, preview.SkippedIds, errors);
            }

            int applied = 0;
            foreach (SettingsProfilePreviewEntry entry in preview.Entries.Where(entry => entry.State == SettingsProfilePreviewState.Proposed))
            {
                int separator = entry.StableId.IndexOf('.');
                if (separator <= 0 || separator == entry.StableId.Length - 1)
                {
                    errors.Add(entry.StableId + ": invalid stable ID.");
                    continue;
                }

                SettingSetResult result = m_settings.TrySet(
                    entry.StableId.Substring(0, separator),
                    entry.StableId.Substring(separator + 1),
                    entry.ProposedValue);
                if (!result.Success)
                {
                    errors.Add(entry.StableId + ": " + result.Error);
                }
                else
                {
                    applied++;
                }
            }

            return new SettingsProfileApplyResult(applied, preview.SkippedIds, errors);
        }

        public bool TrySave(SettingsProfile profile, out string error)
        {
            error = string.Empty;
            if (profile is null)
            {
                error = "Profile is required.";
                return false;
            }
            if (!TryGetProfilePath(profile.Name, out string path, out error))
            {
                return false;
            }

            if (!TryWriteAtomic(path, Serialize(profile), out error))
            {
                return false;
            }
            lock (m_gate)
            {
                m_profiles[profile.Name] = profile;
            }
            return true;
        }

        public bool TryDelete(string name, out string error)
        {
            error = string.Empty;
            if (!TryGet(name, out SettingsProfile? profile) || profile is null)
            {
                error = "Profile was not found.";
                return false;
            }
            try
            {
                File.Delete(GetProfilePath(profile.Name));
                lock (m_gate)
                {
                    m_profiles.Remove(profile.Name);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryDuplicate(string sourceName, string destinationName, out SettingsProfile? profile, out string error)
        {
            profile = null;
            error = string.Empty;
            if (!TryGet(sourceName, out SettingsProfile? source) || source is null)
            {
                error = "Source profile was not found.";
                return false;
            }
            try
            {
                profile = source.With(destinationName?.Trim() ?? string.Empty);
                lock (m_gate)
                {
                    if (m_profiles.ContainsKey(profile.Name))
                    {
                        profile = null;
                        error = "A profile with that name already exists.";
                        return false;
                    }
                }
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
            return TrySave(profile, out error);
        }

        public bool TryRename(string sourceName, string destinationName, out SettingsProfile? profile, out string error)
        {
            profile = null;
            error = string.Empty;
            if (!TryGet(sourceName, out SettingsProfile? source) || source is null)
            {
                error = "Source profile was not found.";
                return false;
            }
            if (!string.Equals(source.Name, destinationName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                ProfileExists(destinationName?.Trim() ?? string.Empty))
            {
                error = "A profile with that name already exists.";
                return false;
            }
            try
            {
                profile = source.With(destinationName?.Trim() ?? string.Empty);
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
            if (!TrySave(profile, out error))
            {
                profile = null;
                return false;
            }
            try
            {
                if (!string.Equals(source.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(GetProfilePath(source.Name));
                    lock (m_gate)
                    {
                        m_profiles.Remove(source.Name);
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryImport(string path, string? nameOverride, out SettingsProfile? profile, out string error)
        {
            profile = null;
            error = string.Empty;
            try
            {
                profile = Parse(File.ReadAllText(path));
                if (!string.IsNullOrWhiteSpace(nameOverride))
                {
                    profile = profile.With(nameOverride!.Trim());
                }
                if (!TrySave(profile, out error))
                {
                    profile = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "Profile import failed: " + exception.Message;
                return false;
            }
        }

        public bool TryExport(string name, string path, out string error)
        {
            error = string.Empty;
            if (!TryGet(name, out SettingsProfile? profile) || profile is null)
            {
                error = "Profile was not found.";
                return false;
            }
            return TryWriteAtomic(path, Serialize(profile), out error);
        }

        [ConsoleCommand(
            documentation: "Lists saved TajsCOI settings profiles.",
            customCommandName: "tajs_profile_list")]
        public string ListProfilesCommand() =>
            List() is IReadOnlyList<SettingsProfile> profiles && profiles.Count != 0
                ? "TajsCOI profiles: " + string.Join(", ", profiles.Select(profile => profile.Name))
                : "TajsCOI profiles: none saved.";

        [ConsoleCommand(
            documentation: "Captures current profile-safe settings into a named profile.",
            customCommandName: "tajs_profile_capture")]
        public string CaptureProfile(string? name, string? categories = "", string? modules = "")
        {
            string profileName = name?.Trim() ?? string.Empty;
            if (profileName.Length == 0)
            {
                return "Usage: tajs_profile_capture <name> [comma-separated-categories] [comma-separated-modules]";
            }
            string[] selectedCategories = SplitList(categories);
            string[] selectedModules = SplitList(modules);
            Dictionary<string, object> values = m_settings.GetSnapshot()
                .Where(snapshot => snapshot.Descriptor.Flags.HasFlag(SettingFlags.ProfileSafe) &&
                                   (selectedCategories.Length == 0 || selectedCategories.Contains(snapshot.Descriptor.Category, StringComparer.Ordinal)) &&
                                   (selectedModules.Length == 0 || selectedModules.Contains(snapshot.Descriptor.ModId, StringComparer.Ordinal)))
                .ToDictionary(snapshot => snapshot.Descriptor.StableId, snapshot => snapshot.Value, StringComparer.Ordinal);
            var profile = new SettingsProfile(
                CurrentSchema,
                typeof(TajsSettingsProfileService).Assembly.GetName().Version?.ToString() ?? "unknown",
                profileName,
                selectedCategories,
                selectedModules,
                values);
            return TrySave(profile, out string error)
                ? "Profile '" + profile.Name + "' saved with " + values.Count + " profile-safe setting(s)."
                : "Profile could not be saved: " + error;
        }

        [ConsoleCommand(
            documentation: "Previews a settings profile without changing runtime values.",
            customCommandName: "tajs_profile_preview")]
        public string PreviewProfile(string? name)
        {
            if (!TryGet(name ?? string.Empty, out SettingsProfile? profile) || profile is null)
            {
                return "Profile was not found.";
            }
            SettingsProfilePreview preview = Preview(profile);
            return "Profile '" + profile.Name + "' preview: " +
                   string.Join(
                       ", ",
                       preview.Entries.GroupBy(entry => entry.State).OrderBy(group => group.Key).Select(group => group.Key + "=" + group.Count())) +
                   (preview.SkippedIds.Count == 0 ? string.Empty : "; skipped=" + string.Join(",", preview.SkippedIds));
        }

        [ConsoleCommand(
            documentation: "Applies a validated settings profile through ordinary setting paths.",
            customCommandName: "tajs_profile_apply")]
        public string ApplyProfile(string? name)
        {
            if (!TryGet(name ?? string.Empty, out SettingsProfile? profile) || profile is null)
            {
                return "Profile was not found.";
            }
            SettingsProfileApplyResult result = Apply(profile);
            return "Profile '" + profile.Name + "': applied=" + result.AppliedCount +
                   ", skipped=" + result.SkippedIds.Count + ", errors=" + result.Errors.Count +
                   (result.Errors.Count == 0 ? string.Empty : " (" + string.Join(" | ", result.Errors) + ")");
        }

        [ConsoleCommand(
            documentation: "Duplicates a settings profile under a new name.",
            customCommandName: "tajs_profile_duplicate")]
        public string DuplicateProfile(string? source, string? destination) =>
            TryDuplicate(source ?? string.Empty, destination ?? string.Empty, out _, out string duplicateError)
                ? "Profile duplicated."
                : "Profile was not duplicated: " + duplicateError;

        [ConsoleCommand(
            documentation: "Renames a settings profile.",
            customCommandName: "tajs_profile_rename")]
        public string RenameProfile(string? source, string? destination) =>
            TryRename(source ?? string.Empty, destination ?? string.Empty, out _, out string renameError)
                ? "Profile renamed."
                : "Profile was not renamed: " + renameError;

        [ConsoleCommand(
            documentation: "Deletes a settings profile.",
            customCommandName: "tajs_profile_delete")]
        public string DeleteProfile(string? name) =>
            TryDelete(name ?? string.Empty, out string deleteError)
                ? "Profile deleted."
                : "Profile was not deleted: " + deleteError;

        [ConsoleCommand(
            documentation: "Imports a settings profile JSON file.",
            customCommandName: "tajs_profile_import")]
        public string ImportProfile(string? path, string? nameOverride = "") =>
            TryImport(path ?? string.Empty, nameOverride, out _, out string importError)
                ? "Profile imported."
                : "Profile was not imported: " + importError;

        [ConsoleCommand(
            documentation: "Exports a settings profile JSON file.",
            customCommandName: "tajs_profile_export")]
        public string ExportProfile(string? name, string? path) =>
            TryExport(name ?? string.Empty, path ?? string.Empty, out string exportError)
                ? "Profile exported."
                : "Profile was not exported: " + exportError;

        private static string[] SplitList(string? value) =>
            (value ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private void LoadExisting()
        {
            try
            {
                if (!Directory.Exists(m_rootDirectory))
                {
                    return;
                }
                foreach (string path in Directory.EnumerateFiles(m_rootDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        SettingsProfile profile = Parse(File.ReadAllText(path));
                        m_profiles[profile.Name] = profile;
                    }
                    catch (Exception exception)
                    {
                        m_log.Warning("Settings profile '" + Path.GetFileName(path) + "' was ignored: " + exception.Message);
                    }
                }
            }
            catch (Exception exception)
            {
                m_log.Warning("Settings profiles could not be enumerated: " + exception.Message);
            }
        }

        private bool TryGetProfilePath(string name, out string path, out string error)
        {
            error = string.Empty;
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Profile name is required.";
                return false;
            }
            string normalized = name.Trim();
            if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || normalized is "." or "..")
            {
                error = "Profile name contains invalid file-name characters.";
                return false;
            }
            path = GetProfilePath(normalized);
            return true;
        }

        private string GetProfilePath(string name) => Path.Combine(m_rootDirectory, Sanitize(name) + ".json");

        private bool ProfileExists(string name)
        {
            lock (m_gate)
            {
                return m_profiles.ContainsKey(name);
            }
        }

        private static string Sanitize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
            {
                builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or ' ' ? character : '_');
            }
            string result = builder.ToString().Trim();
            return result.Length == 0 ? "profile" : result;
        }

        private static bool IsSelected(SettingsProfile profile, SettingDescriptor descriptor) =>
            (profile.Categories.Count == 0 || profile.Categories.Contains(descriptor.Category, StringComparer.Ordinal)) &&
            (profile.Modules.Count == 0 || profile.Modules.Contains(descriptor.ModId, StringComparer.Ordinal));

        private static string Serialize(SettingsProfile profile)
        {
            var root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["schema"] = profile.Schema,
                ["suite_version"] = profile.SuiteVersion,
                ["name"] = profile.Name,
                ["categories"] = profile.Categories.Cast<object>().ToArray(),
                ["modules"] = profile.Modules.Cast<object>().ToArray(),
                ["values"] = profile.Values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            };
            var builder = new StringBuilder(512);
            AppendJsonObject(builder, root);
            return builder.ToString();
        }

        private static SettingsProfile Parse(string text)
        {
            object parsed = new JsonParser().Parse(new StringReader(text));
            if (parsed is not Dict<string, object> root)
            {
                throw new FormatException("Profile root must be a JSON object.");
            }
            int schema = GetInt(root, "schema");
            if (schema <= 0 || schema > CurrentSchema)
            {
                throw new FormatException("Unsupported profile schema " + schema + ".");
            }
            string suiteVersion = GetString(root, "suite_version");
            string name = GetString(root, "name");
            string[] categories = GetArray(root, "categories");
            string[] modules = GetArray(root, "modules");
            if (!root.TryGetValue("values", out object rawValues) || rawValues is not Dict<string, object> values)
            {
                throw new FormatException("Profile requires a values object.");
            }
            return new SettingsProfile(schema, suiteVersion, name, categories, modules, values);
        }

        private static int GetInt(Dict<string, object> root, string key) =>
            root.TryGetValue(key, out object value)
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                : throw new FormatException("Profile field '" + key + "' is missing.");

        private static string GetString(Dict<string, object> root, string key) =>
            root.TryGetValue(key, out object value) && value is string text
                ? text
                : throw new FormatException("Profile field '" + key + "' must be text.");

        private static string[] GetArray(Dict<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out object value) || value is not IEnumerable<object> values)
            {
                throw new FormatException("Profile field '" + key + "' must be an array.");
            }
            return values.Select(item => item as string ?? throw new FormatException("Profile arrays must contain text."))
                .ToArray();
        }

        private static bool TryWriteAtomic(string path, string content, out string error)
        {
            error = string.Empty;
            string? temporary = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                temporary = fullPath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    File.Replace(temporary, fullPath, fullPath + ".bak", true);
                }
                else
                {
                    File.Move(temporary, fullPath);
                }
                temporary = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                if (temporary is not null)
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch
                    {
                        // Best-effort cleanup only.
                    }
                }
            }
        }

        private static void AppendJsonObject(StringBuilder builder, IEnumerable<KeyValuePair<string, object>> values)
        {
            builder.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> item in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
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
                    foreach (object item in array)
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
                    throw new InvalidOperationException("Unsupported profile value type " + value.GetType().FullName + ".");
            }
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            JsonWriter.JsonEscapeString(value, builder);
            builder.Append('"');
        }
    }
}
