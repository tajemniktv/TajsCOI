// Taj's COI Mods | EntityRectangleSelectionTool.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core.Entities.Static;
using UnityEngine;

namespace TajsCOI.Tweaks.Features.Selection
{
    /// <summary>
    ///     Shared screen-space rectangle selection lifecycle. Feature owners provide the current
    ///     candidate snapshot, filtering predicate, and mouse-up action; this class owns input,
    ///     camera projection, de-duplication, and fail-open cancellation.
    /// </summary>
    internal sealed class EntityRectangleSelectionTool
    {
        private readonly Func<IEnumerable<IStaticEntity>> m_candidates;
        private readonly Func<IStaticEntity, bool> m_canSelect;
        private readonly Action<IReadOnlyList<IStaticEntity>> m_onCompleted;
        private readonly List<IStaticEntity> m_matches = new();
        private Vector2 m_start;
        private Vector2 m_current;
        private bool m_dragging;

        internal EntityRectangleSelectionTool(
            Func<IEnumerable<IStaticEntity>> candidates,
            Func<IStaticEntity, bool> canSelect,
            Action<IReadOnlyList<IStaticEntity>> onCompleted)
        {
            m_candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            m_canSelect = canSelect ?? throw new ArgumentNullException(nameof(canSelect));
            m_onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        }

        internal bool IsActive { get; private set; }

        internal string Activate(string instruction)
        {
            IsActive = true;
            m_dragging = false;
            m_matches.Clear();
            return instruction ?? string.Empty;
        }

        internal void UpdateInput()
        {
            if (!IsActive)
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

            if (m_dragging && Input.GetMouseButton(0) && screen != m_current)
            {
                m_current = screen;
                ComputeMatches();
            }

            if (m_dragging && Input.GetMouseButtonUp(0))
            {
                IReadOnlyList<IStaticEntity> completed = m_matches.ToArray();
                Deactivate();
                try
                {
                    m_onCompleted(completed);
                }
                catch
                {
                    // Optional selection consumers must never interfere with the game input loop.
                }
            }
        }

        internal void Deactivate()
        {
            IsActive = false;
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
            var seen = new HashSet<int>();
            try
            {
                foreach (IStaticEntity entity in m_candidates())
                {
                    if (entity is null || !seen.Add(entity.Id.Value) || !m_canSelect(entity) ||
                        !IsInRect(entity, camera, minX, maxX, minY, maxY))
                    {
                        continue;
                    }
                    m_matches.Add(entity);
                }
            }
            catch
            {
                // Entity enumeration is scene-scoped and can race teardown; retain an empty,
                // conservative selection for this frame.
                m_matches.Clear();
            }
        }

        private static bool IsInRect(
            IStaticEntity entity,
            Camera camera,
            float minX,
            float maxX,
            float minY,
            float maxY)
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
