// Taj's COI Mods | SaveRepairContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TajsCOI.Core.SaveRepair
{
    /// <summary>
    ///     The result of one detector pass. Detector output is intentionally immutable so report
    ///     and repair cannot accidentally acquire different classification rules.
    /// </summary>
    internal sealed class SaveRepairFinding
    {
        internal SaveRepairFinding(
            string handlerId,
            SaveRepairStatus status,
            int itemCount,
            string detail = "")
        {
            HandlerId = handlerId ?? throw new ArgumentNullException(nameof(handlerId));
            Status = status;
            ItemCount = itemCount;
            Detail = detail ?? string.Empty;
        }

        internal string HandlerId { get; }
        internal SaveRepairStatus Status { get; }
        internal int ItemCount { get; }
        internal string Detail { get; }
    }

    internal enum SaveRepairStatus
    {
        NotLoaded,
        Clean,
        NeedsRepair,
        Unsupported,
        Unavailable,
    }

    internal readonly struct SaveRepairMutation
    {
        internal SaveRepairMutation(bool succeeded, int changedCount, string failure = "")
        {
            Succeeded = succeeded;
            ChangedCount = changedCount;
            Failure = failure ?? string.Empty;
        }

        internal bool Succeeded { get; }
        internal int ChangedCount { get; }
        internal string Failure { get; }

        internal static SaveRepairMutation Failed(string failure) => new(false, 0, failure);
        internal static SaveRepairMutation SucceededWith(int changedCount) => new(true, changedCount);
    }

    /// <summary>
    ///     Formal ownership and compatibility metadata for one audited repair. The registry is
    ///     deliberately delegate-based: feature-specific private API knowledge stays in Core's
    ///     owning service while all callers share one detector and one verification path.
    /// </summary>
    internal sealed class SaveRepairHandler
    {
        internal SaveRepairHandler(
            string id,
            string owner,
            string targetKind,
            string versionShape,
            Func<SaveRepairFinding> detect,
            Func<SaveRepairMutation> repair,
            Func<SaveRepairFinding> verify)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            TargetKind = targetKind ?? throw new ArgumentNullException(nameof(targetKind));
            VersionShape = versionShape ?? throw new ArgumentNullException(nameof(versionShape));
            Detect = detect ?? throw new ArgumentNullException(nameof(detect));
            Repair = repair ?? throw new ArgumentNullException(nameof(repair));
            Verify = verify ?? throw new ArgumentNullException(nameof(verify));
        }

        internal string Id { get; }
        internal string Owner { get; }
        internal string TargetKind { get; }
        internal string VersionShape { get; }
        internal Func<SaveRepairFinding> Detect { get; }
        internal Func<SaveRepairMutation> Repair { get; }
        internal Func<SaveRepairFinding> Verify { get; }
    }

    /// <summary>
    ///     Deterministic handler registry. Duplicate IDs are rejected at construction time so a
    ///     command cannot silently select a different owner after a later feature is added.
    /// </summary>
    internal sealed class SaveRepairHandlerRegistry
    {
        private readonly IReadOnlyList<SaveRepairHandler> m_handlers;

        internal SaveRepairHandlerRegistry(IEnumerable<SaveRepairHandler> handlers)
        {
            if (handlers is null)
            {
                throw new ArgumentNullException(nameof(handlers));
            }

            SaveRepairHandler[] values = handlers.ToArray();
            if (values.Length == 0 || values.Any(handler => handler is null))
            {
                throw new ArgumentException("At least one non-null save-repair handler is required.", nameof(handlers));
            }

            if (values.Select(handler => handler.Id).Distinct(StringComparer.Ordinal).Count() != values.Length)
            {
                throw new ArgumentException("Save-repair handler IDs must be unique.", nameof(handlers));
            }

            m_handlers = values;
        }

        internal IReadOnlyList<SaveRepairHandler> Handlers => m_handlers;

        internal bool TryGet(string id, out SaveRepairHandler? handler)
        {
            handler = m_handlers.FirstOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal));
            return handler is not null;
        }
    }

    /// <summary>
    ///     A small, append-only report sidecar. It is separate from the save and is never allowed
    ///     to replace an existing file. This is useful even when native save completion is
    ///     asynchronous: it records the exact detector result and requested output slot.
    /// </summary>
    internal static class SaveRepairManifest
    {
        internal const string Header = "TajsCOISaveRepairManifestV1";

        internal static bool TryWriteNew(
            string path,
            string sourceSave,
            string outputSave,
            SaveRepairFinding finding,
            SaveRepairFinding verification,
            int changedCount,
            out string failure)
        {
            failure = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || finding is null || verification is null)
            {
                failure = "repair manifest arguments were incomplete";
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
                string? directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    failure = "repair manifest directory was unavailable";
                    return false;
                }
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception)
            {
                failure = "repair manifest path was invalid (" + exception.GetType().Name + ")";
                return false;
            }

            // CreateNew is intentional. A stale or corrupt report is evidence, not permission to
            // overwrite a user's file or to claim a different repair under the same name.
            try
            {
                using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.WriteLine(Header);
                    writer.WriteLine("source=" + Encode(sourceSave));
                    writer.WriteLine("output=" + Encode(outputSave));
                    writer.WriteLine("handler=" + Encode(finding.HandlerId));
                    writer.WriteLine("status-before=" + finding.Status);
                    writer.WriteLine("changed=" + changedCount);
                    writer.WriteLine("status-after=" + verification.Status);
                    writer.WriteLine("items-after=" + verification.ItemCount);
                    writer.WriteLine("detail=" + Encode(verification.Detail));
                    writer.Flush();
                    stream.Flush(true);
                }
                return true;
            }
            catch (Exception exception)
            {
                failure = "repair manifest could not be written (" + exception.GetType().Name + ")";
                return false;
            }
        }

        private static string Encode(string? value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }
}
