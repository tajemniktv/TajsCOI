// Taj's COI Mods | EntityMetadataContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace TajsCOI.Common.Metadata
{
    /// <summary>
    ///     Stable identity for optional per-save entity metadata. Numeric IDs are deliberately
    ///     paired with a prototype fingerprint so a recycled native ID cannot inherit old text.
    /// </summary>
    public readonly struct EntityMetadataIdentity : IEquatable<EntityMetadataIdentity>
    {
        public EntityMetadataIdentity(int entityId, string prototypeFingerprint)
        {
            if (entityId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId), "Entity IDs cannot be negative.");
            }

            EntityId = entityId;
            PrototypeFingerprint = Require(prototypeFingerprint, nameof(prototypeFingerprint));
        }

        public int EntityId { get; }
        public string PrototypeFingerprint { get; }

        public bool Equals(EntityMetadataIdentity other) =>
            EntityId == other.EntityId &&
            string.Equals(PrototypeFingerprint, other.PrototypeFingerprint, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is EntityMetadataIdentity other && Equals(other);

        public override int GetHashCode() =>
            EntityId * 397 ^ StringComparer.Ordinal.GetHashCode(PrototypeFingerprint ?? string.Empty);

        public static bool operator ==(EntityMetadataIdentity left, EntityMetadataIdentity right) => left.Equals(right);
        public static bool operator !=(EntityMetadataIdentity left, EntityMetadataIdentity right) => !left.Equals(right);

        public override string ToString() => EntityId + ":" + (PrototypeFingerprint ?? string.Empty);

        private static string Require(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Prototype fingerprint cannot be empty.", parameter)
                : value.Trim();
    }

    public sealed class EntityMetadataRecord
    {
        public EntityMetadataRecord(
            EntityMetadataIdentity identity,
            string? alias = null,
            string? note = null,
            string? groupId = null)
        {
            Identity = identity;
            Alias = Normalize(alias);
            Note = Normalize(note);
            GroupId = Normalize(groupId);
        }

        public EntityMetadataIdentity Identity { get; }
        public string Alias { get; }
        public string Note { get; }
        public string? GroupId { get; }
        public bool HasDisplayMetadata => Alias.Length != 0 || Note.Length != 0 || GroupId is not null;

        public EntityMetadataRecord With(string? alias, string? note, string? groupId) =>
            new(Identity, alias, note, groupId);

        private static string Normalize(string? value) => (value ?? string.Empty).Trim();
    }

    public sealed class EntityMetadataGroup
    {
        public EntityMetadataGroup(
            string groupId,
            string name,
            int order,
            string color,
            bool locked)
        {
            GroupId = Require(groupId, nameof(groupId));
            Name = Require(name, nameof(name));
            if (order < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(order));
            }
            Order = order;
            Color = NormalizeColor(color);
            Locked = locked;
        }

        public string GroupId { get; }
        public string Name { get; }
        public int Order { get; }
        public string Color { get; }
        public bool Locked { get; }

        public EntityMetadataGroup With(string name, int order, string color, bool locked) =>
            new(GroupId, name, order, color, locked);

        private static string Require(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Group text cannot be empty.", parameter)
                : value.Trim();

        private static string NormalizeColor(string value)
        {
            string normalized = Require(value, nameof(value));
            if (normalized[0] == '#')
            {
                normalized = normalized.Substring(1);
            }
            if (normalized.Length != 6 && normalized.Length != 8 ||
                !long.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                throw new ArgumentException("Group color must be a six- or eight-digit hexadecimal value.", nameof(value));
            }
            return "#" + normalized.ToUpperInvariant();
        }
    }

    /// <summary>
    ///     Read-only metadata surface for dependent Tajs modules. Core owns mutation and
    ///     persistence; consumers may safely display these snapshots without retaining scene objects.
    /// </summary>
    public interface IEntityMetadataLookup
    {
        public IReadOnlyList<EntityMetadataRecord> GetEntityMetadataSnapshot();

        public IReadOnlyList<EntityMetadataGroup> GetGroupSnapshot();

        public bool TryGetEntityMetadata(EntityMetadataIdentity identity, out EntityMetadataRecord? metadata);

        public bool TryGetGroup(string groupId, out EntityMetadataGroup? group);

        public IReadOnlyList<EntityMetadataRecord> ResolveLiveMetadata(IEnumerable<EntityMetadataIdentity> liveEntities);
    }
}
