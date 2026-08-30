// Taj's COI Mods | Wave6ContractsTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
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
        [Fact]
        public void WorldBrowserDeduplicatesAndQueriesSafeRows()
        {
            var rows = new[]
            {
                new WorldEntitySnapshot(2, WorldEntityKind.Mine, "Iron mine", 8, 4, "active", true, 10, "iron"),
                new WorldEntitySnapshot(2, WorldEntityKind.Mine, "Duplicate", 8, 4, "active", true, 10, "iron"),
                new WorldEntitySnapshot(3, WorldEntityKind.Settlement, "Harbor", 1, 2, "unrepaired", false, null, ""),
            };

            IReadOnlyList<WorldEntitySnapshot> snapshot = WorldEntityBrowser.Snapshot(rows);
            IReadOnlyList<WorldEntitySnapshot> result = WorldEntityBrowser.Query(snapshot, new WorldEntityQuery { Search = "iron" });

            Assert.Equal(2, snapshot.Count);
            Assert.Single(result);
            Assert.Equal(2, result[0].Id);
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
    }
}
