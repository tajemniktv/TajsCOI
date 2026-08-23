// Taj's COI Mods | PerformanceFeatureHost.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using TajsCOI.Performance.Features.SaveLoadReadBuffer;
using TajsCOI.Performance.Features.StreamingSaveCompression;
using TajsCOI.Performance.Features.LowProductTextures;
using TajsCOI.Performance.Features.ProductBufferShrink;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Installs independently configured patch features. Candidates remain disabled by default
    ///     and fail open when their exact 0.8.7a compatibility seam is unavailable.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class PerformanceFeatureHost
    {
        private static readonly IReadOnlyList<IPerformanceFeature> s_features =
            new IPerformanceFeature[]
            {
                new SaveLoadReadBufferFeature(),
                new StreamingSaveCompressionFeature(),
                new LowProductTexturesFeature(),
                new ProductBufferShrinkFeature(),
            };

        public PerformanceFeatureHost(ITajsRuntime runtime, ITajsSettings settings)
        {
            PerformanceSettingsCatalog.RegisterAll(settings);
            PerformanceSettingsCatalog.LoadStartupValues(settings);

            foreach (IPerformanceFeature feature in s_features)
            {
                ITajsLogger log = runtime.GetLogger("TajsPerformance", feature.Id);
                if (!settings.Get<bool>(PerformanceSettingsCatalog.ModId, feature.ConfigKey))
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
                "Only individually switchable patch features are registered",
                $"{s_features.Count} patch feature(s) registered",
                "Registered patch features were evaluated independently; manual command features report separately."));
        }
    }
}
