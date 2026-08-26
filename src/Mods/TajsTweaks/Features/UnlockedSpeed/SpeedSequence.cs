// Taj's COI Mods | SpeedSequence.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TajsCOI.Tweaks.Features.UnlockedSpeed
{
    /// <summary>
    ///     Builds the bounded sequence used by the shared game-speed shortcuts. The parser is
    ///     deliberately independent of Unity and the game so malformed settings can fall back
    ///     without affecting simulation state.
    /// </summary>
    internal static class SpeedSequence
    {
        private static readonly int[] s_vanilla = { 1, 2, 3, 12 };

        internal static IReadOnlyList<int> Build(int maximum, string mode, string customData)
        {
            int boundedMaximum = Math.Max(1, maximum);
            if (string.Equals(mode, UnlockedSpeedSetting.EveryIntegerSequenceMode, StringComparison.Ordinal))
            {
                return Enumerable.Range(1, boundedMaximum).ToArray();
            }

            if (string.Equals(mode, UnlockedSpeedSetting.CustomSequenceMode, StringComparison.Ordinal))
            {
                int[] custom = ParseCustom(customData, boundedMaximum);
                if (custom.Length > 0)
                {
                    return custom;
                }
            }

            var vanilla = new SortedSet<int>(s_vanilla.Where(x => x <= boundedMaximum));
            vanilla.Add(1);
            return vanilla.ToArray();
        }

        private static int[] ParseCustom(string? data, int maximum)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return Array.Empty<int>();
            }

            var values = new SortedSet<int>();
            foreach (string token in data!.Split(','))
            {
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
                    value >= 1 && value <= maximum)
                {
                    values.Add(value);
                }
            }

            if (values.Count == 0)
            {
                return Array.Empty<int>();
            }

            values.Add(1);
            values.Add(maximum);
            return values.ToArray();
        }
    }
}
