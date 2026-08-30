// Taj's COI Mods | OverclockingSelectionTool.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using TajsCOI.Tweaks.Features.Selection;
using EntityId = Mafi.Core.EntityId;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    ///     Lightweight screen-space group picker. It only enumerates selectable supported entities
    ///     while the user is actively dragging, never from the simulation tick, and applies
    ///     membership on mouse-up.
    /// </summary>
    internal sealed class OverclockingSelectionTool
    {
        private readonly IEntitiesManager m_entities;
        private readonly TajsOverclockingFeature m_feature;
        private readonly EntityRectangleSelectionTool m_selection;
        private int m_groupId = -1;

        internal OverclockingSelectionTool(
            IEntitiesManager entities,
            TajsOverclockingFeature feature,
            System.Func<int, bool>? canSelectId = null)
        {
            m_entities = entities ?? throw new System.ArgumentNullException(nameof(entities));
            m_feature = feature ?? throw new System.ArgumentNullException(nameof(feature));
            m_selection = new EntityRectangleSelectionTool(
                () => EnumerateCandidates(m_entities),
                entity => m_feature.CanControl(entity.Id),
                OnSelectionCompleted,
                maxCandidates: SceneSelectionLimits.MaxInteractiveCandidates,
                canSelectId: canSelectId);
        }

        internal bool IsActive => m_selection.IsActive;

        internal string Activate(int groupId)
        {
            OverclockGroup? group = m_feature.GetGroup(groupId);
            if (group is null)
            {
                return "Group " + groupId + " does not exist.";
            }

            if (group.Locked)
            {
                return "Group " + groupId + " is locked.";
            }

            m_groupId = groupId;
            return m_selection.Activate(
                "Drag a rectangle over machines, belts, and pipes; release the mouse to add them to group " +
                groupId + ". Escape or right-click cancels.");
        }

        internal void UpdateInput() => m_selection.UpdateInput();

        internal void Deactivate() => m_selection.Deactivate();

        private void OnSelectionCompleted(IReadOnlyList<IStaticEntity> matches)
        {
            int queued = 0;
            foreach (IStaticEntity entity in matches)
            {
                if (m_feature.QueueAddToGroup(m_groupId, new EntityId(entity.Id.Value), out _))
                {
                    queued++;
                }
            }
            m_feature.SelectionStatus = "Queued " + queued + " entity/entities for group " + m_groupId + ".";
        }

        private static IEnumerable<IStaticEntity> EnumerateCandidates(IEntitiesManager entities)
        {
            foreach (IStaticEntity entity in entities.GetAllEntitiesOfType<IStaticEntity>())
            {
                yield return entity;
            }
        }
    }
}
