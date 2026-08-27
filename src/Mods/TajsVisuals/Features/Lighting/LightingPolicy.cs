// Taj's COI Mods | LightingPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi;

namespace TajsCOI.Visuals.Features.Lighting
{
    /// <summary>
    ///     User-owned visual deltas. Values are deliberately independent of the renderer's
    ///     mutable state so a policy can be reapplied without compounding the previous result.
    /// </summary>
    public readonly struct LightingPolicy : IEquatable<LightingPolicy>
    {
        public LightingPolicy(float intensityMultiplier, float angleOffsetDegrees, float shadowStrengthMultiplier)
        {
            IntensityMultiplier = intensityMultiplier;
            AngleOffsetDegrees = angleOffsetDegrees;
            ShadowStrengthMultiplier = shadowStrengthMultiplier;
        }

        public float IntensityMultiplier { get; }

        public float AngleOffsetDegrees { get; }

        public float ShadowStrengthMultiplier { get; }

        public static LightingPolicy Identity => new(1f, 0f, 1f);

        public LightingPolicy Sanitized() => new(
            Math.Max(0f, IsFinite(IntensityMultiplier) ? IntensityMultiplier : 1f),
            IsFinite(AngleOffsetDegrees) ? AngleOffsetDegrees : 0f,
            Clamp(IsFinite(ShadowStrengthMultiplier) ? ShadowStrengthMultiplier : 1f, 0f, 1f));

        public static LightingPolicy Combine(LightingPolicy basePolicy, LightingPolicy overlay)
        {
            LightingPolicy left = basePolicy.Sanitized();
            LightingPolicy right = overlay.Sanitized();
            return new LightingPolicy(
                left.IntensityMultiplier * right.IntensityMultiplier,
                left.AngleOffsetDegrees + right.AngleOffsetDegrees,
                left.ShadowStrengthMultiplier * right.ShadowStrengthMultiplier).Sanitized();
        }

        public bool Equals(LightingPolicy other) =>
            IntensityMultiplier.Equals(other.IntensityMultiplier) &&
            AngleOffsetDegrees.Equals(other.AngleOffsetDegrees) &&
            ShadowStrengthMultiplier.Equals(other.ShadowStrengthMultiplier);

        public override bool Equals(object? obj) => obj is LightingPolicy other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + IntensityMultiplier.GetHashCode();
                hash = hash * 31 + AngleOffsetDegrees.GetHashCode();
                return hash * 31 + ShadowStrengthMultiplier.GetHashCode();
            }
        }

        public static bool operator ==(LightingPolicy left, LightingPolicy right) => left.Equals(right);

        public static bool operator !=(LightingPolicy left, LightingPolicy right) => !left.Equals(right);

        private static float Clamp(float value, float minimum, float maximum) =>
            Math.Min(maximum, Math.Max(minimum, value));

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    ///     Four named presentation phases. Phase values are policies rather than raw light values,
    ///     which lets the day/night feature compose with the base #64 controls.
    /// </summary>
    public enum VisualLightingPhase
    {
        Dawn,
        Day,
        Dusk,
        Night,
    }

    public sealed class VisualPhaseConfiguration
    {
        public VisualPhaseConfiguration(
            float dawnStart,
            float dayStart,
            float duskStart,
            float nightStart,
            LightingPolicy dawn,
            LightingPolicy day,
            LightingPolicy dusk,
            LightingPolicy night)
        {
            DawnStart = dawnStart;
            DayStart = dayStart;
            DuskStart = duskStart;
            NightStart = nightStart;
            Dawn = dawn;
            Day = day;
            Dusk = dusk;
            Night = night;
        }

        public float DawnStart { get; }
        public float DayStart { get; }
        public float DuskStart { get; }
        public float NightStart { get; }
        public LightingPolicy Dawn { get; }
        public LightingPolicy Day { get; }
        public LightingPolicy Dusk { get; }
        public LightingPolicy Night { get; }

        public LightingPolicy Evaluate(float normalizedClock)
        {
            float dawnStart = Clamp01(DawnStart);
            float dayStart = Math.Max(dawnStart, Clamp01(DayStart));
            float duskStart = Math.Max(dayStart, Clamp01(DuskStart));
            float nightStart = Math.Max(duskStart, Clamp01(NightStart));
            float clock = NormalizeClock(normalizedClock);

            if (clock < dawnStart)
            {
                float length = 1f - nightStart + dawnStart;
                return Interpolate(Night, Dawn, length <= 0f ? 1f : (clock + 1f - nightStart) / length);
            }
            if (clock < dayStart)
            {
                return Interpolate(Dawn, Day, SegmentT(clock, dawnStart, dayStart));
            }
            if (clock < duskStart)
            {
                return Interpolate(Day, Dusk, SegmentT(clock, dayStart, duskStart));
            }
            if (clock < nightStart)
            {
                return Interpolate(Dusk, Night, SegmentT(clock, duskStart, nightStart));
            }
            return Night.Sanitized();
        }

        internal static float NormalizeClock(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }
            float result = value - (float)Math.Floor(value);
            return result < 0f ? result + 1f : result;
        }

        private static float SegmentT(float value, float start, float end) =>
            end <= start ? 1f : Clamp01((value - start) / (end - start));

        private static LightingPolicy Interpolate(LightingPolicy from, LightingPolicy to, float amount)
        {
            LightingPolicy left = from.Sanitized();
            LightingPolicy right = to.Sanitized();
            float t = Clamp01(amount);
            return new LightingPolicy(
                Lerp(left.IntensityMultiplier, right.IntensityMultiplier, t),
                Lerp(left.AngleOffsetDegrees, right.AngleOffsetDegrees, t),
                Lerp(left.ShadowStrengthMultiplier, right.ShadowStrengthMultiplier, t)).Sanitized();
        }

        private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

        private static float Clamp01(float value) => Math.Min(1f, Math.Max(0f, value));
    }

    public static class PresentationClock
    {
        /// <summary>
        ///     Converts smooth simulation steps to a repeating presentation clock. CoI's exact
        ///     0.8.7b calendar has 20 simulation steps per day; this method only reads that
        ///     progress and never writes or advances the simulation calendar.
        /// </summary>
        public static float FromSimulationSteps(double smoothSteps, int stepsPerDay = 20)
        {
            if (double.IsNaN(smoothSteps) || double.IsInfinity(smoothSteps) || stepsPerDay <= 0)
            {
                return 0f;
            }
            double progress = smoothSteps / stepsPerDay;
            progress -= Math.Floor(progress);
            return (float)progress;
        }

        /// <summary>
        ///     Anchors the presentation clock to the authoritative simulation date while using
        ///     smooth render interpolation for the intra-day fraction. The date is read-only;
        ///     this method never advances or writes calendar state.
        /// </summary>
        public static float FromSimulationDate(GameDate date, double smoothSteps, int stepsPerDay = 20)
        {
            if (double.IsNaN(smoothSteps) || double.IsInfinity(smoothSteps) || stepsPerDay <= 0)
            {
                return FromSimulationSteps(date.Value, stepsPerDay);
            }

            double dayFraction = smoothSteps / stepsPerDay;
            dayFraction -= Math.Floor(dayFraction);
            double anchoredDay = date.Value + dayFraction;
            anchoredDay -= Math.Floor(anchoredDay);
            return (float)anchoredDay;
        }
    }
}
