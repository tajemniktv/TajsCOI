// Taj's COI Mods | Wave6ContractsTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using TajsCOI.Tweaks.Features.Fleet;
using TajsCOI.Tweaks.Features.Research;
using TajsCOI.Tweaks.Features.Ships;
using TajsCOI.Tweaks.Features.Trains;
using TajsCOI.Tweaks.Features.World;
using Xunit;

namespace TajsCOI.Tests.RuntimeContracts
{
    public sealed class Wave6ContractsTests
    {
        [Theory]
        [InlineData(1d, 0, "vanilla", 1d)]
        [InlineData(1.5d, 1, "efficient", 1.5d)]
        [InlineData(1d, 1, "efficient", 0.75d)]
        [InlineData(1d, 0, "power", 0.75d)]
        [InlineData(100d, 2, "vanilla", 3d)]
        public void TrainModifierPolicyUsesIndependentBoundedMultipliers(
            double configured,
            int property,
            string profile,
            double expected)
        {
            Assert.Equal(expected, TrainModifierPolicy.ResolveMultiplier(configured, (TrainModifierProperty)property, profile), 6);
        }

        [Fact]
        public void TrainModifierPercentIsRelativeToBase()
        {
            Assert.Equal(-25d, TrainModifierPolicy.ToModifierPercent(0.75d), 6);
            Assert.Equal(0d, TrainModifierPolicy.ToModifierPercent(1d), 6);
            Assert.Equal(50d, TrainModifierPolicy.ToModifierPercent(1.5d), 6);
        }

        [Fact]
        public void WorldBrowserDeduplicatesAndQueriesSafeRows()
        {
            var rows = new[]
            {
                new WorldEntitySnapshot(2, WorldEntityKind.Mine, "Iron mine", 8, 4, "active", true, 10, "iron", "North pit", "Primary extraction"),
                new WorldEntitySnapshot(2, WorldEntityKind.Mine, "Duplicate", 8, 4, "active", true, 10, "iron"),
                new WorldEntitySnapshot(3, WorldEntityKind.Settlement, "Harbor", 1, 2, "unrepaired", false, null, ""),
            };

            IReadOnlyList<WorldEntitySnapshot> snapshot = WorldEntityBrowser.Snapshot(rows);
            IReadOnlyList<WorldEntitySnapshot> result = WorldEntityBrowser.Query(snapshot, new WorldEntityQuery { Search = "iron" });

            Assert.Equal(2, snapshot.Count);
            Assert.Single(result);
            Assert.Equal(2, result[0].Id);

            IReadOnlyList<WorldEntitySnapshot> aliasResult = WorldEntityBrowser.Query(
                snapshot,
                new WorldEntityQuery { Search = "north pit" });
            Assert.Single(aliasResult);
            Assert.Equal("Primary extraction", aliasResult[0].Note);
        }

        [Fact]
        public void WorldBrowserFiltersByKindAndSortsByDistance()
        {
            var rows = WorldEntityBrowser.Snapshot(
                new[]
                {
                    new WorldEntitySnapshot(1, WorldEntityKind.Mine, "Far mine", 8, 6, "active", true, 10, "iron"),
                    new WorldEntitySnapshot(2, WorldEntityKind.Mine, "Near mine", 1, 2, "active", true, 5, "coal"),
                    new WorldEntitySnapshot(3, WorldEntityKind.Settlement, "Harbor", 0, 1, "active", false, null),
                });

            IReadOnlyList<WorldEntitySnapshot> result = WorldEntityBrowser.Query(
                rows,
                new WorldEntityQuery { Kind = WorldEntityKind.Mine, SortBy = WorldEntitySortField.Distance });

            Assert.Equal(new[] { 2, 1 }, result.Select(row => row.Id));
        }

        [Fact]
        public void MineDepletionRequiresOwnedFiniteKnownZero()
        {
            Assert.True(MineDepletionClassifier.IsDepleted(true, 0));
            Assert.False(MineDepletionClassifier.IsDepleted(false, 0));
            Assert.False(MineDepletionClassifier.IsDepleted(true, null));
            Assert.False(MineDepletionClassifier.IsDepleted(true, double.NaN));
        }

