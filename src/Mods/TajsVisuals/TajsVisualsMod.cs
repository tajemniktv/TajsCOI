// Taj's COI Mods | TajsVisualsMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi.Core.Mods;

namespace TajsCOI.Visuals
{
    /// <summary>
    ///     Root visuals mod. Scene-owned lighting work lives in the feature host so the mod
    ///     remains safe to load in menus and in scenes without a renderer.
    /// </summary>
    public sealed class TajsVisualsMod : DataOnlyMod
    {
        public TajsVisualsMod(ModManifest manifest) : base(manifest)
        {
        }

        public override void RegisterPrototypes(ProtoRegistrator registrator)
        {
        }
    }
}
