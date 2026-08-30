// Taj's COI Mods | TajsPerformanceMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi.Core.Mods;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Root performance mod. Process-lifetime load candidates are installed from persisted
    ///     opt-ins during construction; scene-owned reporting and the remaining features are
    ///     installed by <see cref="PerformanceFeatureHost" /> after dependency resolution.
    /// </summary>
    public sealed class TajsPerformanceMod : DataOnlyMod
    {
        public TajsPerformanceMod(ModManifest manifest) : base(manifest)
        {
            // Process-lifetime load candidates must be installed before dependency resolution
            // constructs the gameplay-scene host. The reader is fail-closed and only honors an
            // already-persisted explicit opt-in; all reporting remains with the scene host.
            PerformanceEarlyPatchBootstrap.InstallFromPersistedSettings();
        }

        public override void RegisterPrototypes(ProtoRegistrator registrator)
        {
            // TajsPerformance declares no prototypes; its global feature host installs
            // evidence-backed runtime patches after dependency resolution.
        }
    }
}
