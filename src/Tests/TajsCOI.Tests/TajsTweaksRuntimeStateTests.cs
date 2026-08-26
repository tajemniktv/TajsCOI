// Taj's COI Mods | TajsTweaksRuntimeStateTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using TajsCOI.Tweaks;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class TajsTweaksRuntimeStateTests
    {
        [Fact]
        public void TowerColorOverridesIgnoreMalformedAndOutOfRangeEntries()
        {
            IReadOnlyDictionary<int, int> actual = TajsTweaksRuntimeState.ParseTowerColors(
                "12=3;bad;13=8;14=9;15=-1;16=x;17=0");

            Assert.Equal(3, actual[12]);
            Assert.Equal(8, actual[13]);
            Assert.Equal(0, actual[17]);
            Assert.DoesNotContain(14, actual.Keys);
            Assert.DoesNotContain(15, actual.Keys);
            Assert.DoesNotContain(16, actual.Keys);
        }

        [Fact]
        public void TowerColorOverridesFormatInStableTowerOrder()
        {
            var colors = new Dictionary<int, int> { [42] = 2, [3] = 7, [19] = 1 };

            Assert.Equal("3=7,19=1,42=2", TajsTweaksRuntimeState.FormatTowerColors(colors));
        }
    }
}
