// Taj's COI Mods | PolygonConstraintTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Tweaks.Features.Selection;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class PolygonConstraintTests
    {
        [Fact]
        public void AxisConstraintLocksTheNonDominantCoordinate()
        {
            PolygonVector2 result = PolygonConstraintMath.Apply(new PolygonVector2(10f, 10f), new PolygonVector2(14f, 12f), true, false);

            Assert.Equal(14f, result.X);
            Assert.Equal(10f, result.Y);
        }

        [Fact]
        public void AxisConstraintUsesStableXWinnerForTies()
        {
            PolygonVector2 result = PolygonConstraintMath.Apply(new PolygonVector2(0f, 0f), new PolygonVector2(-3f, 3f), true, false);

            Assert.Equal(-3f, result.X);
            Assert.Equal(0f, result.Y);
        }

        [Fact]
        public void GridSnapUsesOriginalIntendedPosition()
        {
            PolygonVector2 origin = new(10.2f, 10.2f);
            PolygonVector2 intended = new(12.6f, 11.4f);
            PolygonVector2 result = PolygonConstraintMath.Apply(origin, intended, false, true);

            Assert.Equal(13f, result.X);
            Assert.Equal(11f, result.Y);
            Assert.Equal(12.6f, intended.X);
            Assert.Equal(11.4f, intended.Y);
        }

        [Fact]
        public void GridSnapUsesConfiguredInterval()
        {
            PolygonVector2 result = PolygonConstraintMath.Apply(
                new PolygonVector2(0f, 0f),
                new PolygonVector2(2.9f, 5.1f),
                false,
                true,
                gridSize: 2f);

            Assert.Equal(2f, result.X);
            Assert.Equal(6f, result.Y);
        }

        [Fact]
        public void AxisRunsBeforeGridAndOnlyTheFreeAxisSnaps()
        {
            PolygonVector2 result = PolygonConstraintMath.Apply(new PolygonVector2(10.2f, 10.2f), new PolygonVector2(12.6f, 11.4f), true, true);

            Assert.Equal(13f, result.X);
            Assert.Equal(10.2f, result.Y);
        }

        [Fact]
        public void NoModifierLeavesCursorUntouched()
        {
            PolygonVector2 intended = new(12.6f, 11.4f);

            Assert.Equal(intended, PolygonConstraintMath.Apply(new PolygonVector2(10f, 10f), intended, false, false));
        }
    }
}
