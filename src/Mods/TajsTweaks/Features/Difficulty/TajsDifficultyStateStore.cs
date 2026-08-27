// Taj's COI Mods | TajsDifficultyStateStore.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mafi;
using Mafi.Core;
using Mafi.Core.Game;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    /// <summary>
    ///     The 0.8.7b save metadata has no immutable save id. This fingerprint combines the
    ///     native file identity fields instead of using the display name alone. File timestamp
    ///     and size change when a save is written, so the owning feature rebinds the sidecar only
    ///     after a successful native save.
    /// </summary>
    internal sealed class TajsDifficultySaveIdentity
    {
        private TajsDifficultySaveIdentity(string fingerprint)
        {
            Fingerprint = fingerprint;
        }

        internal string Fingerprint { get; }

        internal static TajsDifficultySaveIdentity FromSaveFile(SaveFileInfo save)
        {
            string canonical = string.Join(
                "\n",
                save.GameName ?? string.Empty,
                save.NameNoExtension ?? string.Empty,
                save.Extension ?? string.Empty,
                save.WriteTimestamp.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                save.SizeBytes.ToString(CultureInfo.InvariantCulture));
            return new TajsDifficultySaveIdentity(Hash(canonical));
        }

        internal static TajsDifficultySaveIdentity? FromSavePath(string path, string gameName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    return null;
                }

                return FromSaveFile(
                    new SaveFileInfo(
                        Path.GetFileNameWithoutExtension(file.Name),
                        gameName,
                        file.LastWriteTimeUtc,
                        file.Length,
                        file.Extension));
            }
            catch
            {
                return null;
            }
        }

        private static string Hash(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes)
                {
                    builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }

    /// <summary>
    ///     Stores only scalar original difficulty values. V1 name-based sidecars, corrupt V2
    ///     sidecars, and identity collisions are never trusted or overwritten automatically.
    /// </summary>
    internal sealed class TajsDifficultyStateStore
    {
        private const string Header = "TajsTweaksDifficultyV2";
        private const string LegacyHeader = "TajsTweaksDifficultyV1";
        private const int SchemaVersion = 2;

        private readonly Dictionary<string, string> m_originalValues = new(StringComparer.Ordinal);
        private readonly string m_rootDirectory;
        private string? m_filePath;
        private string? m_identityFingerprint;
        private bool m_allowWrite;
        private bool m_baselineAvailable;
        private string m_baselineStatus = "original-save baseline unavailable.";

        internal TajsDifficultyStateStore(string? rootDirectory = null)
        {
            m_rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Captain of Industry",
                    "TajsTweaks",
                    "Difficulty")
                : rootDirectory!;
        }

        internal IReadOnlyDictionary<string, string> OriginalValues => m_originalValues;

        internal bool IsBaselineAvailable => m_baselineAvailable;

        internal string BaselineStatus => m_baselineStatus;

        internal string? IdentityFingerprint => m_identityFingerprint;

        internal void LoadOrCapture(
            TajsDifficultySaveIdentity? identity,
            string saveName,
            GameDifficultyConfig current,
            IReadOnlyDictionary<string, PropertyInfo> properties)
        {
            m_originalValues.Clear();
            m_filePath = identity is null
                ? null
                : Path.Combine(m_rootDirectory, identity.Fingerprint, "state.txt");
            m_identityFingerprint = identity?.Fingerprint;
            m_allowWrite = false;
            m_baselineAvailable = false;
            m_baselineStatus = "original-save baseline unavailable.";

            if (identity is not null && File.Exists(m_filePath))
            {
                if (!TryRead(m_filePath!, identity.Fingerprint, properties))
                {
                    // Do not replace a file that may contain recoverable data. The native
                    // difficulty UI and console remain usable without the optional baseline.
                    m_baselineStatus = "original-save baseline unavailable: sidecar is invalid or belongs to another save.";
                    return;
                }

                m_baselineAvailable = true;
                m_allowWrite = true;
                m_baselineStatus = "original-save baseline identity verified.";
                if (CaptureMissing(current, properties))
                {
                    Save();
                }

                return;
            }

            if (HasLegacySidecar(saveName))
            {
                // V1 is keyed only by a sanitized name. Preserve it, but never guess that it
                // belongs to the active save and never silently migrate it.
                m_baselineStatus = "original-save baseline unavailable: legacy name-based sidecar requires explicit recapture.";
                return;
            }

            CaptureMissing(current, properties);
            m_baselineAvailable = true;
            m_baselineStatus = identity is null
                ? "original-save baseline is in memory only: native save identity was unavailable."
                : "original-save baseline captured with verified save identity.";
            m_allowWrite = identity is not null;
            if (m_allowWrite)
            {
                Save();
            }
        }

        /// <summary>
        ///     Rebinds an already-captured baseline to the metadata of a successfully written
        ///     native save. A pre-existing target sidecar is treated as an identity collision.
        /// </summary>
        internal bool RebindAfterSave(string? savePath, string gameName)
        {
            if (!m_baselineAvailable || string.IsNullOrWhiteSpace(savePath))
            {
                return false;
            }

            TajsDifficultySaveIdentity? identity = TajsDifficultySaveIdentity.FromSavePath(savePath!, gameName);
            if (identity is null)
            {
                return false;
            }

            if (string.Equals(m_identityFingerprint, identity.Fingerprint, StringComparison.Ordinal))
            {
                return m_allowWrite && Save();
            }

            string nextPath = Path.Combine(m_rootDirectory, identity.Fingerprint, "state.txt");
            if (File.Exists(nextPath))
            {
                m_baselineAvailable = false;
                m_allowWrite = false;
                m_baselineStatus = "original-save baseline unavailable: save identity collides with an existing sidecar.";
                return false;
            }

            m_identityFingerprint = identity.Fingerprint;
            m_filePath = nextPath;
            m_allowWrite = true;
            m_baselineStatus = "original-save baseline identity verified.";
            return Save();
        }

        internal bool TryGetOriginal(string memberName, PropertyInfo property, out object? value)
        {
            value = null;
            return m_baselineAvailable &&
                   m_originalValues.TryGetValue(memberName, out string? encoded) &&
                   TryDecode(encoded, property.PropertyType, out value);
        }

        internal bool Save()
        {
            if (!m_allowWrite || !m_baselineAvailable || m_filePath is null || m_identityFingerprint is null)
            {
                return false;
            }

            string? temporary = null;
            try
            {
                string? directory = Path.GetDirectoryName(m_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                temporary = m_filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllLines(
                    temporary,
                    new[] { Header, "schema=" + SchemaVersion.ToString(CultureInfo.InvariantCulture), "identity=" + m_identityFingerprint }.Concat(
                        m_originalValues
                            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => pair.Key + "=" + pair.Value)),
                    new UTF8Encoding(false));

                if (File.Exists(m_filePath))
                {
                    File.Replace(temporary, m_filePath, m_filePath + ".bak", true);
                }
                else
                {
                    File.Move(temporary, m_filePath);
                }

                temporary = null;
                return true;
            }
            catch
            {
                // Optional metadata must never interfere with saving or loading the game.
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
                    }
                }
            }
        }

        internal static bool TryEncode(object? value, out string encoded)
        {
            encoded = string.Empty;
            if (value is Percent percent)
            {
                encoded = percent == Percent.MaxValue
                    ? "unlimited"
                    : percent.ToIntPercentRounded().ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (value is Enum enumValue)
            {
                encoded = "enum:" + Convert.ToInt32(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        internal static bool TryDecode(string encoded, Type targetType, out object? value)
        {
            value = null;
            string input = encoded?.Trim() ?? string.Empty;
            if (targetType == typeof(Percent))
            {
                if (string.Equals(input, "unlimited", StringComparison.OrdinalIgnoreCase))
                {
                    value = Percent.MaxValue;
                    return true;
                }

                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
                {
                    value = percent.Percent();
                    return true;
                }

                return false;
            }

            if (targetType.IsEnum && input.StartsWith("enum:", StringComparison.Ordinal))
            {
                if (int.TryParse(input.Substring("enum:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int enumValue) &&
                    Enum.IsDefined(targetType, enumValue))
                {
                    value = Enum.ToObject(targetType, enumValue);
                    return true;
                }
            }

            return false;
        }

        private bool CaptureMissing(GameDifficultyConfig current, IReadOnlyDictionary<string, PropertyInfo> properties)
        {
            bool added = false;
            foreach (KeyValuePair<string, PropertyInfo> pair in properties)
            {
                if (m_originalValues.ContainsKey(pair.Key))
                {
                    continue;
                }

                try
                {
                    if (TryEncode(pair.Value.GetValue(current), out string encoded))
                    {
                        m_originalValues[pair.Key] = encoded;
                        added = true;
                    }
                }
                catch
                {
                    // One incompatible field must not block the remaining scalar values.
                }
            }

            return added;
        }

        private bool TryRead(string path, string expectedIdentity, IReadOnlyDictionary<string, PropertyInfo> properties)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 3 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
                {
                    return false;
                }

                var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
                var members = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in lines.Skip(1))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0 || separator == line.Length - 1)
                    {
                        return false;
                    }

                    string name = line.Substring(0, separator).Trim();
                    string encoded = line.Substring(separator + 1).Trim();
                    if (name is "schema" or "identity")
                    {
                        if (metadata.ContainsKey(name))
                        {
                            return false;
                        }

                        metadata[name] = encoded;
                        continue;
                    }

                    if (members.ContainsKey(name))
                    {
                        return false;
                    }
                    members[name] = encoded;

                    // Unknown members are retained by the file but ignored by this version;
                    // known members must decode as their reflected scalar type.
                    if (properties.TryGetValue(name, out PropertyInfo? property))
                    {
                        if (!TryDecode(encoded, property.PropertyType, out _))
                        {
                            return false;
                        }
                    }
                }

                bool valid = metadata.TryGetValue("schema", out string? schema) &&
                             string.Equals(schema, SchemaVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
                             metadata.TryGetValue("identity", out string? identity) &&
                             string.Equals(identity, expectedIdentity, StringComparison.Ordinal);
                if (!valid)
                {
                    m_originalValues.Clear();
                    return false;
                }

                foreach (KeyValuePair<string, string> member in members)
                {
                    if (properties.ContainsKey(member.Key))
                    {
                        m_originalValues[member.Key] = member.Value;
                    }
                }

                return true;
            }
            catch
            {
                m_originalValues.Clear();
                return false;
            }
        }

        private bool HasLegacySidecar(string saveName)
        {
            string safeName = Sanitize(saveName);
            if (safeName.Length == 0)
            {
                safeName = "current";
            }

            string legacyPath = Path.Combine(m_rootDirectory, safeName, "state.txt");
            if (!File.Exists(legacyPath))
            {
                return false;
            }

            // Keep the constant in the code so migration behavior stays visibly tied to the
            // actual predecessor format, while deliberately refusing to parse or overwrite it.
            _ = LegacyHeader;
            return true;
        }

        private static string Sanitize(string? value)
        {
            string input = value?.Trim() ?? string.Empty;
            if (input.Length == 0)
            {
                return string.Empty;
            }

            char[] chars = input
                .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_')
                .ToArray();
            return new string(chars).Trim('.', '_');
        }
    }
}
