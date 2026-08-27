// Taj's COI Mods | WorldShipOrderArbiter.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace TajsCOI.Tweaks.Features.World
{
    internal enum WorldShipOrderOwner
    {
        None,
        Manual,
        AutoDelivery,
        AutoExploration,
        AutoReturn,
    }

    /// <summary>Small process-scoped arbiter. It stores IDs/owners only, never resolver objects.</summary>
    internal sealed class WorldShipOrderArbiter
    {
        private readonly object m_gate = new();
        private readonly Dictionary<int, WorldShipOrderOwner> m_claims = new();

        internal bool ManualOrderActive(int shipId)
        {
            lock (m_gate)
            {
                return m_claims.TryGetValue(shipId, out WorldShipOrderOwner owner) && owner == WorldShipOrderOwner.Manual;
            }
        }

        internal WorldShipOrderOwner GetOwner(int shipId)
        {
            lock (m_gate)
            {
                return m_claims.TryGetValue(shipId, out WorldShipOrderOwner owner) ? owner : WorldShipOrderOwner.None;
            }
        }

        internal bool CanClaim(int shipId, WorldShipOrderOwner owner)
        {
            if (owner == WorldShipOrderOwner.None)
            {
                return false;
            }
            lock (m_gate)
            {
                return !m_claims.TryGetValue(shipId, out WorldShipOrderOwner current) ||
                       current == WorldShipOrderOwner.None ||
                       current == owner;
            }
        }

        internal bool TryClaim(int shipId, WorldShipOrderOwner owner)
        {
            if (!CanClaim(shipId, owner))
            {
                return false;
            }
            lock (m_gate)
            {
                if (m_claims.TryGetValue(shipId, out WorldShipOrderOwner current) &&
                    current != WorldShipOrderOwner.None && current != owner)
                {
                    return false;
                }
                m_claims[shipId] = owner;
                return true;
            }
        }

        internal void Release(int shipId, WorldShipOrderOwner owner)
        {
            lock (m_gate)
            {
                if (m_claims.TryGetValue(shipId, out WorldShipOrderOwner current) && current == owner)
                {
                    m_claims.Remove(shipId);
                }
            }
        }

        internal void SetManualOrder(int shipId, bool active)
        {
            lock (m_gate)
            {
                if (active)
                {
                    m_claims[shipId] = WorldShipOrderOwner.Manual;
                }
                else if (m_claims.TryGetValue(shipId, out WorldShipOrderOwner current) && current == WorldShipOrderOwner.Manual)
                {
                    m_claims.Remove(shipId);
                }
            }
        }

        internal void Clear() { lock (m_gate) m_claims.Clear(); }
    }

    internal readonly struct ExplorationCandidate
    {
        internal ExplorationCandidate(int locationId, double roundTripCost, bool viable, bool knownEnemy, double enemyMargin)
        {
            LocationId = locationId;
            RoundTripCost = roundTripCost;
            Viable = viable;
            KnownEnemy = knownEnemy;
            EnemyMargin = enemyMargin;
        }

        internal int LocationId { get; }
        internal double RoundTripCost { get; }
        internal bool Viable { get; }
        internal bool KnownEnemy { get; }
        internal double EnemyMargin { get; }
    }

    internal static class ExplorationCandidatePolicy
    {
        internal static bool IsViable(
            bool docked,
            bool home,
            bool inBattle,
            bool repairing,
            bool cargoLoaded,
            bool fuelSufficient,
            bool knownEnemy,
            double enemyMargin,
            double requiredMargin,
            bool allowUnknownStrength)
        {
            if (!docked || !home || inBattle || repairing || cargoLoaded || !fuelSufficient)
            {
                return false;
            }
            return knownEnemy ? enemyMargin >= requiredMargin : allowUnknownStrength;
        }

        internal static ExplorationCandidate? ChooseNearest(IEnumerable<ExplorationCandidate> candidates)
        {
            ExplorationCandidate? best = null;
            foreach (ExplorationCandidate candidate in candidates ?? Array.Empty<ExplorationCandidate>())
            {
                if (!candidate.Viable || double.IsNaN(candidate.RoundTripCost) || double.IsInfinity(candidate.RoundTripCost))
                {
                    continue;
                }
                if (!best.HasValue || candidate.RoundTripCost < best.Value.RoundTripCost ||
                    candidate.RoundTripCost == best.Value.RoundTripCost && candidate.LocationId < best.Value.LocationId)
                {
                    best = candidate;
                }
            }
            return best;
        }
    }
}
