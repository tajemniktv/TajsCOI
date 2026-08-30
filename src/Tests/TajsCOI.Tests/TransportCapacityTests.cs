// Taj's COI Mods | TransportCapacityTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Linq;
using Mafi;
using TajsCOI.Common.Tuning;
using TajsCOI.Tweaks.Features.TransportCapacity;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class TransportCapacityTests
    {
        [Fact]
        public void CapacityReductionDefersWithoutTruncatingContainedQuantity()
        {
            int capacity = 100;
            int contained = 120;
            var service = new BaseValueOverrideService();

            Assert.True(service.TryRegister("truck.1", () => capacity, value => capacity = value, 1, int.MaxValue));
            double requested = service.Registrations.Single().BaseValue * 0.5d;
            Assert.Equal(CapacityReductionDecision.Deferred, CapacityReductionPolicy.Evaluate(requested, contained, allowOverCapacity: false));
            Assert.Equal(120, contained);
            Assert.Equal(100, capacity);
        }

        [Fact]
        public void CapacityReductionCanBeExplicitlyOverCapacityWithoutDataLoss()
        {
            Assert.Equal(
                CapacityReductionDecision.OverCapacity,
                CapacityReductionPolicy.Evaluate(50d, 75d, allowOverCapacity: true));
            Assert.Equal(75d, CapacityReductionPolicy.EffectiveCapacity(50d, 75d));
        }

        [Fact]
        public void CapacityPolicyRejectsInvalidAndOverflowValues()
        {
            Assert.Equal(
                CapacityReductionDecision.Invalid,
                CapacityReductionPolicy.Evaluate(double.NaN, 1d, allowOverCapacity: true));
            Assert.Equal(
                CapacityReductionDecision.Invalid,
                CapacityReductionPolicy.Evaluate(1d, double.PositiveInfinity, allowOverCapacity: true));

            int capacity = 100;
            var service = new BaseValueOverrideService();
            Assert.True(service.TryRegister("truck.1", () => capacity, value => capacity = value, 1, int.MaxValue));
            Assert.False(service.TrySetMultiplier("truck.1", double.MaxValue));
            Assert.Equal(100, capacity);
        }

        [Fact]
        public void BaseRegistrationIsIdempotentAndOneXRestoresCapturedNativeBase()
        {
            int capacity = 100;
            var service = new BaseValueOverrideService();
            Assert.True(service.TryRegister("wagon.variant", () => capacity, value => capacity = value, 1, int.MaxValue));
            Assert.True(service.TrySetMultiplier("wagon.variant", 2d));
            Assert.Equal(200, capacity);

            // Re-registration observes the modified value but keeps the first immutable base.
            Assert.True(service.TryRegister("wagon.variant", () => capacity, value => capacity = value, 1, int.MaxValue));
            Assert.Equal(100d, service.Registrations.Single().BaseValue);
            Assert.True(service.TrySetMultiplier("wagon.variant", 1d));
            Assert.Equal(100, capacity);
            Assert.True(service.TryReset("wagon.variant"));
            Assert.Equal(100, capacity);
        }

        [Fact]
        public void FixedPointCapacityMultiplierPreservesRawPrecision()
        {
            Percent multiplier = 100.Percent();
            var service = new BaseValueOverrideService();
            Assert.True(service.TryRegister(
                "ship.variant",
                typeof(Percent),
                () => multiplier,
                value => multiplier = (Percent)value!,
                1d,
                int.MaxValue));
            Assert.True(service.TrySetMultiplier("ship.variant", 1.5d));
            Assert.Equal(150000, multiplier.RawValue);
            Assert.True(service.TryReset("ship.variant"));
            Assert.Equal(100000, multiplier.RawValue);
        }
    }
}
