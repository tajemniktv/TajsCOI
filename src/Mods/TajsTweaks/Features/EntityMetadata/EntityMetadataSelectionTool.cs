// Taj's COI Mods | EntityMetadataSelectionTool.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using TajsCOI.Common.Metadata;
using TajsCOI.Tweaks.Features.Selection;

namespace TajsCOI.Tweaks.Features.EntityMetadata
{
    /// <summary>
    ///     Assigns a metadata group to live static entities selected by the shared rectangle
    ///     picker. Existing aliases and notes are retained; Core remains the mutation owner.
    /// </summary>
    internal sealed class EntityMetadataSelectionTool
    {
        private readonly IEntityMetadataService m_metadata;
        private readonly EntityRectangleSelectionTool m_selection;
        private string? m_groupId;

        internal EntityMetadataSelectionTool(IEntitiesManager entities, IEntityMetadataService metadata)
        {
            if (entities is null)
            {
                throw new ArgumentNullException(nameof(entities));
            }
            m_metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            m_selection = new EntityRectangleSelectionTool(
                () => EnumerateCandidates(entities),
                entity => !entity.IsDestroyed,
                ApplyGroup);
        }

        internal bool IsActive => m_selection.IsActive;

        internal string Activate(string groupId)
        {
            if (!m_metadata.TryGetGroup(groupId, out EntityMetadataGroup? group) || group is null)
            {
                return "Metadata group does not exist.";
            }
            if (group.Locked)
            {
                return "Metadata group is locked.";
            }

            m_groupId = group.GroupId;
            return m_selection.Activate(
                "Drag a rectangle over entities; release the mouse to assign metadata group '" +
                group.Name + "'. Escape or right-click cancels.");
        }

        internal void UpdateInput() => m_selection.UpdateInput();

        internal void Deactivate()
        {
            m_groupId = null;
            m_selection.Deactivate();
        }

        private void ApplyGroup(IReadOnlyList<IStaticEntity> matches)
        {
            string? groupId = m_groupId;
            if (groupId is null)
            {
                return;
            }

            int changed = 0;
            int failed = 0;
            foreach (IStaticEntity entity in matches)
            {
                var identity = new EntityMetadataIdentity(entity.Id.Value, "proto:" + entity.Prototype.Id.Value);
                if (m_metadata.TryGetEntityMetadata(identity, out EntityMetadataRecord? current) &&
                    string.Equals(current?.GroupId, groupId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (m_metadata.TrySetEntityMetadata(
                        identity,
                        current?.Alias,
                        current?.Note,
                        groupId,
                        out _))
                {
                    changed++;
                }
                else
                {
                    failed++;
                }
            }

            m_groupId = null;
            string suffix = failed == 0 ? string.Empty : "; failed=" + failed;
            SelectionStatus = "Assigned metadata group to " + changed + " entity/entities" + suffix + ".";
        }

        internal string SelectionStatus { get; private set; } = string.Empty;

        private static IEnumerable<IStaticEntity> EnumerateCandidates(IEntitiesManager entities) =>
            entities.GetAllEntitiesOfType<IStaticEntity>();
    }
}
