// Taj's COI Mods | BootstrapApi.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace TajsCOI.Bootstrap
{
    public enum BootstrapState
    {
        NotInitialized,
        Ready,
        IncompatibleHarmony,
        Failed,
        Disabled,
    }

    /// <summary>
    ///     Primitive, dependency-free status returned by the early bootstrapper. It intentionally
    ///     contains no MaFi, Unity, Harmony, or mod-specific types.
    /// </summary>
    public sealed class BootstrapStatus
    {
        internal BootstrapStatus(
            BootstrapState state,
            string message,
            string canonicalPath,
            string canonicalHash,
            string canonicalVersion)
        {
            State = state;
            Message = message ?? string.Empty;
            CanonicalPath = canonicalPath ?? string.Empty;
            CanonicalSha256 = canonicalHash ?? string.Empty;
            CanonicalVersion = canonicalVersion ?? string.Empty;
        }

        public BootstrapState State { get; }
        public string Message { get; }
        public string CanonicalPath { get; }
        public string CanonicalSha256 { get; }
        public string CanonicalVersion { get; }
        public bool IsReady => State == BootstrapState.Ready;

        public override string ToString() =>
            "TajsBootstrap " + State + ": " + Message +
            (CanonicalPath.Length == 0 ? string.Empty : " path=" + CanonicalPath) +
            (CanonicalVersion.Length == 0 ? string.Empty : " version=" + CanonicalVersion) +
            (CanonicalSha256.Length == 0 ? string.Empty : " sha256=" + CanonicalSha256);
    }

    /// <summary>
    ///     The only public entry point intended for a UnityDoorstop bootstrap assembly.
    ///     Callers provide the packaged Harmony path; no Steam or installation path is guessed.
    /// </summary>
    public static class BootstrapApi
    {
        private static readonly object s_gate = new();
        private static BootstrapStatus s_status = new(
            BootstrapState.NotInitialized,
            "Bootstrap has not been initialized.",
            string.Empty,
            string.Empty,
            string.Empty);

        public static BootstrapStatus Status
        {
            get
            {
                lock (s_gate)
                {
                    return s_status;
                }
            }
        }

        public static BootstrapStatus Initialize(string? canonicalHarmonyPath)
        {
            lock (s_gate)
            {
                s_status = BootstrapLoader.LoadCanonicalHarmony(canonicalHarmonyPath);
                return s_status;
            }
        }

        public static BootstrapStatus InitializeFromGameRoot(string? gameRoot)
        {
            string? path = BootstrapLoader.FindCanonicalHarmony(gameRoot);
            return Initialize(path);
        }

        public static BootstrapStatus Disable()
        {
            lock (s_gate)
            {
                BootstrapLoader.RemoveAssemblyResolver();
                s_status = new BootstrapStatus(
                    BootstrapState.Disabled,
                    "Bootstrap disabled; the normal no-bootstrap mod installation remains available.",
                    s_status.CanonicalPath,
                    s_status.CanonicalSha256,
                    s_status.CanonicalVersion);
                return s_status;
            }
        }
    }

    internal static class BootstrapLoader
    {
        private static readonly object s_gate = new();
        private static ResolveEventHandler? s_resolver;
        private static string? s_canonicalPath;
        private static string? s_canonicalVersion;

        internal static string? FindCanonicalHarmony(string? gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                return null;
            }

            string root = Path.GetFullPath(gameRoot!.Trim());
            string[] candidates =
            {
                Path.Combine(root, "0Harmony.dll"),
                Path.Combine(root, "Captain of Industry_Data", "Managed", "0Harmony.dll"),
                Path.Combine(root, "Mods", "TajsCore", "0Harmony.dll"),
                Path.Combine(root, "TajsCOI", "Bootstrap", "0Harmony.dll"),
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        internal static BootstrapStatus LoadCanonicalHarmony(string? canonicalHarmonyPath)
        {
            if (string.IsNullOrWhiteSpace(canonicalHarmonyPath))
            {
                return Failed("Canonical 0Harmony.dll path was not supplied.");
            }

            string path;
            try
            {
                path = Path.GetFullPath(canonicalHarmonyPath!.Trim());
                if (!File.Exists(path))
                {
                    return Failed("Canonical 0Harmony.dll was not found.", path);
                }
            }
            catch (Exception exception)
            {
                return Failed("Canonical Harmony path is invalid: " + exception.Message);
            }

            AssemblyName expected;
            string hash;
            try
            {
                expected = AssemblyName.GetAssemblyName(path);
                hash = ComputeSha256(path);
            }
            catch (Exception exception)
            {
                return Failed("Canonical 0Harmony.dll metadata could not be read: " + exception.Message, path);
            }

            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            Assembly[] harmonyAssemblies = loaded
                .Where(assembly => string.Equals(assembly.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (Assembly assembly in harmonyAssemblies)
            {
                AssemblyName actual = assembly.GetName();
                string? loadedPath = TryGetAssemblyPath(assembly);
                if (actual.Version != expected.Version ||
                    loadedPath is not null && !string.Equals(Path.GetFullPath(loadedPath), path, StringComparison.OrdinalIgnoreCase))
                {
                    return new BootstrapStatus(
                        BootstrapState.IncompatibleHarmony,
                        "An incompatible 0Harmony assembly is already loaded; bootstrap refused to replace it.",
                        path,
                        hash,
                        expected.Version?.ToString() ?? string.Empty);
                }
            }

            try
            {
                lock (s_gate)
                {
                    s_canonicalPath = path;
                    s_canonicalVersion = expected.Version?.ToString() ?? string.Empty;
                    InstallAssemblyResolver();
                }
                if (harmonyAssemblies.Length == 0)
                {
                    Assembly.LoadFrom(path);
                }
                return new BootstrapStatus(
                    BootstrapState.Ready,
                    "Canonical 0Harmony.dll is loaded and the narrow resolver is active.",
                    path,
                    hash,
                    expected.Version?.ToString() ?? string.Empty);
            }
            catch (Exception exception)
            {
                RemoveAssemblyResolver();
                return new BootstrapStatus(
                    BootstrapState.Failed,
                    "Canonical 0Harmony.dll could not be loaded: " + exception.GetType().Name + ": " + exception.Message,
                    path,
                    hash,
                    expected.Version?.ToString() ?? string.Empty);
            }
        }

        internal static void RemoveAssemblyResolver()
        {
            lock (s_gate)
            {
                if (s_resolver is not null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= s_resolver;
                    s_resolver = null;
                }
                s_canonicalPath = null;
                s_canonicalVersion = null;
            }
        }

        private static void InstallAssemblyResolver()
        {
            if (s_resolver is not null)
            {
                return;
            }
            s_resolver = ResolveHarmony;
            AppDomain.CurrentDomain.AssemblyResolve += s_resolver;
        }

        private static Assembly? ResolveHarmony(object? sender, ResolveEventArgs args)
        {
            AssemblyName requested;
            try
            {
                requested = new AssemblyName(args.Name);
            }
            catch
            {
                return null;
            }
            if (!string.Equals(requested.Name, "0Harmony", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            lock (s_gate)
            {
                return s_canonicalPath is null ? null : Assembly.LoadFrom(s_canonicalPath);
            }
        }

        private static BootstrapStatus Failed(string message, string path = "") =>
            new(BootstrapState.Failed, message, path, string.Empty, string.Empty);

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(stream);
                var builder = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes)
                {
                    builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static string? TryGetAssemblyPath(Assembly assembly)
        {
            try
            {
                return string.IsNullOrWhiteSpace(assembly.Location) ? null : assembly.Location;
            }
            catch
            {
                return null;
            }
        }
    }
}
