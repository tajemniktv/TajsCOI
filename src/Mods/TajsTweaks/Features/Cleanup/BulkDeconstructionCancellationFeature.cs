// Taj's COI Mods | BulkDeconstructionCancellationFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Input;
using Mafi.Unity.Entities;
using TajsCOI.Tweaks.Features.Selection;
using UnityEngine;
using CoreEntityId = Mafi.Core.EntityId;

namespace TajsCOI.Tweaks.Features.Cleanup
{
    /// <summary>
    ///     Scene-owned picker for cancelling deconstruction. It reuses the common rectangle input
    ///     service, retains only IDs for the preview, and schedules the native toggle command in
    ///     bounded chunks after commit.
    /// </summary>
    internal sealed class BulkDeconstructionCancellationFeature : IDisposable
    {
        internal const string ComponentId = "BulkDeconstructionCancellation";
        private static readonly ColorRgba s_highlightColor = new(255, 185, 40, 160);

        private readonly IEntitiesManager m_entities;
        private readonly IInputScheduler m_scheduler;
        private readonly EntitiesRenderingManager? m_rendering;
        private readonly EntityRectangleSelectionTool m_selection;
        private readonly List<BulkDeconstructionSelectionEntry> m_pending = new();
        private readonly Queue<int> m_queued = new();
        private readonly PooledHighlightUtility<int, HighlightLease>? m_highlightPool;
        private bool m_truncated;
        private int m_lastStaleCount;
        private bool m_disposed;

        internal BulkDeconstructionCancellationFeature(
            IEntitiesManager entities,
            IInputScheduler scheduler,
            EntitiesRenderingManager? rendering = null,
            Func<int, bool>? canSelectId = null)
        {
            m_entities = entities ?? throw new ArgumentNullException(nameof(entities));
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            m_rendering = rendering;
            m_highlightPool = m_rendering is null
                ? null
                : new PooledHighlightUtility<int, HighlightLease>(
                    () => new HighlightLease(),
                    (lease, id) =>
                    {
                        if (!m_entities.TryGetEntity<IStaticEntity>(new CoreEntityId(id), out IStaticEntity? entity) ||
                            entity is null || !BulkDeconstructionCancellationPolicy.IsDeconstructing(entity) ||
                            entity is not IRenderedEntity rendered)
                        {
                            throw new InvalidOperationException("The deconstruction highlight entity is unavailable.");
                        }

                        lease.Handle = m_rendering.AddHighlight(rendered, s_highlightColor);
                        if (lease.Handle == 0)
                        {
                            throw new InvalidOperationException("The renderer returned an empty highlight handle.");
                        }
                    },
                    lease =>
                    {
                        if (lease.Handle != 0)
                        {
                            m_rendering.RemoveHighlight(lease.Handle);
                            lease.Handle = 0;
                        }
                    });
            m_selection = new EntityRectangleSelectionTool(
                EnumerateCandidates,
                BulkDeconstructionCancellationPolicy.IsDeconstructing,
                OnSelectionCompleted,
                UpdatePreviewHighlights,
                IsInSelectionGeometry,
                BulkDeconstructionCancellationLimits.MaxSelectedEntities,
                OnSelectionCancelled,
                IsInTileBounds,
                canSelectId);
        }

        internal bool IsActive => m_selection.IsActive;
        internal IReadOnlyList<BulkDeconstructionSelectionEntry> PendingSelection => m_pending;
        internal int QueuedCount => m_queued.Count;
        internal bool HasActiveFilter => IsActive || m_pending.Count != 0 || m_queued.Count != 0;

        internal string Activate()
        {
            ThrowIfDisposed();
            m_pending.Clear();
            m_truncated = false;
            m_lastStaleCount = 0;
            ClearHighlights();
            return m_selection.Activate(
                "Drag over pending/in-progress deconstruction to cancel it. Escape or right-click cancels.");
        }

        internal void UpdateInput()
        {
            if (m_disposed)
            {
                return;
            }
            m_selection.UpdateInput();
            ProcessQueuedCommands(BulkDeconstructionCancellationLimits.CommandsPerRenderUpdate);
        }

        internal void Deactivate()
        {
            if (m_disposed)
            {
                return;
            }
            m_selection.Deactivate();
            m_pending.Clear();
            m_truncated = false;
            ClearHighlights();
        }

        private void OnSelectionCancelled()
        {
            // Mouse Escape/right-click also cancels the reviewed post-drag preview. The shared
            // picker owns the transient drag state; this owner releases the pending value-only
            // plan and its renderer handles.
            m_pending.Clear();
            m_truncated = false;
            ClearHighlights();
        }

