// Taj's COI Mods | TajsCoreMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using Mafi.Core.Mods;
using TajsCOI.Core.Infrastructure;
using TajsCOI.Core.Settings;

#endregion

namespace TajsCOI.Core
{
    /// <summary>
    ///     Suite runtime foundation. Core deliberately owns no gameplay behavior.
    /// </summary>
    public sealed class TajsCoreMod : DataOnlyMod
    {
        public TajsCoreMod(ModManifest manifest) : base(manifest)
        {
            BuildMetadata.Initialize(manifest);
            TajsMainMenuBridge.TryInstall();
        }

        public override void RegisterPrototypes(ProtoRegistrator registrator)
        {
        }
    }
}
