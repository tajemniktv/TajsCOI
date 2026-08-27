// Taj's COI Mods | TransportNetworkTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Localization;
using TajsCOI.Tweaks.Features.TransportNetwork;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class TransportNetworkTests
    {
        [Fact]
        public void TraversalTerminatesOnCyclesAndStoresStableClassifications()
        {
            var first = new FakeRenderedEntity(1);
            var second = new FakeRenderedEntity(2);
            var third = new FakeRenderedEntity(3);
            first.Connections.Add(second);
            second.Connections.Add(third);
            third.Connections.Add(first);

            TransportNetworkTrace trace = Trace(first, 32);

            Assert.False(trace.IsTruncated);
            Assert.Equal(new[] { 1, 2, 3 }, trace.Entries.Select(x => x.EntityId.Value));
            Assert.Equal(TransportNetworkEntityClassification.Segment, trace.Entries[0].Classification);
            Assert.Equal(TransportNetworkEntityClassification.Connector, trace.Entries[1].Classification);
            Assert.Equal(TransportNetworkEntityClassification.Endpoint, trace.Entries[2].Classification);
        }

        [Fact]
        public void TraversalStopsAtExplicitBoundAndReportsTruncation()
        {
            var first = new FakeRenderedEntity(1);
            var second = new FakeRenderedEntity(2);
            var third = new FakeRenderedEntity(3);
            first.Connections.Add(second);
            second.Connections.Add(third);

            TransportNetworkTrace trace = Trace(first, 2);

            Assert.True(trace.IsTruncated);
            Assert.Equal(2, trace.Count);
            Assert.Equal(new[] { 1, 2 }, trace.Entries.Select(x => x.EntityId.Value));
        }

        private static TransportNetworkTrace Trace(FakeRenderedEntity seed, int maximum)
        {
            var descriptions = new Dictionary<int, TransportNetworkNodeDescription>();
            var pending = new Queue<FakeRenderedEntity>();
            pending.Enqueue(seed);
            var seen = new HashSet<int>();
            while (pending.Count > 0)
            {
                FakeRenderedEntity entity = pending.Dequeue();
                if (!seen.Add(entity.Id.Value))
                {
                    continue;
                }
                descriptions[entity.Id.Value] = new TransportNetworkNodeDescription(
                    entity.Id,
                    entity.Classification,
                    entity.Connections.Select(x => new TransportNetworkConnection(x)));
                foreach (FakeRenderedEntity connection in entity.Connections)
                {
                    if (!seen.Contains(connection.Id.Value))
                    {
                        pending.Enqueue(connection);
                    }
                }
            }

            return TransportNetworkTraversal.Trace(
                seed,
                entity => descriptions.TryGetValue(entity.Id.Value, out TransportNetworkNodeDescription? description)
                    ? description
                    : null,
                maximum);
        }

        private sealed class FakeRenderedEntity : IRenderedEntity
        {
            internal FakeRenderedEntity(int id)
            {
                Id = new EntityId(id);
                Connections = new List<FakeRenderedEntity>();
                Classification = id switch
                {
                    1 => TransportNetworkEntityClassification.Segment,
                    2 => TransportNetworkEntityClassification.Connector,
                    _ => TransportNetworkEntityClassification.Endpoint,
                };
            }

            internal List<FakeRenderedEntity> Connections { get; }

            internal TransportNetworkEntityClassification Classification { get; set; }

            public EntityId Id { get; }

            public EntityProto Prototype => null!;

            public EntityContext Context => null!;

            public bool IsEnabled => true;

            public bool IsPaused => false;

            public bool CanBePaused => false;

            public bool IsDestroyed => false;

            public ulong RendererData { get; set; }

            public LocStrFormatted DefaultTitle => default;

            public void UpdateIsEnabled() { }

            public void UpdateIsBroken() { }

            public void UpdateProperties() { }

            public void SetPaused(bool isPaused) { }

            public void AddObserver(IEntityObserver observer) { }

            public void RemoveObserver(IEntityObserver observer) { }
        }
    }
}
