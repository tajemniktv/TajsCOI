// Taj's COI Mods | BulkDeconstructionCancellationContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi.Core.Entities.Static;

namespace TajsCOI.Tweaks.Features.Cleanup
{
    /// <summary>
    ///     Immutable preview row for the bulk cancel-deconstruction tool. The live entity is
    ///     intentionally not retained; commit resolves the ID again through the native manager.
    /// </summary>
    internal readonly struct BulkDeconstructionSelectionEntry : IEquatable<BulkDeconstructionSelectionEntry>
    {
        internal BulkDeconstructionSelectionEntry(int entityId, string title)
        {
            EntityId = entityId;
            Title = title ?? string.Empty;
        }

        internal int EntityId { get; }
        internal string Title { get; }

        public bool Equals(BulkDeconstructionSelectionEntry other) =>
            EntityId == other.EntityId && string.Equals(Title, other.Title, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is BulkDeconstructionSelectionEntry other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return EntityId * 397 ^ (Title?.GetHashCode() ?? 0);
            }
        }
    }

    internal static class BulkDeconstructionCancellationLimits
    {
        internal const int MaxSelectedEntities = 2048;
        internal const int CommandsPerRenderUpdate = 64;
    }

    /// <summary>
    ///     The game's construction manager remains the sole construction-state authority. This
    ///     predicate only recognizes the two native states that can be cancelled by the tool.
    /// </summary>
    internal static class BulkDeconstructionCancellationPolicy
    {
        internal static bool IsDeconstructing(IStaticEntity? entity) =>
            entity is not null && !entity.IsDestroyed &&
            (entity.ConstructionState == ConstructionState.PendingDeconstruction ||
             entity.ConstructionState == ConstructionState.InDeconstruction);

        internal static IReadOnlyList<T> TakeBounded<T>(IEnumerable<T> values, int maximum, out bool truncated)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            List<T> result = values.Take(Math.Max(0, maximum) + 1).ToList();
            truncated = result.Count > maximum;
            if (truncated)
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }
    }
}
