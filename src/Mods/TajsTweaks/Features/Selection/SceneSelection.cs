// Taj's COI Mods | SceneSelection.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace TajsCOI.Tweaks.Features.Selection
{
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
    internal sealed class SceneSelection<THandle> : IDisposable
    {
        private readonly Func<SceneSelectionPoint, IEnumerable<string>> m_pointQuery;
        private readonly Func<SceneSelectionRectangle, IEnumerable<string>> m_rectangleQuery;
        private readonly PooledHighlightUtility<string, THandle>? m_highlights;
        private readonly List<string> m_selected = new();
        private readonly int m_maximumSelectionCount;
        private bool m_active;
        private bool m_disposed;

        internal SceneSelection(
            Func<SceneSelectionPoint, IEnumerable<string>> pointQuery,
            Func<SceneSelectionRectangle, IEnumerable<string>> rectangleQuery,
            int maximumSelectionCount,
            PooledHighlightUtility<string, THandle>? highlights = null)
        {
            m_pointQuery = pointQuery ?? throw new ArgumentNullException(nameof(pointQuery));
            m_rectangleQuery = rectangleQuery ?? throw new ArgumentNullException(nameof(rectangleQuery));
            m_maximumSelectionCount = Math.Max(1, maximumSelectionCount);
            m_highlights = highlights;
        }

        internal bool IsActive => m_active && !m_disposed;

        internal IReadOnlyList<string> SelectedEntityIds => m_selected;

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
            m_active = true;
            return true;
        }

        internal bool SelectPoint(SceneSelectionPoint point) => Select(m_pointQuery(point));

        internal bool SelectRectangle(SceneSelectionRectangle rectangle) => Select(m_rectangleQuery(rectangle));

        internal bool HandleEscape() => IsActive && Cancel();

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

        private bool Select(IEnumerable<string>? ids)
        {
            if (!IsActive)
            {
                return false;
            }

            var next = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (ids is not null)
            {
                foreach (string? id in ids)
                {
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
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

            next.Sort(StringComparer.Ordinal);
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
                return false;
            }
        }
    }
}