        internal string BuildAreaPreview(SceneAreaBounds bounds)
        {
            ThrowIfDisposed();
            if (!bounds.IsWithin(256, 256, 65536))
            {
                return "Area rejected: bounds must be ordered and no larger than 256x256 tiles.";
            }

            // The shared scene selector owns bounded candidate capture, stable IDs, and the
            // temporary coordinator session for world-area callers as well as screen drags.
            IStaticEntity[] matches = m_selection.SelectWorldRectangle(bounds).ToArray();
            OnSelectionCompleted(matches);
            m_truncated |= m_selection.LastCandidateSnapshotTruncated;
            return Status();
        }

        internal string Status()
        {
            if (m_disposed)
            {
                return "Bulk cancel-deconstruction is unavailable in this scene.";
            }
            string capped = m_truncated
                ? "; selection capped at " + BulkDeconstructionCancellationLimits.MaxSelectedEntities
                : string.Empty;
            return "Bulk cancel-deconstruction: selected=" + m_pending.Count +
                   ", queued=" + m_queued.Count + ", stale=" + m_lastStaleCount + capped + ".";
        }

        /// <summary>
        ///     Re-resolves every preview ID and verifies the native state immediately before it is
        ///     queued. Processing repeats the check before each command, covering changes between
        ///     commit and a later chunk without inventing a second construction-state machine.
        /// </summary>
        internal string Commit()
        {
            ThrowIfDisposed();
            if (m_pending.Count == 0)
            {
                return "Bulk cancel-deconstruction has no pending selection.";
            }

            int queued = 0;
            int stale = 0;
            foreach (BulkDeconstructionSelectionEntry entry in m_pending)
            {
                if (!TryResolveDeconstructing(entry.EntityId, out _))
                {
                    stale++;
                    continue;
                }

                m_queued.Enqueue(entry.EntityId);
                queued++;
            }

            m_lastStaleCount = stale;
            m_pending.Clear();
            m_selection.Deactivate();
            ClearHighlights();
            return "Bulk cancel-deconstruction queued " + queued + " command(s); skipped stale entries=" + stale + ".";
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            Deactivate();
            m_queued.Clear();
            m_highlightPool?.Dispose();
            m_disposed = true;
        }

        private IEnumerable<IStaticEntity> EnumerateCandidates()
        {
            foreach (IStaticEntity entity in m_entities.GetAllEntitiesOfType<IStaticEntity>())
            {
                // Query construction state up front so the shared selector snapshots only
                // relevant objects at drag start. The selector still rechecks the predicate while
                // the drag is in progress.
                if (BulkDeconstructionCancellationPolicy.IsDeconstructing(entity))
                {
                    yield return entity;
                }
            }
        }

        private void OnSelectionCompleted(IReadOnlyList<IStaticEntity> matches)
        {
            m_pending.Clear();
            IReadOnlyList<IStaticEntity> bounded = BulkDeconstructionCancellationPolicy.TakeBounded(
                matches.Where(BulkDeconstructionCancellationPolicy.IsDeconstructing)
                    .OrderBy(entity => entity.Id.Value),
                BulkDeconstructionCancellationLimits.MaxSelectedEntities,
                out bool truncated);
            m_truncated = truncated || m_selection.LastCandidateSnapshotTruncated;
            foreach (IStaticEntity entity in bounded)
            {
                m_pending.Add(new BulkDeconstructionSelectionEntry(entity.Id.Value, entity.GetTitle()));
            }
            // The shared picker clears its transient drag matches on mouse-up. Reapply the
            // bounded preview here so the reviewed selection remains visibly highlighted until
            // commit/cancel/scene teardown releases the handles.
            UpdatePreviewHighlights(bounded);
        }

        private void UpdatePreviewHighlights(IReadOnlyList<IStaticEntity> matches)
        {
            if (m_disposed)
            {
                return;
            }

            var requested = new HashSet<int>(
                matches.Where(entity => BulkDeconstructionCancellationPolicy.IsDeconstructing(entity) &&
                                        entity is IRenderedEntity)
                    .Select(entity => entity.Id.Value));
            if (m_highlightPool is null)
            {
                return;
            }
            try
            {
                m_highlightPool.Set(requested);
            }
            catch
            {
                // Preview highlighting is optional and must never affect selection.
                m_highlightPool.Clear();
            }
        }

