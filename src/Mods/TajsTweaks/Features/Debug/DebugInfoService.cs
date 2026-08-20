// Taj's Game | DebugInfoService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Simulation;
using TajsTweaks.Infrastructure;
using TajsTweaks.Interop;

#endregion

namespace TajsTweaks.Features.Debug;

[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class DebugInfoService
{
    private readonly SimLoopEvents _simLoop;

    public DebugInfoService(SimLoopEvents simLoop)
    {
        _simLoop = simLoop;
    }

    [ConsoleCommand(
        documentation: "Shows TajsTweaks version, build provenance and basic runtime status.",
        customCommandName: "tajs_tweaks_info")]
    public string GetInfo()
    {
        var coreVersion = typeof(SimLoopEvents).Assembly.GetName().Version?.ToString() ?? "unknown";
        var speedInterop = SimLoopAccess.CanSetRequestedSpeed ? "ready" : "unavailable";

        return
            $"TajsTweaks {BuildMetadata.Version} | {BuildMetadata.Configuration} | git {BuildMetadata.GitCommit} | " +
            $"built {BuildMetadata.BuildTimestampUtc} | Mafi.Core {coreVersion} | " +
            $"requested speed {_simLoop.SimSpeedMult}x | speed interop {speedInterop}.";
    }
}
