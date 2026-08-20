#region

using Mafi;
using Mafi.Collections;
using Mafi.Core.Mods;

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
        Log.Info("TajsTweaks: constructed");
    }

    public override void RegisterPrototypes(ProtoRegistrator registrator)
    {
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