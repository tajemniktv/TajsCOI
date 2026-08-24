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

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Installs independently configured patch features. Candidates remain disabled by default
    ///     and fail open when their exact 0.8.7a compatibility seam is unavailable.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class PerformanceFeatureHost
    {
        private sealed class FeatureDefinition
        {
            internal FeatureDefinition(string id, string configKey, Func<IPerformanceFeature> create)
            {
                Id = id;
                ConfigKey = configKey;
                Create = create;
            }

            internal string Id { get; }
            internal string ConfigKey { get; }
            internal Func<IPerformanceFeature> Create { get; }
        }

        private static readonly IReadOnlyList<FeatureDefinition> s_features =
            new FeatureDefinition[]
            {
                new("SaveLoadReadBuffer", SaveLoadReadBufferSettings.EnableConfigKey, () => new SaveLoadReadBufferFeature()),
                new("StreamingSaveCompression", StreamingSaveCompressionSettings.EnableConfigKey, () => new StreamingSaveCompressionFeature()),
                new("LowProductTextures", LowProductTexturesSettings.EnableConfigKey, () => new LowProductTexturesFeature()),
                new("LazyResourceVisualization", LazyResourceVisualizationSettings.EnableConfigKey, () => new LazyResourceVisualizationFeature()),
                new("ProductBufferShrink", ProductBufferShrinkSettings.EnableConfigKey, () => new ProductBufferShrinkFeature()),
            };

        private static readonly object s_processConfigGate = new();
        private static IReadOnlyDictionary<string, bool>? s_processEnabled;

        public PerformanceFeatureHost(ITajsRuntime runtime, ITajsSettings settings)
        {
            PerformanceSettingsCatalog.RegisterAll(settings);
            IReadOnlyDictionary<string, bool> processEnabled = GetProcessConfiguration(settings);

            foreach (FeatureDefinition definition in s_features)
            {
                IPerformanceFeature feature = definition.Create();
                ITajsLogger log = runtime.GetLogger(PerformanceSettingsCatalog.ModId, definition.Id);
                if (!processEnabled[definition.Id])
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
                $"{s_features.Count} patch feature(s) registered",
                "Registered patch features were evaluated independently; manual command features report separately."));
        }

        private static IReadOnlyDictionary<string, bool> GetProcessConfiguration(ITajsSettings settings)
        {
            lock (s_processConfigGate)
            {
                if (s_processEnabled is not null)
                {
                    return s_processEnabled;
                }

                PerformanceSettingsCatalog.LoadStartupValues(settings);
                s_processEnabled = s_features.ToDictionary(
                    x => x.Id,
                    x => settings.Get<bool>(PerformanceSettingsCatalog.ModId, x.ConfigKey),
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
