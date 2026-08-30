// Taj's COI Mods | BlueprintRecycleBinStore.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TajsCOI.Core.Blueprints
{
    /// <summary>
    ///     Small process-independent sidecar for recoverable native blueprint deletion. Entries
    ///     contain portable payloads only; no live library/UI object is retained or serialized.
    /// </summary>
    internal sealed class BlueprintRecycleBinStore
    {
        internal const int MaximumEntries = 512;

        private readonly string m_filePath;
        private readonly Dictionary<string, BlueprintPortableEnvelope> m_entries = new(StringComparer.Ordinal);

        internal BlueprintRecycleBinStore(string filePath)
        {
            m_filePath = string.IsNullOrWhiteSpace(filePath)
                ? throw new ArgumentException("Recycle-bin path cannot be empty.", nameof(filePath))
                : Path.GetFullPath(filePath);
        }

        internal IReadOnlyList<BlueprintPortableEnvelope> Snapshot() =>
            new ReadOnlyCollection<BlueprintPortableEnvelope>(m_entries.Values
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.StableId, StringComparer.Ordinal)
                .ToArray());

        internal bool Load(out string error)
        {
            error = string.Empty;
            m_entries.Clear();
            if (!File.Exists(m_filePath))
            {
                return true;
            }

            try
            {
                BlueprintPortableEnvelope[]? values;
                using (var stream = File.OpenRead(m_filePath))
                {
                    values = (BlueprintPortableEnvelope[]?)new DataContractJsonSerializer(typeof(BlueprintPortableEnvelope[])).ReadObject(stream);
                }
                if (values is null)
                {
                    return true;
                }
                if (values.Length > MaximumEntries)
                {
                    error = "Recycle-bin sidecar exceeds the supported entry limit.";
                    return false;
                }

                foreach (BlueprintPortableEnvelope value in values)
                {
                    if (!BlueprintPortableCodec.TryRead(Serialize(value), out BlueprintPortableEnvelope? validated, out string validationError))
                    {
                        error = "Recycle-bin sidecar contains invalid content: " + validationError;
                        m_entries.Clear();
                        return false;
                    }
                    m_entries[validated!.StableId] = validated;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Recycle-bin sidecar could not be loaded: " + ex.Message;
                m_entries.Clear();
                return false;
            }
        }

        internal bool TryAdd(BlueprintPortableEnvelope envelope, out string error)
        {
            error = string.Empty;
            if (envelope is null || !BlueprintPortableCodec.TryRead(Serialize(envelope), out BlueprintPortableEnvelope? validated, out error))
            {
                if (error.Length == 0) error = "Recycle-bin entry is invalid.";
                return false;
            }
            if (!m_entries.ContainsKey(validated!.StableId) && m_entries.Count >= MaximumEntries)
            {
                error = "Recycle bin is full; purge an older entry first.";
                return false;
            }
            m_entries[validated.StableId] = validated;
            return true;
        }

        internal bool TryGet(string stableId, out BlueprintPortableEnvelope? envelope) =>
            m_entries.TryGetValue(stableId?.Trim() ?? string.Empty, out envelope);

        internal bool Remove(string stableId) =>
            !string.IsNullOrWhiteSpace(stableId) && m_entries.Remove(stableId.Trim());

        internal bool Save(out string error)
        {
            error = string.Empty;
            string? directory = Path.GetDirectoryName(m_filePath);
            string temporaryPath = m_filePath + ".tmp";
            try
            {
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (var stream = File.Create(temporaryPath))
                {
                    new DataContractJsonSerializer(typeof(BlueprintPortableEnvelope[])).WriteObject(stream, m_entries.Values.ToArray());
                }
                if (File.Exists(m_filePath))
                {
                    File.Replace(temporaryPath, m_filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, m_filePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Recycle-bin sidecar could not be saved: " + ex.Message;
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                return false;
            }
        }

        private static string Serialize(BlueprintPortableEnvelope value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(BlueprintPortableEnvelope)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
