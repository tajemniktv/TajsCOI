// Taj's COI Mods | DebugInfoService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Collections.Generic;
using System.Text;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Mods;
using Mafi.Core.Simulation;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Runtime;
using TajsCOI.Core.Infrastructure;

#endregion

namespace TajsCOI.Core.Features.Debug
{
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class DebugInfoService
    {
        private readonly SimLoopEvents m_simLoop;
        private readonly ITajsRuntime m_runtime;
        private readonly HarmonyRuntimeInfo m_harmony;

        public DebugInfoService(SimLoopEvents simLoop, ITajsRuntime runtime, TajsCoreMod coreMod)
        {
            m_simLoop = simLoop;
            m_runtime = runtime;
            m_harmony = HarmonyRuntimeInfo.Inspect(coreMod.Manifest.RootDirectoryPath);
            m_runtime.ReportCompatibility(m_harmony.ToCompatibilityReport());
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

        [ConsoleCommand(
            documentation: "Shows loaded Taj's mods, shared runtime assemblies and component compatibility.",
            customCommandName: "tajs_core_status")]
        public string GetStatus()
        {
            var builder = new StringBuilder(512);
            builder.AppendLine(GetInfo());
            builder.AppendLine("Harmony:");
            builder.AppendLine($"  packaged: {m_harmony.PackagedVersion}");
            builder.AppendLine($"  loaded:   {m_harmony.LoadedVersion}");
            builder.AppendLine($"  status:   {m_harmony.State}");
            builder.AppendLine("Tajs mods:");

            foreach (LoadedModData mod in ModsLoader.LoadedAndFailedMods)
            {
                if (!mod.Manifest.Id.StartsWith("Tajs", StringComparison.Ordinal))
                {
                    continue;
                }

                string status = mod.LoadError.HasValue ? "FAILED: " + mod.LoadError.Value : "loaded";
                builder.AppendLine($"  {mod.Manifest.Id} {mod.Manifest.Version}: {status}");
            }

            builder.AppendLine("Compatibility:");
            IReadOnlyList<CompatibilityReport> reports = m_runtime.GetCompatibilitySnapshot();
            if (reports.Count == 0)
            {
                builder.AppendLine("  no component reports");
            }
            else
            {
                foreach (CompatibilityReport report in reports)
                {
                    builder.AppendLine($"  {report.ModId}/{report.ComponentId}: {report.State}");
                    builder.AppendLine($"    expected: {report.Expected}");
                    builder.AppendLine($"    observed: {report.Observed}");
                    builder.AppendLine($"    reason:   {report.Reason}");
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
