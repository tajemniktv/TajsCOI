using System;
using System.Linq;
using System.Reflection;
using Mafi.Core;
using Mafi;
using TajsCOI.Common.Tuning;
using TajsCOI.Tweaks.Features.Difficulty;
using TajsCOI.Tweaks.Features.InfrastructureTuning;
using TajsCOI.Tweaks.Features.ProgressionSandbox;
using TajsCOI.Tweaks.Features.Sandbox;
using TajsCOI.Tweaks.Features.Storage;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class Wave7SandboxTests
    {
        [Fact]
        public void BaseValueOverrideDerivesFromImmutableBaseAndResetsExactly()
        {
            int value = 10;
            var service = new BaseValueOverrideService();

            Assert.True(service.TryRegister("test", () => value, next => value = next, 0, 100, BaseValueApplyMode.Immediate));
            value = 99;
            Assert.True(service.TrySetMultiplier("test", 2.5));
            Assert.Equal(25, value);
            Assert.True(service.TrySetMultiplier("test", 3));
            Assert.Equal(30, value);
            Assert.True(service.TryReset("test"));
            Assert.Equal(10, value);
            Assert.Equal(10, service.Registrations.Single().BaseValue);
        }

        [Fact]
        public void BaseValueOverrideRejectsNonFiniteAndFailedSettersWithoutClaimingState()
        {
            double value = 4;
            var service = new BaseValueOverrideService();
            Assert.True(service.TryRegister("test", () => value, next => throw new InvalidOperationException(), 0d, 10d));
            Assert.False(service.TrySetMultiplier("test", double.NaN));
            Assert.False(service.TrySetMultiplier("test", 2));
            Assert.True(service.TryGetEffectiveValue("test", out double effective));
            Assert.Equal(4d, effective);
        }

        [Fact]
        public void BaseValueOverrideConvertsDiscreteQuantityWithoutCompounding()
        {
            Quantity quantity = 8.Quantity();
            var service = new BaseValueOverrideService();

            Assert.True(service.TryRegister("quantity", () => quantity, next => quantity = next, 0.Quantity(), 100.Quantity()));
            Assert.True(service.TrySetMultiplier("quantity", 1.5));
            Assert.Equal(12, quantity.Value);
            Assert.True(service.TryReset("quantity"));
            Assert.Equal(8, quantity.Value);
        }

        [Fact]
        public void DiseaseScalingPreservesTierOrderAndInclusiveEligibility()
        {
            int[] scaled = DiseaseScalingPolicy.Compute(
                DiseaseScalingPolicy.VanillaThresholds,
                4096,
                DiseaseScalingMode.MapScaled);

            Assert.Equal(0, scaled[0]);
            Assert.True(Enumerable.Zip(scaled, scaled.Skip(1), (left, right) => right > left).All(x => x));
            Assert.True(DiseaseScalingPolicy.IsEligible(scaled[3], scaled[3]));
            Assert.False(DiseaseScalingPolicy.IsEligible(scaled[3] - 1, scaled[3]));
        }

        [Fact]
        public void DiseaseScalingRejectsInvalidCustomFractionsAndCachesOnce()
        {
            Assert.False(DiseaseScalingPolicy.TryParseCustomFractions("0,.5,.5,.8,1,1", 6, out _));
            Assert.False(DiseaseScalingPolicy.TryParseCustomFractions("0,.2,.4,.6,.8", 6, out _));

            var cache = new DiseaseThresholdCache();
            var first = cache.GetOrCompute(DiseaseScalingPolicy.VanillaThresholds, 4096, DiseaseScalingMode.MapScaled, null);
            var second = cache.GetOrCompute(DiseaseScalingPolicy.VanillaThresholds, 1024, DiseaseScalingMode.MapScaled, null);
            Assert.Same(first, second);
        }

        [Fact]
        public void ThermalReductionNeverStrandsStoredHeat()
        {
            Assert.Equal(120d, InfrastructureTuningFeature.EffectiveThermalCapacity(100d, 120d));
            Assert.Equal(100d, InfrastructureTuningFeature.EffectiveThermalCapacity(100d, 50d));
            Assert.False(InfrastructureTuningFeature.CanChargeThermalStorage(100d, 100d));
            Assert.True(InfrastructureTuningFeature.CanChargeThermalStorage(100d, 99d));
        }

        [Fact]
        public void DestructiveStorageAndBulldozePoliciesRequireExplicitOptIn()
        {
            Assert.False(StorageEmptyPolicy.IsAuthorized(true, false));
            Assert.True(StorageEmptyPolicy.IsAuthorized(true, true));
            Assert.True(StorageEmptyPolicy.IsNativeClearAvailable());
            Assert.False(BulldozeOverrideFeature.IsWhitelistedType(typeof(FakeBridge), nameof(FakeBridge)));
            Assert.True(BulldozeOverrideFeature.IsWhitelistedType(typeof(FakeBuilding), nameof(FakeBuilding)));
        }

        [Fact]
        public void SandboxOutputOwnersAreIndependentPerAffectedOutput()
        {
            string[] owners =
            {
                SandboxControlsFeature.SolidWasteOwner,
                SandboxControlsFeature.BiowasteOwner,
                SandboxControlsFeature.FocusInfiniteOwner,
                SandboxControlsFeature.FocusMultiplierOwner,
            };

            Assert.Equal(owners.Length, owners.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(SandboxControlsFeature.SolidWasteOwner, owners.Skip(1));
            Assert.DoesNotContain(SandboxControlsFeature.BiowasteOwner, owners.Skip(2));
            Assert.True(SandboxControlsFeature.ShouldSuppressWasteOutput(WasteOutput.Solid, disableSolidWaste: true, disableBiowaste: false));
            Assert.False(SandboxControlsFeature.ShouldSuppressWasteOutput(WasteOutput.Biowaste, disableSolidWaste: true, disableBiowaste: false));
            Assert.False(SandboxControlsFeature.ShouldSuppressWasteOutput(WasteOutput.Solid, disableSolidWaste: false, disableBiowaste: true));
            Assert.True(SandboxControlsFeature.ShouldSuppressWasteOutput(WasteOutput.Biowaste, disableSolidWaste: false, disableBiowaste: true));
        }

        [Fact]
        public void InstantStorageEmptyIsAConfirmedValueOnlyInputCommand()
        {
            Assert.True(typeof(Mafi.Core.Input.InputCommand).IsAssignableFrom(typeof(TajsStorageInstantEmptyCmd)));
            Assert.Equal(
                new[] { typeof(EntityId), typeof(bool) },
                typeof(TajsStorageInstantEmptyCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
        }

        private sealed class FakeBridge { }
        private sealed class FakeBuilding { }

    }
}
