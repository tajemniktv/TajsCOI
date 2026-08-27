// Taj's COI Mods | HeightLayerFilterTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using TajsCOI.Tweaks.Features.Presentation;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class HeightLayerFilterTests
    {
        [Fact]
        public void CutoffKeepsEntitiesWhoseRangeCrossesTheActiveLayer()
        {
            var index = new HeightLayerSceneIndex();
            index.Register(1, 0, 2, "building");
            index.Register(2, 4, 5, "building");
            index.Register(3, 2, 6, "transport");

            index.SetCutoff(2);

            Assert.True(index.IsVisible(1));
            Assert.False(index.IsVisible(2));
            Assert.True(index.IsVisible(3));
            Assert.Equal(new[] { 1, 3 }, index.QueryVisible().Select(record => record.EntityId));
        }

        [Fact]
        public void CutoffChangesOnlyPresentationAndShowAllRestoresEveryBinding()
        {
            var calls = new List<string>();
            var binding = new HeightLayerRenderBinding(
                visible => calls.Add("renderer:" + visible),
                visible => calls.Add("ports:" + visible),
                enabled => calls.Add("hit:" + enabled));
            var index = new HeightLayerSceneIndex();
            index.Register(9, 4, 4, "building", binding);

            IReadOnlyList<HeightLayerVisibilityChange> hidden = index.SetCutoff(1);
            Assert.Single(hidden);
            Assert.False(hidden[0].Visible);
            Assert.Contains("renderer:False", calls);
            Assert.Contains("ports:False", calls);
            Assert.Contains("hit:False", calls);

            IReadOnlyList<HeightLayerVisibilityChange> restored = index.ShowAll();
            Assert.Single(restored);
            Assert.True(restored[0].Visible);
            Assert.Equal("renderer:True", calls.Last(x => x.StartsWith("renderer:")));
            Assert.Equal("ports:True", calls.Last(x => x.StartsWith("ports:")));
            Assert.Equal("hit:True", calls.Last(x => x.StartsWith("hit:")));
        }

        [Fact]
        public void ReplacingOrRemovingAnEntityDoesNotLeaveAnOldRangeOrBinding()
        {
            int restored = 0;
            var index = new HeightLayerSceneIndex();
            index.Register(7, 0, 0, "old", new HeightLayerRenderBinding(setRendererVisible: _ => restored++));
            index.SetCutoff(1);
            index.Register(7, 8, 9, "new");

            Assert.False(index.Records.ContainsKey(7) && index.Records[7].Category == "old");
            Assert.False(index.IsVisible(7));
            Assert.True(index.Remove(7));
            Assert.True(index.IsVisible(7));
            Assert.True(restored > 0);
        }

        [Fact]
        public void CategoryQueryIsAppliedAfterLayerVisibility()
        {
            var index = new HeightLayerSceneIndex();
            index.Register(3, 0, 8, "transport");
            index.Register(4, 8, 9, "transport");
            index.Register(5, 0, 8, "building");
            index.SetCutoff(3);

            Assert.Equal(new[] { 3 }, index.QueryVisible("transport").Select(record => record.EntityId));
        }
    }
}
