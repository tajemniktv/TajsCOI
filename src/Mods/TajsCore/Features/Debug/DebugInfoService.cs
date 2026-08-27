// Taj's COI Mods | DebugInfoService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Collections.Generic;
using System.Text;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Simulation;
using TajsCOI.Bootstrap;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Diagnostics;
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
            BootstrapStatus bootstrap = BootstrapApi.Status;
            m_runtime.ReportCompatibility(
                new CompatibilityReport(
                    "TajsBootstrap",
                    "EarlyLoader",
                    BootstrapCompatibility(bootstrap.State),
                    "Optional Doorstop.Entrypoint.Start with canonical 0Harmony.dll",
                    bootstrap.State + (bootstrap.CanonicalVersion.Length == 0 ? string.Empty : " " + bootstrap.CanonicalVersion),
                    bootstrap.State == BootstrapState.Ready
                        ? "Canonical Harmony was loaded by the optional early loader."
                        : "Optional bootstrap is inactive; the normal no-bootstrap Tajs installation remains available."));
            m_runtime.RegisterCapability(
                new RuntimeCapabilityDescriptor(
                    "TajsCore.HarmonyInspection",
                    "TajsCore",
                    "HarmonyDiagnostics",
                    RuntimeCapabilityState.Available,
                    BuildMetadata.Version,
                    "On-demand read-only Harmony ownership and collision inspection.",
                    string.Empty,
                    RuntimeComponentLifetime.Process));
            m_runtime.RegisterComponent(
                new RuntimeComponentDescriptor(
                    "TajsCore",
                    "HarmonyDiagnostics",
                    RuntimeComponentLifetime.Process,
                    "Harmony.GetAllPatchedMethods and Harmony.GetPatchInfo",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()));
        }

        private static CompatibilityState BootstrapCompatibility(BootstrapState state) =>
            state == BootstrapState.Ready || state == BootstrapState.NotInitialized || state == BootstrapState.Disabled
                ? CompatibilityState.Compatible
                : CompatibilityState.Disabled;

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
            builder.AppendLine("Loaded mods:");

            foreach (LoadedModSnapshot mod in m_runtime.GetLoadedModSnapshot())
            {
                string status = mod.LoadSucceeded ? "loaded" : "FAILED: " + mod.LoadError;
                builder.AppendLine($"  {mod.Id} {mod.Version}: {status}");
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

            builder.AppendLine("Capabilities:");
            IReadOnlyList<RuntimeCapabilityDescriptor> capabilities = m_runtime.GetCapabilitySnapshot();
            if (capabilities.Count == 0)
            {
                builder.AppendLine("  none");
            }
            else
            {
                foreach (RuntimeCapabilityDescriptor capability in capabilities)
                {
                    builder.AppendLine(
                        $"  {capability.CapabilityId}: {capability.State} ({capability.ModId}/{capability.ComponentId})");
                    if (capability.Reason.Length > 0)
                    {
                        builder.AppendLine("    reason: " + capability.Reason);
                    }
                }
            }

            builder.AppendLine("Components:");
            IReadOnlyList<RuntimeComponentDescriptor> components = m_runtime.GetComponentSnapshot();
            if (components.Count == 0)
            {
                builder.AppendLine("  none");
            }
            else
            {
                foreach (RuntimeComponentDescriptor component in components)
                {
                    string owners = component.HarmonyOwnerIds.Count == 0
                        ? "none"
                        : string.Join(",", component.HarmonyOwnerIds);
                    builder.AppendLine(
                        $"  {component.ModId}/{component.ComponentId}: lifetime={component.Lifetime}, owners={owners}");
                    builder.AppendLine("    expected: " + component.ExpectedSeam);
                    builder.AppendLine(
                        "    required: " + string.Join(",", component.RequiredCapabilityIds) +
                        "; optional: " + string.Join(",", component.OptionalCapabilityIds));
                }
            }

            HarmonyInspectionSnapshot harmony = m_runtime.GetHarmonyInspectionSnapshot();
            builder.AppendLine("Harmony targets:");
            builder.AppendLine(
                $"  Tajs targets={harmony.TajsPatchedTargetCount}, shared={harmony.SharedTargetCount}, " +
                $"attention={harmony.AttentionCount}, patches={harmony.TajsPatchCount}");
            if (!harmony.IsAvailable)
            {
                builder.AppendLine("  error: " + harmony.Error);
            }

            return builder.ToString().TrimEnd();
        }
    }
}
