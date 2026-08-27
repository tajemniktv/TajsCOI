// Taj's COI Mods | TrainTuningContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Tweaks.Features.Trains
{
    internal enum TrainModifierProperty
    {
        Climbing,
        Fuel,
        Pollution,
    }

    internal readonly struct TrainModifierValue
    {
        internal TrainModifierValue(TrainModifierProperty property, double value)
        {
            Property = property;
            Value = double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        }

        internal TrainModifierProperty Property { get; }
        internal double Value { get; }
    }

    internal static class TrainModifierPolicy
    {
        internal static IReadOnlyList<TrainModifierValue> ReplaceProperty(
            IEnumerable<TrainModifierValue> existing,
            TrainModifierValue replacement)
        {
            return (existing ?? Array.Empty<TrainModifierValue>())
                .Where(value => value.Property != replacement.Property)
                .Append(replacement)
                .OrderBy(value => value.Property)
                .ToArray();
        }
    }

    internal enum LocomotiveNumberAssignment
    {
        Sequential,
        Random,
    }

    internal static class LocomotiveNumbering
    {
        internal static IReadOnlyList<int> Assign(
            IEnumerable<int> locomotiveIds,
            IEnumerable<int> usedNumbers,
            int namespaceSize,
            LocomotiveNumberAssignment mode,
            int randomSeed)
        {
            List<int> ids = (locomotiveIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToList();
            HashSet<int> used = new(usedNumbers ?? Array.Empty<int>());
            List<int> free = Enumerable.Range(1, Math.Max(0, namespaceSize)).Where(number => !used.Contains(number)).ToList();
            if (ids.Count > free.Count)
            {
                return Array.Empty<int>();
            }
            if (mode == LocomotiveNumberAssignment.Random)
            {
                var random = new Random(randomSeed);
                for (int index = free.Count - 1; index > 0; index--)
                {
                    int other = random.Next(index + 1);
                    (free[index], free[other]) = (free[other], free[index]);
                }
            }
            return free.Take(ids.Count).ToArray();
        }
    }
}
