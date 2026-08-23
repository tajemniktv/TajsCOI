// Taj's COI Mods | PerformanceFeatureHost.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Installs independently configured performance features. The initial host intentionally
    ///     has no registered features: candidate fixes graduate here only after profiler evidence.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class PerformanceFeatureHost
    {
        private static readonly IReadOnlyList<IPerformanceFeature> s_features =
            Array.Empty<IPerformanceFeature>();

        public PerformanceFeatureHost(TajsPerformanceMod mod, ITajsRuntime runtime)
        {
            ITajsLogger log = runtime.GetLogger("TajsPerformance", "FeatureHost");

            foreach (IPerformanceFeature feature in s_features)
            {
                if (!mod.JsonConfig.GetBool(feature.ConfigKey))
                {
                    runtime.ReportCompatibility(new CompatibilityReport(
                        "TajsPerformance",
                        feature.Id,
                        CompatibilityState.Disabled,
                        "Feature explicitly enabled after a supporting profiler capture",
                        "Disabled by configuration",
                        "No Harmony patches were installed."));
                    continue;
                }

                try
                {
                    feature.Install(runtime, log);
                }
                catch (Exception exception)
                {
                    log.Exception(exception, $"Feature '{feature.Id}' could not be installed; vanilla behavior remains active.");
                    runtime.ReportCompatibility(new CompatibilityReport(
                        "TajsPerformance",
                        feature.Id,
                        CompatibilityState.Disabled,
                        "Compatible 0.8.7a targets and a successful patch installation",
                        exception.GetType().Name,
                        "Installation failed open; vanilla behavior remains active."));
                }
            }

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                "FeatureHost",
                CompatibilityState.Compatible,
                "Only evidence-backed, individually switchable features are registered",
                $"{s_features.Count} feature(s) registered",
                s_features.Count == 0
                    ? "Scaffold is active; no speculative optimization is installed."
                    : "Registered features were evaluated independently."));
        }
    }
}
