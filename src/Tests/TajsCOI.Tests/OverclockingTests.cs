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
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class OverclockingTests
    {
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
