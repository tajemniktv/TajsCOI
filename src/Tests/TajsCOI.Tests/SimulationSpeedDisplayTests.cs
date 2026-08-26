// Taj's COI Mods | SimulationSpeedDisplayTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Tweaks;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class SimulationSpeedDisplayTests
    {
        [Theory]
        [InlineData(1, "1x")]
        [InlineData(15, "15x")]
        public void FormatsRequestedMultiplierLikeSpeedPlusPlus(int speed, string expected) => Assert.Equal(expected, SimulationSpeedDisplayText.Format(speed));
    }
}
