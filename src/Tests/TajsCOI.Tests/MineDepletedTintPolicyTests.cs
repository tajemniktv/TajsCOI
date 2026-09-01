// Taj's COI Mods | MineDepletedTintPolicyTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Tweaks.Features.World;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class MineDepletedTintPolicyTests
    {
        [Fact]
        public void NativeBaselineIsRefreshedOnEveryNativeColorUpdate()
        {
            var baseline = new MineDepletedTintFeature.OriginalColors();
            var firstNormal = new object();
            var firstHover = new object();
            var latestNormal = new object();
            var latestHover = new object();

            baseline.CaptureNative(firstNormal, firstHover);
            baseline.CaptureNative(latestNormal, latestHover);

            Assert.True(baseline.HasBaseline);
            Assert.Same(latestNormal, baseline.Normal);
            Assert.Same(latestHover, baseline.Hover);
        }

        [Fact]
        public void NullNativeValuesDoNotReplaceAUsableBaseline()
        {
            var baseline = new MineDepletedTintFeature.OriginalColors();
            var nativeNormal = new object();
            var nativeHover = new object();

            Assert.True(baseline.CaptureNative(nativeNormal, nativeHover));
            Assert.False(baseline.CaptureNative(null, null));

            Assert.True(baseline.HasBaseline);
            Assert.Same(nativeNormal, baseline.Normal);
            Assert.Same(nativeHover, baseline.Hover);
        }
    }
}
