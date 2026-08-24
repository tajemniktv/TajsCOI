// Taj's COI Mods | PerformanceSettingsCatalog.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using TajsCOI.Common.Settings;
using TajsCOI.Performance.Features.LowProductTextures;
using TajsCOI.Performance.Features.LazyResourceVisualization;
using TajsCOI.Performance.Features.ManualAssetTrim;
using TajsCOI.Performance.Features.ProductBufferShrink;
using TajsCOI.Performance.Features.SaveLoadReadBuffer;
using TajsCOI.Performance.Features.StreamingSaveCompression;

namespace TajsCOI.Performance
{
    internal static class PerformanceSettingsCatalog
    {
        internal const string ModId = "TajsPerformance";
        private const string ModDisplayName = "Taj's Performance";
        private const SettingFlags CandidateFlags = SettingFlags.Advanced | SettingFlags.Experimental;

        internal static readonly IReadOnlyList<SettingDescriptor> All = new[]
        {
            SettingDescriptor.Boolean(
                ModId, ModDisplayName, SaveLoadReadBufferSettings.EnableConfigKey,
                "Large save/load buffer",
                "Uses a measured 64 KiB buffered save reader while preserving checksum preflight and load semantics.",
                false, "Save and Load", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags),
            SettingDescriptor.Integer(
                ModId, ModDisplayName, SaveLoadReadBufferSettings.SizeConfigKey,
                "Save/load buffer size", "Buffered save-reader size in KiB; local measurements plateaued at 64 KiB.",
                64, 16, 256, 1, "Save and Load", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags,
                componentRequirement: SaveLoadReadBufferSettings.EnableConfigKey),
            SettingDescriptor.Boolean(
                ModId, ModDisplayName, StreamingSaveCompressionSettings.EnableConfigKey,
                "Streaming save compression",
                "Streams gzip directly into CoI's seekable temporary save file while retaining post-write validation and atomic rename.",
                false, "Save and Load", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags),
            SettingDescriptor.Boolean(
                ModId, ModDisplayName, StreamingSaveCompressionSettings.SkipUncompressedChecksumConfigKey,
                "Skip uncompressed save checksum",
                "Skips the redundant uncompressed CRC pass. Compressed CRC and post-write validation remain enabled.",
                false, "Save and Load", applyMode: SettingApplyMode.RestartGame,
                flags: CandidateFlags | SettingFlags.Dangerous,
                componentRequirement: StreamingSaveCompressionSettings.EnableConfigKey),
            SettingDescriptor.Boolean(
                ModId, ModDisplayName, LowProductTexturesSettings.EnableConfigKey,
                "Low product textures",
                "Uses CoI's normal texture-array rebuild path with lower-resolution product texture slices.",
                false, "Rendering", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags),
            SettingDescriptor.Boolean(
                ModId, ModDisplayName, LazyResourceVisualizationSettings.EnableConfigKey,
                "Lazy resource visualization build",
                "Defers the hidden whole-map resource-bar build until the first resource overlay activation.",
                false, "Rendering", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags),
            SettingDescriptor.Integer(
                ModId, ModDisplayName, LowProductTexturesSettings.MipBiasConfigKey,
                "Product texture mip bias", "3 = Low, 4 = Very Low; CoI's 64 px minimum slice size remains enforced.",
                3, 3, 4, 1, "Rendering", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags,
                componentRequirement: LowProductTexturesSettings.EnableConfigKey),
            SettingDescriptor.Boolean(
                ModId, ModDisplayName, ManualAssetTrimSettings.EnableConfigKey,
                "Manual asset trim",
                "Enables the paused-only trim_unused_assets command. The action is never run automatically.",
                false, "Memory", applyMode: SettingApplyMode.Immediate, flags: CandidateFlags),
            SettingDescriptor.Boolean(
                ModId, ModDisplayName, ProductBufferShrinkSettings.EnableConfigKey,
                "Product buffer shrinking",
                "Conservatively shrinks remappable live/reserve product buffers after sustained under-utilization.",
                false, "Rendering", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags),
            SettingDescriptor.Integer(
                ModId, ModDisplayName, ProductBufferShrinkSettings.ObservationFramesConfigKey,
                "Buffer shrink observation frames",
                "Consecutive render uploads at or below 25% utilization required before shrinking.",
                600, 120, 3600, 1, "Rendering", applyMode: SettingApplyMode.RestartGame, flags: CandidateFlags,
                componentRequirement: ProductBufferShrinkSettings.EnableConfigKey),
        };

        internal static void RegisterAll(ITajsSettings settings)
        {
            foreach (SettingDescriptor descriptor in All)
            {
                settings.Register(descriptor);
            }
        }

        internal static void LoadStartupValues(ITajsSettings settings)
        {
            SaveLoadReadBufferSettings.Update(settings.Get<int>(ModId, SaveLoadReadBufferSettings.SizeConfigKey));
            StreamingSaveCompressionSettings.Update(
                settings.Get<bool>(ModId, StreamingSaveCompressionSettings.SkipUncompressedChecksumConfigKey));
            LowProductTexturesSettings.Update(settings.Get<int>(ModId, LowProductTexturesSettings.MipBiasConfigKey));
            ProductBufferShrinkSettings.Update(
                settings.Get<int>(ModId, ProductBufferShrinkSettings.ObservationFramesConfigKey));
        }
    }
}
