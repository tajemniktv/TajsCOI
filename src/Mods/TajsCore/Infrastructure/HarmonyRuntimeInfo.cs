// Taj's COI Mods | HarmonyRuntimeInfo.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TajsCOI.Common.Compatibility;

namespace TajsCOI.Core.Infrastructure
{
    internal sealed class HarmonyRuntimeInfo
    {
        private const string HarmonyAssemblyName = "0Harmony";

        private HarmonyRuntimeInfo(
            string packagedVersion,
            string loadedVersion,
            CompatibilityState state,
            string reason)
        {
            PackagedVersion = packagedVersion;
            LoadedVersion = loadedVersion;
            State = state;
            Reason = reason;
        }

        internal string PackagedVersion { get; }

        internal string LoadedVersion { get; }

        internal CompatibilityState State { get; }

        internal string Reason { get; }

        internal static HarmonyRuntimeInfo Inspect(string coreRootPath)
        {
            string packagedPath = Path.Combine(coreRootPath, HarmonyAssemblyName + ".dll");
            string packagedVersion = ReadPackagedVersion(packagedPath);
            string[] loadedVersions = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => string.Equals(assembly.GetName().Name, HarmonyAssemblyName, StringComparison.Ordinal))
                .Select(assembly => assembly.GetName().Version?.ToString() ?? "unknown")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(version => version, StringComparer.Ordinal)
                .ToArray();
            string loadedVersion = loadedVersions.Length == 0 ? "unavailable" : string.Join(", ", loadedVersions);

            if (packagedVersion == "unavailable")
            {
                return new HarmonyRuntimeInfo(
                    packagedVersion,
                    loadedVersion,
                    CompatibilityState.Disabled,
                    "The packaged Harmony assembly is missing or unreadable.");
            }

            if (loadedVersions.Length == 0)
            {
                return new HarmonyRuntimeInfo(
                    packagedVersion,
                    loadedVersion,
                    CompatibilityState.Disabled,
                    "No Harmony assembly is loaded in the current AppDomain.");
            }

            if (loadedVersions.Length == 1 && string.Equals(packagedVersion, loadedVersions[0], StringComparison.Ordinal))
            {
                return new HarmonyRuntimeInfo(
                    packagedVersion,
                    loadedVersion,
                    CompatibilityState.Compatible,
                    "The packaged and loaded Harmony assembly versions match.");
            }

            return new HarmonyRuntimeInfo(
                packagedVersion,
                loadedVersion,
                CompatibilityState.Degraded,
                "The loaded Harmony assembly version differs from the version packaged by TajsCore.");
        }

        internal CompatibilityReport ToCompatibilityReport() =>
            new(
                "TajsCore",
                "HarmonyRuntime",
                State,
                $"Loaded Harmony version {PackagedVersion}",
                $"Loaded Harmony version(s): {LoadedVersion}",
                Reason);

        private static string ReadPackagedVersion(string path)
        {
            try
            {
                return File.Exists(path)
                    ? AssemblyName.GetAssemblyName(path).Version?.ToString() ?? "unknown"
                    : "unavailable";
            }
            catch (Exception)
            {
                return "unavailable";
            }
        }
    }
}
