// Taj's COI Mods | TajsSaveIdentity.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TajsCOI.Common.Persistence
{
    /// <summary>
    ///     Identity for optional per-save sidecars. OwnershipKey describes the concrete file
    ///     lineage; TajsSaveIdentityRegistry preserves it across native replacement saves and
    ///     verified renames. RevisionKey describes the current file revision and is deliberately
    ///     not used as the sidecar directory.
    /// </summary>
    public sealed class TajsSaveIdentity
    {
        private TajsSaveIdentity(
            string ownershipKey,
            string revisionKey,
            string displayName,
            string gameName,
            string? path,
            string physicalKey,
            bool isVerified,
            bool isStronglyVerified)
        {
            OwnershipKey = ownershipKey;
            RevisionKey = revisionKey;
            DisplayName = displayName;
            GameName = gameName;
            PhysicalPath = path;
            PhysicalKey = physicalKey;
            IsVerified = isVerified;
            IsStronglyVerified = isStronglyVerified;
        }

        public string OwnershipKey { get; }

        public string RevisionKey { get; }

        public string DisplayName { get; }

        public string GameName { get; }

        public string? PhysicalPath { get; }

        public string PhysicalKey { get; }

        /// <summary>Whether the identity is safe to use for persistent sidecar ownership.</summary>
        public bool IsVerified { get; }

        /// <summary>Whether a stable OS file identity was observed rather than a weak fallback.</summary>
        public bool IsStronglyVerified { get; }

        public static TajsSaveIdentity FromMetadata(
            string? gameName,
            string? nameNoExtension,
            string? extension,
            DateTime writeTimestamp,
            long sizeBytes)
        {
            string game = Clean(gameName);
            string name = Clean(nameNoExtension);
            string ext = Clean(extension);
            string revision = Hash(string.Join("\n", "revision-v1", game, name, ext,
                writeTimestamp.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                sizeBytes.ToString(CultureInfo.InvariantCulture)));
            // SaveFileInfo does not carry an immutable identifier. Keep this metadata-only value
            // usable for diagnostics/tests, but mark it unverified so stores can fail closed.
            return new TajsSaveIdentity(revision, revision, name, game, null, string.Empty, false, false);
        }

        public static TajsSaveIdentity? FromFile(string path, string? gameName, string? displayName = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    return null;
                }

                string game = Clean(gameName);
                string name = Clean(displayName ?? Path.GetFileNameWithoutExtension(file.Name));
                string extension = Clean(file.Extension);
                string? fileId = TryGetStableFileId(file.FullName);
                string creationTicks = file.CreationTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                string physicalKey = fileId is null
                    ? string.Join(":", "creation", creationTicks)
                    : string.Join(":", "file", fileId, "creation", creationTicks);
                string lineageSeed = fileId is null
                    ? string.Join("\n", "lineage-weak-v1", game, extension,
                        creationTicks)
                    : string.Join("\n", "lineage-file-id-v2", game, extension, fileId, creationTicks);
                // Keep the display name as metadata only. A native rename changes the file
                // name but not the concrete revision represented by timestamp/size.
                string revisionSeed = string.Join("\n", "revision-v3", game, extension, physicalKey,
                    file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                    file.Length.ToString(CultureInfo.InvariantCulture));
                return new TajsSaveIdentity(
                    Hash(lineageSeed),
                    Hash(revisionSeed),
                    name,
                    game,
                    file.FullName,
                    physicalKey,
                    fileId is not null,
                    fileId is not null);
            }
            catch
            {
                return null;
            }
        }

        public static bool IsAutosavePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            return name.EndsWith("-autosave", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("-autosave-paused", StringComparison.OrdinalIgnoreCase);
        }

        internal TajsSaveIdentity WithOwnershipKey(string ownershipKey) => new(
            ownershipKey,
            RevisionKey,
            DisplayName,
            GameName,
            PhysicalPath,
            PhysicalKey,
            IsVerified,
            IsStronglyVerified);

        private static string Clean(string? value) => value?.Trim() ?? string.Empty;

        private static string Hash(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes)
                {
                    builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string? TryGetStableFileId(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    if (!GetFileInformationByHandle(stream.SafeFileHandle, out ByHandleFileInformation info))
                    {
                        return null;
                    }

                    ulong index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
                    return info.VolumeSerialNumber.ToString("x8", CultureInfo.InvariantCulture) + ":" +
                           index.ToString("x16", CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                return null;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(
            Microsoft.Win32.SafeHandles.SafeFileHandle handle,
            out ByHandleFileInformation fileInformation);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            internal uint FileAttributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }
    }

    /// <summary>
    ///     Reconciles mutable physical save revisions with a durable logical lineage. The
    ///     registry is optional metadata; failed reads/writes never block gameplay.
    /// </summary>
    public sealed class TajsSaveIdentityRegistry
    {
        private const string Header = "TajsSaveIdentityRegistryV2";
        private const string LegacyHeader = "TajsSaveIdentityRegistryV1";
        private readonly string m_path;

        public TajsSaveIdentityRegistry(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Identity registry root cannot be empty.", nameof(rootDirectory));
            }

            m_path = Path.Combine(Path.GetFullPath(rootDirectory), "_identity-bindings.tsv");
        }

        public TajsSaveIdentity? Resolve(string path, string? gameName, string? displayName = null)
        {
            TajsSaveIdentity? raw = TajsSaveIdentity.FromFile(path, gameName, displayName);
            if (raw is null)
            {
                return null;
            }

            List<Binding> bindings = Read();
            string canonicalPath = CanonicalPath(path);
            string game = (gameName ?? string.Empty).Trim();
            Binding? pathBinding = bindings.FirstOrDefault(binding =>
                string.Equals(binding.Path, canonicalPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(binding.GameName, game, StringComparison.Ordinal));
            Binding? physicalBinding = bindings.FirstOrDefault(binding =>
                raw.IsStronglyVerified && !string.IsNullOrWhiteSpace(binding.RevisionKey) &&
                string.Equals(binding.PhysicalKey, raw.PhysicalKey, StringComparison.Ordinal) &&
                string.Equals(binding.GameName, game, StringComparison.Ordinal) &&
                string.Equals(binding.RevisionKey, raw.RevisionKey, StringComparison.Ordinal));

            if (pathBinding is not null && string.Equals(pathBinding.PhysicalKey, raw.PhysicalKey, StringComparison.Ordinal))
            {
                pathBinding.RevisionKey = raw.RevisionKey;
                Write(bindings);
                return raw.WithOwnershipKey(pathBinding.OwnershipKey);
            }

            if (physicalBinding is not null)
            {
                string physicalOwnership = physicalBinding.OwnershipKey;
                bindings.RemoveAll(binding =>
                    string.Equals(binding.GameName, game, StringComparison.Ordinal) &&
                    string.Equals(binding.OwnershipKey, physicalOwnership, StringComparison.Ordinal));
                bindings.Add(new Binding
                {
                    Path = canonicalPath,
                    GameName = game,
                    PhysicalKey = raw.PhysicalKey,
                    RevisionKey = raw.RevisionKey,
                    OwnershipKey = physicalOwnership
                });
                Write(bindings);
                return raw.WithOwnershipKey(physicalOwnership);
            }

            string ownership = raw.OwnershipKey;
            if (pathBinding is not null)
            {
                bindings.Remove(pathBinding);
            }
            bindings.Add(new Binding
            {
                Path = canonicalPath,
                GameName = game,
                PhysicalKey = raw.PhysicalKey,
                RevisionKey = raw.RevisionKey,
                OwnershipKey = ownership
            });
            Write(bindings);
            return raw;
        }

        public TajsSaveIdentity? Rebind(string path, string? gameName, TajsSaveIdentity? previous, string? displayName = null)
        {
            TajsSaveIdentity? raw = TajsSaveIdentity.FromFile(path, gameName, displayName);
            if (raw is null)
            {
                return null;
            }

            if (previous is null || string.IsNullOrWhiteSpace(previous.OwnershipKey))
            {
                return Resolve(path, gameName, displayName);
            }

            string canonicalPath = CanonicalPath(path);
            string game = (gameName ?? string.Empty).Trim();
            bool sameGame = string.Equals(previous.GameName, game, StringComparison.Ordinal);
            bool samePath = sameGame && previous.PhysicalPath is string previousPath &&
                            string.Equals(CanonicalPath(previousPath), canonicalPath, StringComparison.OrdinalIgnoreCase);
            bool samePhysicalFile = sameGame && previous.IsStronglyVerified &&
                                    string.Equals(previous.PhysicalKey, raw.PhysicalKey, StringComparison.Ordinal) &&
                                    string.Equals(previous.RevisionKey, raw.RevisionKey, StringComparison.Ordinal);

            // A changed path plus a changed physical file is a copy/save-as (or a
            // delete/recreate), not a normal revision. Without an immutable native save ID we
            // must fail closed and let Resolve establish a new lineage rather than transferring
            // entity-ID keyed policy into an unrelated file. A rename that preserves the file
            // identity remains safe because samePhysicalFile is true.
            if (!samePath && !samePhysicalFile)
            {
                return Resolve(path, gameName, displayName);
            }

            List<Binding> bindings = Read();
            // Remove stale aliases for this ownership so rename/rebind does not accumulate
            // obsolete path entries. The sidecar directory remains stable and is never moved.
            bindings.RemoveAll(binding =>
                string.Equals(binding.GameName, game, StringComparison.Ordinal) &&
                string.Equals(binding.OwnershipKey, previous.OwnershipKey, StringComparison.Ordinal));
            bindings.RemoveAll(binding =>
                string.Equals(binding.Path, canonicalPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(binding.GameName, game, StringComparison.Ordinal));
            bindings.Add(new Binding
            {
                Path = canonicalPath,
                GameName = game,
                PhysicalKey = raw.PhysicalKey,
                RevisionKey = raw.RevisionKey,
                OwnershipKey = previous.OwnershipKey
            });
            Write(bindings);
            return raw.WithOwnershipKey(previous.OwnershipKey);
        }

        private List<Binding> Read()
        {
            var result = new List<Binding>();
            try
            {
                if (!File.Exists(m_path))
                {
                    return result;
                }

                string[] lines = File.ReadAllLines(m_path);
                if (lines.Length == 0 || (!string.Equals(lines[0], Header, StringComparison.Ordinal) &&
                                         !string.Equals(lines[0], LegacyHeader, StringComparison.Ordinal)))
                {
                    return result;
                }

                foreach (string line in lines.Skip(1))
                {
                    string[] fields = line.Split('\t');
                    if (fields.Length == 6 && fields[0] == "B")
                    {
                        result.Add(new Binding
                        {
                            Path = Decode(fields[1]),
                            GameName = Decode(fields[2]),
                            PhysicalKey = Decode(fields[3]),
                            RevisionKey = Decode(fields[4]),
                            OwnershipKey = Decode(fields[5])
                        });
                    }
                    else if (fields.Length == 5 && fields[0] == "B" && lines[0] == LegacyHeader)
                    {
                        // V1 bindings had no revision marker. They remain readable for
                        // diagnostics but are never used for changed-path physical matching.
                        result.Add(new Binding
                        {
                            Path = Decode(fields[1]),
                            GameName = Decode(fields[2]),
                            PhysicalKey = Decode(fields[3]),
                            OwnershipKey = Decode(fields[4])
                        });
                    }
                }
            }
            catch
            {
                result.Clear();
            }

            return result;
        }

        private void Write(List<Binding> bindings)
        {
            string? temporary = null;
            try
            {
                string? directory = Path.GetDirectoryName(m_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                temporary = m_path + ".tmp." + Guid.NewGuid().ToString("N");
                var lines = new List<string> { Header };
                lines.AddRange(bindings
                    .OrderBy(binding => binding.Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(binding => binding.GameName, StringComparer.Ordinal)
                    .Select(binding => string.Join("\t", "B", Encode(binding.Path), Encode(binding.GameName),
                        Encode(binding.PhysicalKey), Encode(binding.RevisionKey), Encode(binding.OwnershipKey))));
                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                if (File.Exists(m_path))
                {
                    File.Replace(temporary, m_path, m_path + ".bak", true);
                }
                else
                {
                    File.Move(temporary, m_path);
                }
                temporary = null;
            }
            catch
            {
            }
            finally
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); } catch { }
                }
            }
        }

        private static string CanonicalPath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        private static string Decode(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); } catch { return string.Empty; }
        }

        private sealed class Binding
        {
            internal string Path = string.Empty;
            internal string GameName = string.Empty;
            internal string PhysicalKey = string.Empty;
            internal string RevisionKey = string.Empty;
            internal string OwnershipKey = string.Empty;
        }
    }
}
