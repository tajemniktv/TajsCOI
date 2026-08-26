// Taj's COI Mods | OverclockingTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

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
    }
}
