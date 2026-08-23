// Taj's COI Mods | BuildMetadata.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System.Globalization;
using System.IO;
using System.Reflection;
using Mafi.Core.Mods;
using TajsCOI.Common.Build;

#endregion

namespace TajsCOI.Core.Infrastructure
{
    internal static class BuildMetadata
    {
        private static readonly Assembly s_assembly = typeof(BuildMetadata).Assembly;
        private static AssemblyBuildInfo s_info = AssemblyBuildInfo.Read(s_assembly);

        internal static string Version => s_info.Version;
        internal static string Configuration => s_info.Configuration;
        internal static string GitCommit => s_info.GitCommit;
        internal static string BuildTimestampUtc =>
            s_info.BuildTimestampUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) ?? "unknown";

        internal static void Initialize(ModManifest manifest)
        {
            string assemblyName = s_assembly.GetName().Name ?? "TajsCore";
            string assemblyPath = Path.Combine(manifest.RootDirectoryPath, assemblyName + ".dll");
            s_info = AssemblyBuildInfo.Read(s_assembly, assemblyPath);
        }
    }
}
