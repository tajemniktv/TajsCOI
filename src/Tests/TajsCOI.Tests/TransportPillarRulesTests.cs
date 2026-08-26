// Taj's COI Mods | TransportPillarRulesTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using Mafi.Core.Entities.Static.Layout;
using TajsCOI.Common.Settings;
using TajsCOI.Tweaks;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class TransportPillarRulesTests
    {
        [Fact]
        public void PillarSettingsAreBoundedRestartScopedAndShowVanillaValues()
        {
            SettingDescriptor[] settings = TajsTweaksSettingsCatalog.All
                .Where(x => x.Key == TajsTweaksSettingsCatalog.TransportPillarSupportRadius ||
                            x.Key == TajsTweaksSettingsCatalog.TransportPillarMaxHeight ||
                            x.Key == TajsTweaksSettingsCatalog.TrainTrackPillarMaxHeight ||
                            x.Key == TajsTweaksSettingsCatalog.TrainTrackPillarSupportDistance ||
                            x.Key == TajsTweaksSettingsCatalog.IgnorePillarRequirements)
                .ToArray();

            Assert.Equal(5, settings.Length);
            Assert.All(
                settings.Where(x => x.Key != TajsTweaksSettingsCatalog.IgnorePillarRequirements),
                setting =>
                {
                    Assert.Equal(SettingApplyMode.RestartGame, setting.ApplyMode);
                    Assert.Contains("vanilla:", setting.DisplayName, StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(SettingValueType.Integer, setting.ValueType);
                    Assert.Equal(1d, setting.Minimum);
                });
            Assert.Equal(
                TransportPillarRulesFeature.MaxConfiguredSupportRadius,
                settings.Single(x => x.Key == TajsTweaksSettingsCatalog.TransportPillarSupportRadius).Maximum);
            Assert.Equal(
                TransportPillarRulesFeature.MaxConfiguredPillarHeight,
                settings.Single(x => x.Key == TajsTweaksSettingsCatalog.TransportPillarMaxHeight).Maximum);
            Assert.Equal(
                TransportPillarRulesFeature.MaxConfiguredTrainSupportDistance,
                settings.Single(x => x.Key == TajsTweaksSettingsCatalog.TrainTrackPillarSupportDistance).Maximum);
            SettingDescriptor ignore = settings.Single(x => x.Key == TajsTweaksSettingsCatalog.IgnorePillarRequirements);
            Assert.Equal(SettingApplyMode.RestartGame, ignore.ApplyMode);
            Assert.Equal(SettingValueType.Boolean, ignore.ValueType);
            Assert.False((bool)ignore.DefaultValue);
        }

        [Fact]
        public void VanillaDefaultsMatchTheNativePillarRules()
        {
            Assert.Equal(4, TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.TransportPillarSupportRadius).DefaultValue);
            Assert.Equal(6, TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.TransportPillarMaxHeight).DefaultValue);
            Assert.Equal(6, TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.TrainTrackPillarMaxHeight).DefaultValue);
            Assert.Equal(7, TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.TrainTrackPillarSupportDistance).DefaultValue);
        }

        [Fact]
        public void IgnorePillarRequirementRemovesOnlyThePillarConstraintBit()
        {
            LayoutTileConstraint original = LayoutTileConstraint.UsingPillar | LayoutTileConstraint.Ground;

            LayoutTileConstraint result = TransportPillarRulesFeature.RemovePillarConstraint(original);

            Assert.Equal(LayoutTileConstraint.Ground, result);
        }

        [Fact]
        public void AreaBoundsAreFiniteAndInclusive()
        {
            Assert.True(TransportPillarRulesFeature.IsAreaWithinBounds(-10, 4, 53, 67, out int width, out int height));
            Assert.Equal(64, width);
            Assert.Equal(64, height);
            Assert.False(TransportPillarRulesFeature.IsAreaWithinBounds(0, 0, 64, 0, out _, out _));
            Assert.False(TransportPillarRulesFeature.IsAreaWithinBounds(4, 0, 3, 0, out _, out _));
            Assert.False(TransportPillarRulesFeature.IsAreaWithinBounds(int.MinValue, 0, int.MaxValue, 0, out _, out _));
        }
    }
}
