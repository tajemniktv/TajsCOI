// Taj's COI Mods | EfficiencyOverlayTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi.Core.Factory;
using TajsCOI.Tweaks;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class EfficiencyOverlayTests
    {
        [Fact]
        public void EmptyProductivityHistoryIsUnavailable()
        {
            Assert.Equal(-1, EfficiencyOverlayPresentation.Percentage(new ProductivityCounterHistory.Data()));
            Assert.Equal("—", EfficiencyOverlayPresentation.Format("percentage", -1, "Idle"));
        }

        [Fact]
        public void WorkingPercentageUsesAllHistoryCategories()
        {
            var data = new ProductivityCounterHistory.Data(8, 1, 1, 0);
            Assert.Equal(80, EfficiencyOverlayPresentation.Percentage(data));
            Assert.Equal("80%", EfficiencyOverlayPresentation.Format("percentage", 80, "Working"));
        }

        [Theory]
        [InlineData("status", 50, "Working", "Working")]
        [InlineData("compact", 50, "Working", "●")]
        public void DisplayModesRemainCompact(string mode, int percentage, string status, string expected)
        {
            Assert.Equal(expected, EfficiencyOverlayPresentation.Format(mode, percentage, status));
        }
    }
}
