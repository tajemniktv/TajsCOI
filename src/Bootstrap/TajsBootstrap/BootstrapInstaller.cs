// Taj's COI Mods | BootstrapInstaller.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace TajsCOI.Bootstrap
{
    public enum BootstrapInstallState
    {
        Failed,
        Refused,
        Installed,
        Verified,
        Drifted,
        Disabled,
        Uninstalled,
    }

    /// <summary>
    ///     Explicit source files for the optional bootstrap payload. The installer never
    ///     discovers a Steam path and only installs the bundled x64 Doorstop proxy when the
    ///     caller supplies it. The existing version.dll is never a Tajs-owned file.
    /// </summary>
    public sealed class BootstrapInstallRequest
    {
        public BootstrapInstallRequest(string gameRoot, string bootstrapAssemblyPath, string canonicalHarmonyPath)
            : this(gameRoot, bootstrapAssemblyPath, canonicalHarmonyPath, GetBundledProxyPath(bootstrapAssemblyPath))
        {
        }

        public BootstrapInstallRequest(
            string gameRoot,
            string bootstrapAssemblyPath,
            string canonicalHarmonyPath,
            string doorstopProxyPath)
        {
            GameRoot = gameRoot ?? string.Empty;
            BootstrapAssemblyPath = bootstrapAssemblyPath ?? string.Empty;
            CanonicalHarmonyPath = canonicalHarmonyPath ?? string.Empty;
            DoorstopProxyPath = doorstopProxyPath ?? string.Empty;
        }

        public string GameRoot { get; }
        public string BootstrapAssemblyPath { get; }
        public string CanonicalHarmonyPath { get; }
        public string DoorstopProxyPath { get; }

        private static string GetBundledProxyPath(string bootstrapAssemblyPath)
        {
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(bootstrapAssemblyPath.Trim()));
                return directory is null ? string.Empty : Path.Combine(directory, "winhttp.dll");
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public sealed class BootstrapInstallResult
    {
        internal BootstrapInstallResult(BootstrapInstallState state, string message, string manifestPath)
        {
            State = state;
            Message = message ?? string.Empty;
            ManifestPath = manifestPath ?? string.Empty;
        }

        public BootstrapInstallState State { get; }
        public string Message { get; }
        public string ManifestPath { get; }

        public bool Succeeded => State == BootstrapInstallState.Installed ||
                                 State == BootstrapInstallState.Verified ||
                                 State == BootstrapInstallState.Disabled ||
                                 State == BootstrapInstallState.Uninstalled;

        public override string ToString() => "TajsBootstrap installer " + State + ": " + Message;
    }

    /// <summary>
    ///     Conservative file custody for the optional early bootstrap payload. Ownership is
    ///     manifest-based: repair and uninstall refuse to overwrite or remove drifted files.
    ///     The installer owns only the files recorded in its manifest. The existing root
    ///     version.dll and any unknown winhttp.dll or doorstop_config.ini are never overwritten.
    /// </summary>
    public static class BootstrapInstaller
    {
        private const int ManifestSchema = 1;
        private const string ManifestFileName = "TajsBootstrap.install.json";
        private const string PayloadDirectory = "TajsCOI\\Bootstrap";
        private const string BootstrapRelativePath = PayloadDirectory + "\\TajsBootstrap.dll";
        private const string HarmonyRelativePath = PayloadDirectory + "\\0Harmony.dll";
        private const string DoorstopProxyRelativePath = "winhttp.dll";
        private const string DoorstopConfigRelativePath = "doorstop_config.ini";

        public static string? DiscoverGameRoot(string? runtimeExecutablePath = null)
        {
            string? executable = runtimeExecutablePath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                try
                {
                    executable = Process.GetCurrentProcess().MainModule?.FileName;
                }
                catch
                {
                    executable = null;
                }
            }

            if (string.IsNullOrWhiteSpace(executable))
            {
                return null;
            }

            string? directory = Path.GetDirectoryName(Path.GetFullPath(executable));
            for (int i = 0; directory is not null && i < 6; i++)
            {
                if (Directory.Exists(Path.Combine(directory, "Captain of Industry_Data", "Managed")) ||
                    File.Exists(Path.Combine(directory, "Captain of Industry.exe")))
                {
                    return directory;
                }
                directory = Directory.GetParent(directory)?.FullName;
            }
            return null;
        }

        public static BootstrapInstallResult Install(BootstrapInstallRequest request)
        {
            if (!TryPrepare(
                    request,
                    out string root,
                    out string bootstrapSource,
                    out string harmonySource,
                    out string doorstopProxySource,
                    out string manifestPath,
                    out BootstrapInstallResult? failure))
            {
                return failure!;
            }

            try
            {
                InstallManifest? existing = TryReadManifest(manifestPath, out string? readError);
                if (readError is not null)
                {
                    return Refused(manifestPath, "Existing bootstrap manifest is unreadable: " + readError);
                }

                BootstrapFileRecord[] expected = BuildExpectedRecords(
                    root,
                    bootstrapSource,
                    harmonySource,
                    doorstopProxySource,
                    BuildDoorstopConfig(bootstrapSource));
                if (existing is not null && !string.Equals(existing.GameRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    return Refused(manifestPath, "Existing bootstrap manifest belongs to another game root.");
                }

                foreach (BootstrapFileRecord record in expected)
                {
                    string destination = SafeCombine(root, record.RelativePath);
                    if (!File.Exists(destination))
                    {
                        continue;
                    }
                    string currentHash = ComputeSha256(destination);
                    bool owned = existing?.Files.Any(file =>
                        string.Equals(file.RelativePath, record.RelativePath, StringComparison.OrdinalIgnoreCase)) == true;
                    if (!string.Equals(currentHash, record.Sha256, StringComparison.OrdinalIgnoreCase) && !owned)
                    {
                        return Refused(manifestPath, "Refusing to overwrite an unknown file: " + record.RelativePath);
                    }
                }

                CopyFileAtomically(bootstrapSource, SafeCombine(root, BootstrapRelativePath));
                CopyFileAtomically(harmonySource, SafeCombine(root, HarmonyRelativePath));
                CopyFileAtomically(doorstopProxySource, SafeCombine(root, DoorstopProxyRelativePath));
                WriteTextAtomically(
                    SafeCombine(root, DoorstopConfigRelativePath),
                    BuildDoorstopConfig(bootstrapSource));
                WriteManifestAtomic(
                    manifestPath,
                    new InstallManifest
                    {
                        Schema = ManifestSchema, GameRoot = root, Enabled = true, Files = expected,
                    });
                return new BootstrapInstallResult(
                    BootstrapInstallState.Installed,
                    "Bootstrap payload, Doorstop proxy, and configuration installed. Existing version.dll was left untouched.",
                    manifestPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                return Failed(manifestPath, "Permission denied; no elevation was attempted: " + exception.Message);
            }
            catch (Exception exception)
            {
                return Failed(manifestPath, exception.GetType().Name + ": " + exception.Message);
            }
        }

        public static BootstrapInstallResult Verify(string gameRoot)
        {
            if (!TryNormalizeRoot(gameRoot, out string root, out string error))
            {
                return Failed(string.Empty, error);
            }
            string manifestPath = GetManifestPath(root);
            InstallManifest? manifest = TryReadManifest(manifestPath, out string? readError);
            if (readError is not null || manifest is null)
            {
                return new BootstrapInstallResult(
                    BootstrapInstallState.Drifted,
                    readError ?? "Bootstrap is not installed.",
                    manifestPath);
            }
            if (!string.Equals(manifest.GameRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return Refused(manifestPath, "Install manifest belongs to another game root.");
            }

            foreach (BootstrapFileRecord record in manifest.Files ?? Array.Empty<BootstrapFileRecord>())
            {
                string path;
                try
                {
                    path = SafeCombine(root, record.RelativePath);
                }
                catch (Exception exception)
                {
                    return Refused(manifestPath, "Install manifest contains an unsafe path: " + exception.Message);
                }
                if (!File.Exists(path) || !string.Equals(ComputeSha256(path), record.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new BootstrapInstallResult(
                        BootstrapInstallState.Drifted,
                        "Bootstrap payload drifted: " + record.RelativePath,
                        manifestPath);
                }
            }
            return new BootstrapInstallResult(
                manifest.Enabled ? BootstrapInstallState.Verified : BootstrapInstallState.Disabled,
                manifest.Enabled ? "Bootstrap payload hashes match the install manifest." : "Bootstrap is disabled.",
                manifestPath);
        }

        public static BootstrapInstallResult Repair(BootstrapInstallRequest request)
        {
            if (!TryPrepare(
                    request,
                    out string root,
                    out string bootstrapSource,
                    out string harmonySource,
                    out string doorstopProxySource,
                    out string manifestPath,
                    out BootstrapInstallResult? failure))
            {
                return failure!;
            }

            InstallManifest? manifest = TryReadManifest(manifestPath, out string? readError);
            if (readError is not null || manifest is null)
            {
                return Refused(manifestPath, readError ?? "Bootstrap is not installed; install first.");
            }
            if (!string.Equals(manifest.GameRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return Refused(manifestPath, "Install manifest belongs to another game root.");
            }
            BootstrapFileRecord[] expected = BuildExpectedRecords(
                root,
                bootstrapSource,
                harmonySource,
                doorstopProxySource,
                BuildDoorstopConfig(bootstrapSource));
            if (!OwnsExpectedFiles(manifest, expected))
            {
                return Refused(manifestPath, "Install manifest does not own the expected bootstrap payload.");
            }

            try
            {
                CopyFileAtomically(bootstrapSource, SafeCombine(root, BootstrapRelativePath));
                CopyFileAtomically(harmonySource, SafeCombine(root, HarmonyRelativePath));
                CopyFileAtomically(doorstopProxySource, SafeCombine(root, DoorstopProxyRelativePath));
                WriteTextAtomically(
                    SafeCombine(root, DoorstopConfigRelativePath),
                    BuildDoorstopConfig(bootstrapSource));
                manifest.Files = expected;
                WriteManifestAtomic(manifestPath, manifest);
                return new BootstrapInstallResult(
                    BootstrapInstallState.Installed,
                    "Owned bootstrap payload, Doorstop proxy, and configuration repaired; existing version.dll was left untouched.",
                    manifestPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                return Failed(manifestPath, "Permission denied; no elevation was attempted: " + exception.Message);
            }
            catch (Exception exception)
            {
                return Failed(manifestPath, exception.GetType().Name + ": " + exception.Message);
            }
        }

        public static BootstrapInstallResult Disable(string gameRoot) => UpdateEnabled(gameRoot, false);

        /// <summary>
        ///     Reads the optional install manifest without changing any files. A missing manifest
        ///     means that no installer-managed bootstrap is present, so direct/manual loading is
        ///     still allowed. A malformed or foreign manifest fails closed for early startup.
        /// </summary>
        internal static bool TryGetEnabled(string? gameRoot, out bool enabled, out string message)
        {
            enabled = true;
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                return true;
            }

            if (!TryNormalizeRoot(gameRoot, out string root, out string rootError))
            {
                message = rootError;
                return false;
            }

            InstallManifest? manifest = TryReadManifest(GetManifestPath(root), out string? readError);
            if (readError is not null)
            {
                enabled = false;
                message = readError;
                return false;
            }
            if (manifest is null)
            {
                return true;
            }

            if (!TryNormalizeRoot(manifest.GameRoot, out string manifestRoot, out string manifestError) ||
                !string.Equals(manifestRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                message = string.IsNullOrEmpty(manifestError)
                    ? "Install manifest belongs to another game root."
                    : manifestError;
                return false;
            }

            enabled = manifest.Enabled;
            message = enabled ? "Bootstrap is enabled." : "Bootstrap is disabled by its install manifest.";
            return true;
        }

        public static BootstrapInstallResult Uninstall(string gameRoot)
        {
            if (!TryNormalizeRoot(gameRoot, out string root, out string error))
            {
                return Failed(string.Empty, error);
            }
            string manifestPath = GetManifestPath(root);
            InstallManifest? manifest = TryReadManifest(manifestPath, out string? readError);
            if (readError is not null || manifest is null)
            {
                return Refused(manifestPath, readError ?? "Bootstrap is not installed.");
            }
            if (!string.Equals(manifest.GameRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return Refused(manifestPath, "Install manifest belongs to another game root.");
            }
            foreach (BootstrapFileRecord record in manifest.Files ?? Array.Empty<BootstrapFileRecord>())
            {
                string path;
                try
                {
                    path = SafeCombine(root, record.RelativePath);
                }
                catch (Exception exception)
                {
                    return Refused(manifestPath, "Install manifest contains an unsafe path: " + exception.Message);
                }
                if (File.Exists(path) && !string.Equals(ComputeSha256(path), record.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Refused(manifestPath, "Refusing to remove a drifted file: " + record.RelativePath);
                }
            }

            try
            {
                foreach (BootstrapFileRecord record in manifest.Files ?? Array.Empty<BootstrapFileRecord>())
                {
                    string path = SafeCombine(root, record.RelativePath);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
                return new BootstrapInstallResult(
                    BootstrapInstallState.Uninstalled,
                    "Owned bootstrap files, Doorstop proxy, configuration, and manifest removed; existing version.dll was left untouched.",
                    manifestPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                return Failed(manifestPath, "Permission denied; no elevation was attempted: " + exception.Message);
            }
            catch (Exception exception)
            {
                return Failed(manifestPath, exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static BootstrapInstallResult UpdateEnabled(string gameRoot, bool enabled)
        {
            if (!TryNormalizeRoot(gameRoot, out string root, out string error))
            {
                return Failed(string.Empty, error);
            }
            string manifestPath = GetManifestPath(root);
            InstallManifest? manifest = TryReadManifest(manifestPath, out string? readError);
            if (readError is not null || manifest is null)
            {
                return Refused(manifestPath, readError ?? "Bootstrap is not installed.");
            }
            if (!string.Equals(manifest.GameRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return Refused(manifestPath, "Install manifest belongs to another game root.");
            }
            try
            {
                manifest.Enabled = enabled;
                WriteManifestAtomic(manifestPath, manifest);
                return new BootstrapInstallResult(
                    enabled ? BootstrapInstallState.Installed : BootstrapInstallState.Disabled,
                    enabled ? "Bootstrap enabled." : "Bootstrap disabled; no payload was removed.",
                    manifestPath);
            }
            catch (Exception exception)
            {
                return Failed(manifestPath, exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static bool TryPrepare(
            BootstrapInstallRequest request,
            out string root,
            out string bootstrapSource,
            out string harmonySource,
            out string doorstopProxySource,
            out string manifestPath,
            out BootstrapInstallResult? failure)
        {
            root = string.Empty;
            bootstrapSource = string.Empty;
            harmonySource = string.Empty;
            doorstopProxySource = string.Empty;
            manifestPath = string.Empty;
            failure = null;
            if (request is null)
            {
                failure = Failed(manifestPath, "Install request is required.");
                return false;
            }
            if (!TryNormalizeRoot(request.GameRoot, out root, out string rootError))
            {
                failure = Failed(manifestPath, rootError);
                return false;
            }
            manifestPath = GetManifestPath(root);
            try
            {
                bootstrapSource = Path.GetFullPath(request.BootstrapAssemblyPath.Trim());
                harmonySource = Path.GetFullPath(request.CanonicalHarmonyPath.Trim());
                doorstopProxySource = Path.GetFullPath(request.DoorstopProxyPath.Trim());
            }
            catch (Exception exception)
            {
                failure = Failed(manifestPath, "Source path is invalid: " + exception.Message);
                return false;
            }
            if (!File.Exists(bootstrapSource) || !File.Exists(harmonySource) || !File.Exists(doorstopProxySource))
            {
                failure = Failed(manifestPath, "Bootstrap, canonical Harmony, and bundled Doorstop proxy source files must all exist.");
                return false;
            }
            return true;
        }

        private static BootstrapFileRecord[] BuildExpectedRecords(
            string root,
            string bootstrapSource,
            string harmonySource,
            string doorstopProxySource,
            string doorstopConfigText) =>
            new[]
            {
                new BootstrapFileRecord
                {
                    RelativePath = BootstrapRelativePath, Sha256 = ComputeSha256(bootstrapSource), Length = new FileInfo(bootstrapSource).Length,
                },
                new BootstrapFileRecord
                {
                    RelativePath = HarmonyRelativePath, Sha256 = ComputeSha256(harmonySource), Length = new FileInfo(harmonySource).Length,
                },
                new BootstrapFileRecord
                {
                    RelativePath = DoorstopProxyRelativePath,
                    Sha256 = ComputeSha256(doorstopProxySource),
                    Length = new FileInfo(doorstopProxySource).Length,
                },
                new BootstrapFileRecord
                {
                    RelativePath = DoorstopConfigRelativePath,
                    Sha256 = ComputeSha256(Encoding.UTF8.GetBytes(doorstopConfigText)),
                    Length = Encoding.UTF8.GetByteCount(doorstopConfigText),
                },
            };

        private static string BuildDoorstopConfig(string bootstrapAssemblyPath)
        {
            string targetAssembly = Path.GetFullPath(bootstrapAssemblyPath.Trim());
            return
                "# TajsCOI-managed UnityDoorstop configuration\r\n" +
                "# Doorstop does not expand environment variables; TajsCOI writes this absolute target path.\r\n" +
                "[General]\r\n" +
                "enabled=true\r\n" +
                "target_assembly=" + targetAssembly + "\r\n" +
                "redirect_output_log=false\r\n";
        }

        private static bool OwnsExpectedFiles(InstallManifest manifest, IReadOnlyList<BootstrapFileRecord> expected) =>
            expected.All(item => manifest.Files?.Any(existing =>
                string.Equals(existing.RelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase)) == true);

        private static string GetManifestPath(string root) => Path.Combine(root, "TajsCOI", ManifestFileName);

        private static string SafeCombine(string root, string relativePath)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Manifest path escapes the game root.");
            }
            return fullPath;
        }

        private static bool TryNormalizeRoot(string? value, out string root, out string error)
        {
            root = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Game root is required.";
                return false;
            }
            try
            {
                root = Path.GetFullPath(value!.Trim()).TrimEnd(Path.DirectorySeparatorChar);
                if (!Directory.Exists(root))
                {
                    error = "Game root does not exist.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "Game root is invalid: " + exception.Message;
                return false;
            }
        }

        private static void CopyFileAtomically(string source, string destination)
        {
            string? directory = Path.GetDirectoryName(destination);
            if (directory is null)
            {
                throw new InvalidDataException("Bootstrap destination has no directory.");
            }
            Directory.CreateDirectory(directory);
            string temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, temp, false);
                if (File.Exists(destination))
                {
                    try
                    {
                        File.Replace(temp, destination, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(destination);
                        File.Move(temp, destination);
                    }
                }
                else
                {
                    File.Move(temp, destination);
                }
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        private static void WriteTextAtomically(string destination, string contents)
        {
            string? directory = Path.GetDirectoryName(destination);
            if (directory is null)
            {
                throw new InvalidDataException("Bootstrap configuration destination has no directory.");
            }
            Directory.CreateDirectory(directory);
            string temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, contents, new UTF8Encoding(false));
                if (File.Exists(destination))
                {
                    try
                    {
                        File.Replace(temp, destination, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(destination);
                        File.Move(temp, destination);
                    }
                }
                else
                {
                    File.Move(temp, destination);
                }
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        private static void WriteManifestAtomic(string path, InstallManifest manifest)
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is null)
            {
                throw new InvalidDataException("Manifest path has no directory.");
            }
            Directory.CreateDirectory(directory);
            var serializer = new DataContractJsonSerializer(typeof(InstallManifest));
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = File.Create(temp))
                {
                    serializer.WriteObject(stream, manifest);
                }
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        private static InstallManifest? TryReadManifest(string path, out string? error)
        {
            error = null;
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(InstallManifest));
                using (FileStream stream = File.OpenRead(path))
                {
                    var manifest = serializer.ReadObject(stream) as InstallManifest;
                    if (manifest is null || manifest.Schema != ManifestSchema || string.IsNullOrWhiteSpace(manifest.GameRoot))
                    {
                        error = "Unsupported or incomplete install manifest.";
                        return null;
                    }
                    return manifest;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
        }

        private static BootstrapInstallResult Failed(string manifestPath, string message) =>
            new(BootstrapInstallState.Failed, message, manifestPath);

        private static BootstrapInstallResult Refused(string manifestPath, string message) =>
            new(BootstrapInstallState.Refused, message, manifestPath);

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                return FormatSha256(sha256.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] contents)
        {
            using (var sha256 = SHA256.Create())
            {
                return FormatSha256(sha256.ComputeHash(contents));
            }
        }

        private static string FormatSha256(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte item in bytes)
            {
                builder.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        [DataContract]
        private sealed class InstallManifest
        {
            [DataMember(Order = 0)] public int Schema { get; set; }

            [DataMember(Order = 1)] public string GameRoot { get; set; } = string.Empty;

            [DataMember(Order = 2)] public bool Enabled { get; set; }

            [DataMember(Order = 3)] public BootstrapFileRecord[] Files { get; set; } = Array.Empty<BootstrapFileRecord>();
        }

        [DataContract]
        private sealed class BootstrapFileRecord
        {
            [DataMember(Order = 0)] public string RelativePath { get; set; } = string.Empty;

            [DataMember(Order = 1)] public string Sha256 { get; set; } = string.Empty;

            [DataMember(Order = 2)] public long Length { get; set; }
        }
    }
}
