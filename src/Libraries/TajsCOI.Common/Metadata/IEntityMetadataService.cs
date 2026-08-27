// Taj's COI Mods | IEntityMetadataService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;

namespace TajsCOI.Common.Metadata
{
    public interface IEntityMetadataService : IEntityMetadataLookup
    {
        bool TrySetEntityMetadata(
            EntityMetadataIdentity identity,
            string? alias,
            string? note,
            string? groupId,
            out string error);

        bool TryClearEntityMetadata(EntityMetadataIdentity identity);

        bool TryCreateGroup(string? name, string? color, out EntityMetadataGroup? group, out string error);

        bool TryUpdateGroup(string groupId, string? name, int order, string? color, bool locked, out string error);

        bool TryDeleteGroup(string groupId);

        /// <summary>
        ///     Removes only identities explicitly confirmed as destroyed. Missing live entities
        ///     are not treated as destruction because scene queries can be partial during reload.
        /// </summary>
        int PruneConfirmed(IEnumerable<EntityMetadataIdentity> confirmedDestroyed);
    }
}
