// Taj's COI Mods | LightingPolicyTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Visuals.Features.Lighting;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class LightingPolicyTests
    {
        [Fact]
        public void CombiningBaseAndPhasePoliciesIsDeterministic()
        {
            LightingPolicy combined = LightingPolicy.Combine(
                new LightingPolicy(2f, 10f, 0.8f),
                new LightingPolicy(0.5f, -4f, 0.5f));

            Assert.Equal(1f, combined.IntensityMultiplier);
            Assert.Equal(6f, combined.AngleOffsetDegrees);
            Assert.Equal(0.4f, combined.ShadowStrengthMultiplier);
        }

        [Fact]
        public void PhaseValuesInterpolateAndWrapAcrossMidnight()
        {
            var configuration = new VisualPhaseConfiguration(
                0.2f,
                0.3f,
                0.7f,
                0.8f,
                new LightingPolicy(0.5f, 10f, 0.8f),
                new LightingPolicy(1f, 20f, 1f),
                new LightingPolicy(0.5f, 30f, 0.8f),
                new LightingPolicy(0.1f, 40f, 0.4f));

            LightingPolicy dawn = configuration.Evaluate(0.2f);
            LightingPolicy halfway = configuration.Evaluate(0.25f);
            LightingPolicy midnight = configuration.Evaluate(0.9f);

            Assert.Equal(0.5f, dawn.IntensityMultiplier);
            Assert.Equal(0.75f, halfway.IntensityMultiplier);
            Assert.Equal(0.1f, midnight.IntensityMultiplier);
            Assert.InRange(configuration.Evaluate(1.1f).IntensityMultiplier, 0f, 1f);
        }

        [Fact]
        public void PresentationClockUsesOnlySmoothSimulationProgress()
        {
            Assert.Equal(0f, PresentationClock.FromSimulationSteps(0));
            Assert.Equal(0.5f, PresentationClock.FromSimulationSteps(10));
            Assert.Equal(0f, PresentationClock.FromSimulationSteps(20));
            Assert.Equal(0.75f, PresentationClock.FromSimulationSteps(-5));
        }

        [Fact]
        public void UnsafePolicyValuesAreClampedOrDefaulted()
        {
            LightingPolicy policy = new LightingPolicy(float.NaN, float.PositiveInfinity, -2f).Sanitized();

            Assert.Equal(1f, policy.IntensityMultiplier);
            Assert.Equal(0f, policy.AngleOffsetDegrees);
            Assert.Equal(0f, policy.ShadowStrengthMultiplier);
        }
    }
}
