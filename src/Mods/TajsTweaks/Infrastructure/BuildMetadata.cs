// Taj's Game | BuildMetadata.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System.Linq;
using System.Reflection;

#endregion

namespace TajsTweaks.Infrastructure;

internal static class BuildMetadata
{
    private static readonly Assembly Assembly = typeof(BuildMetadata).Assembly;

    internal static string Version => Get("ModVersion", Assembly.GetName().Version?.ToString() ?? "unknown");
    internal static string Configuration => Get("BuildConfiguration");
    internal static string GitCommit => Get("GitCommit");
    internal static string BuildTimestampUtc => Get("BuildTimestampUtc");

    private static string Get(string key, string fallback = "unknown")
    {
        return Assembly
                   .GetCustomAttributes<AssemblyMetadataAttribute>()
                   .FirstOrDefault(attribute => attribute.Key == key)
                   ?.Value
               ?? fallback;
    }
}
