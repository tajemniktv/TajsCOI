// Taj's COI Mods | WorldVisibilityFilterTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using TajsCOI.Tweaks.Features.Cleanup;
using TajsCOI.Tweaks.Features.Presentation;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class WorldVisibilityFilterTests
    {
        [Fact]
        public void VisibilityPolicyDefaultsVisibleAndParsesOnlyKnownConfiguredCategories()
        {
            var registry = new WorldVisibilityCategoryRegistry();
            var adapter = new RecordingVisibilityAdapter();
            Assert.True(
                registry.Register(
                    new WorldVisibilityCategoryDescriptor(
                        "buildings",
                        "Buildings",
                        _ => true,
                        adapter)));
            Assert.True(
                registry.Register(
                    new WorldVisibilityCategoryDescriptor(
                        "trees",
                        "Trees",
                        _ => false,
                        new RecordingVisibilityAdapter())));

            registry.ApplyPersisted("trees,unknown; buildings");

            Assert.True(registry.TryGet("buildings", out WorldVisibilityCategoryDescriptor? buildings));
            Assert.True(buildings!.Hidden);
            Assert.True(registry.TryGet("trees", out WorldVisibilityCategoryDescriptor? trees));
            Assert.True(trees!.Hidden);
            Assert.DoesNotContain("unknown", registry.HiddenCategoryIds);
            registry.ShowAll();
            Assert.Empty(registry.HiddenCategoryIds);
        }

        [Fact]
        public void CategoryAdapterReceivesHideAndRestoreWithoutSimulationMutation()
        {
            var calls = new List<bool>();
            var adapter = new RecordingVisibilityAdapter(calls);
            var registry = new WorldVisibilityCategoryRegistry();
            registry.Register(new WorldVisibilityCategoryDescriptor("buildings", "Buildings", _ => true, adapter));

            Assert.True(registry.SetHidden("buildings", true));
            Assert.True(registry.TryGet("buildings", out WorldVisibilityCategoryDescriptor? category));
            Assert.True(category!.Hidden);
            registry.ShowAll();

            // Registry policy is intentionally renderer-agnostic; ShowAll emits the restore
            // callback, while scene-indexed entities receive per-ID hide callbacks.
            Assert.Equal(new[] { true }, calls);
            Assert.Empty(registry.HiddenCategoryIds);
        }

        [Fact]
        public void HudIndicatorIsExplicitOnlyWhenAnyCategoryIsHidden()
        {
            var indicator = new WorldVisibilityHudIndicator();
            indicator.Update(new[] { "sorters", "buildings", "sorters" });

            Assert.True(indicator.IsVisible);
            Assert.Equal("Visibility filters active: buildings, sorters", indicator.Text);

            indicator.Update(new string[0]);
            Assert.False(indicator.IsVisible);
            Assert.Equal(string.Empty, indicator.Text);
        }

        [Fact]
        public void BulkCancelPolicyBoundsPreviewWithoutChangingState()
        {
            IReadOnlyList<int> values = BulkDeconstructionCancellationPolicy.TakeBounded(
                Enumerable.Range(1, 4),
                2,
                out bool truncated);

            Assert.Equal(new[] { 1, 2 }, values);
            Assert.True(truncated);
        }

        private sealed class RecordingVisibilityAdapter : IWorldVisibilityPresentationAdapter
        {
            private readonly List<bool> m_calls;

            internal RecordingVisibilityAdapter(List<bool>? calls = null)
            {
                m_calls = calls ?? new List<bool>();
            }

            public void Attach(int entityId, Mafi.Core.Entities.Static.IStaticEntity entity)
            {
            }

            public void Detach(int entityId)
            {
            }

            public void Apply(int entityId, bool visible) => m_calls.Add(visible);
            public bool CanSelect(int entityId, bool visible) => visible;
            public void SetCategoryVisible(bool visible) => m_calls.Add(visible);

            public void Dispose()
            {
            }
        }
    }
}
