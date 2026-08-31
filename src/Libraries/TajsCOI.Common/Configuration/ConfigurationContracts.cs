// Taj's COI Mods | ConfigurationContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TajsCOI.Common.Configuration
{
    public enum ConfigurationRegistrationStatus
    {
        Added,
        AlreadyRegistered,
        Rejected,
    }

    public sealed class ConfigurationEntityDescriptor
    {
        public ConfigurationEntityDescriptor(string entityId, string typeId, string? prototypeId = null)
        {
            EntityId = Require(entityId, nameof(entityId));
            TypeId = Require(typeId, nameof(typeId));
            PrototypeId = Optional(prototypeId);
        }

        public string EntityId { get; }
        public string TypeId { get; }
        /// <summary>
        ///     Stable native prototype identity when the entity exposes one. A null value is
        ///     intentional for entities that do not have a meaningful prototype; callers must not
        ///     derive one from <see cref="TypeId"/>.
        /// </summary>
        public string? PrototypeId { get; }

        public bool HasPrototype => PrototypeId is not null;

        private static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Configuration entity identifiers cannot be empty.", parameter);
            }

            return value.Trim();
        }

        private static string? Optional(string? value) =>
            value is null || string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed class ConfigurationPayload
    {
        public ConfigurationPayload(
            string handlerId,
            string owner,
            int schemaVersion,
            IReadOnlyDictionary<string, object> values)
        {
            HandlerId = Require(handlerId, nameof(handlerId));
            Owner = Require(owner, nameof(owner));
            if (schemaVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Configuration schema versions start at one.");
            }
            SchemaVersion = schemaVersion;
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> item in values)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    throw new ArgumentException("Configuration value keys cannot be empty.", nameof(values));
                }
                if (!IsPrimitive(item.Value))
                {
                    throw new ArgumentException(
                        "Configuration payloads may contain only primitive/versioned values.",
                        nameof(values));
                }

                copy[item.Key.Trim()] = item.Value;
            }

            Values = new ReadOnlyDictionary<string, object>(copy);
        }

        public string HandlerId { get; }
        public string Owner { get; }
        public int SchemaVersion { get; }
        public IReadOnlyDictionary<string, object> Values { get; }

        private static bool IsPrimitive(object? value) =>
            value is null || value is string || value is bool || value is byte || value is sbyte ||
            value is short || value is ushort || value is int || value is uint || value is long ||
            value is ulong || value is float || value is double || value is decimal;

        private static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Configuration identifiers cannot be empty.", parameter);
            }

            return value.Trim();
        }
    }

    public sealed class ConfigurationSnapshot
    {
        public ConfigurationSnapshot(IEnumerable<ConfigurationPayload>? payloads)
            : this(payloads, null)
        {
        }

        public ConfigurationSnapshot(
            IEnumerable<ConfigurationPayload>? payloads,
            IEnumerable<string>? errors)
        {
            Payloads = Array.AsReadOnly(
                (payloads ?? Enumerable.Empty<ConfigurationPayload>())
                .Where(payload => payload is not null)
                .OrderBy(payload => payload.HandlerId, StringComparer.Ordinal)
                .ToArray());
            Errors = Array.AsReadOnly(
                (errors ?? Enumerable.Empty<string>())
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Select(error => error.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        }

        public IReadOnlyList<ConfigurationPayload> Payloads { get; }
        public IReadOnlyList<string> Errors { get; }
    }

    public sealed class ConfigurationHandlerDescriptor
    {
        public ConfigurationHandlerDescriptor(
            string handlerId,
            string owner,
            int schemaVersion,
            Func<ConfigurationEntityDescriptor, bool> supports,
            Func<object, IReadOnlyDictionary<string, object>> read,
            Func<object, IReadOnlyDictionary<string, object>, bool> apply,
            Func<int, IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>?>? migrate = null)
        {
            HandlerId = Require(handlerId, nameof(handlerId));
            Owner = Require(owner, nameof(owner));
            if (schemaVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Configuration schema versions start at one.");
            }
            SchemaVersion = schemaVersion;
            Supports = supports ?? throw new ArgumentNullException(nameof(supports));
            Read = read ?? throw new ArgumentNullException(nameof(read));
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
            Migrate = migrate;
        }

        public string HandlerId { get; }
        public string Owner { get; }
        public int SchemaVersion { get; }
        public Func<ConfigurationEntityDescriptor, bool> Supports { get; }

        // These delegates are invoked with the current entity only; the registry never stores
        // the entity or resolver in process metadata.
        public Func<object, IReadOnlyDictionary<string, object>> Read { get; }
        public Func<object, IReadOnlyDictionary<string, object>, bool> Apply { get; }
        public Func<int, IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>?>? Migrate { get; }

        private static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Configuration identifiers cannot be empty.", parameter);
            }

            return value.Trim();
        }
    }

    public sealed class ConfigurationRegistrationResult
    {
        public ConfigurationRegistrationResult(ConfigurationRegistrationStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public ConfigurationRegistrationStatus Status { get; }
        public string Message { get; }
        public bool IsSuccess => Status != ConfigurationRegistrationStatus.Rejected;
    }

    public sealed class ConfigurationApplyResult
    {
        public ConfigurationApplyResult(int applied, int skipped, IEnumerable<string>? errors)
        {
            Applied = applied;
            Skipped = skipped;
            Errors = Array.AsReadOnly((errors ?? Enumerable.Empty<string>()).ToArray());
        }

        public int Applied { get; }
        public int Skipped { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Success => Errors.Count == 0;
    }

    public interface IConfigurationRegistry
    {
        public ConfigurationRegistrationResult Register(ConfigurationHandlerDescriptor descriptor);

        /// <summary>
        ///     Removes a handler only when the caller proves ownership. This lets a scene-scoped
        ///     feature release its metadata without allowing another module to remove it by key.
        /// </summary>
        public bool Unregister(string handlerId, string owner);

        public ConfigurationSnapshot Capture(ConfigurationEntityDescriptor entity, object runtimeEntity);

        public ConfigurationApplyResult Apply(
            ConfigurationEntityDescriptor entity,
            object runtimeEntity,
            ConfigurationSnapshot snapshot);

        public IReadOnlyList<ConfigurationHandlerDescriptor> GetHandlerSnapshot();
    }
}
