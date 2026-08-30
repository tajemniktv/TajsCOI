// Taj's COI Mods | WorldManagementContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TajsCOI.Tweaks.Features.World
{
    internal enum WorldEntityKind
    {
        Unknown,
        Settlement,
        Mine,
        Fleet,
        Wreck,
        Other,
    }

    /// <summary>Immutable, discovered-only projection used by the world operations table.</summary>
    internal sealed class WorldEntitySnapshot
    {
        internal WorldEntitySnapshot(
            int id,
            WorldEntityKind kind,
            string name,
            int x,
            int y,
            string status,
            bool owned,
            double? knownQuantity,
            string? prototypeId = null)
        {
            Id = id;
            Kind = kind;
            Name = name?.Trim() ?? string.Empty;
            X = x;
            Y = y;
            Status = status?.Trim() ?? string.Empty;
            Owned = owned;
            KnownQuantity = knownQuantity;
            PrototypeId = prototypeId?.Trim() ?? string.Empty;
        }

        internal int Id { get; }
        internal WorldEntityKind Kind { get; }
        internal string Name { get; }
        internal int X { get; }
        internal int Y { get; }
        internal string Status { get; }
        internal bool Owned { get; }
        internal double? KnownQuantity { get; }
        internal string PrototypeId { get; }

        internal string SafeField(string field) =>
            field switch
            {
                "id" => Id.ToString(CultureInfo.InvariantCulture),
                "kind" => Kind.ToString(),
                "name" => Name,
                "status" => Status,
                "prototype" => PrototypeId,
                "x" => X.ToString(CultureInfo.InvariantCulture),
                "y" => Y.ToString(CultureInfo.InvariantCulture),
                _ => string.Empty,
            };
    }

    internal enum WorldEntitySortField
    {
        Name,
        Kind,
        Status,
        Id,
        X,
        Y,
        Distance,
    }

    internal sealed class WorldEntityQuery
    {
        internal string Search { get; set; } = string.Empty;
        internal WorldEntityKind? Kind { get; set; }
        internal WorldEntitySortField SortBy { get; set; } = WorldEntitySortField.Name;
        internal bool Descending { get; set; }
    }

    /// <summary>Builds table rows from a point-in-time snapshot; never retains live entities.</summary>
    internal static class WorldEntityBrowser
    {
        internal static IReadOnlyList<WorldEntitySnapshot> Snapshot(IEnumerable<WorldEntitySnapshot> discovered)
        {
            if (discovered is null)
            {
                return Array.Empty<WorldEntitySnapshot>();
            }

            return discovered
                .Where(row => row is not null)
                .GroupBy(row => row.Id)
                .Select(group => group.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).First())
                .OrderBy(row => row.Id)
                .ToArray();
        }

        internal static IReadOnlyList<WorldEntitySnapshot> Query(
            IEnumerable<WorldEntitySnapshot> snapshot,
            WorldEntityQuery? query)
        {
            IEnumerable<WorldEntitySnapshot> rows = snapshot ?? Array.Empty<WorldEntitySnapshot>();
            query ??= new WorldEntityQuery();
            string search = query.Search?.Trim() ?? string.Empty;
            if (query.Kind.HasValue)
            {
                rows = rows.Where(row => row.Kind == query.Kind.Value);
            }
            if (search.Length > 0)
            {
                rows = rows.Where(row =>
                    row.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.Status.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.PrototypeId.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.Id.ToString(CultureInfo.InvariantCulture).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            Func<WorldEntitySnapshot, object> key = query.SortBy switch
            {
                WorldEntitySortField.Kind => row => row.Kind,
                WorldEntitySortField.Status => row => row.Status,
                WorldEntitySortField.Id => row => row.Id,
                WorldEntitySortField.X => row => row.X,
                WorldEntitySortField.Y => row => row.Y,
                WorldEntitySortField.Distance => row => (long)row.X * row.X + (long)row.Y * row.Y,
                _ => row => row.Name,
            };

            IOrderedEnumerable<WorldEntitySnapshot> ordered = query.Descending
                ? rows.OrderByDescending(key)
                : rows.OrderBy(key);
            return ordered.ThenBy(row => row.Id).ToArray();
        }
    }

    internal readonly struct MapViewportBounds
    {
        internal MapViewportBounds(float minX, float maxX, float minY, float maxY)
        {
            MinX = Math.Min(minX, maxX);
            MaxX = Math.Max(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxY = Math.Max(minY, maxY);
        }

        internal float MinX { get; }
        internal float MaxX { get; }
        internal float MinY { get; }
        internal float MaxY { get; }
        internal float Width => MaxX - MinX;
        internal float Height => MaxY - MinY;
    }

    internal readonly struct MapPanBounds
    {
        internal MapPanBounds(float minX, float maxX, float minY, float maxY)
        {
            MinX = Math.Min(minX, maxX);
            MaxX = Math.Max(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxY = Math.Max(minY, maxY);
        }

        internal float MinX { get; }
        internal float MaxX { get; }
        internal float MinY { get; }
        internal float MaxY { get; }
    }

    /// <summary>Pure map sizing math. Base extents are immutable; no cumulative zoom/constant edits.</summary>
    internal static class MapViewportMath
    {
        internal static MapViewportBounds ActualExtents(IEnumerable<(float X, float Y)> points, float padding)
        {
            if (points is null)
            {
                return new MapViewportBounds(-padding, padding, -padding, padding);
            }
            (float X, float Y)[] values = points.ToArray();
            if (values.Length == 0)
            {
                return new MapViewportBounds(-padding, padding, -padding, padding);
            }
            float minX = values.Min(point => point.X);
            float maxX = values.Max(point => point.X);
            float minY = values.Min(point => point.Y);
            float maxY = values.Max(point => point.Y);
            return new MapViewportBounds(
                minX - Math.Max(0f, padding),
                maxX + Math.Max(0f, padding),
                minY - Math.Max(0f, padding),
                maxY + Math.Max(0f, padding));
        }

        internal static float DeriveMinimumZoom(MapViewportBounds extents, float viewportWidth, float viewportHeight, float baseZoom)
        {
            if (viewportWidth <= 0f || viewportHeight <= 0f || baseZoom <= 0f)
            {
                return Math.Max(0.0001f, baseZoom);
            }
            float scale = Math.Max(extents.Width / viewportWidth, extents.Height / viewportHeight);
            return Math.Max(0.0001f, baseZoom * Math.Max(1f, scale));
        }

        internal static MapPanBounds DerivePanBounds(MapViewportBounds extents, float padding)
        {
            float safePadding = Math.Max(0f, padding);
            return new MapPanBounds(
                extents.MinX - safePadding,
                extents.MaxX + safePadding,
                extents.MinY - safePadding,
                extents.MaxY + safePadding);
        }
    }

    internal static class MineDepletionClassifier
    {
        internal static bool IsDepleted(bool owned, double? finiteKnownQuantity)
        {
            return owned && finiteKnownQuantity.HasValue &&
                   !double.IsNaN(finiteKnownQuantity.Value) &&
                   !double.IsInfinity(finiteKnownQuantity.Value) &&
                   finiteKnownQuantity.Value == 0d;
        }
    }
}
