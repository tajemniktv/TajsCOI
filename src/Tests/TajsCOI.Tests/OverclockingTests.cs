// Taj's COI Mods | OverclockingTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Serialization;
using TajsCOI.Tweaks.Features.Overclocking;
using TajsCOI.Common.Persistence;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class OverclockingTests
    {
        [Fact]
        public void AutomaticCadenceUsesSimulationTimeAndDoesNotAdvanceWhilePaused()
        {
            double next = OverclockCadence.ScheduleNext(0d, 2);
            Assert.False(OverclockCadence.IsDue(1.99d, next));
            Assert.True(OverclockCadence.IsDue(2d, next));
            // A paused frame has no simulation-time delta; wall-clock/render time is irrelevant.
            Assert.False(OverclockCadence.IsDue(1.99d, next));
            // A high simulation speed simply reaches the same simulation deadline sooner in wall time.
            Assert.True(OverclockCadence.IsDue(20d, next));
        }

        [Fact]
        public void SaveScopedPoliciesDoNotCrossContaminateSameNameOrReusedEntityIds()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-Overclocking-" + Guid.NewGuid().ToString("N"));
            string firstPath = Path.Combine(root, "same-name.save");
            string secondPath = Path.Combine(root, "other.save");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(firstPath, new byte[] { 1, 2, 3 });
                TajsSaveIdentity first = TajsSaveIdentity.FromFile(firstPath, "world")!;
                var firstStore = new OverclockingStateStore(Path.Combine(root, "sidecars"));
                firstStore.LoadForSave(first, "world");
                firstStore.GetOrCreateEntity(42).HasManualOverride = true;
                firstStore.GetOrCreateEntity(42).ManualPercent = 225;
                firstStore.Save();

                File.Copy(firstPath, secondPath);
                TajsSaveIdentity second = TajsSaveIdentity.FromFile(secondPath, "world", "same-name")!;
                var secondStore = new OverclockingStateStore(Path.Combine(root, "sidecars"));
                secondStore.LoadForSave(second, "world");

                Assert.Equal(first.DisplayName, second.DisplayName);
                Assert.NotEqual(first.OwnershipKey, second.OwnershipKey);
                Assert.False(secondStore.TryGetEntity(42, out _));
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
        public void RepeatedNormalSaveRevisionsKeepTheSamePolicyLineage()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-Overclocking-" + Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "slot.save");
            string sidecarRoot = Path.Combine(root, "sidecars");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(savePath, new byte[] { 1, 2, 3 });
                TajsSaveIdentity initial = TajsSaveIdentity.FromFile(savePath, "world")!;
                var store = new OverclockingStateStore(sidecarRoot);
                store.LoadForSave(initial, "world");
                store.GetOrCreateEntity(42).ManualPercent = 225;
                store.Save();

                string replacement = savePath + ".tmp";
                File.WriteAllBytes(replacement, new byte[] { 1, 2, 3, 4 });
                File.Replace(replacement, savePath, null);

                Assert.True(store.RebindAfterSave(savePath, "world"));
                Assert.True(store.TryGetEntity(42, out OverclockEntityPolicy? policy));
                Assert.Equal(225, policy!.ManualPercent);

                var reloaded = new OverclockingStateStore(sidecarRoot);
                reloaded.LoadForSave(TajsSaveIdentity.FromFile(savePath, "world"), "world");
                Assert.True(reloaded.TryGetEntity(42, out OverclockEntityPolicy? reloadedPolicy));
                Assert.Equal(225, reloadedPolicy!.ManualPercent);
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
        public void SaveAsCollisionDoesNotOverwriteTheTargetPolicySidecar()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-Overclocking-" + Guid.NewGuid().ToString("N"));
            string firstPath = Path.Combine(root, "first.save");
            string secondPath = Path.Combine(root, "second.save");
            string sidecarRoot = Path.Combine(root, "sidecars");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(firstPath, new byte[] { 1 });
                File.WriteAllBytes(secondPath, new byte[] { 2 });
                var firstStore = new OverclockingStateStore(sidecarRoot);
                firstStore.LoadForSave(TajsSaveIdentity.FromFile(firstPath, "world"), "world");
                firstStore.GetOrCreateEntity(42).ManualPercent = 225;
                firstStore.Save();

                var secondStore = new OverclockingStateStore(sidecarRoot);
                secondStore.LoadForSave(TajsSaveIdentity.FromFile(secondPath, "world"), "world");
                secondStore.GetOrCreateEntity(99).ManualPercent = 175;
                secondStore.Save();

                Assert.False(firstStore.RebindAfterSave(secondPath, "world"));
                Assert.False(firstStore.TryGetEntity(42, out _));

                var target = new OverclockingStateStore(sidecarRoot);
                target.LoadForSave(TajsSaveIdentity.FromFile(secondPath, "world"), "world");
                Assert.True(target.TryGetEntity(99, out OverclockEntityPolicy? targetPolicy));
                Assert.Equal(175, targetPolicy!.ManualPercent);
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
        public void FirstSaveBindsNewGamePoliciesWithoutDroppingThem()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-Overclocking-" + Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "new-game.save");
            try
            {
                Directory.CreateDirectory(root);
                var store = new OverclockingStateStore(Path.Combine(root, "sidecars"));
                store.LoadForSave(null, "world");
                store.GetOrCreateEntity(42).ManualPercent = 225;
                File.WriteAllBytes(savePath, new byte[] { 1 });

                Assert.True(store.RebindAfterSave(savePath, "world"));
                Assert.True(store.TryGetEntity(42, out OverclockEntityPolicy? policy));
                Assert.Equal(225, policy!.ManualPercent);
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
        public void LegacyNameSidecarIsPreservedAndNeverImported()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-Overclocking-" + Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "same-name.save");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(savePath, new byte[] { 1 });
                string legacyPath = Path.Combine(root, "sidecars", "same-name", "state.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
                File.WriteAllText(legacyPath, "TajsTweaksOverclockingV1\nE\t42\t1\t250");
                TajsSaveIdentity identity = TajsSaveIdentity.FromFile(savePath, "world")!;
                var store = new OverclockingStateStore(Path.Combine(root, "sidecars"));
                store.LoadForSave(identity, "same-name");

                Assert.False(store.TryGetEntity(42, out _));
                Assert.Contains("legacy", store.LoadStatus, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("TajsTweaksOverclockingV1\nE\t42\t1\t250", File.ReadAllText(legacyPath));
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
        public void MalformedIdentitySidecarIsPreservedAndBlocksWrites()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-Overclocking-" + Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "slot.save");
            string sidecarRoot = Path.Combine(root, "sidecars");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(savePath, new byte[] { 1 });
                TajsSaveIdentity identity = TajsSaveIdentity.FromFile(savePath, "world")!;
                string sidecar = Path.Combine(sidecarRoot, identity.OwnershipKey, "state.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
                string original = "TajsTweaksOverclockingV2\nI\t" + identity.OwnershipKey + "\nE\t42\tbad";
                File.WriteAllText(sidecar, original);

                var store = new OverclockingStateStore(sidecarRoot);
                store.LoadForSave(identity, "world");
                store.Save();

                Assert.False(store.TryGetEntity(42, out _));
                Assert.Equal(original, File.ReadAllText(sidecar));
                Assert.Contains("invalid", store.LoadStatus, StringComparison.OrdinalIgnoreCase);
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
        public void CostCurveMatchesConfiguredExponent()
        {
            float multiplier = OverclockingMath.CostMultiplier(200, 124);

            Assert.InRange(multiplier, 2.35f, 2.37f);
            Assert.Equal(1f, OverclockingMath.CostMultiplier(100, 124));
        }

        [Theory]
        [InlineData(10, 100, 10)]
        [InlineData(10, 150, 15)]
        [InlineData(10, 200, 20)]
        [InlineData(1, 150, 2)]
        public void FocusRateScalesWithOverclockPercent(int baseValue, int percent, int expected) =>
            Assert.Equal(expected, OverclockingMath.ScaleRate(baseValue, percent));

        [Fact]
        public void AutomaticFillCurveUsesMaximumNeutralAndMinimumRegions()
        {
            Assert.Equal(300, OverclockingMath.DesiredPercentForFill(5, 100, 300, 10, 50, 90));
            Assert.Equal(100, OverclockingMath.DesiredPercentForFill(50, 100, 300, 10, 50, 90));
            Assert.Equal(100, OverclockingMath.DesiredPercentForFill(95, 100, 300, 10, 50, 90));
        }

        [Fact]
        public void AutomaticAdjustmentHonoursDeadbandStepAndBounds()
        {
            var bounds = new OverclockBounds(100, 300);

            Assert.Equal(150, OverclockingMath.ApplyHysteresis(150, 153, bounds, 5, 25, 5));
            Assert.Equal(175, OverclockingMath.ApplyHysteresis(150, 230, bounds, 0, 25, 5));
            Assert.Equal(100, OverclockingMath.ApplyHysteresis(110, 50, bounds, 0, 25, 5));
        }

        [Theory]
        [InlineData(-50, 5000, 10, 1000)]
        [InlineData(125, 80, 100, 100)]
        [InlineData(40, 260, 40, 260)]
        public void UserPolicyBoundsNormalizeToDeterministicSupportedDomain(
            int minimum,
            int maximum,
            int expectedMinimum,
            int expectedMaximum)
        {
            OverclockBounds bounds = OverclockBounds.Normalize(minimum, maximum);

            Assert.Equal(expectedMinimum, bounds.MinPercent);
            Assert.Equal(expectedMaximum, bounds.MaxPercent);
        }

        [Fact]
        public void TransportCapacityCompensationIsBoundedAndRamped()
        {
            Assert.Equal(10, OverclockingMath.RampedCapacityValue(10, 100, 300, 100, increase: false));
            Assert.Equal(10, OverclockingMath.RampedCapacityValue(10, 200, 300, 100, increase: false));
            Assert.Equal(5, OverclockingMath.RampedCapacityValue(10, 300, 300, 100, increase: false));
            Assert.Equal(30, OverclockingMath.RampedCapacityValue(10, 300, 300, 200, increase: true));
            Assert.Equal(1, OverclockingMath.RampedCapacityValue(1, 300, 300, 300, increase: false));
        }

        [Theory]
        [InlineData(100, 193, 100, 100)]
        [InlineData(100, 193, 140, 194)]
        [InlineData(194, 193, 140, 194)]
        [InlineData(250, 193, 80, 250)]
        [InlineData(3, 4, 300, 5)]
        public void AnimationProcessFitOnlyAdjustsShortOverclockedTimelines(
            int currentProcessTicks,
            int animationTicks,
            int overclockPercent,
            int expectedTicks)
        {
            Assert.Equal(
                expectedTicks,
                OverclockingMath.EnsureAnimationProcessFits(
                    currentProcessTicks,
                    animationTicks,
                    overclockPercent));
        }

        [Fact]
        public void GroupsEnforceSingleMembershipAndCanBeLocked()
        {
            var store = new OverclockingStateStore();
            OverclockGroup first = store.CreateGroup("First");
            OverclockGroup second = store.CreateGroup("Second");

            Assert.True(store.AddMember(first.Id, 42));
            Assert.True(store.AddMember(second.Id, 42));
            Assert.DoesNotContain(42, first.Members);
            Assert.Contains(42, second.Members);

            second.Locked = true;
            Assert.False(store.AddMember(second.Id, 43));
        }

        [Fact]
        public void PolicyCommandsPreserveManualAutoResetInputOrder()
        {
            EntityId entityId = new(42);
            TajsOverclockPolicyCmd[] commands = new[]
            {
                TajsOverclockPolicyCmd.SetManual(entityId, 180),
                TajsOverclockPolicyCmd.SetAuto(entityId, true, null, null),
                TajsOverclockPolicyCmd.Reset(entityId),
            };

            Assert.Equal(
                new[] { TajsOverclockPolicyOperation.SetManual, TajsOverclockPolicyOperation.SetAuto, TajsOverclockPolicyOperation.Reset },
                commands.Select(command => command.Operation));
            Assert.Equal(180, commands[0].Percent);
            Assert.True(commands[1].Enabled);
            Assert.Equal(entityId, commands[2].TargetId);
        }

        [Fact]
        public void GroupPolicyCommandsCarryOnlyStableValueData()
        {
            EntityId entityId = new(42);
            TajsOverclockPolicyCmd auto = TajsOverclockPolicyCmd.SetGroupAuto(7, true, 125, 275);
            TajsOverclockPolicyCmd add = TajsOverclockPolicyCmd.AddToGroup(7, entityId);

            Assert.Equal(TajsOverclockPolicyOperation.SetGroupAuto, auto.Operation);
            Assert.Equal(7, auto.GroupId);
            Assert.True(auto.HasMinimum);
            Assert.Equal(125, auto.Minimum);
            Assert.True(auto.HasMaximum);
            Assert.Equal(275, auto.Maximum);
            Assert.Equal(TajsOverclockPolicyOperation.AddToGroup, add.Operation);
            Assert.Equal(entityId, add.TargetId);
            Assert.Equal(7, add.GroupId);
        }

        [Fact]
        public void PolicyCommandRoundTripsThroughCoiSerializer()
        {
            TajsOverclockPolicyCmd original = TajsOverclockPolicyCmd.SetGroupAuto(7, true, 125, 275);
            using var stream = new MemoryStream();
            using (var writer = new BlobWriter(stream))
            {
                TajsOverclockPolicyCmd.Serialize(original, writer);
                writer.FinalizeSerialization();
            }

            stream.Position = 0;
            var reader = new BlobReader(stream, 0);
            TajsOverclockPolicyCmd restored = TajsOverclockPolicyCmd.Deserialize(reader);
            reader.FinalizeLoading(Mafi.Option<Mafi.DependencyResolver>.None);

            Assert.Equal(original.Operation, restored.Operation);
            Assert.Equal(original.GroupId, restored.GroupId);
            Assert.Equal(original.Enabled, restored.Enabled);
            Assert.Equal(original.Minimum, restored.Minimum);
            Assert.Equal(original.Maximum, restored.Maximum);
        }

        [Fact]
        public void PolicyCommandProcessorRegistersLegacyAndUnifiedInputTypes()
        {
            Type[] interfaces = typeof(TajsOverclockCommandsProcessor).GetInterfaces();

            Assert.Contains(typeof(ICommandProcessor<TajsOverclockSetRateCmd>), interfaces);
            Assert.Contains(typeof(ICommandProcessor<TajsOverclockPolicyCmd>), interfaces);
            Assert.Contains(typeof(Mafi.IAction<TajsOverclockPolicyCmd>), interfaces);
        }

        [Theory]
        [InlineData("125", 125)]
        [InlineData("125%", 125)]
        [InlineData(" 125 % ", 125)]
        public void InspectorRateEntryUsesSharedBoundedEditorParsing(string input, int expected)
        {
            Assert.True(
                OverclockingInspectorPatch.TryParseRequestedRate(input, 100, 300, out int value, out string error),
                error);
            Assert.Equal(expected, value);
        }

        [Theory]
        [InlineData("125.5")]
        [InlineData("125,5")]
        [InlineData("301")]
        [InlineData("125%%")]
        public void InspectorRateEntryRejectsMalformedOrOutOfRangeValues(string input)
        {
            Assert.False(
                OverclockingInspectorPatch.TryParseRequestedRate(input, 100, 300, out _, out string error));
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }
}
