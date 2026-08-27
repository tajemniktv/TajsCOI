// Taj's COI Mods | PresentationLayoutTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using TajsCOI.Common.Settings;
using TajsCOI.Tweaks;
using TajsCOI.Tweaks.Features.Presentation;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class PresentationLayoutTests
    {
        [Fact]
        public void ResearchSpacingUsesRawGridCoordinatesAndRestoresVanilla()
        {
            ResearchTreeSpacingPolicy vanilla = ResearchTreeSpacingPolicy.Resolve("vanilla");
            ResearchTreeSpacingPolicy compact = ResearchTreeSpacingPolicy.Resolve("compact");

            Assert.Equal((488, 220), vanilla.Apply(4, 4));
            Assert.Equal((392, 176), compact.Apply(4, 4));
            Assert.False(vanilla.IsCompact);
            Assert.True(compact.IsCompact);
            Assert.Equal((488, 220), ResearchTreeSpacingPolicy.Resolve("unknown").Apply(4, 4));
        }

        [Fact]
        public void RecipePickerPoliciesAreBoundedAndKeepVanillaDefaults()
        {
            RecipePickerLayoutPolicy vanilla = RecipePickerLayoutPolicy.Resolve("vanilla", 1, 0, 4);
            RecipePickerLayoutPolicy compact = RecipePickerLayoutPolicy.Resolve("compact", 36, 1, 1);
            RecipePickerLayoutPolicy custom = RecipePickerLayoutPolicy.Resolve("custom", 999, -10, 99);

            Assert.True(vanilla.IsVanilla);
            Assert.Equal(2, compact.Columns);
            Assert.Equal(0.75f, compact.TileScale);
            Assert.Equal(0, compact.SpacingPoints);
            Assert.Equal(72f / 36f, custom.TileScale);
            Assert.Equal(0d, custom.SpacingPoints);
            Assert.Equal(4, custom.Columns);
        }

        [Fact]
        public void PresentationSettingsAreImmediateAndCaptureVanillaDimensions()
        {
            SettingDescriptorAssert(TajsTweaksSettingsCatalog.ResearchTreeLayout, "vanilla");
            SettingDescriptorAssert(TajsTweaksSettingsCatalog.RecipePickerDensity, "vanilla");

            var tile = TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.RecipePickerTileSize);
            Assert.Equal(36, tile.DefaultValue);
            Assert.Equal(24, tile.Minimum);
            Assert.Equal(72, tile.Maximum);

            var spacing = TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.RecipePickerSpacing);
            Assert.Equal(1d, spacing.DefaultValue);
            Assert.Equal(0d, spacing.Minimum);
            Assert.Equal(8d, spacing.Maximum);
        }

        [Fact]
        public void HudActionPolicyUsesStableIdsAndUnknownActionsRemainVisible()
        {
            IReadOnlyDictionary<string, HudActionPreference> policy = HudActionPolicyCodec.Parse("calendar.speed.0=2:false:true;status.food.0=0:true:false");

            Assert.Equal(2, policy["calendar.speed.0"].Order);
            Assert.False(policy["calendar.speed.0"].Visible == true);
            Assert.True(policy["calendar.speed.0"].Core);
            Assert.True(new HudActionPreference(null, true, false).Visible == true);
        }

        [Fact]
        public void TowerInspectorPresentationPreferencesAreGlobalAndBounded()
        {
            SettingDescriptorAssert(TajsTweaksSettingsCatalog.AdaptiveTowerInspector, false);
            SettingDescriptorAssert(TajsTweaksSettingsCatalog.InspectorSectionCollapsed, string.Empty);
            SettingDescriptorAssert(TajsTweaksSettingsCatalog.InspectorVehicleFilters, "excavator,truck,tree_planter,tree_harvester");

            var rows = TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.InspectorVehicleVisibleRows);
            Assert.Equal(8, rows.DefaultValue);
            Assert.Equal(3, rows.Minimum);
            Assert.Equal(24, rows.Maximum);
            Assert.Equal(SettingScope.Global, rows.Scope);
        }

        private static void SettingDescriptorAssert(string key, string defaultValue)
        {
            var descriptor = TajsTweaksSettingsCatalog.All.Single(x => x.Key == key);
            Assert.Equal(defaultValue, descriptor.DefaultValue);
            Assert.Equal(SettingApplyMode.Immediate, descriptor.ApplyMode);
            Assert.Equal(SettingScope.Global, descriptor.Scope);
        }

        private static void SettingDescriptorAssert(string key, bool defaultValue)
        {
            var descriptor = TajsTweaksSettingsCatalog.All.Single(x => x.Key == key);
            Assert.Equal(defaultValue, descriptor.DefaultValue);
            Assert.Equal(SettingApplyMode.Immediate, descriptor.ApplyMode);
            Assert.Equal(SettingScope.Global, descriptor.Scope);
        }
    }
}
