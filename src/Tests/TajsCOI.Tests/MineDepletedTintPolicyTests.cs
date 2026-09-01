// Taj's COI Mods | MineDepletedTintPolicyTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi.Core.World;
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

        [Fact]
        public void NativeColorSetterUsesThe0_8_7bFiveArgumentSignature()
        {
            System.Reflection.MethodInfo? method = MineDepletedTintFeature.FindLocationColorMethod(typeof(TestLocationPin));

            Assert.NotNull(method);
            Assert.Equal("setLocationColor", method!.Name);
            Assert.Equal(typeof(void), method.ReturnType);
            Assert.Equal(
                new[] { "Mafi.Core.World.WorldMapLocationState", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean" },
                System.Array.ConvertAll(method.GetParameters(), parameter => parameter.ParameterType.FullName));
        }

        private sealed class TestLocationPin
        {
            private void setLocationColor(WorldMapLocationState state, bool hasEnemy, bool isOwnedByPlayer, bool isEnemyKnown, bool hasEntity)
            {
            }
        }
    }
}
