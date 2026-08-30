// Taj's COI Mods | DiseaseScalingPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    internal enum DiseaseScalingMode
    {
        Vanilla,
        MapScaled,
        Custom,
    }

    /// <summary>
    /// Pure policy for the six native distance tiers.  It deliberately does not create or mutate
    /// disease prototypes; a gameplay adapter can use the returned thresholds for future
    /// selection while existing CurrentDisease/save state remains untouched.
    /// </summary>
    internal static class DiseaseScalingPolicy
    {
        internal static readonly int[] VanillaThresholds = { 0, 600, 900, 1100, 1430, 1700 };

        internal static int[] Compute(
            IReadOnlyList<int> vanillaThresholds,
            int mapSpan,
            DiseaseScalingMode mode,
            string? customFractions = null)
        {
            if (vanillaThresholds is null || vanillaThresholds.Count == 0 || mode == DiseaseScalingMode.Vanilla || mapSpan <= 0)
            {
                return Copy(vanillaThresholds ?? VanillaThresholds);
            }

            double[] fractions = mode == DiseaseScalingMode.Custom
                ? ParseFractions(customFractions, vanillaThresholds.Count)
                : vanillaThresholds.Select(value => value / (double)vanillaThresholds[vanillaThresholds.Count - 1]).ToArray();
            if (fractions.Length != vanillaThresholds.Count || !AreValidFractions(fractions))
            {
                return Copy(vanillaThresholds);
            }

            // A strict ordering requires at least one coordinate per tier.  Falling back to
            // vanilla for an undersized/invalid map is safer than collapsing disease tiers.
            if (mapSpan < fractions.Length - 1)
            {
                return Copy(vanillaThresholds);
            }

            int[] result = new int[fractions.Length];
            result[0] = 0;
            for (int index = 1; index < result.Length; index++)
            {
                int candidate = (int)Math.Round(fractions[index] * mapSpan, MidpointRounding.AwayFromZero);
                candidate = Math.Max(result[index - 1] + 1, candidate);
                candidate = Math.Min(mapSpan, candidate);
                if (candidate <= result[index - 1])
                {
                    return Copy(vanillaThresholds);
                }
                result[index] = candidate;
            }
            return result;
        }

        internal static bool IsEligible(int distance, int threshold) => distance >= threshold;

        internal static bool TryParseMode(string? value, out DiseaseScalingMode mode)
        {
            if (string.Equals(value, "map_scaled", StringComparison.OrdinalIgnoreCase))
            {
                mode = DiseaseScalingMode.MapScaled;
                return true;
            }
            if (string.Equals(value, "custom", StringComparison.OrdinalIgnoreCase))
            {
                mode = DiseaseScalingMode.Custom;
                return true;
            }
            mode = DiseaseScalingMode.Vanilla;
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, "vanilla", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryParseCustomFractions(string? value, int count, out double[] fractions)
        {
            fractions = ParseFractions(value, count);
            return fractions.Length == count && AreValidFractions(fractions);
        }

        private static double[] ParseFractions(string? value, int count)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<double>();
            }
            string[] tokens = (value ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != count)
            {
                return Array.Empty<double>();
            }
            var result = new double[count];
            for (int index = 0; index < tokens.Length; index++)
            {
                if (!double.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]))
                {
                    return Array.Empty<double>();
                }
            }
            return result;
        }

        private static bool AreValidFractions(IReadOnlyList<double> fractions)
        {
            if (fractions.Count == 0 || fractions[0] < 0d || fractions[0] > 0.000001d)
            {
                return false;
            }
            for (int index = 0; index < fractions.Count; index++)
            {
                if (double.IsNaN(fractions[index]) || double.IsInfinity(fractions[index]) || fractions[index] < 0d || fractions[index] > 1d)
                {
                    return false;
                }
                if (index > 0 && fractions[index] <= fractions[index - 1])
                {
                    return false;
                }
            }
            return true;
        }

        private static int[] Copy(IReadOnlyList<int> values)
        {
            var result = new int[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                result[index] = values[index];
            }
            return result;
        }
    }

    internal sealed class DiseaseThresholdCache
    {
        private int[]? m_thresholds;

        internal void Reset() => m_thresholds = null;

        internal IReadOnlyList<int> GetOrCompute(
            IReadOnlyList<int> vanillaThresholds,
            int mapSpan,
            DiseaseScalingMode mode,
            string? customFractions)
        {
            return m_thresholds ??= DiseaseScalingPolicy.Compute(vanillaThresholds, mapSpan, mode, customFractions);
        }
    }
}
