// Taj's COI Mods | Wave5SelectionTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using TajsCOI.Tweaks.Features.Selection;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class Wave5SelectionTests
    {
        [Fact]
        public void SceneAreaBoundsNormalizesAndEnforcesBoundedArea()
        {
            var bounds = new SceneAreaBounds(5, 8, 2, 4);
            Assert.Equal(2, bounds.MinX);
            Assert.Equal(4, bounds.MinY);
            Assert.Equal(5, bounds.MaxX);
            Assert.Equal(8, bounds.MaxY);
            Assert.Equal(20, bounds.Area);
            Assert.True(bounds.Contains(3, 6));
            Assert.False(bounds.Contains(1, 6));
            Assert.True(bounds.IsWithin(64, 64, 4096));
            Assert.False(new SceneAreaBounds(0, 0, 64, 0).IsWithin(64, 64, 4096));
        }

        [Fact]
        public void SceneSelectionDiffReportsEnteredAndExitedOnly()
        {
            SceneSelectionDiff.Compute(
                new[] { "old", "shared", "duplicate", "duplicate" },
                new[] { "shared", "new", "new" },
                null,
                out IReadOnlyList<string> entered,
                out IReadOnlyList<string> exited);

            Assert.Single(entered);
            Assert.Equal("new", entered[0]);
            Assert.Equal(2, exited.Count);
            Assert.Contains("old", exited);
            Assert.Contains("duplicate", exited);
        }

        [Fact]
        public void HighlightPoolReleasesExitedAndAllHandlesOnDispose()
        {
            int next = 0;
            var released = new List<int>();
            var pool = new PooledHighlightUtility<string, int>(
                () => ++next,
                (_, _) => { },
                handle => released.Add(handle));

            pool.Set(new[] { "a", "b" });
            Assert.Equal(2, pool.Count);
            pool.Set(new[] { "b", "c" });
            Assert.Equal(2, pool.Count);
            Assert.Contains(1, released);
            pool.Dispose();
            Assert.Equal(0, pool.Count);
            Assert.Equal(3, released.Count);
        }
    }
}
