// Taj's COI Mods | SceneSelection.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace TajsCOI.Tweaks.Features.Selection
{
    internal interface ISceneSelectionOwner
    {
        void CancelSelection();
    }

    internal static class SceneSelectionCoordinator
    {
        private static readonly object s_gate = new();
        private static WeakReference<object>? s_activeOwner;

        internal static bool TryActivate(object owner)
        {
            if (owner is null)
            {
                return false;
            }

            lock (s_gate)
            {
                if (s_activeOwner?.TryGetTarget(out object? active) == true &&
                    !ReferenceEquals(active, owner))
                {
                    return false;
                }

                s_activeOwner = new WeakReference<object>(owner);
                return true;
            }
        }

        internal static void Deactivate(object owner)
        {
            if (owner is null)
            {
                return;
            }

            lock (s_gate)
            {
                if (s_activeOwner?.TryGetTarget(out object? active) != true ||
                    ReferenceEquals(active, owner))
                {
                    s_activeOwner = null;
                }
            }
        }

        /// <summary>
        ///     Common Escape/right-click cancellation hook. The coordinator retains only the
        ///     owner weakly, so a scene teardown cannot be kept alive by process state.
        /// </summary>
        internal static bool CancelActive()
        {
            lock (s_gate)
            {
                if (s_activeOwner?.TryGetTarget(out object? active) != true)
                {
                    s_activeOwner = null;
                    return false;
                }

                if (active is not ISceneSelectionOwner owner)
                {
                    return false;
                }

                try
                {
                    owner.CancelSelection();
                    return true;
                }
                catch
                {
                    // A teardown race must not strand the coordinator or break the caller's
                    // input loop when an owner cannot complete its visual cleanup.
                    s_activeOwner = null;
                    return false;
                }
            }
        }
    }

    internal static class SceneSelectionLimits
    {
        // A defensive cap for interactive candidate snapshots. Feature-specific operations may
        // choose a stricter limit, but no generic rectangle tool should enumerate unbounded scene
        // state while the user is dragging.
        internal const int MaxInteractiveCandidates = 8192;
    }

    internal readonly struct SceneSelectionPoint
    {
        internal SceneSelectionPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
    }

    internal readonly struct SceneSelectionRectangle
    {
        internal SceneSelectionRectangle(float x0, float z0, float x1, float z1)
        {
            MinX = Math.Min(x0, x1);
            MinZ = Math.Min(z0, z1);
            MaxX = Math.Max(x0, x1);
            MaxZ = Math.Max(z0, z1);
        }

        internal float MinX { get; }
        internal float MinZ { get; }
        internal float MaxX { get; }
        internal float MaxZ { get; }
    }

    /// <summary>
    ///     Scene-scoped selection owner shared by the small set of entity tools that need the same
    ///     lifetime rules. Queries are supplied by the owning native feature; only stable IDs are
    ///     retained here, and highlights are always released on cancel/deactivation/disposal.
    /// </summary>
    internal sealed class SceneSelection<THandle> : IDisposable, ISceneSelectionOwner
    {
        private readonly Func<SceneSelectionPoint, IEnumerable<int>> m_pointQuery;
        private readonly Func<SceneSelectionRectangle, IEnumerable<int>> m_rectangleQuery;
        private readonly Func<SceneSelectionPoint, IEnumerable<int>>? m_worldPointQuery;
        private readonly Func<SceneAreaBounds, IEnumerable<int>>? m_worldRectangleQuery;
        private readonly PooledHighlightUtility<int, THandle>? m_highlights;
        private readonly List<int> m_selected = new();
        private readonly int m_maximumSelectionCount;
        private bool m_active;
        private bool m_disposed;

        internal SceneSelection(
            Func<SceneSelectionPoint, IEnumerable<int>> pointQuery,
            Func<SceneSelectionRectangle, IEnumerable<int>> rectangleQuery,
            int maximumSelectionCount,
            PooledHighlightUtility<int, THandle>? highlights = null,
            Func<SceneSelectionPoint, IEnumerable<int>>? worldPointQuery = null,
            Func<SceneAreaBounds, IEnumerable<int>>? worldRectangleQuery = null)
        {
            m_pointQuery = pointQuery ?? throw new ArgumentNullException(nameof(pointQuery));
            m_rectangleQuery = rectangleQuery ?? throw new ArgumentNullException(nameof(rectangleQuery));
            m_maximumSelectionCount = Math.Max(1, maximumSelectionCount);
            m_highlights = highlights;
            m_worldPointQuery = worldPointQuery;
            m_worldRectangleQuery = worldRectangleQuery;
        }

        internal bool IsActive => m_active && !m_disposed;

        /// <summary>Stable native EntityId.Value keys retained for the active scene session.</summary>
        internal IReadOnlyList<int> SelectedEntityIds => m_selected;

        internal bool LastQueryTruncated { get; private set; }

        internal bool Activate()
        {
            if (m_disposed)
            {
                return false;
            }

            if (!SceneSelectionCoordinator.TryActivate(this))
            {
                return false;
            }

            // Reusing a tool starts a fresh selection session. Do not expose IDs or renderer
            // handles from the previous session while the owner is preparing its next query.
            m_selected.Clear();
            m_highlights?.Clear();
            LastQueryTruncated = false;
            m_active = true;
            return true;
        }

        internal bool SelectPoint(SceneSelectionPoint point)
        {
            if (!IsActive)
            {
                return false;
            }

            try
            {
                return Select(m_pointQuery(point));
            }
            catch
            {
                // Native world queries are compatibility seams. A failed query must release
                // temporary highlights and relinquish the shared tool owner immediately.
                Cancel();
                return false;
            }
        }

        internal bool SelectRectangle(SceneSelectionRectangle rectangle)
        {
            if (!IsActive)
            {
                return false;
            }

            try
            {
                return Select(m_rectangleQuery(rectangle));
            }
            catch
            {
                Cancel();
                return false;
            }
        }

        internal bool SelectWorldPoint(SceneSelectionPoint point)
        {
            if (!IsActive || m_worldPointQuery is null)
            {
                return false;
            }

            try
            {
                return Select(m_worldPointQuery(point));
            }
            catch
            {
                Cancel();
                return false;
            }
        }

        internal bool SelectWorldRectangle(SceneAreaBounds bounds)
        {
            if (!IsActive || m_worldRectangleQuery is null)
            {
                return false;
            }

            try
            {
                return Select(m_worldRectangleQuery(bounds));
            }
            catch
            {
                Cancel();
                return false;
            }
        }

        internal bool HandleEscape() => IsActive && Cancel();

        public void CancelSelection() => Cancel();

        internal bool Cancel()
        {
            if (m_disposed)
            {
                return false;
            }

            m_active = false;
            SceneSelectionCoordinator.Deactivate(this);
            m_selected.Clear();
            m_highlights?.Clear();
            return true;
        }

        internal void Deactivate() => Cancel();

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            Cancel();
            m_highlights?.Dispose();
            m_disposed = true;
        }

        private bool Select(IEnumerable<int>? ids)
        {
            if (!IsActive)
            {
                return false;
            }

            LastQueryTruncated = false;
            var next = new List<int>();
            var seen = new HashSet<int>();
            int inspected = 0;
            int maximumInspected = m_maximumSelectionCount > int.MaxValue / 16
                ? int.MaxValue
                : m_maximumSelectionCount * 16;
            if (ids is not null)
            {
                foreach (int id in ids)
                {
                    if (++inspected > maximumInspected)
                    {
                        LastQueryTruncated = true;
                        break;
                    }
                    if (!seen.Add(id))
                    {
                        continue;
                    }
                    next.Add(id);
                    if (next.Count == m_maximumSelectionCount)
                    {
                        break;
                    }
                }
            }

            next.Sort();
            try
            {
                m_highlights?.Set(next);
                m_selected.Clear();
                m_selected.AddRange(next);
                return true;
            }
            catch
            {
                m_highlights?.Clear();
                m_selected.Clear();
                // Renderer teardown is a lifecycle boundary. Do not leave a dead selector
                // holding the shared coordinator after a highlight allocation failure.
                Cancel();
                return false;
            }
        }
    }
}
