// Taj's COI Mods | EntityMetadataTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using TajsCOI.Common.Metadata;
using TajsCOI.Core.Metadata;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class EntityMetadataTests
    {
        [Fact]
        public void NumericIdReuseDoesNotMatchADifferentPrototype()
        {
            var original = new EntityMetadataIdentity(42, "machine:assembler:v1");
            var replacement = new EntityMetadataIdentity(42, "machine:assembler:v2");

            Assert.NotEqual(original, replacement);
            Assert.NotEqual(original.GetHashCode(), replacement.GetHashCode());
        }

        [Fact]
        public void StateRoundTripPreservesEscapedMetadataAndGroupLink()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.MetadataTests", Guid.NewGuid().ToString("N"));
            try
            {
                var first = new EntityMetadataStateStore(root);
                first.Load("save-fingerprint");
                var group = new EntityMetadataGroup("group-a", "Ore\twatch", 0, "#abc123", false);
                first.SetGroup(group);
                var identity = new EntityMetadataIdentity(7, "proto:ore-mine:v3");
                first.SetEntity(new EntityMetadataRecord(identity, "North\nMine", "Watch\tfor\twater", group.GroupId));
                Assert.True(first.Save());

                var second = new EntityMetadataStateStore(root);
                second.Load("save-fingerprint");
                Assert.True(second.Groups.ContainsKey(group.GroupId));
                Assert.True(second.Entities.TryGetValue(identity, out EntityMetadataRecord? record));
                Assert.Equal("North\nMine", record!.Alias);
                Assert.Equal("Watch\tfor\twater", record.Note);
                Assert.Equal(group.GroupId, record.GroupId);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void RebindRefusesToOverwriteAnExistingIdentitySidecar()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.MetadataTests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new EntityMetadataStateStore(root);
                store.Load("first");
                Assert.True(store.Save());

                var collision = new EntityMetadataStateStore(root);
                collision.Load("second");
                var staleIdentity = new EntityMetadataIdentity(9, "proto:stale");
                store.SetEntity(new EntityMetadataRecord(staleIdentity, "stale", string.Empty, null));
                Assert.True(collision.Save());

                Assert.False(store.Rebind("second"));
                Assert.Empty(store.Entities);
                Assert.Empty(store.Groups);
                store.SetEntity(new EntityMetadataRecord(staleIdentity, "new", string.Empty, null));
                Assert.False(store.Save());
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void InvalidGroupColorIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new EntityMetadataGroup("g", "Group", 0, "not-a-color", false));
            Assert.Equal("#AABBCC", new EntityMetadataGroup("g", "Group", 0, "aabbcc", false).Color);
        }
    }
}
