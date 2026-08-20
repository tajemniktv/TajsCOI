# TajsCOI - Taj's mods for Captain of Industry

A Rider-first workspace for Captain of Industry mods.

## Layout

- Mods live in `src/Mods/<ModName>/`.
- Shared CoI build/deploy plumbing lives in `src/Mods/Directory.Build.*`.
- Unity projects stay outside `src/Mods/` so Unity-generated projects do not inherit the normal mod build settings.

## Tajs Tweaks

### Console commands

`set_game_speed_unlocked 30`

Sets the requested simulation speed while bypassing the vanilla 20x validation.

`get_game_speed_unlocked`

Shows the current requested speed and configured maximum.

`tajs_tweaks_info`

Shows mod version, Debug/Release configuration, Git commit, build timestamp, loaded `Mafi.Core` assembly version, requested simulation speed and whether the private speed interop is available.

### Configuration

`src/Mods/TajsTweaks/config.json` currently exposes:

- `unlocked_speed_max` - maximum value accepted by `set_game_speed_unlocked` (default 100, configurable from 20 to 500).

The command reads the current config value each time it runs.

## Normal development loop

Build in Rider, or run:

```powershell
.\build.ps1
```

Each successful Debug or Release build automatically deploys the live mod to:

```text
%APPDATA%\Captain of Industry\Mods\TajsTweaks
```

Build and launch CoI in one command:

```powershell
.\build-and-run.ps1
```

Release build and launch:

```powershell
.\build-and-run.ps1 -Configuration Release
```

The mod is a DLL, so it does not need a Rider `.NET` Run/Debug configuration. If desired, point a Rider PowerShell/Shell Script configuration at `build-and-run.ps1`.

## Log helper

Follow the newest CoI log and show TajsTweaks/warnings/errors by default:

```powershell
.\tail-log.ps1
```

Show every line:

```powershell
.\tail-log.ps1 -All
```

Custom filter:

```powershell
.\tail-log.ps1 -Pattern "TajsTweaks|Path|Simulation"
```

## Build provenance

Every mod build records these values in .NET assembly metadata:

- mod version
- build configuration
- UTC build timestamp
- current Git commit (12-character SHA, or `unknown` when Git is unavailable)

`tajs_tweaks_info` reads those values from the loaded DLL, which makes it much easier to tell which build is actually running.

No `CodeTaskFactory` or Visual-Studio-only manifest task is used.

## Versioning / manifest

The project properties are the source of truth:

`src/Mods/TajsTweaks/TajsTweaks.csproj`

```xml
<ModVersion>0.1.0</ModVersion>
<ModDisplayName>Tajs Tweaks</ModDisplayName>
<ModDescription>...</ModDescription>
<ModAuthor>TajemnikTV</ModAuthor>
<MinGameVersion>0.8.7</MinGameVersion>
<MaxVerifiedGameVersion>0.8.7</MaxVerifiedGameVersion>
```

On build they are used for:

- .NET assembly metadata
- generated `manifest.json`
- deploy messages
- Release package filename

## Release ZIP

A Release build additionally creates:

```text
%APPDATA%\Captain of Industry\Mods\TajsTweaks_<version>.zip
```

The game itself loads the deployed folder. The ZIP is a distribution/archive artifact.

Disable ZIP creation for a build with:

```powershell
dotnet build -c Release -p:CreateReleaseZip=false
```

Disable live deployment with:

```powershell
dotnet build -p:DeployToModsFolder=false
```

## Private API / interop

Private or reflection-based game access belongs under `src/Mods/TajsTweaks/Interop/` rather than inside individual features.

For example, unlocked speed uses `Interop/SimLoopAccess.cs`. If MaFi changes `SimSpeedMult` internals in a future update, that compatibility seam should be the first place to fix instead of hunting reflection code across the mod.

## Adding another mod

Create `src/Mods/TajsSomething/TajsSomething.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <IsCoiMod>true</IsCoiMod>
        <ModId>TajsSomething</ModId>
        <ModVersion>0.1.0</ModVersion>
        <ModDisplayName>Tajs Something</ModDisplayName>
        <ModDescription>Description here.</ModDescription>
        <ModAuthor>TajemnikTV</ModAuthor>
        <MinGameVersion>0.8.7</MinGameVersion>
        <MaxVerifiedGameVersion>0.8.7</MaxVerifiedGameVersion>
        <AssemblyName>$(ModId)</AssemblyName>
        <RootNamespace>TajsSomething</RootNamespace>
    </PropertyGroup>
</Project>
```

Then add the project to `TajsCOI.slnx`.

It inherits:

- `net48`
- CoI DLL paths from `COI_ROOT`
- Rider/dotnet-compatible framework references
- build provenance metadata
- generated manifest
- automatic live deployment
- Release ZIP packaging
- CoI installation validation

## Adding more game / Unity assemblies

Shared projects currently reference only:

- `Mafi.dll`
- `Mafi.Core.dll`
- `Mafi.Base.dll`

When a feature actually needs UI/Unity APIs, add only the specific references it needs, such as `Mafi.Unity.dll` and the relevant `UnityEngine.*` modules.

## Unity

When a dedicated Unity project exists (for example `src/TajsCOI.Unity`), a mod can opt into AssetBundle deployment with:

```xml
<AssetBundlesSource>$(UnityProjectDir)\AssetBundles</AssetBundlesSource>
```
