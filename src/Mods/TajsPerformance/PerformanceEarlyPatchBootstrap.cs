// Taj's COI Mods | PerformanceEarlyPatchBootstrap.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Performance.Features.LazyResourceVisualization;
using TajsCOI.Performance.Features.PathabilityInitialization;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Installs only the opt-in load-time candidates while the data-only mod is being
    ///     constructed. This is intentionally small and process-scoped; scene-owned features
    ///     continue to be installed and reported by <see cref="PerformanceFeatureHost" />.
    /// </summary>
    internal static class PerformanceEarlyPatchBootstrap
    {
        internal static void InstallFromPersistedSettings()
        {
            if (PerformanceStartupSettings.TryReadPersistedBoolean(
                    "TajsPerformance." + LazyResourceVisualizationSettings.EnableConfigKey,
                    out bool lazyEnabled) && lazyEnabled)
            {
                LazyResourceVisualizationFeature.TryInstallProcessEarly();
            }

            if (PerformanceStartupSettings.TryReadPersistedBoolean(
                    "TajsPerformance." + PathabilityInitializationSettings.EnableConfigKey,
                    out bool pathabilityEnabled) && pathabilityEnabled)
            {
                PathabilityInitializationFeature.TryInstallProcessEarly();
            }
        }
    }
}
