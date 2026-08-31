// Taj's COI Mods | EntityMetadataLifecycleContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TajsCOI.Common.Metadata;
using TajsCOI.Tweaks.Features.EntityMetadata;
using Xunit;

namespace TajsCOI.Tests.RuntimeContracts
{
    /// <summary>
    ///     Scene-lifetime contract for the process-static native inspector postfix. The Harmony
    ///     target remains process-lived, while its metadata lookup is replaced or disconnected at
    ///     every gameplay-scene boundary.
    /// </summary>
    public sealed class EntityMetadataLifecycleContractTests
    {
        private const string HarmonyOwner = "TajsCOI.Tests.EntityMetadataInspectorLifecycle";

        [Fact]
        public void BindingCanBeReplacedAcrossGameplayScenes()
        {
            var first = new StubLookup();
            var second = new StubLookup();
            try
            {
                EntityMetadataInspectorPatch.Reset();
                EntityMetadataInspectorPatch.Bind(first);
                Assert.True(EntityMetadataInspectorPatch.IsBoundTo(first));

                EntityMetadataInspectorPatch.Bind(second);
                Assert.False(EntityMetadataInspectorPatch.IsBoundTo(first));
                Assert.True(EntityMetadataInspectorPatch.IsBoundTo(second));

                // A late termination callback from the previous host must not disconnect the
                // replacement scene's lookup.
                EntityMetadataInspectorPatch.Unbind(first);
                Assert.True(EntityMetadataInspectorPatch.IsBoundTo(second));
            }
            finally
            {
                EntityMetadataInspectorPatch.Reset();
            }
        }

        [Fact]
        public void UnbindReleasesTheOldLookupAndIsIdempotent()
        {
            var oldLookup = new StubLookup();
            EntityMetadataInspectorPatch.Reset();
            try
            {
                EntityMetadataInspectorPatch.Bind(oldLookup);
                EntityMetadataInspectorPatch.Unbind(oldLookup);
                Assert.False(EntityMetadataInspectorPatch.HasLiveLookup);

                // A repeated termination callback must not throw or resurrect the old scene.
                EntityMetadataInspectorPatch.Unbind(oldLookup);
                Assert.False(EntityMetadataInspectorPatch.HasLiveLookup);
            }
            finally
            {
                EntityMetadataInspectorPatch.Reset();
            }
        }

        [Fact]
        public void LookupBindingIsWeakAndDoesNotRequireTheOldResolverAfterTermination()
        {
            FieldInfo field = RuntimeContractAssertions.RequireField(
                typeof(EntityMetadataInspectorPatch),
                "s_lookup",
                typeof(WeakReference<IEntityMetadataLookup>),
                isStatic: true);
            Assert.Equal(typeof(WeakReference<IEntityMetadataLookup>), field.FieldType);

            EntityMetadataInspectorPatch.Reset();
            try
            {
                var oldLookup = new StubLookup();
                EntityMetadataInspectorPatch.Bind(oldLookup);
                EntityMetadataInspectorPatch.Unbind(oldLookup);
                oldLookup = null!;

                Assert.False(EntityMetadataInspectorPatch.HasLiveLookup);
            }
            finally
            {
                EntityMetadataInspectorPatch.Reset();
            }
        }

        [Fact]
        public void InstallationIsIdempotentWhileSceneBindingChanges()
        {
            var harmony = new Harmony(HarmonyOwner);
            try
            {
                harmony.UnpatchAll(HarmonyOwner);
                EntityMetadataInspectorPatch.Reset();
                EntityMetadataInspectorPatch.Install(harmony, new StubLookup());
                MethodBase target = Assert.Single(
                    typeof(Mafi.Unity.Ui.InspectorsManager).GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                    method => method.Name == nameof(Mafi.Unity.Ui.InspectorsManager.TryActivateFor) &&
                              method.GetParameters().Length == 2);
                Assert.Equal(1, CountOwner(Harmony.GetPatchInfo(target), HarmonyOwner));

                EntityMetadataInspectorPatch.Install(harmony, new StubLookup());
                Assert.Equal(1, CountOwner(Harmony.GetPatchInfo(target), HarmonyOwner));
                Assert.True(EntityMetadataInspectorPatch.IsInstalled);
            }
            finally
            {
                EntityMetadataInspectorPatch.Reset();
                harmony.UnpatchAll(HarmonyOwner);
            }
        }

        private static int CountOwner(Patches? patches, string owner) =>
            patches?.Postfixes?.Count(patch => string.Equals(patch.owner, owner, StringComparison.Ordinal)) ?? 0;

        private sealed class StubLookup : IEntityMetadataLookup
        {
            public IReadOnlyList<EntityMetadataRecord> GetEntityMetadataSnapshot() =>
                Array.Empty<EntityMetadataRecord>();

            public IReadOnlyList<EntityMetadataGroup> GetGroupSnapshot() =>
                Array.Empty<EntityMetadataGroup>();

            public bool TryGetEntityMetadata(EntityMetadataIdentity identity, out EntityMetadataRecord? metadata)
            {
                metadata = null;
                return false;
            }

            public bool TryGetGroup(string groupId, out EntityMetadataGroup? group)
            {
                group = null;
                return false;
            }

            public IReadOnlyList<EntityMetadataRecord> ResolveLiveMetadata(IEnumerable<EntityMetadataIdentity> liveEntities) =>
                Array.Empty<EntityMetadataRecord>();
        }
    }
}
