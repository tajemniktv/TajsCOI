// Taj's COI Mods | DifficultyTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Linq;
using Mafi;
using Mafi.Core.Game;
using TajsCOI.Tweaks.Features.Difficulty;
using Xunit;
using XAssert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class DifficultyTests
    {
        [Fact]
        public void CatalogCoversEveryRequestedPercentDomainWithBoundedRanges()
        {
            string[] requested =
            {
                "ExtraContractsProfit",
                "TreesGrowthDiff",
                "ExtraStartingMaterial",
                "MaintenanceDiff",
                "FuelConsumptionDiff",
                "RainYieldDiff",
                "BaseHealthDiff",
                "ResourceMiningDiff",
                "SettlementConsumptionDiff",
                "SettlementFoodConsumptionDiff",
                "WorldMinesReservesDiff",
                "FarmsYieldDiff",
                "UnityProductionDiff",
                "SolarPowerDiff",
                "ConstructionCostsDiff",
                "ResearchCostDiff",
                "DiseaseMortalityDiff",
                "PollutionDiff",
                "QuickActionsCostDiff",
            };

            foreach (string memberName in requested)
            {
                TajsDifficultyDefinition definition = XAssert.Single(
                    TajsDifficultyOptionCatalog.Definitions,
                    value => value.MemberName == memberName);
                XAssert.NotNull(definition.Range);
                XAssert.InRange(definition.Range!.Minimum, -100, 0);
                XAssert.InRange(definition.Range.Maximum, 0, 2000);
                XAssert.True(definition.Range.Step > 0);
            }
        }

        [Fact]
        public void ExtendedRangesRetainVanillaPresetValues()
        {
            string[][] required =
            {
                new[] { "MaintenanceDiff", "-75", "-50", "-25", "0", "25", "50" },
                new[] { "FuelConsumptionDiff", "-30", "-15", "0", "15", "30" },
                new[] { "FarmsYieldDiff", "-50", "-25", "0", "25", "50" },
                new[] { "SolarPowerDiff", "-25", "0", "25", "50", "100" },
            };

            foreach (string[] requirement in required)
            {
                TajsDifficultyRange range = TajsDifficultyOptionCatalog.FindRange(requirement[0]);
                int[] values = range.Values().ToArray();
                foreach (string expected in requirement.Skip(1))
                {
                    XAssert.Contains(int.Parse(expected), values);
                }
            }
        }

        [Fact]
        public void CatalogIncludesNativeEnumDifficultyDomainsAndConservativeExtremes()
        {
            XAssert.Contains(TajsDifficultyOptionCatalog.Definitions, value => value.MemberName == "TrainsNoFuel");
            XAssert.Contains(TajsDifficultyOptionCatalog.Definitions, value => value.MemberName == "Sandbox");
            XAssert.DoesNotContain(
                TajsDifficultyOptionCatalog.Definitions.Where(value => value.Range is not null),
                value => value.Range!.Maximum > 2000);
        }

        [Fact]
        public void StateStoreRoundTripsPercentAndEnumScalars()
        {
            XAssert.True(TajsDifficultyStateStore.TryEncode(25.Percent(), out string encodedPercent));
            XAssert.True(TajsDifficultyStateStore.TryDecode(encodedPercent, typeof(Percent), out object? decodedPercent));
            XAssert.Equal(25, ((Percent)decodedPercent!).ToIntPercentRounded());

            XAssert.True(TajsDifficultyStateStore.TryEncode(GameDifficultyConfig.TrainsNoFuelSetting.Stop, out string encodedEnum));
            XAssert.True(
                TajsDifficultyStateStore.TryDecode(
                    encodedEnum,
                    typeof(GameDifficultyConfig.TrainsNoFuelSetting),
                    out object? decodedEnum));
            XAssert.Equal(GameDifficultyConfig.TrainsNoFuelSetting.Stop, decodedEnum);
        }

        [Fact]
        public void StateStoreUsesExplicitUnlimitedMarker()
        {
            XAssert.True(TajsDifficultyStateStore.TryEncode(Percent.MaxValue, out string encoded));
            XAssert.Equal("unlimited", encoded);
            XAssert.True(TajsDifficultyStateStore.TryDecode(encoded, typeof(Percent), out object? decoded));
            XAssert.Equal(Percent.MaxValue, decoded);
        }
    }
}
