// Taj's COI Mods | TerrainGridTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Linq;
using TajsCOI.Common.Settings;
using TajsCOI.Tweaks;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class TerrainGridTests
    {
        [Fact]
        public void TerrainGridIsAnImmediateOptInPreference()
        {
            SettingDescriptor descriptor = TajsTweaksSettingsCatalog.All.Single(x => x.Key == TajsTweaksSettingsCatalog.TerrainGrid);

            Assert.Equal(TajsTweaksSettingsCatalog.ModId, descriptor.ModId);
            Assert.Equal(false, descriptor.DefaultValue);
            Assert.Equal(SettingApplyMode.Immediate, descriptor.ApplyMode);
            Assert.Equal(SettingScope.Global, descriptor.Scope);
            Assert.Equal("terrain_grid", descriptor.Key);
        }
    }
}
