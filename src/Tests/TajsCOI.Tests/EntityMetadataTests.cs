// Taj's COI Mods | EntityMetadataTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using TajsCOI.Common.Metadata;
using TajsCOI.Common.Persistence;
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
            string savePath = Path.Combine(root, "saves", "save-a.save");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                File.WriteAllBytes(savePath, new byte[] { 1, 2, 3 });
                TajsSaveIdentity saveIdentity = TajsSaveIdentity.FromFile(savePath, "world")!;
                var first = new EntityMetadataStateStore(root);
                first.LoadIdentity(saveIdentity);
                var group = new EntityMetadataGroup("group-a", "Ore\twatch", 0, "#abc123", false);
                first.SetGroup(group);
                var entityIdentity = new EntityMetadataIdentity(7, "proto:ore-mine:v3");
                first.SetEntity(new EntityMetadataRecord(entityIdentity, "North\nMine", "Watch\tfor\twater", group.GroupId));
                Assert.True(first.Save());

                var second = new EntityMetadataStateStore(root);
                second.LoadIdentity(TajsSaveIdentity.FromFile(savePath, "world"));
                Assert.True(second.Groups.ContainsKey(group.GroupId));
                Assert.True(second.Entities.TryGetValue(entityIdentity, out EntityMetadataRecord? record));
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
            string firstPath = Path.Combine(root, "saves", "first.save");
            string secondPath = Path.Combine(root, "saves", "second.save");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
                File.WriteAllBytes(firstPath, new byte[] { 1 });
                File.WriteAllBytes(secondPath, new byte[] { 2 });
                var store = new EntityMetadataStateStore(root);
                TajsSaveIdentity firstIdentity = TajsSaveIdentity.FromFile(firstPath, "world")!;
                TajsSaveIdentity secondIdentity = TajsSaveIdentity.FromFile(secondPath, "world")!;
                store.LoadIdentity(firstIdentity);
                Assert.True(store.Save());

                var collision = new EntityMetadataStateStore(root);
                collision.LoadIdentity(secondIdentity);
                var staleIdentity = new EntityMetadataIdentity(9, "proto:stale");
                store.SetEntity(new EntityMetadataRecord(staleIdentity, "stale", string.Empty, null));
                Assert.True(collision.Save());

                Assert.False(store.RebindIdentity(secondIdentity));
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
        public void MutationsRollBackWhenBoundSidecarCannotBeWritten()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.MetadataTests", Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "saves", "save-a.save");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                File.WriteAllBytes(savePath, new byte[] { 1, 2, 3 });
                TajsSaveIdentity identity = TajsSaveIdentity.FromFile(savePath, "world")!;
                var store = new EntityMetadataStateStore(root);
                store.LoadIdentity(identity);
                var group = new EntityMetadataGroup("group-a", "Ore", 0, "#abc123", false);
                var entity = new EntityMetadataIdentity(7, "proto:ore-mine:v3");
                var original = new EntityMetadataRecord(entity, "North Mine", "Watch water", group.GroupId);
                store.SetGroup(group);
                store.SetEntity(original);

                // A directory at the sidecar file path makes the atomic replacement fail while
                // leaving the bound store in place.
                Directory.CreateDirectory(Path.Combine(root, identity.OwnershipKey, "metadata.tsv"));
                var service = new TajsEntityMetadataService(store);
                try
                {
                    Assert.False(service.TrySetEntityMetadata(entity, "Changed", "Changed", group.GroupId, out _));
                    Assert.True(service.TryGetEntityMetadata(entity, out EntityMetadataRecord? afterSet));
                    AssertMetadataEqual(original, afterSet!);

                    Assert.False(service.TryClearEntityMetadata(entity));
                    Assert.True(service.TryGetEntityMetadata(entity, out EntityMetadataRecord? afterClear));
                    AssertMetadataEqual(original, afterClear!);

                    Assert.False(service.TryUpdateGroup(group.GroupId, "Changed", 3, "#123456", true, out _));
                    Assert.True(service.TryGetGroup(group.GroupId, out EntityMetadataGroup? afterUpdate));
                    Assert.Equal(group.Name, afterUpdate!.Name);
                    Assert.Equal(group.Order, afterUpdate.Order);
                    Assert.Equal(group.Color, afterUpdate.Color);
                    Assert.Equal(group.Locked, afterUpdate.Locked);

                    Assert.False(service.TryDeleteGroup(group.GroupId));
                    Assert.True(service.TryGetGroup(group.GroupId, out EntityMetadataGroup? afterDelete));
                    Assert.NotNull(afterDelete);
                    Assert.True(service.TryGetEntityMetadata(entity, out EntityMetadataRecord? afterDeleteEntity));
                    AssertMetadataEqual(original, afterDeleteEntity!);
                }
                finally
                {
                    service.Dispose();
                }
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
        public void RegistryWriteFailureKeepsMetadataInMemoryWithoutClaimingDurability()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.MetadataFailure-" + Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "save.save");
            string invalidRoot = Path.Combine(root, "sidecars-file");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(savePath, new byte[] { 1, 2, 3 });
                Directory.CreateDirectory(invalidRoot);
                Directory.CreateDirectory(Path.Combine(invalidRoot, "_identity-bindings.tsv"));
                TajsSaveIdentity identity = TajsSaveIdentity.FromFile(savePath, "world")!;
                var store = new EntityMetadataStateStore(invalidRoot);
                store.LoadIdentity(identity);
                var entity = new EntityMetadataIdentity(7, "proto:ore-mine:v3");
                store.SetEntity(new EntityMetadataRecord(entity, "North Mine", "Watch water", null));

                Assert.Equal(
                    TajsSaveIdentityBindingStatus.IdentityUsableForSessionBindingPersistenceFailed,
                    store.IdentityBindingStatus);
                Assert.False(store.IdentityBindingPersisted);
                Assert.True(store.Save());
                Assert.True(store.Entities.ContainsKey(entity));
                Assert.True(File.Exists(Path.Combine(invalidRoot, identity.OwnershipKey, "metadata.tsv")));
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

        private static void AssertMetadataEqual(EntityMetadataRecord expected, EntityMetadataRecord actual)
        {
            Assert.Equal(expected.Identity, actual.Identity);
            Assert.Equal(expected.Alias, actual.Alias);
            Assert.Equal(expected.Note, actual.Note);
            Assert.Equal(expected.GroupId, actual.GroupId);
        }
    }
}
