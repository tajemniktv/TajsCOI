// Taj's COI Mods | DumpingDecisionContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Profiler.Probes.Dumping;
using Xunit;

namespace TajsCOI.Tests.RuntimeContracts
{
    public sealed class DumpingDecisionContractTests
    {
        [Fact]
        public void DumpSearchClassificationKeepsGlobalAndExplicitScopesDistinct()
        {
            Assert.Equal(
                DumpSearchContractPath.UnknownProduct,
                DumpSearchContract.Classify(hasProduct: false, hasExplicitTowerList: false, globallyAllowed: false));
            Assert.Equal(
                DumpSearchContractPath.GlobalAllowed,
                DumpSearchContract.Classify(hasProduct: true, hasExplicitTowerList: false, globallyAllowed: true));
            Assert.Equal(
                DumpSearchContractPath.GlobalForbiddenNoLocalTower,
                DumpSearchContract.Classify(hasProduct: true, hasExplicitTowerList: false, globallyAllowed: false));
            Assert.Equal(
                DumpSearchContractPath.ExplicitTower,
                DumpSearchContract.Classify(hasProduct: true, hasExplicitTowerList: true, globallyAllowed: true));
            Assert.Equal(
                DumpSearchContractPath.ExplicitTowerGlobalForbiddenRejected,
                DumpSearchContract.Classify(hasProduct: true, hasExplicitTowerList: true, globallyAllowed: false));
        }

        [Fact]
        public void LocalFallbackRequiresEveryEligibilityConditionAndFailsOpen()
        {
            Assert.True(
                DumpSearchContract.IsLocalFallbackEligible(
                    hasProduct: true,
                    globallyAllowed: false,
                    localTowerAcceptsProduct: true,
                    inRange: true,
                    inspectionSucceeded: true));

            Assert.False(
                DumpSearchContract.IsLocalFallbackEligible(
                    hasProduct: false,
                    globallyAllowed: false,
                    localTowerAcceptsProduct: true,
                    inRange: true,
                    inspectionSucceeded: true));
            Assert.False(
                DumpSearchContract.IsLocalFallbackEligible(
                    hasProduct: true,
                    globallyAllowed: true,
                    localTowerAcceptsProduct: true,
                    inRange: true,
                    inspectionSucceeded: true));
            Assert.False(
                DumpSearchContract.IsLocalFallbackEligible(
                    hasProduct: true,
                    globallyAllowed: false,
                    localTowerAcceptsProduct: false,
                    inRange: true,
                    inspectionSucceeded: true));
            Assert.False(
                DumpSearchContract.IsLocalFallbackEligible(
                    hasProduct: true,
                    globallyAllowed: false,
                    localTowerAcceptsProduct: true,
                    inRange: false,
                    inspectionSucceeded: true));
            Assert.False(
                DumpSearchContract.IsLocalFallbackEligible(
                    hasProduct: true,
                    globallyAllowed: false,
                    localTowerAcceptsProduct: true,
                    inRange: true,
                    inspectionSucceeded: false));
        }
    }
}
