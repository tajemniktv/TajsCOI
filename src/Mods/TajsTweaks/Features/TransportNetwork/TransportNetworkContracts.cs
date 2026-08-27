// Taj's COI Mods | TransportNetworkContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Ports;
using Mafi.Core.Ports.Io;

namespace TajsCOI.Tweaks.Features.TransportNetwork
{
    public enum TransportNetworkEntityClassification
    {
        Segment,
        Connector,
        Endpoint,
    }

    /// <summary>
    ///     Describes one connected edge without making traversal depend on a particular native
    ///     connector implementation. Optional integrations can provide a target rendered entity;
    ///     the final trace still stores only its stable ID and classification.
    /// </summary>
    public readonly struct TransportNetworkConnection
    {
        public TransportNetworkConnection(
            IRenderedEntity target,
            bool hasAuthoritativeDirection = false)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            HasAuthoritativeDirection = hasAuthoritativeDirection;
        }

        public IRenderedEntity Target { get; }

        public bool HasAuthoritativeDirection { get; }
    }

    /// <summary>
    ///     A port exposed by a connector adapter. Native ports carry their authoritative game
    ///     direction and renderer ID; optional adapters may omit NativePort and therefore cannot
    ///     request an arrow.
    /// </summary>
    public readonly struct TransportNetworkPort
    {
        public TransportNetworkPort(IoPort nativePort)
        {
            NativePort = nativePort ?? throw new ArgumentNullException(nameof(nativePort));
        }

        public IoPort? NativePort { get; }

        public bool HasAuthoritativeDirection => NativePort is not null;
    }

    public sealed class TransportNetworkNodeDescription
    {
        public TransportNetworkNodeDescription(
            EntityId entityId,
            TransportNetworkEntityClassification classification,
            IEnumerable<TransportNetworkConnection>? connections,
            IEnumerable<TransportNetworkPort>? ports = null)
        {
            EntityId = entityId;
            Classification = classification;
            Connections = new List<TransportNetworkConnection>(connections ?? Array.Empty<TransportNetworkConnection>());
            Ports = new List<TransportNetworkPort>(ports ?? Array.Empty<TransportNetworkPort>());
        }

        public EntityId EntityId { get; }

        public TransportNetworkEntityClassification Classification { get; }

        public IReadOnlyList<TransportNetworkConnection> Connections { get; }

        public IReadOnlyList<TransportNetworkPort> Ports { get; }
    }

    /// <summary>
    ///     Adapter seam for optional connector implementations. The visualizer never guesses from
    ///     type names: an adapter must explicitly claim and describe a rendered entity.
    /// </summary>
    public interface ITransportNetworkConnectorAdapter
    {
        bool TryDescribe(IRenderedEntity entity, out TransportNetworkNodeDescription description);
    }

    /// <summary>
    ///     Stable result of a transport-network traversal. No entity or renderer references are
    ///     retained here, so a trace is safe to replace when the scene selection changes.
    /// </summary>
    public sealed class TransportNetworkTrace
    {
        internal TransportNetworkTrace(
            EntityId seedEntityId,
            IReadOnlyList<TransportNetworkTraceEntry> entries,
            bool isTruncated)
        {
            SeedEntityId = seedEntityId;
            Entries = entries;
            IsTruncated = isTruncated;
        }

        public EntityId SeedEntityId { get; }

        public IReadOnlyList<TransportNetworkTraceEntry> Entries { get; }

        public bool IsTruncated { get; }

        public int Count => Entries.Count;
    }

    public readonly struct TransportNetworkTraceEntry
    {
        internal TransportNetworkTraceEntry(EntityId entityId, TransportNetworkEntityClassification classification)
        {
            EntityId = entityId;
            Classification = classification;
        }

        public EntityId EntityId { get; }

        public TransportNetworkEntityClassification Classification { get; }
    }

    internal static class TransportNetworkTraversal
    {
        internal const int DefaultMaximumEntities = 4096;

        internal static TransportNetworkTrace Trace(
            IRenderedEntity seed,
            Func<IRenderedEntity, TransportNetworkNodeDescription?> describe,
            int maximumEntities = DefaultMaximumEntities)
        {
            if (seed is null)
            {
                throw new ArgumentNullException(nameof(seed));
            }
            if (describe is null)
            {
                throw new ArgumentNullException(nameof(describe));
            }
            if (maximumEntities <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEntities));
            }

            var visited = new HashSet<EntityId>();
            var pending = new Queue<IRenderedEntity>();
            var entries = new List<TransportNetworkTraceEntry>();
            pending.Enqueue(seed);
            EntityId seedId = seed.Id;
            bool truncated = false;

            while (pending.Count > 0)
            {
                IRenderedEntity entity = pending.Dequeue();
                if (entity is null || entity.IsDestroyed || !visited.Add(entity.Id))
                {
                    continue;
                }

                TransportNetworkNodeDescription? description;
                try
                {
                    description = describe(entity);
                }
                catch
                {
                    // A broken optional adapter must not prevent vanilla networks from being
                    // visualized, nor should it turn a selection click into a game-loop error.
                    continue;
                }

                if (description is null || description.EntityId != entity.Id)
                {
                    continue;
                }

                entries.Add(new TransportNetworkTraceEntry(description.EntityId, description.Classification));
                foreach (TransportNetworkConnection connection in description.Connections)
                {
                    IRenderedEntity target = connection.Target;
                    if (target is null || target.IsDestroyed || visited.Contains(target.Id))
                    {
                        continue;
                    }
                    if (visited.Count + pending.Count >= maximumEntities)
                    {
                        truncated = true;
                        break;
                    }
                    pending.Enqueue(target);
                }
                if (truncated)
                {
                    break;
                }
            }

            return new TransportNetworkTrace(seedId, entries, truncated);
        }
    }

    /// <summary>
    ///     Vanilla adapter for transports, sorters, balancers (Zippers), lifts, mini-zippers, and
    ///     all other entities that expose native IoPorts. Connectivity is read directly from each
    ///     port's ConnectedPort owner; no island-wide entity enumeration is required.
    /// </summary>
    public sealed class NativeTransportNetworkConnectorAdapter : ITransportNetworkConnectorAdapter
    {
        public bool TryDescribe(IRenderedEntity entity, out TransportNetworkNodeDescription description)
        {
            if (entity is not IEntityWithPorts withPorts || withPorts.IsDestroyed)
            {
                description = null!;
                return false;
            }

            var connections = new List<TransportNetworkConnection>();
            var ports = new List<TransportNetworkPort>();
            foreach (IoPort port in withPorts.Ports)
            {
                if (port is null || port.IsDestroyed)
                {
                    continue;
                }

                ports.Add(new TransportNetworkPort(port));
                if (port.ConnectedPort.HasValue && port.ConnectedPort.Value.OwnerEntity is IRenderedEntity connected)
                {
                    connections.Add(new TransportNetworkConnection(connected, hasAuthoritativeDirection: true));
                }
            }

            description = new TransportNetworkNodeDescription(
                withPorts.Id,
                Classify(withPorts),
                connections,
                ports);
            return true;
        }

        private static TransportNetworkEntityClassification Classify(IEntityWithPorts entity) =>
            entity is Mafi.Core.Factory.Transports.Transport
                ? TransportNetworkEntityClassification.Segment
                : entity is Mafi.Core.Factory.Sorters.Sorter ||
                  entity is Mafi.Core.Factory.Zippers.Zipper ||
                  entity is Mafi.Core.Factory.Zippers.MiniZipper ||
                  entity is Mafi.Core.Factory.Lifts.Lift
                    ? TransportNetworkEntityClassification.Connector
                    : TransportNetworkEntityClassification.Endpoint;
    }

    /// <summary>
    ///     Process-lifetime registration point for optional connector integrations. Registrations
    ///     are weak so a scene-owned adapter cannot keep gameplay objects alive across reloads.
    /// </summary>
    public static class TransportNetworkConnectorAdapters
    {
        private static readonly object s_gate = new();
        private static readonly List<WeakReference<ITransportNetworkConnectorAdapter>> s_adapters = new();

        public static IDisposable Register(ITransportNetworkConnectorAdapter adapter)
        {
            if (adapter is null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            var reference = new WeakReference<ITransportNetworkConnectorAdapter>(adapter);
            lock (s_gate)
            {
                s_adapters.Add(reference);
            }
            return new Registration(reference);
        }

        internal static IReadOnlyList<ITransportNetworkConnectorAdapter> Snapshot()
        {
            var result = new List<ITransportNetworkConnectorAdapter>();
            lock (s_gate)
            {
                for (int index = s_adapters.Count - 1; index >= 0; index--)
                {
                    if (!s_adapters[index].TryGetTarget(out ITransportNetworkConnectorAdapter? adapter) || adapter is null)
                    {
                        s_adapters.RemoveAt(index);
                        continue;
                    }
                    result.Add(adapter);
                }
            }
            return result;
        }

        private sealed class Registration : IDisposable
        {
            private readonly WeakReference<ITransportNetworkConnectorAdapter> m_reference;
            private bool m_disposed;

            internal Registration(WeakReference<ITransportNetworkConnectorAdapter> reference) => m_reference = reference;

            public void Dispose()
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                lock (s_gate)
                {
                    s_adapters.Remove(m_reference);
                }
            }
        }
    }
}
