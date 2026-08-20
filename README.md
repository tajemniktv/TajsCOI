# TajsCOI - Taj's mods for Captain of Industry

A fresh attempt at modding Coi.

## Naming/layout

- Mods live in `src/Mods/<ModName>/`

This keeps generic CoI tooling separate from in-game mod names.

## Features

### Taj's Tweaks

`tajs_tweaks_info`

It prints out basic info (whether the mod is loaded) and the game speed.

`set_game_speed_unlocked 30`

Sets the game speed to 30, bypassing the artificial 20 limit

## Adding another mod

Create:

`src/Mods/TajsSomething/TajsSomething.csproj`

with:

```xml

<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <IsCoiMod>true</IsCoiMod>
        <ModId>TajsSomething</ModId>
        <AssemblyName>TajsSomething</AssemblyName>
        <RootNamespace>TajsSomething</RootNamespace>
        <Version>0.1.0</Version>
    </PropertyGroup>
</Project>
```

Add its `manifest.json`, then add the project to `TajsCOI.slnx`.

It automatically inherits:

- net48
- current CoI DLL locations
- Rider/dotnet-compatible framework references
- automatic mod deployment
- validation of the CoI installation

## Adding more game/Unity assemblies

The starter deliberately references only:

- Mafi.dll
- Mafi.Core.dll
- Mafi.Base.dll

When a mod actually needs UI/Unity APIs, add only the specific references it uses (for example `Mafi.Unity.dll` plus required `UnityEngine.*` modules). Keeping those optional avoids dragging half of Unity into every tiny mod.

## Unity

When I eventually rename/create a dedicated Unity project, eg `src/TajsCOI.Unity`, a mod can opt into automatic AssetBundle deployment by adding this to its `.csproj`:

```xml

<AssetBundlesSource>$(MSBuildProjectDirectory)\..\..\TajsCOI.Unity\AssetBundles</AssetBundlesSource>
```

## Versioning

Versions in these files have to be kept in sync manually:

- `TajsTweaks.csproj`
- `manifest.json`

### One source of truth

Edit these values in:

`src/Mods/TajsTweaks/TajsTweaks.csproj`

```xml

<ModVersion>0.1.0</ModVersion>
<ModDisplayName>Tajs Tweaks</ModDisplayName>
<ModDescription>...</ModDescription>
<ModAuthor>Taj</ModAuthor>
<MinGameVersion>0.8.7</MinGameVersion>
<MaxVerifiedGameVersion>0.8.7</MaxVerifiedGameVersion>
```

On build, they are used for:

- .NET assembly metadata
- generated `manifest.json`
- deploy messages
- Release package file name

So there is no longer a csproj/manifest version to keep manually synchronized.

### Unity DLL sync

Each build copies the latest DLL, PDB and XML docs to:

`src/ExampleMod.Unity/Assets/DLLs`

### Live deployment

Every Debug or Release build deploys the mod to:

`%APPDATA%\Captain of Industry\Mods\TajsTweaks`

Disable for one build with:

`dotnet build -p:DeployToModsFolder=false`

### Release ZIP

A Release build additionally creates:

`%APPDATA%\Captain of Industry\Mods\TajsTweaks_0.1.0.zip`

The ZIP contains the mod directory as its root, matching the convenient distribution pattern used by MaFi's ExampleMod.

Disable it with:

`dotnet build -c Release -p:CreateReleaseZip=false`