        [Fact]
        public void ShipUnloadSelectorProtectsReservationsAndChoosesSmallest()
        {
            UnloadBufferCandidate? selected = ShipUnloadSelector.Select(
                ShipUnloadPolicy.SmallestStackFirst,
                new[]
                {
                    new UnloadBufferCandidate("b1", "iron", 40, true, false, false),
                    new UnloadBufferCandidate("b2", "steel", 5, true, false, true),
                    new UnloadBufferCandidate("b3", "copper", 10, true, false, false),
                });

            Assert.True(selected.HasValue);
            Assert.Equal("copper", selected.Value.ProductId);
        }

        [Fact]
        public void ResearchQueueReorderRejectsLockedAndDuplicateEntries()
        {
            var entries = new[]
            {
                new ResearchQueueEntry("a", true, false, false),
                new ResearchQueueEntry("b", true, false, true),
                new ResearchQueueEntry("a", true, false, false),
            };

            IReadOnlyList<ResearchQueueEntry> validated = ResearchQueuePolicy.Validate(entries);
            Assert.Single(validated);
            Assert.False(ResearchQueuePolicy.CanReorder(new[] { "a", "b" }, "missing", 0));
        }

        [Fact]
        public void FleetPlannerHonorsUnassignedFirstAndCap()
        {
            var vehicles = new[]
            {
                new FleetVehicleSnapshot(5, "truck", true, null, null, null),
                new FleetVehicleSnapshot(2, "truck", false, null, null, null),
                new FleetVehicleSnapshot(3, "truck", false, null, null, null),
            };
            IReadOnlyList<int> selected = FleetReplacementPlanner.Match(
                vehicles,
                new FleetReplacementFilterSnapshot("truck", "", "unassigned-first", null, null, null, 2));

            Assert.Equal(new[] { 2, 3 }, selected);
        }

        [Fact]
        public void FleetPlannerUsesZoneMasksWithoutCollapsingMultiZoneVehicles()
        {
            var vehicles = new[]
            {
                new FleetVehicleSnapshot(1, "truck", false, null, null, null, 0b0010UL),
                new FleetVehicleSnapshot(2, "truck", false, null, null, null, 0b0110UL),
                new FleetVehicleSnapshot(3, "truck", false, null, null, null, 0b1000UL),
            };

            IReadOnlyList<int> selected = FleetReplacementPlanner.Match(
                vehicles,
                new FleetReplacementFilterSnapshot("truck", "truck-t2", string.Empty, null, null, null, 10, 0b0100UL));

            Assert.Equal(new[] { 2 }, selected);
        }

        [Fact]
        public void LocomotiveNumberingIsDeterministicAndCollisionSafe()
        {
            IReadOnlyList<int> assigned = LocomotiveNumbering.Assign(
                new[] { 3, 1, 2 },
                new[] { 1 },
                4,
                LocomotiveNumberAssignment.Sequential,
                7);

            Assert.Equal(new[] { 2, 3, 4 }, assigned);
        }

        [Fact]
        public void LocomotiveNumberingUsesNativeTypeDigitRanges()
        {
            Assert.True(LocomotiveNumbering.TryGetSupportedRange(3, out int minimum, out int maximum));
            Assert.Equal(301, minimum);
            Assert.Equal(400, maximum);
            Assert.True(LocomotiveNumbering.IsValidForType(301, 3));
            Assert.True(LocomotiveNumbering.IsValidForType(400, 3));
            Assert.False(LocomotiveNumbering.IsValidForType(300, 3));
            Assert.False(LocomotiveNumbering.IsValidForType(401, 3));
            Assert.False(LocomotiveNumbering.TryGetSupportedRange(0, out _, out _));
        }

        [Fact]
        public void LocomotiveNumberingAssignsWithinNativeRange()
        {
            IReadOnlyList<int> assigned = LocomotiveNumbering.AssignInRange(
                new[] { 9, 2, 4 },
                new[] { 301, 303 },
                301,
                305,
                LocomotiveNumberAssignment.Sequential,
                12);

            Assert.Equal(new[] { 302, 304, 305 }, assigned);
        }
    }
}
