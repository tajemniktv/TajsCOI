// Taj's COI Mods | ShipyardTransportContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Tweaks.Features.Ships
{
    internal readonly struct ReservedCargo
    {
        internal ReservedCargo(string productId, int amount)
        {
            ProductId = productId?.Trim() ?? string.Empty;
            Amount = Math.Max(0, amount);
        }

        internal string ProductId { get; }
        internal int Amount { get; }
    }

    internal readonly struct OutputPortMetadata
    {
        internal OutputPortMetadata(string outputId, IEnumerable<string> compatibleProducts)
        {
            OutputId = outputId?.Trim() ?? string.Empty;
            CompatibleProducts = new HashSet<string>(compatibleProducts ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        internal string OutputId { get; }
        internal IReadOnlyCollection<string> CompatibleProducts { get; }
    }

    internal readonly struct TransferChoice
    {
        internal TransferChoice(string productId, string outputId, int amount)
        {
            ProductId = productId;
            OutputId = outputId;
            Amount = Math.Max(0, amount);
        }

        internal string ProductId { get; }
        internal string OutputId { get; }
        internal int Amount { get; }
        internal bool IsEmpty => Amount <= 0 || string.IsNullOrEmpty(ProductId) || string.IsNullOrEmpty(OutputId);
    }

    /// <summary>Fair, non-destructive surplus selector for shipyard output ports.</summary>
    internal sealed class ShipyardOutputTransportPolicy
    {
        private int m_cursor;

        internal TransferChoice? Choose(
            IEnumerable<(string ProductId, int Available)> cargo,
            IEnumerable<ReservedCargo> reserved,
            IEnumerable<OutputPortMetadata> outputs)
        {
            Dictionary<string, int> reservedByProduct = (reserved ?? Array.Empty<ReservedCargo>())
                .GroupBy(item => item.ProductId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount), StringComparer.Ordinal);
            List<(string ProductId, int Available)> candidates = (cargo ?? Array.Empty<(string ProductId, int Available)>())
                .Where(item => !string.IsNullOrEmpty(item.ProductId) && item.Available > 0)
                .Select(item => (item.ProductId, Math.Max(0, item.Available - (reservedByProduct.TryGetValue(item.ProductId, out int reserve) ? reserve : 0))))
                .Where(item => item.Item2 > 0)
                .ToList();
            List<OutputPortMetadata> ports = (outputs ?? Array.Empty<OutputPortMetadata>())
                .Where(port => !string.IsNullOrEmpty(port.OutputId))
                .ToList();
            if (candidates.Count == 0 || ports.Count == 0)
            {
                return null;
            }

            for (int offset = 0; offset < candidates.Count * ports.Count; offset++)
            {
                int index = (m_cursor + offset) % (candidates.Count * ports.Count);
                (string productId, int available) = candidates[index % candidates.Count];
                OutputPortMetadata port = ports[index % ports.Count];
                if (port.CompatibleProducts.Contains(productId))
                {
                    m_cursor = (index + 1) % (candidates.Count * ports.Count);
                    return new TransferChoice(productId, port.OutputId, available);
                }
            }
            return null;
        }

        internal void Reset() => m_cursor = 0;
    }

    internal enum ShipUnloadPolicy
    {
        Vanilla,
        SmallestStackFirst,
    }

    internal readonly struct UnloadBufferCandidate
    {
        internal UnloadBufferCandidate(string bufferId, string productId, int amount, bool callerEligible, bool skipped, bool reserved)
        {
            BufferId = bufferId?.Trim() ?? string.Empty;
            ProductId = productId?.Trim() ?? string.Empty;
            Amount = amount;
            CallerEligible = callerEligible;
            Skipped = skipped;
            Reserved = reserved;
        }

        internal string BufferId { get; }
        internal string ProductId { get; }
        internal int Amount { get; }
        internal bool CallerEligible { get; }
        internal bool Skipped { get; }
        internal bool Reserved { get; }
    }

    internal static class ShipUnloadSelector
    {
        internal static UnloadBufferCandidate? Select(ShipUnloadPolicy policy, IEnumerable<UnloadBufferCandidate> candidates)
        {
            if (policy == ShipUnloadPolicy.Vanilla)
            {
                return null;
            }
            return (candidates ?? Array.Empty<UnloadBufferCandidate>())
                .Where(candidate => candidate.CallerEligible && !candidate.Skipped && !candidate.Reserved && candidate.Amount > 0)
                .OrderBy(candidate => candidate.Amount)
                .ThenBy(candidate => candidate.BufferId, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }
}
