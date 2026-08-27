// Taj's COI Mods | GroundwaterPolicyTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Common.Settings;
using TajsCOI.Tweaks;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class GroundwaterPolicyTests
    {
        [Fact]
        public void PoliciesParseAndLegacyInfiniteValueMigratesToTheNewOwner()
        {
            Assert.Equal(GroundwaterPolicy.Vanilla, GroundwaterPolicyRules.Parse("vanilla", legacyInfinite: false));
            Assert.Equal(GroundwaterPolicy.Regenerate, GroundwaterPolicyRules.Parse("regenerate", legacyInfinite: false));
            Assert.Equal(GroundwaterPolicy.MaintainMinimum, GroundwaterPolicyRules.Parse("maintain-minimum", legacyInfinite: false));
            Assert.Equal(GroundwaterPolicy.Infinite, GroundwaterPolicyRules.Parse("vanilla", legacyInfinite: true));
            Assert.Equal("maintain_minimum", GroundwaterPolicyRules.ToSettingValue(GroundwaterPolicy.MaintainMinimum));
        }

        [Fact]
        public void RefillIsMissingOnlyAndNeverExceedsNativeCapacity()
        {
            Assert.Equal(0, GroundwaterPolicyRules.CalculateRefill(70, 100, GroundwaterPolicy.Vanilla, 50, 25));
            Assert.Equal(30, GroundwaterPolicyRules.CalculateRefill(70, 100, GroundwaterPolicy.Regenerate, 50, 25));
            Assert.Equal(30, GroundwaterPolicyRules.CalculateRefill(70, 100, GroundwaterPolicy.Infinite, 50, 25));
            Assert.Equal(0, GroundwaterPolicyRules.CalculateRefill(100, 100, GroundwaterPolicy.Infinite, 50, 25));
            Assert.Equal(0, GroundwaterPolicyRules.CalculateRefill(120, 100, GroundwaterPolicy.Infinite, 50, 25));
        }

        [Fact]
        public void MaintainMinimumOnlyAddsTheDeficitToTheConfiguredFloor()
        {
            Assert.Equal(15, GroundwaterPolicyRules.CalculateRefill(10, 100, GroundwaterPolicy.MaintainMinimum, 5, 25));
            Assert.Equal(0, GroundwaterPolicyRules.CalculateRefill(25, 100, GroundwaterPolicy.MaintainMinimum, 5, 25));
            Assert.Equal(0, GroundwaterPolicyRules.CalculateRefill(10, 100, GroundwaterPolicy.MaintainMinimum, 5, 0));
            Assert.Equal(90, GroundwaterPolicyRules.CalculateRefill(10, 100, GroundwaterPolicy.MaintainMinimum, 5, 200));
        }

        [Fact]
        public void AutomaticLifecycleUsesGameDayAndSuppressesDuplicateSameDayCallbacks()
        {
            Assert.False(GroundwaterPolicyRules.ShouldApplyAutomatic(GroundwaterPolicy.Vanilla, null, 10));
            Assert.True(GroundwaterPolicyRules.ShouldApplyAutomatic(GroundwaterPolicy.Regenerate, null, 10));
            Assert.False(GroundwaterPolicyRules.ShouldApplyAutomatic(GroundwaterPolicy.Regenerate, 10, 10));
            Assert.True(GroundwaterPolicyRules.ShouldApplyAutomatic(GroundwaterPolicy.Regenerate, 10, 11));
        }

        [Fact]
        public void PolicySettingsAreImmediateAndVanillaByDefault()
        {
            SettingDescriptor policy = Assert.Single(
                TajsTweaksSettingsCatalog.All,
                descriptor => descriptor.Key == TajsTweaksSettingsCatalog.GroundwaterPolicy);

            Assert.Equal(SettingValueType.Choice, policy.ValueType);
            Assert.Equal("vanilla", policy.DefaultValue);
            Assert.Equal(SettingApplyMode.Immediate, policy.ApplyMode);
            Assert.Equal(4, policy.Choices.Count);
            Assert.Contains(policy.Choices, choice => choice.Value == "maintain_minimum");
        }
    }
}