        private void ProcessQueuedCommands(int budget)
        {
            int processed = 0;
            while (processed++ < budget && m_queued.Count != 0)
            {
                int id = m_queued.Dequeue();
                if (!TryResolveDeconstructing(id, out IStaticEntity? entity))
                {
                    m_lastStaleCount++;
                    continue;
                }
                try
                {
                    // This is the native save-aware cancel/toggle path used by the game's
                    // construction inspector; no direct construction mutation occurs here.
                    m_scheduler.ScheduleInputCmd(new ToggleStaticEntityConstructionCmd(entity!.Id));
                }
                catch
                {
                    // A scene teardown can invalidate the scheduler between checks. Leave the
                    // remaining queue untouched and fail open for this frame.
                    m_lastStaleCount++;
                    break;
                }
            }
        }

        private bool TryResolveDeconstructing(int id, out IStaticEntity? entity)
        {
            if (!m_entities.TryGetEntity<IStaticEntity>(new CoreEntityId(id), out entity) || entity is null)
            {
                return false;
            }
            return BulkDeconstructionCancellationPolicy.IsDeconstructing(entity);
        }

        private void ClearHighlights()
        {
            m_highlightPool?.Clear();
        }

        private sealed class HighlightLease
        {
            internal ulong Handle;
        }

        private static bool IsInSelectionGeometry(
            IStaticEntity entity,
            Camera camera,
            float minX,
            float maxX,
            float minY,
            float maxY)
        {
            if (entity is Transport transport)
            {
                bool hadPivot = false;
                Vector2? previous = null;
                foreach (Tile3i pivot in transport.Trajectory.Pivots)
                {
                    hadPivot = true;
                    Vector2 screen = Project(camera, pivot);
                    if (IsInside(screen, minX, maxX, minY, maxY) ||
                        previous.HasValue && SegmentIntersects(previous.Value, screen, minX, maxX, minY, maxY))
                    {
                        return true;
                    }
                    previous = screen;
                }
                if (hadPivot)
                {
                    return false;
                }
            }

            Tile3i tile = entity.CenterTile;
            return IsInside(Project(camera, tile), minX, maxX, minY, maxY);
        }

        private static bool IsInTileBounds(IStaticEntity entity, SceneAreaBounds bounds)
        {
            if (entity is Transport transport)
            {
                Vector2? previous = null;
                foreach (Tile3i pivot in transport.Trajectory.Pivots)
                {
                    Vector2 current = new(pivot.X, pivot.Y);
                    if (bounds.Contains((int)current.x, (int)current.y) ||
                        previous.HasValue && SegmentIntersects(
                            previous.Value,
                            current,
                            bounds.MinX,
                            bounds.MaxX,
                            bounds.MinY,
                            bounds.MaxY))
                    {
                        return true;
                    }
                    previous = current;
                }
                return false;
            }
            return bounds.Contains(entity.CenterTile.X, entity.CenterTile.Y);
        }

        private static Vector2 Project(Camera camera, Tile3i tile)
        {
            Vector3 screen = camera.WorldToScreenPoint(new Vector3(tile.X * 2f, tile.Z * 2f, tile.Y * 2f));
            return screen.z < 0f
                ? new Vector2(float.NaN, float.NaN)
                : new Vector2(screen.x, Screen.height - screen.y);
        }

        private static bool IsInside(Vector2 point, float minX, float maxX, float minY, float maxY) =>
            !float.IsNaN(point.x) && point.x >= minX && point.x <= maxX &&
            point.y >= minY && point.y <= maxY;

        private static bool SegmentIntersects(Vector2 start, Vector2 end, float minX, float maxX, float minY, float maxY)
        {
            if (float.IsNaN(start.x) || float.IsNaN(end.x))
            {
                return false;
            }
            if (IsInside(start, minX, maxX, minY, maxY) || IsInside(end, minX, maxX, minY, maxY))
            {
                return true;
            }

            float dx = end.x - start.x;
            float dy = end.y - start.y;
            float t0 = 0f;
            float t1 = 1f;
            return Clip(-dx, start.x - minX, ref t0, ref t1) &&
                   Clip(dx, maxX - start.x, ref t0, ref t1) &&
                   Clip(-dy, start.y - minY, ref t0, ref t1) &&
                   Clip(dy, maxY - start.y, ref t0, ref t1);
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Math.Abs(p) < 0.00001f)
            {
                return q >= 0f;
            }
            float ratio = q / p;
            if (p < 0f)
            {
                if (ratio > t1)
                {
                    return false;
                }
                if (ratio > t0)
                {
                    t0 = ratio;
                }
            }
            else
            {
                if (ratio < t0)
                {
                    return false;
                }
                if (ratio < t1)
                {
                    t1 = ratio;
                }
            }
            return true;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(BulkDeconstructionCancellationFeature));
            }
        }
    }
}
