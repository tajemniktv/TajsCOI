// Taj's COI Mods | BuildMetadata.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Mafi.Core.Mods;

#endregion

namespace TajsTweaks.Infrastructure;

internal static class BuildMetadata
{
    private const string Unknown = "unknown";

    private static readonly Assembly Assembly = typeof(BuildMetadata).Assembly;

    internal static string Version => Get("ModVersion", Assembly.GetName().Version?.ToString() ?? Unknown);
    internal static string Configuration => Get("BuildConfiguration");
    internal static string GitCommit => Get("GitCommit");
    internal static string BuildTimestampUtc { get; private set; } = Unknown;

    internal static void Initialize(ModManifest manifest)
    {
        try
        {
            var assemblyName = Assembly.GetName().Name ?? "TajsTweaks";
            var assemblyPath = Path.Combine(manifest.RootDirectoryPath, assemblyName + ".dll");

            if (File.Exists(assemblyPath))
                BuildTimestampUtc = File.GetLastWriteTimeUtc(assemblyPath)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        catch
        {
            BuildTimestampUtc = Unknown;
        }
    }

    private static string Get(string key, string fallback = Unknown)
    {
        return Assembly
                   .GetCustomAttributes<AssemblyMetadataAttribute>()
                   .FirstOrDefault(attribute => attribute.Key == key)
                   ?.Value
               ?? fallback;
    }
}