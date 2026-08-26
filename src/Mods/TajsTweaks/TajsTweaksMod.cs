// Taj's COI Mods | TajsTweaksMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using Mafi.Core.Mods;
using TajsCOI.Tweaks.Features.Difficulty;

#endregion

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Root mod class. Keep this boring.
    ///     Content/prototypes can be registered here; runtime systems should generally
    ///     live in focused GlobalDependency services.
    /// </summary>
    public sealed class TajsTweaksMod : DataOnlyMod
    {
        public TajsTweaksMod(ModManifest manifest) : base(manifest)
        {
            // This runs while the mod catalog is built, before the main-menu new-game wizard
            // creates its difficulty controls. It only widens native percent option arrays; all
            // live-save changes remain owned by TajsDifficultyFeature.
            TajsDifficultyOptionCatalog.ApplyExtendedOptions();
        }

        public override void RegisterPrototypes(ProtoRegistrator registrator)
        {
            // Add prototype/data registration here as features are added.
            // Example: `registrator.RegisterData<MyMachineData>();`
        }
    }
}
