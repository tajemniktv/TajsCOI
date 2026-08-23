// Taj's COI Mods | TajsPerformanceMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi.Core.Mods;
using TajsCOI.Performance.Features.SaveLoadReadBuffer;
using TajsCOI.Performance.Features.StreamingSaveCompression;
using TajsCOI.Performance.Features.LowProductTextures;
using TajsCOI.Performance.Features.ManualAssetTrim;
using TajsCOI.Performance.Features.ProductBufferShrink;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Root performance mod. Evidence-backed features are installed by
    ///     <see cref="PerformanceFeatureHost"/> after dependency resolution.
    /// </summary>
    public sealed class TajsPerformanceMod : DataOnlyMod
    {
        public TajsPerformanceMod(ModManifest manifest) : base(manifest)
        {
        }

        public override void RegisterPrototypes(ProtoRegistrator registrator)
        {
            SaveLoadReadBufferSettings.Update(JsonConfig.GetInt(SaveLoadReadBufferSettings.SizeConfigKey));
            StreamingSaveCompressionSettings.Update(
                JsonConfig.GetBool(StreamingSaveCompressionSettings.SkipUncompressedChecksumConfigKey));
            LowProductTexturesSettings.Update(JsonConfig.GetInt(LowProductTexturesSettings.MipBiasConfigKey));
            ManualAssetTrimSettings.Update(JsonConfig.GetBool(ManualAssetTrimSettings.EnableConfigKey));
            ProductBufferShrinkSettings.Update(JsonConfig.GetInt(ProductBufferShrinkSettings.ObservationFramesConfigKey));
        }
    }
}
