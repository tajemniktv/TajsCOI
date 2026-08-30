// Taj's COI Mods | SceneAreaSelection.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Tweaks.Features.Selection
{
    /// <summary>
    ///     A bounded, value-only rectangle used by scene tools. The selection is deliberately
    ///     independent of Unity input and renderer objects so it can be reused by tools that
    ///     obtain their candidates from a native spatial probe.
    /// </summary>
    internal readonly struct SceneAreaBounds : IEquatable<SceneAreaBounds>
    {
        internal SceneAreaBounds(int minX, int minY, int maxX, int maxY)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        internal int MinX { get; }
        internal int MinY { get; }
        internal int MaxX { get; }
        internal int MaxY { get; }

        internal long Area => (long)MaxX - MinX + 1L <= 0 || (long)MaxY - MinY + 1L <= 0
            ? 0
            : ((long)MaxX - MinX + 1L) * ((long)MaxY - MinY + 1L);

        internal bool Contains(int x, int y) =>
            x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        internal bool IsWithin(int maxWidth, int maxHeight, int maxCells)
        {
            long width = (long)MaxX - MinX + 1L;
            long height = (long)MaxY - MinY + 1L;
            return width > 0 && height > 0 && width <= maxWidth && height <= maxHeight &&
                   width * height <= maxCells;
        }

        public bool Equals(SceneAreaBounds other) =>
            MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY;

        public override bool Equals(object? obj) => obj is SceneAreaBounds other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MinX;
                hash = hash * 397 ^ MinY;
                hash = hash * 397 ^ MaxX;
                return hash * 397 ^ MaxY;
            }
        }

        public static bool operator ==(SceneAreaBounds left, SceneAreaBounds right) => left.Equals(right);

        public static bool operator !=(SceneAreaBounds left, SceneAreaBounds right) => !left.Equals(right);
    }

    /// <summary>
    ///     Computes entered/exited values for a scene-owned set without retaining the candidates
    ///     between scenes. This is the common diff primitive used by previews and terrain ranges.
    /// </summary>
    internal static class SceneSelectionDiff
    {
        internal static void Compute<T>(
            IEnumerable<T>? previous,
            IEnumerable<T>? current,
            IEqualityComparer<T>? comparer,
            out IReadOnlyList<T> entered,
            out IReadOnlyList<T> exited)
        {
            comparer ??= EqualityComparer<T>.Default;
            var oldSet = new HashSet<T>(comparer);
            var newSet = new HashSet<T>(comparer);
            if (previous is not null)
            {
                foreach (T item in previous)
                {
                    oldSet.Add(item);
                }
            }
            if (current is not null)
            {
                foreach (T item in current)
                {
                    newSet.Add(item);
                }
            }

            var added = new List<T>();
            foreach (T item in newSet)
            {
                if (!oldSet.Contains(item))
                {
                    added.Add(item);
                }
            }

            var removed = new List<T>();
            foreach (T item in oldSet)
            {
                if (!newSet.Contains(item))
                {
                    removed.Add(item);
                }
            }

            entered = added;
            exited = removed;
        }
    }

    /// <summary>
    ///     Bounded world-rectangle ID query for scene tools that do not use the screen drag
    ///     adapter. It retains only stable integer EntityId.Value keys and keeps source inspection
    ///     bounded even when the native scene index is unavailable.
    /// </summary>
    internal static class SceneSelectionWorldQuery
    {
        internal static IReadOnlyList<int> SelectIds<T>(
            IEnumerable<T>? source,
            SceneAreaBounds bounds,
            Func<T, bool> intersects,
            Func<T, int> idSelector,
            int maximumSelectionCount,
            out bool truncated)
        {
            IReadOnlyList<T> selected = SelectItems(source, bounds, intersects, idSelector, maximumSelectionCount, out truncated);
            var ids = selected.Select(idSelector).ToList();
            ids.Sort();
            return ids;
        }

        internal static IReadOnlyList<T> SelectItems<T>(
            IEnumerable<T>? source,
            SceneAreaBounds bounds,
            Func<T, bool> intersects,
            Func<T, int> idSelector,
            int maximumSelectionCount,
            out bool truncated)
        {
            if (intersects is null)
            {
                throw new ArgumentNullException(nameof(intersects));
            }
            if (idSelector is null)
            {
                throw new ArgumentNullException(nameof(idSelector));
            }

            truncated = false;
            int maximum = Math.Max(1, maximumSelectionCount);
            int maximumInspected = maximum > int.MaxValue / 16 ? int.MaxValue : maximum * 16;
            var selected = new List<T>();
            var seen = new HashSet<int>();
            if (source is null)
            {
                return selected;
            }

            try
            {
                int inspected = 0;
                foreach (T item in source)
                {
                    if (++inspected > maximumInspected)
                    {
                        truncated = true;
                        break;
                    }
                    if (!intersects(item))
                    {
                        continue;
                    }

                    int id = idSelector(item);
                    if (seen.Add(id))
                    {
                        selected.Add(item);
                    }
                }
            }
            catch
            {
                truncated = true;
            }

            if (selected.Count > maximum)
            {
                selected.RemoveRange(maximum, selected.Count - maximum);
                truncated = true;
            }
            return selected;
        }
    }

    /// <summary>
    ///     Small reusable pool for highlight handles. A scene owner supplies acquire/apply/release
    ///     callbacks for the native renderer; this class never creates persistent Unity objects and
    ///     always releases every handle on clear or disposal.
    /// </summary>
    internal sealed class PooledHighlightUtility<TKey, THandle> : IDisposable
    {
        private readonly Func<THandle> m_acquire;
        private readonly Action<THandle, TKey> m_apply;
        private readonly Action<THandle> m_release;
        private readonly Dictionary<TKey, THandle> m_active;
        private bool m_disposed;

        internal PooledHighlightUtility(
            Func<THandle> acquire,
            Action<THandle, TKey> apply,
            Action<THandle> release,
            IEqualityComparer<TKey>? comparer = null)
        {
            m_acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
            m_apply = apply ?? throw new ArgumentNullException(nameof(apply));
            m_release = release ?? throw new ArgumentNullException(nameof(release));
            m_active = new Dictionary<TKey, THandle>(comparer ?? EqualityComparer<TKey>.Default);
        }

        internal int Count => m_active.Count;

        internal void Set(IEnumerable<TKey> keys)
        {
            if (m_disposed)
            {
                return;
            }

            var requested = new HashSet<TKey>(keys ?? Array.Empty<TKey>(), m_active.Comparer);
            var removed = new List<TKey>();
            foreach (TKey key in m_active.Keys)
            {
                if (!requested.Contains(key))
                {
                    removed.Add(key);
                }
            }
            foreach (TKey key in removed)
            {
                Release(key);
            }
            foreach (TKey key in requested)
            {
                if (m_active.ContainsKey(key))
                {
                    continue;
                }
                THandle handle = m_acquire();
                try
                {
                    m_apply(handle, key);
                    m_active.Add(key, handle);
                }
                catch
                {
                    m_release(handle);
                    throw;
                }
            }
        }

        internal void Clear()
        {
            if (m_disposed)
            {
                return;
            }
            foreach (TKey key in new List<TKey>(m_active.Keys))
            {
                Release(key);
            }
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            Clear();
            m_disposed = true;
        }

        private void Release(TKey key)
        {
            if (!m_active.TryGetValue(key, out THandle handle))
            {
                return;
            }
            m_active.Remove(key);
            try
            {
                m_release(handle);
            }
            catch
            {
                // Renderer teardown is fail-open; never strand the remaining handles.
            }
        }
    }
}
