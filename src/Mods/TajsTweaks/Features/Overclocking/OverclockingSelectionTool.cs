// Taj's COI Mods | OverclockingSelectionTool.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Offices;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Waste;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Transports;
using UnityEngine;
using EntityId = Mafi.Core.EntityId;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    /// Lightweight screen-space group picker. It only enumerates selectable supported entities
    /// while the user is actively dragging, never from the simulation tick, and applies
    /// membership on mouse-up.
    /// </summary>
    internal sealed class OverclockingSelectionTool
    {
        private readonly IEntitiesManager m_entities;
        private readonly TajsOverclockingFeature m_feature;
        private readonly List<int> m_matches = new();
        private int m_groupId = -1;
        private Vector2 m_start;
        private Vector2 m_current;
        private bool m_active;
        private bool m_dragging;

        internal OverclockingSelectionTool(IEntitiesManager entities, TajsOverclockingFeature feature)
        {
            m_entities = entities;
            m_feature = feature;
        }

        internal bool IsActive => m_active;

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
            m_active = true;
            m_dragging = false;
            return "Drag a rectangle over machines, belts, and pipes; release the mouse to add them to group " + groupId + ". Escape or right-click cancels.";
        }

        internal void UpdateInput()
        {
            if (!m_active)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Deactivate();
                return;
            }

            Vector2 screen = new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (!m_dragging && Input.GetMouseButtonDown(0))
            {
                m_start = screen;
                m_current = screen;
                m_dragging = true;
                ComputeMatches();
            }

            if (m_dragging && Input.GetMouseButton(0))
            {
                if (screen != m_current)
                {
                    m_current = screen;
                    ComputeMatches();
                }
            }

            if (m_dragging && Input.GetMouseButtonUp(0))
            {
                int added = 0;
                foreach (int id in m_matches)
                {
                    if (m_feature.AddToGroup(m_groupId, new EntityId(id)))
                    {
                        added++;
                    }
                }

                Deactivate();
                m_feature.SelectionStatus = "Added " + added + " entity/entities to group " + m_groupId + ".";
            }
        }

        internal void Deactivate()
        {
            m_active = false;
            m_dragging = false;
            m_matches.Clear();
        }

        private void ComputeMatches()
        {
            m_matches.Clear();
            Camera? camera = Camera.main;
            if (camera is null)
            {
                return;
            }

            float minX = Mathf.Min(m_start.x, m_current.x);
            float maxX = Mathf.Max(m_start.x, m_current.x);
            float minY = Mathf.Min(m_start.y, m_current.y);
            float maxY = Mathf.Max(m_start.y, m_current.y);
            try
            {
                AddMatches(m_entities.GetAllEntitiesOfType<Machine>(), camera, minX, maxX, minY, maxY);
                AddMatches(m_entities.GetAllEntitiesOfType<OreSortingPlant>(), camera, minX, maxX, minY, maxY);
                AddMatches(m_entities.GetAllEntitiesOfType<OfficeBuilding>(), camera, minX, maxX, minY, maxY);
                AddMatches(m_entities.GetAllEntitiesOfType<WasteSortingPlant>(), camera, minX, maxX, minY, maxY);
                AddMatches(m_entities.GetAllEntitiesOfType<Transport>(), camera, minX, maxX, minY, maxY);
            }
            catch
            {
            }
        }

        private void AddMatches<T>(IEnumerable<T> entities, Camera camera, float minX, float maxX, float minY, float maxY)
            where T : class, IStaticEntity
        {
            foreach (T entity in entities)
            {
                if (m_feature.CanControl(entity.Id) && IsInRect(entity, camera, minX, maxX, minY, maxY))
                {
                    m_matches.Add(entity.Id.Value);
                }
            }
        }

        private static bool IsInRect(IStaticEntity entity, Camera camera, float minX, float maxX, float minY, float maxY)
        {
            Tile3i tile = entity.CenterTile;
            Vector3 position = new(tile.X * 2f, tile.Z * 2f, tile.Y * 2f);
            Vector3 screen = camera.WorldToScreenPoint(position);
            if (screen.z < 0f)
            {
                return false;
            }

            float invertedY = Screen.height - screen.y;
            return screen.x >= minX && screen.x <= maxX && invertedY >= minY && invertedY <= maxY;
        }
    }
}
