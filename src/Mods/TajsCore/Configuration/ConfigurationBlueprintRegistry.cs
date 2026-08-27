// Taj's COI Mods | ConfigurationBlueprintRegistry.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using TajsCOI.Common.Configuration;

namespace TajsCOI.Core.Configuration
{
    /// <summary>
    /// Core-owned registry for extension payloads attached to the game's native copy/apply
    /// pipeline. It stores descriptors only. Runtime entities are supplied for each operation
    /// and are never retained by this process-lifetime service.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsEverything)]
    public sealed class ConfigurationBlueprintRegistry : IConfigurationRegistry
    {
        private readonly object m_gate = new();
        private readonly Dictionary<string, ConfigurationHandlerDescriptor> m_handlers =
            new(StringComparer.Ordinal);

        public ConfigurationRegistrationResult Register(ConfigurationHandlerDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            lock (m_gate)
            {
                if (!m_handlers.TryGetValue(descriptor.HandlerId, out ConfigurationHandlerDescriptor? previous))
                {
                    m_handlers.Add(descriptor.HandlerId, descriptor);
                    return new ConfigurationRegistrationResult(
                        ConfigurationRegistrationStatus.Added,
                        "Configuration handler registered: " + descriptor.HandlerId);
                }

                if (!string.Equals(previous.Owner, descriptor.Owner, StringComparison.Ordinal) ||
                    previous.SchemaVersion != descriptor.SchemaVersion)
                {
                    return new ConfigurationRegistrationResult(
                        ConfigurationRegistrationStatus.Rejected,
                        "Configuration handler '" + descriptor.HandlerId + "' has incompatible ownership or schema metadata.");
                }

                return new ConfigurationRegistrationResult(
                    ConfigurationRegistrationStatus.AlreadyRegistered,
                    "Configuration handler already registered: " + descriptor.HandlerId);
            }
        }

        public ConfigurationSnapshot Capture(ConfigurationEntityDescriptor entity, object runtimeEntity)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }
            if (runtimeEntity is null)
            {
                throw new ArgumentNullException(nameof(runtimeEntity));
            }

            ConfigurationHandlerDescriptor[] handlers = GetHandlerSnapshot().ToArray();
            var payloads = new List<ConfigurationPayload>();
            foreach (ConfigurationHandlerDescriptor handler in handlers)
            {
                try
                {
                    if (!handler.Supports(entity))
                    {
                        continue;
                    }

                    IReadOnlyDictionary<string, object> values = handler.Read(runtimeEntity);
                    if (values is null)
                    {
                        continue;
                    }

                    payloads.Add(new ConfigurationPayload(
                        handler.HandlerId,
                        handler.Owner,
                        handler.SchemaVersion,
                        values));
                }
                catch
                {
                    // An extension is optional: malformed/failed reads must not remove the
                    // native configuration or prevent other handlers from being copied.
                }
            }

            return new ConfigurationSnapshot(payloads);
        }

        public ConfigurationApplyResult Apply(
            ConfigurationEntityDescriptor entity,
            object runtimeEntity,
            ConfigurationSnapshot snapshot)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }
            if (runtimeEntity is null)
            {
                throw new ArgumentNullException(nameof(runtimeEntity));
            }
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var errors = new List<string>();
            int applied = 0;
            int skipped = 0;
            ConfigurationHandlerDescriptor[] handlers = GetHandlerSnapshot().ToArray();
            foreach (ConfigurationPayload payload in snapshot.Payloads)
            {
                ConfigurationHandlerDescriptor? handler = handlers.FirstOrDefault(item =>
                    string.Equals(item.HandlerId, payload.HandlerId, StringComparison.Ordinal));
                if (handler is null || !handler.Supports(entity))
                {
                    skipped++;
                    continue;
                }
                if (!string.Equals(payload.Owner, handler.Owner, StringComparison.Ordinal))
                {
                    skipped++;
                    errors.Add(payload.HandlerId + ": owner mismatch");
                    continue;
                }

                try
                {
                    IReadOnlyDictionary<string, object>? values = payload.Values;
                    if (payload.SchemaVersion != handler.SchemaVersion)
                    {
                        if (payload.SchemaVersion > handler.SchemaVersion || handler.Migrate is null)
                        {
                            skipped++;
                            errors.Add(payload.HandlerId + ": unsupported schema " + payload.SchemaVersion);
                            continue;
                        }

                        values = handler.Migrate(payload.SchemaVersion, payload.Values);
                        if (values is null)
                        {
                            skipped++;
                            errors.Add(payload.HandlerId + ": migration rejected the payload");
                            continue;
                        }
                    }

                    if (handler.Apply(runtimeEntity, values))
                    {
                        applied++;
                    }
                    else
                    {
                        skipped++;
                        errors.Add(payload.HandlerId + ": apply returned false");
                    }
                }
                catch (Exception exception)
                {
                    skipped++;
                    errors.Add(payload.HandlerId + ": " + exception.GetType().Name);
                }
            }

            return new ConfigurationApplyResult(applied, skipped, errors);
        }

        public IReadOnlyList<ConfigurationHandlerDescriptor> GetHandlerSnapshot()
        {
            lock (m_gate)
            {
                return m_handlers.Values
                    .OrderBy(handler => handler.HandlerId, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }
}
