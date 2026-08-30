// Taj's COI Mods | TransportFlowLimitTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using TajsCOI.Tweaks.Features.TransportFlowLimits;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class TransportFlowLimitTests
    {
        public TransportFlowLimitTests()
        {
            TransportFlowLimitState.ResetForTests();
        }

        [Fact]
        public void UnlimitedTransportExitsWithoutReservingTokens()
        {
            bool limited = TransportFlowLimitState.TryReserve(42, 100, 10, out int allowed, out _);

            Assert.False(limited);
            Assert.Equal(100, allowed);
        }

        [Fact]
        public void TokenBucketUsesSimulationTimeAndRefundsNativeRemainder()
        {
            Assert.True(TransportFlowLimitState.TrySetLimit(42, 100));

            // One-second bounded burst starts full. Native accepts only 40, so 60 tokens return.
            Assert.True(TransportFlowLimitState.TryReserve(42, 150, 100, out int allowed, out var first));
            Assert.Equal(100, allowed);
            Assert.Equal(150, first.Requested);
            TransportFlowLimitState.CompleteReservation(42, first, 40);

            // Same simulation step cannot exceed the remaining 60-token balance.
            Assert.True(TransportFlowLimitState.TryReserve(42, 100, 100, out int sameStep, out var second));
            Assert.Equal(60, sameStep);
            TransportFlowLimitState.CompleteReservation(42, second, 60);

            // Ten simulation steps accrue one second's rate, but never exceed the one-second burst.
            Assert.True(TransportFlowLimitState.TryReserve(42, 100, 110, out int nextStep, out var third));
            Assert.Equal(100, nextStep);
            TransportFlowLimitState.CompleteReservation(42, third, 0);
        }

        [Fact]
        public void BucketIsHardBoundedAndClockRewindDoesNotMintTokens()
        {
            Assert.True(TransportFlowLimitState.TrySetLimit(7, 25));

            Assert.True(TransportFlowLimitState.TryReserve(7, 25, 100, out int initial, out var first));
            Assert.Equal(25, initial);
            TransportFlowLimitState.CompleteReservation(7, first, 0);

            // A large jump only refills the one-second burst (25), not an unbounded backlog.
            Assert.True(TransportFlowLimitState.TryReserve(7, 100, 1_000_000, out int refill, out var second));
            Assert.Equal(25, refill);
            TransportFlowLimitState.CompleteReservation(7, second, 25);

            // Rewinding the simulation step leaves the bucket empty.
            Assert.True(TransportFlowLimitState.TryReserve(7, 1, 900, out int rewind, out _));
            Assert.Equal(0, rewind);
        }

        [Theory]
        [InlineData(-1d)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(1_000_001d)]
        public void InvalidFixedPointLimitsAreRejected(double limit)
        {
            Assert.False(TransportFlowLimitState.TrySetLimit(42, limit));
            Assert.False(TransportFlowLimitState.TryGetLimit(42, out _));
        }

        [Fact]
        public void ZeroClearsPolicyAndReregistrationStartsFromNativeBase()
        {
            Assert.True(TransportFlowLimitState.TrySetLimit(42, 100));
            Assert.True(TransportFlowLimitState.TryReserve(42, 100, 10, out int first, out var reservation));
            Assert.Equal(100, first);
            TransportFlowLimitState.CompleteReservation(42, reservation, 0);

            // Re-registering with a new rate must not compound or inherit the old bucket.
            Assert.True(TransportFlowLimitState.TrySetLimit(42, 200));
            Assert.True(TransportFlowLimitState.TryReserve(42, 200, 10, out int reregis, out var reregisReservation));
            Assert.Equal(200, reregis);
            TransportFlowLimitState.CompleteReservation(42, reregisReservation, 0);

            Assert.True(TransportFlowLimitState.TrySetLimit(42, 0));
            Assert.False(TransportFlowLimitState.TryGetLimit(42, out _));
            Assert.False(TransportFlowLimitState.TryReserve(42, 100, 10, out int unlimited, out _));
            Assert.Equal(100, unlimited);
        }
    }
}
