// Taj's COI Mods | UnlockedSpeedTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Linq;
using TajsCOI.Tweaks.Features.UnlockedSpeed;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class UnlockedSpeedTests
    {
        [Fact]
        public void VanillaSequencePreservesNormalSpeedSteps()
        {
            Assert.Equal(
                new[] { 1, 2, 3, 12 },
                SpeedSequence.Build(100, UnlockedSpeedSetting.VanillaSequenceMode, string.Empty));
        }

        [Fact]
        public void EveryIntegerSequenceIsBoundedByConfiguredMaximum()
        {
            Assert.Equal(
                new[] { 1, 2, 3, 4, 5 },
                SpeedSequence.Build(5, UnlockedSpeedSetting.EveryIntegerSequenceMode, string.Empty));
        }

        [Fact]
        public void CustomSequenceIsSortedDistinctAndAlwaysReachesOneAndMaximum()
        {
            Assert.Equal(
                new[] { 1, 7, 20, 100 },
                SpeedSequence.Build(100, UnlockedSpeedSetting.CustomSequenceMode, "100,7,7,invalid,20,0"));
        }

        [Fact]
        public void EmptyOrInvalidCustomSequenceFallsBackToVanilla()
        {
            int[] actual = SpeedSequence.Build(100, UnlockedSpeedSetting.CustomSequenceMode, "invalid,0,-4")
                .ToArray();
            Assert.Equal(new[] { 1, 2, 3, 12 }, actual);
        }
    }
}
