// Taj's COI Mods | TajsTweaksMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using Mafi;
using Mafi.Core.Mods;
using HarmonyLib;
using TajsCOI.Tweaks.Features.Difficulty;
using TajsCOI.Tweaks.Features.MapEditor;

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

            // Map-editor preservation must be ready on the main menu, before a gameplay-scoped
            // feature host exists. Installation is process-idempotent and fail-open on unsupported
            // game versions; the host retries/reporting path remains available in gameplay scenes.
            try
            {
                MapEditorThirdPartyFeature.InstallProcess();
            }
            catch (Exception exception)
            {
                // The native menu seam is optional; vanilla map-editor startup remains intact,
                // but an opt-in compatibility failure must be visible to the user.
                Log.Warning(
                    "Map editor third-party preservation is unavailable; vanilla startup remains active. " +
                    (MapEditorNativeContract.LastFailure ?? exception.GetType().Name) + ".");
            }
        }

        public override void RegisterPrototypes(ProtoRegistrator registrator)
        {
            // Add prototype/data registration here as features are added.
            // Example: `registrator.RegisterData<MyMachineData>();`
        }
    }
}
