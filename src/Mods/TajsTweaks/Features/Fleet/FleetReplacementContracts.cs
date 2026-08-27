// Taj's COI Mods | FleetReplacementContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Tweaks.Features.Fleet
{
    internal readonly struct FleetReplacementFilterSnapshot
    {
        internal FleetReplacementFilterSnapshot(
            string sourcePrototypeId,
            string targetPrototypeId,
            string assignmentState,
            int? depotId,
            int? zoneId,
            int? assigneeId,
            int maxCount)
        {
            SourcePrototypeId = sourcePrototypeId?.Trim() ?? string.Empty;
            TargetPrototypeId = targetPrototypeId?.Trim() ?? string.Empty;
            AssignmentState = assignmentState?.Trim() ?? string.Empty;
            DepotId = depotId;
            ZoneId = zoneId;
            AssigneeId = assigneeId;
            MaxCount = Math.Max(0, maxCount);
        }

        internal string SourcePrototypeId { get; }
        internal string TargetPrototypeId { get; }
        internal string AssignmentState { get; }
        internal int? DepotId { get; }
        internal int? ZoneId { get; }
        internal int? AssigneeId { get; }
        internal int MaxCount { get; }
    }

    internal readonly struct FleetVehicleSnapshot
    {
        internal FleetVehicleSnapshot(int id, string prototypeId, bool assigned, int? depotId, int? zoneId, int? assigneeId)
        {
            Id = id;
            PrototypeId = prototypeId?.Trim() ?? string.Empty;
            Assigned = assigned;
            DepotId = depotId;
            ZoneId = zoneId;
            AssigneeId = assigneeId;
        }

        internal int Id { get; }
        internal string PrototypeId { get; }
        internal bool Assigned { get; }
        internal int? DepotId { get; }
        internal int? ZoneId { get; }
        internal int? AssigneeId { get; }
    }

    internal static class FleetReplacementPlanner
    {
        internal static IReadOnlyList<int> Match(
            IEnumerable<FleetVehicleSnapshot> vehicles,
            FleetReplacementFilterSnapshot filter,
            Func<FleetVehicleSnapshot, bool>? nativeCompatible = null)
        {
            IEnumerable<FleetVehicleSnapshot> query = vehicles ?? Array.Empty<FleetVehicleSnapshot>();
            if (filter.SourcePrototypeId.Length > 0)
            {
                query = query.Where(vehicle => string.Equals(vehicle.PrototypeId, filter.SourcePrototypeId, StringComparison.Ordinal));
            }
            if (string.Equals(filter.AssignmentState, "assigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(vehicle => vehicle.Assigned);
            }
            else if (string.Equals(filter.AssignmentState, "unassigned", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(vehicle => !vehicle.Assigned);
            }
            if (filter.DepotId.HasValue) query = query.Where(vehicle => vehicle.DepotId == filter.DepotId);
            if (filter.ZoneId.HasValue) query = query.Where(vehicle => vehicle.ZoneId == filter.ZoneId);
            if (filter.AssigneeId.HasValue) query = query.Where(vehicle => vehicle.AssigneeId == filter.AssigneeId);
            if (nativeCompatible is not null) query = query.Where(nativeCompatible);
            query = string.Equals(filter.AssignmentState, "unassigned-first", StringComparison.OrdinalIgnoreCase)
                ? query.OrderBy(vehicle => vehicle.Assigned ? 1 : 0).ThenBy(vehicle => vehicle.Id)
                : query.OrderBy(vehicle => vehicle.Id);
            return query.Take(filter.MaxCount).Select(vehicle => vehicle.Id).ToArray();
        }
    }
}
