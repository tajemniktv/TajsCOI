// Taj's COI Mods | EntityRectangleSelectionTool.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly Action<IReadOnlyList<IStaticEntity>>? m_onMatchesChanged;
        private readonly Func<IStaticEntity, Camera, float, float, float, float, bool>? m_geometry;
        private readonly int m_maxCandidates;
        private readonly SceneSelection<int> m_sceneSelection;
        private readonly Dictionary<string, IStaticEntity> m_candidateById = new(StringComparer.Ordinal);
        private readonly List<IStaticEntity> m_candidateSnapshot = new();
        private readonly List<IStaticEntity> m_matches = new();
        private Vector2 m_start;
        private Vector2 m_current;
        private bool m_dragging;

        internal EntityRectangleSelectionTool(
            Func<IEnumerable<IStaticEntity>> candidates,
            Func<IStaticEntity, bool> canSelect,
            Action<IReadOnlyList<IStaticEntity>> onCompleted,
            Action<IReadOnlyList<IStaticEntity>>? onMatchesChanged = null,
            Func<IStaticEntity, Camera, float, float, float, float, bool>? geometry = null,
            int maxCandidates = 0)
        {
            m_candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            m_canSelect = canSelect ?? throw new ArgumentNullException(nameof(canSelect));
            m_onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            m_onMatchesChanged = onMatchesChanged;
            m_geometry = geometry;
            m_maxCandidates = Math.Max(0, maxCandidates);
            m_sceneSelection = new SceneSelection<int>(
                QueryPoint,
                QueryRectangle,
                m_maxCandidates > 0 ? m_maxCandidates : SceneSelectionLimits.MaxInteractiveCandidates);
        }

        internal bool IsActive => m_sceneSelection.IsActive;
        internal bool CandidateSnapshotTruncated { get; private set; }
        internal bool LastCandidateSnapshotTruncated { get; private set; }

        internal string Activate(string instruction)
        {
            if (!m_sceneSelection.Activate())
            {
                return "Another selection tool is already active.";
            }
            m_dragging = false;
            m_candidateById.Clear();
            m_candidateSnapshot.Clear();
            CandidateSnapshotTruncated = false;
            LastCandidateSnapshotTruncated = false;
            m_matches.Clear();
            NotifyMatchesChanged();
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
                CaptureCandidates();
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
            m_sceneSelection.Deactivate();
            m_dragging = false;
            m_candidateById.Clear();
            m_candidateSnapshot.Clear();
            LastCandidateSnapshotTruncated = CandidateSnapshotTruncated;
            CandidateSnapshotTruncated = false;
            m_matches.Clear();
            NotifyMatchesChanged();
        }

        internal void Dispose()
        {
            m_sceneSelection.Dispose();
            m_dragging = false;
            m_candidateById.Clear();
            m_candidateSnapshot.Clear();
            m_matches.Clear();
            NotifyMatchesChanged();
        }

        private void ComputeMatches()
        {
            float minX = Mathf.Min(m_start.x, m_current.x);
            float maxX = Mathf.Max(m_start.x, m_current.x);
            float minY = Mathf.Min(m_start.y, m_current.y);
            float maxY = Mathf.Max(m_start.y, m_current.y);
            try
            {
                m_sceneSelection.SelectRectangle(new SceneSelectionRectangle(minX, minY, maxX, maxY));
                m_matches.Clear();
                foreach (string id in m_sceneSelection.SelectedEntityIds)
                {
                    if (m_candidateById.TryGetValue(id, out IStaticEntity? entity))
                    {
                        m_matches.Add(entity);
                    }
                }
                m_matches.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
            }
            catch
            {
                // Entity enumeration is scene-scoped and can race teardown; retain an empty,
                // conservative selection for this frame.
                m_matches.Clear();
            }
            NotifyMatchesChanged();
        }

        private void CaptureCandidates()
        {
            m_candidateSnapshot.Clear();
            try
            {
                foreach (IStaticEntity entity in m_candidates())
                {
                    if (entity is null)
                    {
                        continue;
                    }

                    string id = StableId(entity.Id.Value);
                    if (m_candidateById.ContainsKey(id))
                    {
                        continue;
                    }
                    int limit = m_maxCandidates > 0 ? m_maxCandidates : SceneSelectionLimits.MaxInteractiveCandidates;
                    if (m_candidateSnapshot.Count >= limit)
                    {
                        CandidateSnapshotTruncated = true;
                        break;
                    }
                    m_candidateSnapshot.Add(entity);
                    m_candidateById.Add(id, entity);
                }
            }
            catch
            {
                // Scene enumeration can race a reload; an empty snapshot is the safe result.
                m_candidateById.Clear();
                m_candidateSnapshot.Clear();
            }
        }

        private IEnumerable<string> QueryPoint(SceneSelectionPoint point)
        {
            foreach (IStaticEntity entity in m_candidateSnapshot)
            {
                Tile3i tile = entity.CenterTile;
                if (tile.X == (int)Math.Round(point.X) &&
                    tile.Y == (int)Math.Round(point.Y) &&
                    tile.Z == (int)Math.Round(point.Z) &&
                    m_canSelect(entity))
                {
                    yield return StableId(entity.Id.Value);
                }
            }
        }

        private IEnumerable<string> QueryRectangle(SceneSelectionRectangle rectangle)
        {
            Camera? camera = Camera.main;
            if (camera is null)
            {
                yield break;
            }

            foreach (IStaticEntity entity in m_candidateSnapshot)
            {
                if (m_canSelect(entity) &&
                    (m_geometry?.Invoke(
                         entity,
                         camera,
                         rectangle.MinX,
                         rectangle.MaxX,
                         rectangle.MinZ,
                         rectangle.MaxZ) ??
                     IsInRect(
                         entity,
                         camera,
                         rectangle.MinX,
                         rectangle.MaxX,
                         rectangle.MinZ,
                         rectangle.MaxZ)))
                {
                    yield return StableId(entity.Id.Value);
                }
            }
        }

        private static string StableId(int id) => id.ToString("D10", CultureInfo.InvariantCulture);

        private void NotifyMatchesChanged()
        {
            if (m_onMatchesChanged is null)
            {
                return;
            }

            try
            {
                m_onMatchesChanged(m_matches.ToArray());
            }
            catch
            {
                // Preview/highlight consumers are optional and must never break input handling.
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
