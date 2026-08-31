// Taj's COI Mods | TajsDifficultyStateStore.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mafi;
using Mafi.Core;
using Mafi.Core.Game;
using TajsCOI.Common.Persistence;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    /// <summary>
    ///     Compatibility facade for the shared sidecar identity. SaveFileInfo alone is metadata
    ///     only and therefore remains unverified; path-based identities use the stable filesystem
    ///     file identity and keep timestamp/size in a separate revision marker.
    /// </summary>
    internal sealed class TajsDifficultySaveIdentity
    {
        private TajsDifficultySaveIdentity(TajsSaveIdentity identity, string fingerprint)
        {
            Inner = identity;
            Fingerprint = fingerprint;
        }

        internal TajsSaveIdentity Inner { get; }
        internal string Fingerprint { get; }
        internal string OwnershipKey => Inner.OwnershipKey;
        internal string RevisionKey => Inner.RevisionKey;
        internal bool IsVerified => Inner.IsVerified;
        internal bool IsStronglyVerified => Inner.IsStronglyVerified;
        internal string DisplayName => Inner.DisplayName;

        internal static TajsDifficultySaveIdentity FromInner(TajsSaveIdentity identity) =>
            new TajsDifficultySaveIdentity(identity, identity.RevisionKey);

        internal static TajsDifficultySaveIdentity FromSaveFile(SaveFileInfo save)
        {
            TajsSaveIdentity identity = TajsSaveIdentity.FromMetadata(
                save.GameName,
                save.NameNoExtension,
                save.Extension,
                save.WriteTimestamp,
                save.SizeBytes);
            return new TajsDifficultySaveIdentity(identity, identity.RevisionKey);
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

                TajsSaveIdentity? identity = TajsSaveIdentity.FromFile(path, gameName);
                return identity is null ? null : new TajsDifficultySaveIdentity(identity, identity.RevisionKey);
            }
            catch
            {
                return null;
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
        private readonly TajsSaveIdentityRegistry m_identityRegistry;
        private string? m_filePath;
        private string? m_identityFingerprint;
        private string? m_revisionFingerprint;
        private TajsDifficultySaveIdentity? m_identity;
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
                : Path.GetFullPath(rootDirectory!);
            m_identityRegistry = new TajsSaveIdentityRegistry(m_rootDirectory);
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
            if (identity?.Inner.PhysicalPath is string physicalPath)
            {
                TajsSaveIdentity? resolved = m_identityRegistry.Resolve(
                    physicalPath,
                    identity.Inner.GameName,
                    identity.Inner.DisplayName);
                if (resolved is not null)
                {
                    identity = TajsDifficultySaveIdentity.FromInner(resolved);
                }
            }

            m_originalValues.Clear();
            m_identity = identity;
            bool hasVerifiedFileIdentity = identity?.IsStronglyVerified == true;
            m_filePath = !hasVerifiedFileIdentity
                ? null
                : Path.Combine(m_rootDirectory, identity!.OwnershipKey, "state.txt");
            m_identityFingerprint = identity?.OwnershipKey;
            m_revisionFingerprint = identity?.RevisionKey;
            m_allowWrite = false;
            m_baselineAvailable = false;
            m_baselineStatus = "original-save baseline unavailable.";

            if (identity is not null && !hasVerifiedFileIdentity)
            {
                if (HasLegacySidecar(saveName))
                {
                    // V1 is keyed only by a sanitized name. Preserve it, but never guess that it
                    // belongs to the active save and never silently migrate it.
                    m_baselineStatus = "original-save baseline unavailable: legacy name-based sidecar requires explicit recapture.";
                    return;
                }

                // SaveFileInfo metadata is useful for diagnostics but is not an ownership
                // proof: timestamp/size/name can describe an unrelated save. Keep the native
                // baseline in memory only until a concrete file identity is available.
                CaptureMissing(current, properties);
                m_baselineAvailable = true;
                m_baselineStatus = "original-save baseline captured in memory only: verified file identity was unavailable.";
                return;
            }

            if (identity is not null && File.Exists(m_filePath))
            {
                if (!TryRead(m_filePath!, identity.OwnershipKey, properties))
                {
                    // Do not replace a file that may contain recoverable data. The native
                    // difficulty UI and console remain usable without the optional baseline.
                    m_baselineStatus = "original-save baseline unavailable: sidecar is invalid or belongs to another save.";
                    return;
                }

                m_baselineAvailable = true;
                m_allowWrite = true;
                m_baselineStatus = identity.IsVerified
                    ? "original-save baseline identity verified."
                    : "original-save baseline loaded from metadata-only identity.";
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
                : identity.IsVerified
                    ? "original-save baseline captured with verified save identity."
                    : "original-save baseline captured from metadata-only identity.";
            m_allowWrite = hasVerifiedFileIdentity;
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

            TajsSaveIdentity? rebound = m_identityRegistry.Rebind(
                savePath!,
                gameName,
                m_identity?.Inner,
                identity.DisplayName);
            if (rebound is not null)
            {
                identity = TajsDifficultySaveIdentity.FromInner(rebound);
            }

            if (m_identity is null && m_identityFingerprint is null)
            {
                // A new game has no prior save owner to protect. Bind the in-memory baseline to
                // the first successful save instead of dropping values captured before that save.
                m_identity = identity;
                m_identityFingerprint = identity.OwnershipKey;
                m_revisionFingerprint = identity.RevisionKey;
                m_filePath = identity.IsStronglyVerified
                    ? Path.Combine(m_rootDirectory, identity.OwnershipKey, "state.txt")
                    : null;
                m_allowWrite = identity.IsStronglyVerified;
                m_baselineStatus = identity.IsStronglyVerified
                    ? "original-save baseline captured with verified save identity."
                    : "original-save baseline remains in memory only: verified file identity was unavailable.";
                return m_allowWrite && Save();
            }

            if (string.Equals(m_identityFingerprint, identity.OwnershipKey, StringComparison.Ordinal))
            {
                m_identity = identity;
                m_revisionFingerprint = identity.RevisionKey;
                return m_allowWrite && Save();
            }

            string nextPath = Path.Combine(m_rootDirectory, identity.OwnershipKey, "state.txt");
            if (File.Exists(nextPath))
            {
                m_originalValues.Clear();
                m_baselineAvailable = false;
                m_allowWrite = false;
                m_identityFingerprint = identity.OwnershipKey;
                m_revisionFingerprint = identity.RevisionKey;
                m_identity = identity;
                m_filePath = nextPath;
                m_baselineStatus = "original-save baseline unavailable: save identity collides with an existing sidecar.";
                return false;
            }

            // A changed ownership key means save-as/copy/replacement. Never carry the old
            // baseline into that unrelated file; preserve the old sidecar and fail closed.
            m_originalValues.Clear();
            m_baselineAvailable = false;
            m_allowWrite = false;
            m_identityFingerprint = identity.OwnershipKey;
            m_revisionFingerprint = identity.RevisionKey;
            m_identity = identity;
            m_filePath = nextPath;
            m_baselineStatus = "original-save baseline unavailable: save identity changed; recapture is required.";
            return false;
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
                    new[]
                    {
                        Header,
                        "schema=" + SchemaVersion.ToString(CultureInfo.InvariantCulture),
                        "identity=" + m_identityFingerprint,
                        "revision=" + (m_revisionFingerprint ?? string.Empty)
                    }.Concat(
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
