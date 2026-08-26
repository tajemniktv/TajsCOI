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

        /// <summary>
        /// Keeps the process duration used by COI's ExtendPauseToFit animation state strictly
        /// longer than the effective animation duration when a Tajs overclock shortens the
        /// recipe below it. The returned value is only for the animation state; it must not be
        /// fed back into recipe production timing.
        /// </summary>
        internal static int EnsureAnimationProcessFits(int currentProcessTicks, int animationTicks, int overclockPercent)
        {
            if (overclockPercent == 100 || currentProcessTicks > animationTicks)
            {
                return currentProcessTicks;
            }

            if (animationTicks == int.MaxValue)
            {
                return animationTicks;
            }

            return Math.Max(currentProcessTicks, animationTicks + 1);
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

        internal static int RampedCapacityValue(int vanilla, int percent, int maxPercent, int bonusPercent, bool increase)
        {
            if (vanilla <= 0 || percent <= 100 || maxPercent <= 100 || bonusPercent <= 0)
            {
                return vanilla;
            }

            int range = maxPercent - 100;
            int rampStart = 100 + range / 2;
            int rampEnd = 100 + Math.Max(range / 2 + 1, range * 9 / 10);
            if (percent <= rampStart)
            {
                return vanilla;
            }

            float amount = percent >= rampEnd ? 1f :
                (percent - rampStart) / (float)Math.Max(1, rampEnd - rampStart);
            int target;
            if (increase)
            {
                int bonus = Math.Max(0, vanilla * Math.Min(500, bonusPercent) / 100);
                target = vanilla + bonus;
            }
            else
            {
                int boundedBonus = Math.Min(300, bonusPercent);
                target = Math.Max(1, (vanilla * 100 + 100 + boundedBonus / 2) / (100 + boundedBonus));
            }

            int delta = target - vanilla;
            return vanilla + (int)Math.Round(delta * amount, MidpointRounding.AwayFromZero);
        }
    }
}
