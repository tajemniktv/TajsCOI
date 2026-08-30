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
        private readonly Action<IReadOnlyList<IStaticEntity>>? m_onMatchesChanged;
        private readonly Action? m_onCancelled;
        private readonly Func<IStaticEntity, Camera, float, float, float, float, bool>? m_geometry;
        private readonly Func<IStaticEntity, SceneAreaBounds, bool>? m_worldGeometry;
        private readonly Func<int, bool>? m_canSelectId;
        private readonly int m_maxCandidates;
        private readonly SceneSelection<int> m_sceneSelection;
        private readonly Dictionary<int, IStaticEntity> m_candidateById = new();
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
            int maxCandidates = 0,
            Action? onCancelled = null,
            Func<IStaticEntity, SceneAreaBounds, bool>? worldGeometry = null,
            Func<int, bool>? canSelectId = null)
        {
            m_candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            m_canSelect = canSelect ?? throw new ArgumentNullException(nameof(canSelect));
            m_onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            m_onMatchesChanged = onMatchesChanged;
            m_geometry = geometry;
            m_maxCandidates = Math.Max(0, maxCandidates);
            m_onCancelled = onCancelled;
            m_worldGeometry = worldGeometry;
            m_canSelectId = canSelectId;
            m_sceneSelection = new SceneSelection<int>(
                QueryPoint,
                QueryRectangle,
                m_maxCandidates > 0 ? m_maxCandidates : SceneSelectionLimits.MaxInteractiveCandidates,
                worldPointQuery: QueryWorldPoint,
                worldRectangleQuery: QueryWorldRectangle);
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
                try
                {
                    m_onCancelled?.Invoke();
                }
                catch
                {
                    // Owner cleanup is optional and must not interfere with the input loop.
                }
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
                bool selected = m_sceneSelection.SelectRectangle(new SceneSelectionRectangle(minX, minY, maxX, maxY));
                bool queryTruncated = m_sceneSelection.LastQueryTruncated;
                if (!selected)
                {
                    // SceneSelection consumes native query failures and cancels itself. Mirror
                    // that terminal state in the screen tool so a subsequent mouse-up cannot
                    // commit a stale preview or retain the drag owner.
                    Deactivate();
                    LastCandidateSnapshotTruncated |= queryTruncated;
                    NotifyCancelled();
                    return;
                }

                LastCandidateSnapshotTruncated |= queryTruncated;
                m_matches.Clear();
                foreach (int id in m_sceneSelection.SelectedEntityIds)
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
                m_sceneSelection.Cancel();
                NotifyCancelled();
            }
            NotifyMatchesChanged();
        }

        /// <summary>
        ///     Runs the same bounded, ID-based selector for a world/tile rectangle. The temporary
        ///     activation makes console and preview callers obey the same cross-tool ownership and
        ///     cleanup rules as the screen drag path.
        /// </summary>
        internal IReadOnlyList<IStaticEntity> SelectWorldRectangle(SceneAreaBounds bounds)
        {
            if (!m_sceneSelection.Activate())
            {
                return Array.Empty<IStaticEntity>();
            }

            m_candidateById.Clear();
            m_candidateSnapshot.Clear();
            m_matches.Clear();
            CandidateSnapshotTruncated = false;
            LastCandidateSnapshotTruncated = false;
            try
            {
                CaptureCandidates();
                if (!m_sceneSelection.SelectWorldRectangle(bounds))
                {
                    NotifyCancelled();
                    return Array.Empty<IStaticEntity>();
                }

                LastCandidateSnapshotTruncated |= m_sceneSelection.LastQueryTruncated;
                foreach (int id in m_sceneSelection.SelectedEntityIds)
                {
                    if (m_candidateById.TryGetValue(id, out IStaticEntity? entity))
                    {
                        m_matches.Add(entity);
                    }
                }
                m_matches.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
                return m_matches.ToArray();
            }
            catch
            {
                m_sceneSelection.Cancel();
                NotifyCancelled();
                return Array.Empty<IStaticEntity>();
            }
            finally
            {
                m_sceneSelection.Deactivate();
                LastCandidateSnapshotTruncated = CandidateSnapshotTruncated;
                CandidateSnapshotTruncated = false;
                m_candidateById.Clear();
                m_candidateSnapshot.Clear();
                m_matches.Clear();
            }
        }

        private void CaptureCandidates()
        {
            m_candidateSnapshot.Clear();
            try
            {
                foreach (IStaticEntity entity in m_candidates())
                {
                    if (entity is null || !IsSelectable(entity))
                    {
                        continue;
                    }

                    int id = entity.Id.Value;
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

        private IEnumerable<int> QueryPoint(SceneSelectionPoint point)
        {
            foreach (IStaticEntity entity in m_candidateSnapshot)
            {
                Tile3i tile = entity.CenterTile;
                if (tile.X == (int)Math.Round(point.X) &&
                    tile.Y == (int)Math.Round(point.Y) &&
                    tile.Z == (int)Math.Round(point.Z) &&
                    IsSelectable(entity))
                {
                    yield return entity.Id.Value;
                }
            }
        }

        private IEnumerable<int> QueryRectangle(SceneSelectionRectangle rectangle)
        {
            Camera? camera = Camera.main;
            if (camera is null)
            {
                yield break;
            }

            foreach (IStaticEntity entity in m_candidateSnapshot)
            {
                if (IsSelectable(entity) &&
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
                    yield return entity.Id.Value;
                }
            }
        }

        private IEnumerable<int> QueryWorldPoint(SceneSelectionPoint point)
        {
            foreach (IStaticEntity entity in m_candidateSnapshot)
            {
                Tile3i tile = entity.CenterTile;
                if (tile.X == (int)Math.Round(point.X) &&
                    tile.Y == (int)Math.Round(point.Y) &&
                    tile.Z == (int)Math.Round(point.Z) &&
                    IsSelectable(entity))
                {
                    yield return entity.Id.Value;
                }
            }
        }

        private IEnumerable<int> QueryWorldRectangle(SceneAreaBounds bounds)
        {
            foreach (IStaticEntity entity in m_candidateSnapshot)
            {
                if (IsSelectable(entity) &&
                    (m_worldGeometry?.Invoke(entity, bounds) ??
                     bounds.Contains(entity.CenterTile.X, entity.CenterTile.Y)))
                {
                    yield return entity.Id.Value;
                }
            }
        }

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

        private bool IsSelectable(IStaticEntity entity) =>
            m_canSelect(entity) && (m_canSelectId?.Invoke(entity.Id.Value) ?? true);

        private void NotifyCancelled()
        {
            try
            {
                m_onCancelled?.Invoke();
            }
            catch
            {
                // Owner cleanup is optional and must not interfere with the input loop.
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
