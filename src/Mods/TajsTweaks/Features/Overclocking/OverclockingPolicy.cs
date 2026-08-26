// Taj's COI Mods | OverclockingPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    internal readonly struct OverclockBounds
    {
        internal OverclockBounds(int minPercent, int maxPercent)
        {
            MinPercent = Math.Min(minPercent, maxPercent);
            MaxPercent = Math.Max(minPercent, maxPercent);
        }

        internal int MinPercent { get; }
        internal int MaxPercent { get; }

        internal int Clamp(int percent) => Math.Max(MinPercent, Math.Min(MaxPercent, percent));
    }

    internal readonly struct OverclockEffectivePolicy
    {
        internal OverclockEffectivePolicy(int manualPercent, bool auto, int minPercent, int maxPercent, int groupId)
        {
            ManualPercent = manualPercent;
            Auto = auto;
            MinPercent = minPercent;
            MaxPercent = maxPercent;
            GroupId = groupId;
        }

        internal int ManualPercent { get; }
        internal bool Auto { get; }
        internal int MinPercent { get; }
        internal int MaxPercent { get; }
        internal int GroupId { get; }
    }

    internal static class OverclockingMath
    {
        internal static int ClampPercent(int percent, int minimum, int maximum)
        {
            return Math.Max(Math.Min(minimum, maximum), Math.Min(Math.Max(minimum, maximum), percent));
        }

        internal static float CostMultiplier(int percent, int curvePercent)
        {
            if (percent == 100 || curvePercent <= 0)
            {
                return 1f;
            }

            if (percent < 100)
            {
                return Math.Max(0.05f, 1f - (100f - percent) / 100f);
            }

            double ratio = percent / 100d;
            return (float)Math.Pow(ratio, curvePercent / 100d);
        }

        internal static int WorkersAt(int baseWorkers, int percent, int curvePercent)
        {
            if (baseWorkers <= 0)
            {
                return 0;
            }

            float multiplier = CostMultiplier(percent, curvePercent);
            return Math.Max(1, (int)Math.Ceiling(baseWorkers * multiplier));
        }

        internal static int RoundCost(int baseCost, int percent, int curvePercent)
        {
            if (baseCost <= 0)
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Round(baseCost * CostMultiplier(percent, curvePercent)));
        }

        internal static int DesiredPercentForFill(
            float fillPercent,
            int minPercent,
            int maxPercent,
            int lowFillPercent,
            int neutralFillPercent,
            int highFillPercent)
        {
            int low = ClampPercent(lowFillPercent, 0, 99);
            int neutral = ClampPercent(neutralFillPercent, low + 1, 99);
            int high = ClampPercent(highFillPercent, neutral + 1, 100);
            int min = Math.Min(minPercent, maxPercent);
            int max = Math.Max(minPercent, maxPercent);
            float fill = Math.Max(0f, Math.Min(100f, fillPercent));

            if (fill <= low)
            {
                return max;
            }

            if (fill >= high)
            {
                return min;
            }

            if (fill <= neutral)
            {
                return (int)Math.Round(max + (100 - max) * ((fill - low) / (neutral - low)));
            }

            return (int)Math.Round(100 + (min - 100) * ((fill - neutral) / (high - neutral)));
        }

        internal static int ApplyHysteresis(
            int current,
            int desired,
            OverclockBounds bounds,
            int deadbandPercent,
            int maximumStepPercent,
            int stepPercent)
        {
            if (Math.Abs(desired - current) < Math.Max(0, deadbandPercent))
            {
                return bounds.Clamp(current);
            }

            int limited = Math.Max(current - Math.Max(1, maximumStepPercent),
                Math.Min(current + Math.Max(1, maximumStepPercent), desired));
            int step = Math.Max(1, stepPercent);
            int quantized = (int)Math.Round(limited / (double)step) * step;
            return bounds.Clamp(quantized);
        }

        internal static bool HasDemandSignal(float? fillPercent) => fillPercent.HasValue &&
            !float.IsNaN(fillPercent.Value) && !float.IsInfinity(fillPercent.Value);
    }
}
