// Taj's COI Mods | TajsPerformanceMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi.Core.Mods;

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
            // TajsPerformance declares no prototypes; its global feature host installs
            // evidence-backed runtime patches after dependency resolution.
        }
    }
}
