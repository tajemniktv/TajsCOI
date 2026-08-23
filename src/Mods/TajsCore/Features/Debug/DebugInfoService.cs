// Taj's COI Mods | DebugInfoService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Simulation;
using TajsCOI.Core.Infrastructure;

#endregion

namespace TajsCOI.Core.Features.Debug
{
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class DebugInfoService
    {
        private readonly SimLoopEvents m_simLoop;

        public DebugInfoService(SimLoopEvents simLoop)
        {
            m_simLoop = simLoop;
        }

        [ConsoleCommand(
            documentation: "Shows TajsCore version, build provenance and basic runtime status.",
            customCommandName: "tajs_core_info")]
        public string GetInfo()
        {
            string coreVersion = typeof(SimLoopEvents).Assembly.GetName().Version?.ToString() ?? "unknown";

            return
                $"TajsCore {BuildMetadata.Version} | {BuildMetadata.Configuration} | git {BuildMetadata.GitCommit} | " +
                $"built {BuildMetadata.BuildTimestampUtc} | Mafi.Core {coreVersion} | " +
                $"requested speed {m_simLoop.SimSpeedMult}x.";
        }
    }
}
