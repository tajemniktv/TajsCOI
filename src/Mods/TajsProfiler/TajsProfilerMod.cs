// Taj's COI Mods | TajsProfilerMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using Mafi.Core.Mods;

#endregion

namespace TajsCOI.Profiler
{
    /// <summary>
    ///     Root diagnostics mod. Probe services are discovered as global dependencies.
    /// </summary>
    public sealed class TajsProfilerMod : DataOnlyMod
    {
        public TajsProfilerMod(ModManifest manifest) : base(manifest)
        {
        }

        public override void RegisterPrototypes(ProtoRegistrator registrator)
        {
        }
    }
}
