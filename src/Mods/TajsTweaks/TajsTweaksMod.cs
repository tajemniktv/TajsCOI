// Taj's COI Mods | TajsTweaksMod.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using Mafi;
using Mafi.Collections;
using Mafi.Core.Mods;
using TajsTweaks.Features.DumpingPathfinding;
using TajsTweaks.Features.UnlockedSpeed;
using TajsTweaks.Infrastructure;

#endregion

namespace TajsTweaks;

/// <summary>
///     Root mod class. Keep this boring.
///     Content/prototypes can be registered here; runtime systems should generally
///     live in focused GlobalDependency services.
/// </summary>
public sealed class TajsTweaksMod : DataOnlyMod
{
    public TajsTweaksMod(ModManifest manifest) : base(manifest)
    {
        BuildMetadata.Initialize(manifest);
        Log.Info("TajsTweaks: constructed");
    }

    public override void RegisterPrototypes(ProtoRegistrator registrator)
    {
        var jsonConfig = JsonConfig;
        UnlockedSpeedSettings.Update(jsonConfig.GetInt(UnlockedSpeedSettings.ConfigKey));
        DumpingPathfindingGuardSettings.UpdatePerTruckLimit(
            jsonConfig.GetInt(DumpingPathfindingGuardSettings.PerTruckConfigKey));
        DumpingPathfindingGuardSettings.UpdateTotalLimit(
            jsonConfig.GetInt(DumpingPathfindingGuardSettings.TotalConfigKey));

        jsonConfig.OnValueChanged += paramName =>
        {
            if (paramName == UnlockedSpeedSettings.ConfigKey)
            {
                UnlockedSpeedSettings.Update(jsonConfig.GetInt(UnlockedSpeedSettings.ConfigKey));
            }
            else if (paramName == DumpingPathfindingGuardSettings.PerTruckConfigKey)
            {
                DumpingPathfindingGuardSettings.UpdatePerTruckLimit(
                    jsonConfig.GetInt(DumpingPathfindingGuardSettings.PerTruckConfigKey));
            }
            else if (paramName == DumpingPathfindingGuardSettings.TotalConfigKey)
            {
                DumpingPathfindingGuardSettings.UpdateTotalLimit(
                    jsonConfig.GetInt(DumpingPathfindingGuardSettings.TotalConfigKey));
            }
        };

        // Add prototype/data registration here as features are added.
        // Example: `registrator.RegisterData<MyMachineData>();`
    }

    public override void MigrateJsonConfig(
        VersionSlim savedVersion,
        Dict<string, object> savedValues)
    {
        // Add config migrations here if/when config.json evolves.
    }
}
