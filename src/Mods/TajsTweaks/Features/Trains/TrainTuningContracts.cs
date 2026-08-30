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
        internal const double MinimumMultiplier = 0.1d;
        internal const double MaximumMultiplier = 3d;

        internal static double ResolveMultiplier(
            double configured,
            TrainModifierProperty property,
            string? legacyProfile)
        {
            double multiplier = double.IsNaN(configured) || double.IsInfinity(configured)
                ? 1d
                : Math.Max(MinimumMultiplier, Math.Min(MaximumMultiplier, configured));
            // Keep the original profile as a migration-compatible convenience. Explicit
            // per-property values (anything other than the vanilla default) always win.
            if (Math.Abs(multiplier - 1d) < 0.0001d)
            {
                switch ((legacyProfile ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "efficient" when property is TrainModifierProperty.Fuel or TrainModifierProperty.Pollution:
                    case "power" when property == TrainModifierProperty.Climbing:
                        return 0.75d;
                }
            }
            return multiplier;
        }

        internal static double ToModifierPercent(double multiplier) => (multiplier - 1d) * 100d;

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
        internal static bool TryGetSupportedRange(int typeDigit, out int minimum, out int maximum)
        {
            if (typeDigit < 1 || typeDigit > 9)
            {
                minimum = 0;
                maximum = 0;
                return false;
            }

            minimum = checked(typeDigit * 100 + 1);
            maximum = checked(typeDigit * 100 + 100);
            return true;
        }

        internal static bool IsValidForType(int number, int typeDigit) =>
            TryGetSupportedRange(typeDigit, out int minimum, out int maximum) &&
            number >= minimum && number <= maximum;

        internal static IReadOnlyList<int> Assign(
            IEnumerable<int> locomotiveIds,
            IEnumerable<int> usedNumbers,
            int namespaceSize,
            LocomotiveNumberAssignment mode,
            int randomSeed)
        {
            int maximum = Math.Max(0, namespaceSize);
            return AssignInRange(locomotiveIds, usedNumbers, 1, maximum, mode, randomSeed);
        }

        internal static IReadOnlyList<int> AssignInRange(
            IEnumerable<int> locomotiveIds,
            IEnumerable<int> usedNumbers,
            int minimum,
            int maximum,
            LocomotiveNumberAssignment mode,
            int randomSeed)
        {
            List<int> ids = (locomotiveIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToList();
            HashSet<int> used = new(usedNumbers ?? Array.Empty<int>());
            if (minimum > maximum || minimum < 1)
            {
                return Array.Empty<int>();
            }

            long rangeLength = (long)maximum - minimum + 1L;
            if (rangeLength > int.MaxValue)
            {
                return Array.Empty<int>();
            }

            int count = (int)rangeLength;
            List<int> free = Enumerable.Range(minimum, count).Where(number => !used.Contains(number)).ToList();
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
