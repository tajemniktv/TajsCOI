// Taj's COI Mods | TerrainDesignationPriorityTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Core;
using Mafi.Core.Terrain.Designation;
using TajsCOI.Tweaks.Features.Terrain;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class TerrainDesignationPriorityTests
    {
        [Fact]
        public void ClassifierRecognizesOnlyNativeTerraformingDesignators()
        {
            Assert.Equal(
                TerrainWorkClass.Leveling,
                TerrainDesignationPriorityPolicy.ClassifyId(IdsCore.TerrainDesignators.LevelDesignator.Value));
            Assert.Equal(
                TerrainWorkClass.Digging,
                TerrainDesignationPriorityPolicy.ClassifyId(IdsCore.TerrainDesignators.MiningDesignator.Value));
            Assert.Equal(
                TerrainWorkClass.Filling,
                TerrainDesignationPriorityPolicy.ClassifyId(IdsCore.TerrainDesignators.DumpingDesignator.Value));

            Assert.Equal(TerrainWorkClass.Other, TerrainDesignationPriorityPolicy.ClassifyId("ModdedLevelDesignator"));
            Assert.Equal(TerrainWorkClass.Other, TerrainDesignationPriorityPolicy.ClassifyId("custom-dump"));
            Assert.Equal(TerrainWorkClass.Unknown, TerrainDesignationPriorityPolicy.ClassifyId(null));
            Assert.Equal(TerrainWorkClass.Unknown, TerrainDesignationPriorityPolicy.ClassifyId(" "));
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 0)]
        [InlineData(true, 100)]
        [InlineData(true, 200)]
        [InlineData(true, 400)]
        public void PreferenceAdjustmentIsDisabledOrBounded(
            bool enabled,
            int nativeScore)
        {
            int adjustment = TerrainDesignationPriorityPolicy.Adjustment(
                enabled,
                TerrainWorkClass.Leveling,
                TerrainWorkClass.Leveling,
                nativeScore);

            if (!enabled)
            {
                Assert.Equal(0, adjustment);
            }
            else
            {
                int expected = Math.Min(
                    TerrainDesignationPriorityPolicy.MaxTieBand,
                    Math.Max(0, Math.Abs(nativeScore) / 100));
                Assert.Equal(expected == 0 ? 1 : expected, adjustment);
            }
        }

        [Fact]
        public void UnknownAndNonPreferredCandidatesReceiveNoAdjustment()
        {
            Assert.Equal(
                0,
                TerrainDesignationPriorityPolicy.Adjustment(
                    true,
                    TerrainWorkClass.Unknown,
                    TerrainWorkClass.Leveling,
                    0));
            Assert.Equal(
                0,
                TerrainDesignationPriorityPolicy.Adjustment(
                    true,
                    TerrainWorkClass.Other,
                    TerrainWorkClass.Leveling,
                    0));
            Assert.Equal(
                0,
                TerrainDesignationPriorityPolicy.Adjustment(
                    true,
                    TerrainWorkClass.Digging,
                    TerrainWorkClass.Leveling,
                    0));
        }

        [Fact]
        public void PreferredEligibleWorkWinsWithinTheBoundedNativeScoreBand()
        {
            const int lowerPriorityNativeScore = 101;
            const int preferredNativeScore = 100;

            int adjustedPreferredScore = preferredNativeScore - TerrainDesignationPriorityPolicy.Adjustment(
                true,
                TerrainWorkClass.Leveling,
                TerrainWorkClass.Leveling,
                preferredNativeScore);

            Assert.True(adjustedPreferredScore < lowerPriorityNativeScore);
        }

        [Fact]
        public void IneligiblePreferredWorkLeavesAnEligibleLowerPriorityFallback()
        {
            // The native ready cache is authoritative: an unusable preferred candidate is not
            // scored, so a lower-priority candidate remains selectable.
            var candidates = new[]
            {
                new { WorkClass = TerrainWorkClass.Leveling, NativeScore = 1, NativeEligible = false },
                new { WorkClass = TerrainWorkClass.Digging, NativeScore = 100, NativeEligible = true },
            };
            TerrainWorkClass selected = TerrainWorkClass.Unknown;
            int bestScore = int.MaxValue;
            foreach (var candidate in candidates)
            {
                if (!candidate.NativeEligible)
                {
                    continue;
                }
                int adjustedScore = candidate.NativeScore - TerrainDesignationPriorityPolicy.Adjustment(
                    true,
                    candidate.WorkClass,
                    TerrainWorkClass.Leveling,
                    candidate.NativeScore);
                if (adjustedScore < bestScore)
                {
                    bestScore = adjustedScore;
                    selected = candidate.WorkClass;
                }
            }

            Assert.Equal(TerrainWorkClass.Digging, selected);
        }

        [Fact]
        public void DisabledOrAbsentPreferenceLeavesNativeScoreUntouched()
        {
            const int nativeScore = 37;
            Assert.Equal(
                nativeScore,
                nativeScore - TerrainDesignationPriorityPolicy.Adjustment(
                    false,
                    TerrainWorkClass.Leveling,
                    TerrainWorkClass.Leveling,
                    nativeScore));
            Assert.Equal(
                nativeScore,
                nativeScore - TerrainDesignationPriorityPolicy.Adjustment(
                    true,
                    TerrainWorkClass.Digging,
                    TerrainWorkClass.Leveling,
                    nativeScore));
        }

        [Fact]
        public void PreferredClassWinsOnlyWhenNativeEligibilityProvidesIt()
        {
            Assert.Equal(
                TerrainWorkClass.Leveling,
                TerrainDesignationPriorityPolicy.ChooseNearbyClass(
                    TerrainWorkClass.Digging,
                    sameClassEligible: false,
                    anyEligible: true,
                    preferred: TerrainWorkClass.Leveling));
            Assert.Equal(
                TerrainWorkClass.Digging,
                TerrainDesignationPriorityPolicy.ChooseNearbyClass(
                    TerrainWorkClass.Digging,
                    sameClassEligible: true,
                    anyEligible: true,
                    preferred: TerrainWorkClass.Leveling));
            Assert.Equal(
                TerrainWorkClass.Unknown,
                TerrainDesignationPriorityPolicy.ChooseNearbyClass(
                    TerrainWorkClass.Digging,
                    sameClassEligible: false,
                    anyEligible: false,
                    preferred: TerrainWorkClass.Leveling));
        }

        [Fact]
        public void VanillaOrUnknownSettingLeavesPolicyUnchanged()
        {
            Assert.Equal(TerrainWorkClass.Unknown, TerrainDesignationPriorityPolicy.Parse("vanilla"));
            Assert.Equal(TerrainWorkClass.Unknown, TerrainDesignationPriorityPolicy.Parse(null));
            Assert.Equal(TerrainWorkClass.Unknown, TerrainDesignationPriorityPolicy.Parse("modded-value"));
        }

        [Fact]
        public void PreferRetainsAllNativeCandidatesAndEnumeratesSourceOnce()
        {
            int enumerations = 0;
            IEnumerable<TerrainDesignation> Source()
            {
                enumerations++;
                yield return null!;
                yield return null!;
            }

            IReadOnlyList<TerrainDesignation> result = TerrainDesignationPriorityPolicy.Prefer(
                Source(),
                TerrainWorkClass.Leveling);

            Assert.Equal(1, enumerations);
            Assert.Equal(2, result.Count);
            Assert.Null(result[0]);
            Assert.Null(result[1]);
        }

        [Fact]
        public void FeaturePatchesTheNativeScorePartialSeam()
        {
            MethodInfo? target = TerrainDesignationPriorityFeature.FindScorePartialTarget();

            Assert.NotNull(target);
            Assert.Equal("ScorePartial", target!.Name);
            Assert.Equal("DesignationScorer", target.DeclaringType!.Name);
            Assert.Equal(typeof(Fix32), target.ReturnType);
            Assert.Equal(new[] { typeof(TerrainDesignation) },
                Array.ConvertAll(target.GetParameters(), parameter => parameter.ParameterType));
        }
    }
}
