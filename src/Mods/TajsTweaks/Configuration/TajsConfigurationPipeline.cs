// Taj's COI Mods | TajsConfigurationPipeline.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi;
using Mafi.Core.Entities;
using TajsCOI.Common.Configuration;

namespace TajsCOI.Tweaks.Configuration
{
    /// <summary>
    /// Bridges Core's value-only configuration registry to MaFi's native config bag. The native
    /// method runs first; this bridge appends one namespaced extension record afterwards. It
    /// stores no entity, resolver, or scene callback in process lifetime state.
    /// </summary>
    internal static class TajsConfigurationPipeline
    {
        internal const string ConfigDataKey = "TajsCOI.Configuration.V1";
        private static readonly object s_gate = new();
        private static IConfigurationRegistry? s_registry;

        internal static void Bind(IConfigurationRegistry registry)
        {
            lock (s_gate)
            {
                s_registry = registry;
            }
        }

        internal static bool TryCapture(object runtimeEntity, EntityConfigData data)
        {
            IConfigurationRegistry? registry = GetRegistry();
            if (registry is null || runtimeEntity is not IEntity entity || data is null)
            {
                return false;
            }

            try
            {
                ConfigurationSnapshot snapshot = registry.Capture(Describe(entity), runtimeEntity);
                if (snapshot.Payloads.Count == 0 || !ConfigurationPayloadCodec.TrySerialize(snapshot, out string encoded, out _))
                {
                    return false;
                }

                data.SetString(ConfigDataKey, encoded);
                return true;
            }
            catch
            {
                // Extension capture is optional. The caller retains its legacy compatibility
                // fallback and never prevents vanilla config copy from completing.
                return false;
            }
        }

        internal static bool TryApply(object runtimeEntity, EntityConfigData data)
        {
            IConfigurationRegistry? registry = GetRegistry();
            if (registry is null || runtimeEntity is not IEntity entity || data is null)
            {
                return false;
            }

            string? encoded = data.GetString(ConfigDataKey).ValueOrNull;
            if (string.IsNullOrWhiteSpace(encoded) ||
                !ConfigurationPayloadCodec.TryDeserialize(encoded, out ConfigurationSnapshot snapshot, out _))
            {
                return false;
            }

            try
            {
                ConfigurationApplyResult result = registry.Apply(Describe(entity), runtimeEntity, snapshot);
                return result.Applied > 0 || result.Skipped > 0;
            }
            catch
            {
                return false;
            }
        }

        private static IConfigurationRegistry? GetRegistry()
        {
            lock (s_gate)
            {
                return s_registry;
            }
        }

        private static ConfigurationEntityDescriptor Describe(IEntity entity)
        {
            string type = entity.GetType().FullName ?? entity.GetType().Name;
            return new ConfigurationEntityDescriptor(
                entity.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                type,
                type);
        }
    }
}
