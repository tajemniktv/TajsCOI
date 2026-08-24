// Taj's COI Mods | AssemblyBuildInfo.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TajsCOI.Common.Build
{
    public sealed class AssemblyBuildInfo
    {
        private const string Unknown = "unknown";

        private AssemblyBuildInfo(
            string version,
            string configuration,
            string gitCommit,
            DateTime? buildTimestampUtc)
        {
            Version = version;
            Configuration = configuration;
            GitCommit = gitCommit;
            BuildTimestampUtc = buildTimestampUtc;
        }

        public string Version { get; }

        public string Configuration { get; }

        public string GitCommit { get; }

        public DateTime? BuildTimestampUtc { get; }

        public static AssemblyBuildInfo Read(Assembly assembly, string? physicalDllPath = null)
        {
            if (assembly is null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            DateTime? timestamp = null;
            if (!string.IsNullOrWhiteSpace(physicalDllPath))
            {
                try
                {
                    if (File.Exists(physicalDllPath))
                    {
                        timestamp = File.GetLastWriteTimeUtc(physicalDllPath);
                    }
                }
                catch (IOException)
                {
                    timestamp = null;
                }
                catch (UnauthorizedAccessException)
                {
                    timestamp = null;
                }
                catch (System.Security.SecurityException)
                {
                    timestamp = null;
                }
            }

            return new AssemblyBuildInfo(
                GetMetadata(assembly, "ModVersion", assembly.GetName().Version?.ToString() ?? Unknown),
                GetMetadata(assembly, "BuildConfiguration"),
                GetMetadata(assembly, "GitCommit"),
                timestamp);
        }

        private static string GetMetadata(Assembly assembly, string key, string fallback = Unknown) =>
            assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
                ?.Value
            ?? fallback;
    }
}
