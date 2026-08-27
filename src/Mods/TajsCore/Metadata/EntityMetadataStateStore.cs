// Taj's COI Mods | EntityMetadataStateStore.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TajsCOI.Common.Metadata;

namespace TajsCOI.Core.Metadata
{
    /// <summary>
    ///     File-backed state for the Core metadata service. The format is intentionally small,
    ///     deterministic, and independent of MaFi's save serializer; each field is base64 so
    ///     aliases and notes cannot corrupt a record by containing tabs or newlines.
    /// </summary>
    internal sealed class EntityMetadataStateStore
    {
        private const string Header = "TajsCoreEntityMetadataV1";
        private readonly Dictionary<EntityMetadataIdentity, EntityMetadataRecord> m_entities = new();
        private readonly Dictionary<string, EntityMetadataGroup> m_groups = new(StringComparer.Ordinal);
        private readonly string m_rootDirectory;
        private string? m_filePath;
        private bool m_allowWrite;

        internal EntityMetadataStateStore(string rootDirectory)
        {
            m_rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? throw new ArgumentException("Metadata root directory cannot be empty.", nameof(rootDirectory))
                : Path.GetFullPath(rootDirectory);
        }

        internal IReadOnlyDictionary<EntityMetadataIdentity, EntityMetadataRecord> Entities => m_entities;
        internal IReadOnlyDictionary<string, EntityMetadataGroup> Groups => m_groups;
        internal bool IsBound => m_filePath is not null;

        internal void Load(string? saveIdentity)
        {
            m_entities.Clear();
            m_groups.Clear();
            m_filePath = null;
            m_allowWrite = false;
            if (string.IsNullOrWhiteSpace(saveIdentity))
            {
                return;
            }

            string directory = Path.Combine(m_rootDirectory, saveIdentity!.Trim());
            m_filePath = Path.Combine(directory, "metadata.tsv");
            m_allowWrite = true;
            if (!File.Exists(m_filePath))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(m_filePath, Encoding.UTF8);
                if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
                {
                    ClearLoadedState();
                    return;
                }

                foreach (string line in lines.Skip(1))
                {
                    TryParseLine(line);
                }
            }
            catch
            {
                // Optional metadata must never prevent a save from loading.
                ClearLoadedState();
            }
        }

        internal bool Rebind(string? saveIdentity)
        {
            if (string.IsNullOrWhiteSpace(saveIdentity))
            {
                return false;
            }

            string directory = Path.Combine(m_rootDirectory, saveIdentity!.Trim());
            string nextPath = Path.Combine(directory, "metadata.tsv");
            if (string.Equals(m_filePath, nextPath, StringComparison.OrdinalIgnoreCase))
            {
                m_allowWrite = true;
                return true;
            }

            // A save identity is deliberately content/file based. If a sidecar already exists
            // for the new identity, refuse to overwrite it: this is an identity collision, not
            // evidence that the active records belong to that save.
            if (File.Exists(nextPath))
            {
                m_allowWrite = false;
                return false;
            }

            m_filePath = nextPath;
            m_allowWrite = true;
            return true;
        }

        internal void Unbind()
        {
            m_entities.Clear();
            m_groups.Clear();
            m_filePath = null;
            m_allowWrite = false;
        }

        internal void SetEntity(EntityMetadataRecord record) => m_entities[record.Identity] = record;

        internal bool RemoveEntity(EntityMetadataIdentity identity) => m_entities.Remove(identity);

        internal void SetGroup(EntityMetadataGroup group) => m_groups[group.GroupId] = group;

        internal bool RemoveGroup(string groupId) => m_groups.Remove(groupId);

        internal bool Save()
        {
            if (!m_allowWrite || m_filePath is null)
            {
                return false;
            }

            string? temporary = null;
            try
            {
                string filePath = m_filePath;
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                temporary = m_filePath + ".tmp." + Guid.NewGuid().ToString("N");
                var lines = new List<string> { Header };
                lines.AddRange(
                    m_groups.Values
                        .OrderBy(group => group.Order)
                        .ThenBy(group => group.GroupId, StringComparer.Ordinal)
                        .Select(group => string.Join(
                            "\t",
                            "group",
                            Encode(group.GroupId),
                            Encode(group.Name),
                            group.Order.ToString(CultureInfo.InvariantCulture),
                            Encode(group.Color),
                            group.Locked ? "1" : "0")));
                lines.AddRange(
                    m_entities.Values
                        .OrderBy(entity => entity.Identity.EntityId)
                        .ThenBy(entity => entity.Identity.PrototypeFingerprint, StringComparer.Ordinal)
                        .Select(entity => string.Join(
                            "\t",
                            "entity",
                            entity.Identity.EntityId.ToString(CultureInfo.InvariantCulture),
                            Encode(entity.Identity.PrototypeFingerprint),
                            Encode(entity.Alias),
                            Encode(entity.Note),
                            Encode(entity.GroupId ?? string.Empty))));

                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                if (File.Exists(filePath))
                {
                    File.Replace(temporary, filePath, filePath + ".bak", true);
                }
                else
                {
                    File.Move(temporary, filePath);
                }
                temporary = null;
                return true;
            }
            catch
            {
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

        private void TryParseLine(string line)
        {
            string[] parts = line.Split('\t');
            if (parts.Length == 0)
            {
                return;
            }

            if (string.Equals(parts[0], "group", StringComparison.Ordinal) && parts.Length == 6 &&
                TryDecode(parts[1], out string groupId) && TryDecode(parts[2], out string name) &&
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) &&
                TryDecode(parts[4], out string color) && (parts[5] == "0" || parts[5] == "1"))
            {
                try
                {
                    var group = new EntityMetadataGroup(groupId, name, order, color, parts[5] == "1");
                    m_groups[group.GroupId] = group;
                }
                catch (ArgumentException)
                {
                    // Skip only the malformed optional record.
                }
                return;
            }

            if (string.Equals(parts[0], "entity", StringComparison.Ordinal) && parts.Length == 6 &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id >= 0 &&
                TryDecode(parts[2], out string fingerprint) && TryDecode(parts[3], out string alias) &&
                TryDecode(parts[4], out string note) && TryDecode(parts[5], out string groupIdValue))
            {
                try
                {
                    var identity = new EntityMetadataIdentity(id, fingerprint);
                    string? entityGroupId = groupIdValue.Length == 0 ? null : groupIdValue;
                    if (entityGroupId is not null && !m_groups.ContainsKey(entityGroupId))
                    {
                        entityGroupId = null;
                    }
                    m_entities[identity] = new EntityMetadataRecord(identity, alias, note, entityGroupId);
                }
                catch (ArgumentException)
                {
                    // Skip only the malformed optional record.
                }
            }
        }

        private void ClearLoadedState()
        {
            m_entities.Clear();
            m_groups.Clear();
        }

        private static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static bool TryDecode(string value, out string decoded)
        {
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                return true;
            }
            catch (FormatException)
            {
                decoded = string.Empty;
                return false;
            }
        }
    }
}
