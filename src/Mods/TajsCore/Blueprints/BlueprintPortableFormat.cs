// Taj's COI Mods | BlueprintPortableFormat.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Entities.Blueprints;

namespace TajsCOI.Core.Blueprints
{
    /// <summary>
    ///     Tajs-owned envelope around the native game payload. The native payload remains the
    ///     authoritative layout/configuration; this small versioned envelope adds stable identity,
    ///     compatibility metadata, and a deterministic import-preview contract.
    /// </summary>
    [DataContract]
    public sealed class BlueprintPortableEnvelope
    {
        [DataMember(Order = 1)] public int Version { get; set; } = BlueprintPortableCodec.CurrentVersion;
        [DataMember(Order = 2)] public string StableId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ContentHash { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string NativePayload { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string ItemKind { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string Name { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string FolderPath { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string NativeGameVersion { get; set; } = string.Empty;
        [DataMember(Order = 9)] public List<string> PrototypeIds { get; set; } = new();
    }

    public static class BlueprintPortableCodec
    {
        public const int CurrentVersion = 1;

        public static string Export(
            IBlueprintItem item,
            string nativePayload,
            string folderPath,
            IEnumerable<string>? prototypeIds = null,
            string nativeGameVersion = "0.8.7b")
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(nativePayload)) throw new ArgumentException("Native payload cannot be empty.", nameof(nativePayload));

            string hash = ComputeHash(nativePayload);
            var envelope = new BlueprintPortableEnvelope
            {
                StableId = "native:" + hash,
                ContentHash = hash,
                NativePayload = nativePayload,
                ItemKind = item is IBlueprintsFolder ? "folder" : "blueprint",
                Name = item.Name ?? string.Empty,
                FolderPath = folderPath?.Trim() ?? string.Empty,
                NativeGameVersion = nativeGameVersion?.Trim() ?? string.Empty,
                PrototypeIds = (prototypeIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList(),
            };
            return Serialize(envelope);
        }

        public static bool TryRead(string text, out BlueprintPortableEnvelope? envelope, out string error)
        {
            envelope = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Portable blueprint payload is empty.";
                return false;
            }

            try
            {
                envelope = Deserialize(text);
                if (envelope is null)
                {
                    error = "Portable blueprint payload is empty.";
                    return false;
                }
                if (envelope.Version != CurrentVersion)
                {
                    error = "Unsupported Tajs blueprint export schema " + envelope.Version + "; expected " + CurrentVersion + ".";
                    envelope = null;
                    return false;
                }
                if (string.IsNullOrWhiteSpace(envelope.NativePayload) ||
                    string.IsNullOrWhiteSpace(envelope.StableId) ||
                    string.IsNullOrWhiteSpace(envelope.ContentHash))
                {
                    error = "Portable blueprint payload is missing stable identity or native content.";
                    envelope = null;
                    return false;
                }

                string expectedHash = ComputeHash(envelope.NativePayload);
                if (!string.Equals(expectedHash, envelope.ContentHash, StringComparison.Ordinal))
                {
                    error = "Portable blueprint content hash does not match its native payload.";
                    envelope = null;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                envelope = null;
                error = "Invalid portable blueprint payload: " + ex.Message;
                return false;
            }
        }

        private static string Serialize(BlueprintPortableEnvelope envelope)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(BlueprintPortableEnvelope)).WriteObject(stream, envelope);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static BlueprintPortableEnvelope? Deserialize(string text)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                return (BlueprintPortableEnvelope?)new DataContractJsonSerializer(typeof(BlueprintPortableEnvelope)).ReadObject(stream);
            }
        }

        private static string ComputeHash(string payload)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return string.Concat(digest.Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }
}
