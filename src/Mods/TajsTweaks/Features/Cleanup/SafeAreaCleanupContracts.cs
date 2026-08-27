// Taj's COI Mods | SafeAreaCleanupContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Tweaks.Features.Cleanup
{
    internal enum SafeAreaCleanupMode
    {
        DisconnectedTransport = 1,
        Products = 2,
    }

    /// <summary>
    ///     Product information shown in a cleanup preview. It deliberately contains no live
    ///     entity or buffer reference: a preview can never keep a scene object alive.
    /// </summary>
    internal readonly struct SafeAreaProductPreview : IEquatable<SafeAreaProductPreview>
    {
        internal SafeAreaProductPreview(string productId, int quantity)
        {
            ProductId = productId ?? string.Empty;
            Quantity = quantity;
        }

        internal string ProductId { get; }
        internal int Quantity { get; }

        public bool Equals(SafeAreaProductPreview other) =>
            string.Equals(ProductId, other.ProductId, StringComparison.Ordinal) && Quantity == other.Quantity;

        public override bool Equals(object? obj) => obj is SafeAreaProductPreview other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ProductId?.GetHashCode() ?? 0) * 397) ^ Quantity;
            }
        }
    }

    /// <summary>
    ///     Runtime-only area selection record. The ID and display metadata are safe to retain
    ///     between preview and commit; the live entity is always resolved again before use.
    /// </summary>
    internal readonly struct SafeAreaSelectionEntry
    {
        internal SafeAreaSelectionEntry(
            int entityId,
            string title,
            IReadOnlyList<SafeAreaProductPreview>? products = null)
        {
            EntityId = entityId;
            Title = title ?? string.Empty;
            Products = products ?? Array.Empty<SafeAreaProductPreview>();
        }

        internal int EntityId { get; }
        internal string Title { get; }
        internal IReadOnlyList<SafeAreaProductPreview> Products { get; }
    }

    internal static class SafeAreaCleanupLimits
    {
        // Keep both selection and command fan-out bounded even when a huge map is visible.
        internal const int MaxSelectedEntities = 512;
        internal const int MaxProductCommands = 1024;
    }

    internal static class SafeAreaCleanupPolicy
    {
        internal const string Confirmation = "CONFIRM";
        internal const string QuickPolicy = "ALLOW-QUICK";

        internal static bool TryValidateCommit(
            bool quickRemove,
            string? confirmation,
            string? policy,
            out string error)
        {
            if (!string.Equals(confirmation?.Trim(), Confirmation, StringComparison.Ordinal))
            {
                error = "No cleanup was queued. Review the preview and repeat with CONFIRM.";
                return false;
            }

            string normalizedPolicy = (policy ?? string.Empty).Trim();
            if (quickRemove && !string.Equals(normalizedPolicy, QuickPolicy, StringComparison.Ordinal))
            {
                error = "Quick cleanup requires policy=ALLOW-QUICK and confirmation=CONFIRM.";
                return false;
            }

            if (!quickRemove && normalizedPolicy.Length != 0 &&
                !string.Equals(normalizedPolicy, "NORMAL", StringComparison.OrdinalIgnoreCase))
            {
                error = "Normal cleanup accepts no policy or policy=NORMAL.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        internal static IReadOnlyList<T> TakeBounded<T>(IEnumerable<T> values, int maximum, out bool truncated)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            if (maximum <= 0)
            {
                truncated = values.Any();
                return Array.Empty<T>();
            }

            List<T> result = values.Take(maximum + 1).ToList();
            truncated = result.Count > maximum;
            if (truncated)
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }
    }
}
