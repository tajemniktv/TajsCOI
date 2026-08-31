// Taj's COI Mods | AutoExplorationTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Linq;
using TajsCOI.Tweaks.Features.World;
using TajsCOI.Tweaks;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class AutoExplorationTests
    {
        [Theory]
        [InlineData(false, true, false, false, false, true)]
        [InlineData(true, false, false, false, false, true)]
        [InlineData(true, true, true, false, false, true)]
        [InlineData(true, true, false, true, false, true)]
        [InlineData(true, true, false, false, true, true)]
        [InlineData(true, true, false, false, false, false)]
        public void ReadinessBlocksEveryUnsafeDispatchState(
            bool docked,
            bool home,
            bool inBattle,
            bool repairing,
            bool cargoLoaded,
            bool fuelSufficient)
        {
            Assert.False(ExplorationCandidatePolicy.IsReady(
                docked,
                home,
                inBattle,
                repairing,
                cargoLoaded,
                fuelSufficient));
        }

        [Fact]
        public void ManualOrderClaimBlocksAutomaticExploration()
        {
            WorldShipOrderArbiter arbiter = WorldShipOrderArbiter.Shared;
            const int shipId = 99114;
            arbiter.Clear();
            try
            {
                arbiter.SetManualOrder(shipId, true);

                Assert.True(arbiter.ManualOrderActive(shipId));
                Assert.False(arbiter.CanClaim(shipId, WorldShipOrderOwner.AutoExploration));
                Assert.False(arbiter.TryClaim(shipId, WorldShipOrderOwner.AutoExploration));
            }
            finally
            {
                arbiter.Clear();
            }
        }

        [Fact]
        public void ViabilityCombinesReadinessAndKnownCombatMargin()
        {
            Assert.True(ExplorationCandidatePolicy.IsViable(
                true, true, false, false, false, true, true, 25, 25, false));
            Assert.False(ExplorationCandidatePolicy.IsViable(
                true, true, false, false, false, true, true, 24, 25, false));
            Assert.False(ExplorationCandidatePolicy.IsViable(
                true, false, false, false, false, true, true, 100, 0, false));
            Assert.False(ExplorationCandidatePolicy.IsViable(
                true, true, false, false, false, true, false, 0, 0, false));
        }

        [Fact]
        public void CombatSafetyRequiresConfiguredNativeScoreMargin()
        {
            Assert.True(ExplorationCandidatePolicy.IsCombatSafe(true, true, 125, 100, 25, false));
            Assert.False(ExplorationCandidatePolicy.IsCombatSafe(true, true, 124, 100, 25, false));
            Assert.True(ExplorationCandidatePolicy.IsCombatSafe(true, false, null, null, 500, false));
        }

        [Fact]
        public void CombatSafetyBlocksUnknownByDefaultAndOnlyExplicitPolicyAllowsIt()
        {
            Assert.False(ExplorationCandidatePolicy.IsCombatSafe(false, true, 500, null, 0, false));
            Assert.True(ExplorationCandidatePolicy.IsCombatSafe(false, true, 500, null, 0, true));
            Assert.False(ExplorationCandidatePolicy.IsCombatSafe(true, true, null, 100, 0, false));
            Assert.False(ExplorationCandidatePolicy.IsCombatSafe(true, true, -1, 100, 0, true));
            Assert.False(ExplorationCandidatePolicy.IsCombatSafe(false, true, 500, null, double.NaN, true));
        }

        [Fact]
        public void UnknownStrengthSettingIsExplicitAndConservativeByDefault()
        {
            Assert.Equal(
                false,
                TajsTweaksSettingsCatalog.All
                    .Single(descriptor => descriptor.Key == TajsTweaksSettingsCatalog.AutoExplorationAllowUnknownStrength)
                    .DefaultValue);
            Assert.Equal(
                25d,
                TajsTweaksSettingsCatalog.All
                    .Single(descriptor => descriptor.Key == TajsTweaksSettingsCatalog.AutoExplorationSafetyMarginPercent)
                    .DefaultValue);
        }

        [Fact]
        public void CandidateSelectionIsNearestThenLowestLocationId()
        {
            ExplorationCandidate? selected = ExplorationCandidatePolicy.ChooseNearest(
                new[]
                {
                    new ExplorationCandidate(7, 30, true, false, double.NaN),
                    new ExplorationCandidate(4, 20, true, true, 10),
                    new ExplorationCandidate(2, 20, true, true, 15),
                    new ExplorationCandidate(1, 10, false, true, 100),
                });

            Assert.True(selected.HasValue);
            Assert.Equal(2, selected.Value.LocationId);
        }
    }
}
