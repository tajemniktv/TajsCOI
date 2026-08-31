// Taj's COI Mods | TajsConfigurationPipeline.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using Mafi.Core.Entities;
using TajsCOI.Common.Configuration;
using TajsCOI.Common.Logging;

namespace TajsCOI.Tweaks.Configuration
{
    /// <summary>
    ///     Bridges Core's value-only configuration registry to MaFi's native config bag. The native
    ///     method runs first; this bridge appends one namespaced extension record afterwards. It
    ///     stores no entity, resolver, or scene callback in process lifetime state.
    /// </summary>
    internal static class TajsConfigurationPipeline
    {
        internal const string ConfigDataKey = "TajsCOI.Configuration.V1";
        private static readonly object s_gate = new();
        private static WeakReference<IConfigurationRegistry>? s_registry;
        private static WeakReference<ITajsLogger>? s_log;

        internal static void Bind(IConfigurationRegistry registry, ITajsLogger? log = null)
        {
            lock (s_gate)
            {
                s_registry = new WeakReference<IConfigurationRegistry>(registry ?? throw new ArgumentNullException(nameof(registry)));
                s_log = log is null ? null : new WeakReference<ITajsLogger>(log);
            }
        }

        internal static void Unbind(IConfigurationRegistry registry)
        {
            if (registry is null)
            {
                return;
            }

            lock (s_gate)
            {
                if (s_registry is not null && s_registry.TryGetTarget(out IConfigurationRegistry? current) &&
                    ReferenceEquals(current, registry))
                {
                    s_registry = null;
                    s_log = null;
                }
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
                if (snapshot.Payloads.Count == 0)
                {
                    return false;
                }

                if (!ConfigurationPayloadCodec.TrySerialize(snapshot, out string encoded, out string encodeError))
                {
                    Warn("Configuration copy extension payload was not serialized: " + encodeError);
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
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return false;
            }

            if (!ConfigurationPayloadCodec.TryDeserialize(encoded, out ConfigurationSnapshot snapshot, out string decodeError))
            {
                Warn("Configuration copy extension payload was ignored: " + decodeError);
                return false;
            }

            try
            {
                ConfigurationApplyResult result = registry.Apply(Describe(entity), runtimeEntity, snapshot);
                if (result.Errors.Count != 0 && TryGetLogger(out ITajsLogger? log))
                {
                    log!.WarningOnce(
                        "Configuration copy/apply skipped one or more extension values: " +
                        string.Join(" | ", result.Errors.Take(4)));
                }
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
                return s_registry is not null && s_registry.TryGetTarget(out IConfigurationRegistry? registry)
                    ? registry
                    : null;
            }
        }

        private static bool TryGetLogger(out ITajsLogger? logger)
        {
            lock (s_gate)
            {
                if (s_log is not null && s_log.TryGetTarget(out logger))
                {
                    return true;
                }
            }
            logger = null;
            return false;
        }

        private static void Warn(string message)
        {
            if (message.Length > 256)
            {
                message = message.Substring(0, 256) + "...";
            }
            if (TryGetLogger(out ITajsLogger? logger))
            {
                logger!.WarningOnce(message);
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
