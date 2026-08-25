// Taj's COI Mods | PerformanceFeatureHost.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using TajsCOI.Performance.Features.SaveLoadReadBuffer;
using TajsCOI.Performance.Features.StreamingSaveCompression;
using TajsCOI.Performance.Features.LowProductTextures;
using TajsCOI.Performance.Features.LazyResourceVisualization;
using TajsCOI.Performance.Features.ProductBufferShrink;
using TajsCOI.Performance.Features.RenderingLoadShedding;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Installs independently configured patch features. Candidates remain disabled by default
    ///     and fail open when their exact 0.8.7a compatibility seam is unavailable.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class PerformanceFeatureHost
    {
        private static readonly IReadOnlyList<Func<IPerformanceFeature>> s_featureFactories =
            new Func<IPerformanceFeature>[]
            {
                () => new SaveLoadReadBufferFeature(),
                () => new StreamingSaveCompressionFeature(),
                () => new LowProductTexturesFeature(),
                () => new LazyResourceVisualizationFeature(),
                () => new ProductBufferShrinkFeature(),
            };

        private static readonly object s_processConfigGate = new();
        private static IReadOnlyDictionary<string, bool>? s_processEnabled;

        public PerformanceFeatureHost(ITajsRuntime runtime, ITajsSettings settings)
        {
            PerformanceSettingsCatalog.RegisterAll(settings);
            RenderingLoadSheddingFeature.RefreshFromSettings(settings);
            settings.Changed += (_, change) =>
            {
                RenderingLoadSheddingFeature.RefreshFromSettings(settings);
                RenderingLoadSheddingFeature.OnSettingChanged(change, settings);
            };
            IPerformanceFeature[] features = s_featureFactories.Select(factory => factory()).ToArray();
            IReadOnlyDictionary<string, bool> processEnabled = GetProcessConfiguration(settings, features);

            try
            {
                var rendering = new RenderingLoadSheddingFeature();
                rendering.Install(runtime, runtime.GetLogger(PerformanceSettingsCatalog.ModId, rendering.Id));
            }
            catch (Exception exception)
            {
                runtime.GetLogger(PerformanceSettingsCatalog.ModId, "RenderingLoadShedding")
                    .Exception(exception, "Live rendering controls failed open during installation.");
                runtime.ReportCompatibility(new CompatibilityReport(
                    PerformanceSettingsCatalog.ModId,
                    "RenderingLoadShedding",
                    CompatibilityState.Disabled,
                    "Unity QualitySettings and optional scene particle controls",
                    exception.GetType().Name,
                    "Vanilla rendering remains active."));
            }

            foreach (IPerformanceFeature feature in features)
            {
                ITajsLogger log = runtime.GetLogger(PerformanceSettingsCatalog.ModId, feature.Id);
                if (!processEnabled[feature.Id])
                {
                    if (TryIsProcessPatchInstalled(feature))
                    {
                        runtime.ReportCompatibility(new CompatibilityReport(
                            PerformanceSettingsCatalog.ModId,
                            feature.Id,
                            CompatibilityState.Compatible,
                            "Feature configuration is snapshotted for the process lifetime",
                            "Existing process patch is still installed",
                            "This feature is disabled for the next process; restart the game to remove the current patch."));
                        continue;
                    }

                    runtime.ReportCompatibility(new CompatibilityReport(
                        PerformanceSettingsCatalog.ModId,
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
                    bool existingPatch = TryIsProcessPatchInstalled(feature);
                    string status = existingPatch
                        ? "an existing compatible process patch remains active"
                        : "vanilla behavior remains active";
                    log.Exception(exception, $"Feature '{feature.Id}' could not be installed; {status}.");
                    runtime.ReportCompatibility(new CompatibilityReport(
                        PerformanceSettingsCatalog.ModId,
                        feature.Id,
                        CompatibilityState.Disabled,
                        "Compatible 0.8.7a targets and a successful patch installation",
                        exception.GetType().Name,
                        $"Installation failed open; {status}."));
                }
            }

            runtime.ReportCompatibility(new CompatibilityReport(
                PerformanceSettingsCatalog.ModId,
                "FeatureHost",
                CompatibilityState.Compatible,
                "Only individually switchable patch features are registered",
                $"{features.Length} patch feature(s) registered",
                "Registered patch features were evaluated independently; manual command features report separately."));
        }

        private static IReadOnlyDictionary<string, bool> GetProcessConfiguration(
            ITajsSettings settings,
            IReadOnlyList<IPerformanceFeature> features)
        {
            lock (s_processConfigGate)
            {
                if (s_processEnabled is not null)
                {
                    return s_processEnabled;
                }

                PerformanceSettingsCatalog.LoadStartupValues(settings);
                s_processEnabled = features.ToDictionary(
                    feature => feature.Id,
                    feature => settings.Get<bool>(PerformanceSettingsCatalog.ModId, feature.ConfigKey),
                    StringComparer.Ordinal);
                return s_processEnabled;
            }
        }

        private static bool TryIsProcessPatchInstalled(IPerformanceFeature feature)
        {
            try
            {
                return feature.IsProcessPatchInstalled();
            }
            catch
            {
                return false;
            }
        }
    }
}
