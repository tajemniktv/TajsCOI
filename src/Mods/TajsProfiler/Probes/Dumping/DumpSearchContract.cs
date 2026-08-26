// Taj's COI Mods | DumpSearchContract.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsCOI.Profiler.Probes.Dumping
{
    /// <summary>
    ///     Pure decisions shared by dumping diagnostics and its runtime-contract tests.  Keeping
    ///     these decisions independent from Harmony makes scope and fail-open behavior executable
    ///     without constructing a gameplay resolver or a production dumping job.
    /// </summary>
    internal static class DumpSearchContract
    {
        internal static DumpSearchContractPath Classify(
            bool hasProduct,
            bool hasExplicitTowerList,
            bool globallyAllowed)
        {
            if (!hasProduct)
            {
                return DumpSearchContractPath.UnknownProduct;
            }

            if (hasExplicitTowerList)
            {
                return globallyAllowed
                    ? DumpSearchContractPath.ExplicitTower
                    : DumpSearchContractPath.ExplicitTowerGlobalForbiddenRejected;
            }

            return globallyAllowed
                ? DumpSearchContractPath.GlobalAllowed
                : DumpSearchContractPath.GlobalForbiddenNoLocalTower;
        }

        internal static bool IsLocalFallbackEligible(
            bool hasProduct,
            bool globallyAllowed,
            bool localTowerAcceptsProduct,
            bool inRange,
            bool inspectionSucceeded) =>
            hasProduct && !globallyAllowed && localTowerAcceptsProduct && inRange && inspectionSucceeded;
    }

    internal enum DumpSearchContractPath
    {
        UnknownProduct,
        GlobalAllowed,
        GlobalForbiddenLocalFallback,
        GlobalForbiddenNoLocalTower,
        ExplicitTower,
        ExplicitTowerGlobalForbiddenLocal,
        ExplicitTowerGlobalForbiddenRejected,
        Count,
    }
}
