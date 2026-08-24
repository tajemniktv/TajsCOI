// Taj's COI Mods | ProductBufferShrinkTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using TajsCOI.Performance.Features.ProductBufferShrink;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class ProductBufferShrinkTests : IDisposable
    {
        private readonly int m_observationFrames = ProductBufferShrinkSettings.ObservationFrames;

        public void Dispose() => ProductBufferShrinkSettings.Update(m_observationFrames);

        [Fact]
        public void RequiresSustainedLargeUnderutilization()
        {
            var tracker = new BufferShrinkTracker(observationFrames: 3, cooldownFrames: 2, minimumCapacity: 1024);

            Assert.False(tracker.Observe(200, 2048));
            Assert.False(tracker.Observe(200, 2048));
            Assert.True(tracker.Observe(200, 2048));
            Assert.False(tracker.Observe(200, 2048));
            Assert.False(tracker.Observe(200, 2048));
            Assert.False(tracker.Observe(600, 2048));
        }

        [Fact]
        public void ResetsWhenCapacityOrUtilizationChanges()
        {
            var tracker = new BufferShrinkTracker(observationFrames: 2, cooldownFrames: 0, minimumCapacity: 1024);

            Assert.False(tracker.Observe(200, 2048));
            Assert.False(tracker.Observe(200, 4096));
            Assert.True(tracker.Observe(200, 4096));
            Assert.False(tracker.Observe(700, 2048));
            Assert.False(tracker.Observe(200, 2048));
            Assert.True(tracker.Observe(200, 2048));
        }

        [Fact]
        public void ObservationWindowIsBounded()
        {
            ProductBufferShrinkSettings.Update(1);
            Assert.Equal(120, ProductBufferShrinkSettings.ObservationFrames);
            ProductBufferShrinkSettings.Update(10_000);
            Assert.Equal(3600, ProductBufferShrinkSettings.ObservationFrames);
        }
    }
}
