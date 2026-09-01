// Taj's COI Mods | OverclockingStateStore.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Persistence;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    internal sealed class OverclockEntityPolicy
    {
        internal bool HasManualOverride;
        internal int ManualPercent = 100;
        internal bool HasAutoOverride;
        internal bool Auto;
        internal bool HasBoundsOverride;
        internal int MinPercent = 100;
        internal int MaxPercent = 300;
    }

    internal sealed class OverclockGroup
    {
        internal int Id;
        internal string Name = string.Empty;
        internal bool Locked;
        internal int ColorIndex;
        internal int HighlightAlpha = 85;
        internal int ManualDefault;
        internal bool Auto;
        internal int MinPercent = 100;
        internal int MaxPercent = 300;
        internal readonly HashSet<int> Members = new();
    }

    /// <summary>
    ///     Save-scoped policy metadata. It deliberately lives outside the vanilla save serializer;
    ///     native Machine.m_speedFactorBase remains the persisted machine speed value, while this
    ///     file holds transport speed plus Auto/group intent and therefore cannot alter vanilla
    ///     object/class ID allocation.
    /// </summary>
    internal sealed class OverclockingStateStore
    {
        private const string Header = "TajsTweaksOverclockingV2";
        private const string LegacyHeader = "TajsTweaksOverclockingV1";
        private readonly Dictionary<int, OverclockEntityPolicy> m_entities = new();
        private readonly List<OverclockGroup> m_groups = new();
        private readonly string m_rootDirectory;
        private readonly TajsSaveIdentityRegistry m_identityRegistry;
        private string? m_filePath;
        private string? m_identityKey;
        private string? m_revisionKey;
        private TajsSaveIdentity? m_identity;
        private bool m_parseValid;
        private bool m_allowWrite;
        private bool m_persistenceBlocked;
        private TajsSaveIdentityBindingStatus m_identityBindingStatus =
            TajsSaveIdentityBindingStatus.IdentityUnavailable;
        private int m_nextGroupId = 1;
        private int m_selectedGroupId = -1;

        internal IReadOnlyDictionary<int, OverclockEntityPolicy> Entities => m_entities;
        internal IReadOnlyList<OverclockGroup> Groups => m_groups;
        internal int SelectedGroupId => m_selectedGroupId;
        private string m_loadStatus = "overclocking sidecar identity unavailable.";
        internal string LoadStatus => m_loadStatus +
            (m_identityBindingStatus == TajsSaveIdentityBindingStatus.IdentityUsableForSessionBindingPersistenceFailed
                ? " Binding registry persistence failed; this session is not durable across restart."
                : m_identityBindingStatus == TajsSaveIdentityBindingStatus.IdentityAmbiguous
                    ? " Binding registry identity is ambiguous; sidecar writes are disabled."
                    : string.Empty);
        internal TajsSaveIdentityBindingStatus IdentityBindingStatus => m_identityBindingStatus;
        internal bool IdentityBindingPersisted =>
            m_identityBindingStatus == TajsSaveIdentityBindingStatus.IdentityResolvedAndBindingPersisted;

        internal OverclockingStateStore(string? rootDirectory = null, ITajsLogger? log = null)
        {
            m_rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Captain of Industry",
                    "TajsTweaks",
                    "Overclocking")
                : Path.GetFullPath(rootDirectory!);
            m_identityRegistry = new TajsSaveIdentityRegistry(
                m_rootDirectory,
                message => log?.WarningOnce(message));
        }

        internal void LoadForSave(TajsSaveIdentity? identity, string? saveName = null)
        {
            if (identity?.PhysicalPath is string physicalPath)
            {
                TajsSaveIdentityBindingResult binding = m_identityRegistry.ResolveDetailed(
                    physicalPath,
                    identity.GameName,
                    identity.DisplayName);
                identity = binding.Identity ?? identity;
                m_identityBindingStatus = binding.Status;
            }
            else
            {
                m_identityBindingStatus = TajsSaveIdentityBindingStatus.IdentityUnavailable;
            }

            m_entities.Clear();
            m_groups.Clear();
            m_nextGroupId = 1;
            m_selectedGroupId = -1;
            m_identityKey = identity?.OwnershipKey;
            m_revisionKey = identity?.RevisionKey;
            m_identity = identity;
            m_loadedIdentityMatches = false;
            m_parseValid = true;
            m_allowWrite = CanPersistIdentity(identity);
            m_persistenceBlocked = m_identityBindingStatus == TajsSaveIdentityBindingStatus.IdentityAmbiguous;
            m_filePath = !CanPersistIdentity(identity)
                ? null
                : Path.Combine(m_rootDirectory, identity!.OwnershipKey, "state.txt");
            m_loadStatus = identity is null
                ? "overclocking sidecar identity unavailable; policy is in memory only."
                : identity.IsStronglyVerified
                    ? "overclocking sidecar identity verified."
                    : "overclocking sidecar identity unavailable; metadata-only identity is not trusted for persistence.";

            if (m_filePath is null || !File.Exists(m_filePath))
            {
                if (saveName is not null && HasLegacySidecar(saveName))
                {
                    m_loadStatus = "overclocking sidecar unavailable: legacy name-based sidecar requires explicit recapture.";
                }
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(m_filePath))
                {
                    ParseLine(line);
                }
                if (!m_parseValid || !m_loadedIdentityMatches)
                {
                    ClearLoadedState();
                    m_allowWrite = false;
                    m_persistenceBlocked = true;
                    m_loadStatus = "overclocking sidecar unavailable: identity metadata is invalid or belongs to another save.";
                }
            }
            catch
            {
                // A malformed optional policy file must leave the game at native rates.
                m_entities.Clear();
                m_groups.Clear();
                m_nextGroupId = 1;
                m_selectedGroupId = -1;
                m_allowWrite = false;
                m_persistenceBlocked = true;
                m_loadStatus = "overclocking sidecar unavailable: sidecar is invalid.";
            }
        }

        internal void LoadForSave(string saveName) => LoadForSave(null, saveName);

        private bool m_loadedIdentityMatches;

        internal bool RebindAfterSave(string? savePath, string gameName)
        {
            if (string.IsNullOrWhiteSpace(savePath) || TajsSaveIdentity.IsAutosavePath(savePath))
            {
                return false;
            }

            TajsSaveIdentity? identity = TajsSaveIdentity.FromFile(savePath!, gameName);
            if (identity is null)
            {
                return false;
            }

            TajsSaveIdentityBindingResult binding = m_identityRegistry.RebindDetailed(
                savePath!,
                gameName,
                m_identity,
                identity.DisplayName);
            identity = binding.Identity ?? identity;
            m_identityBindingStatus = binding.Status;

            if (m_identity is null && m_identityKey is null)
            {
                // A new game has no prior save owner to protect. Bind its in-memory policy to
                // the first successful save instead of discarding edits made before that save.
                m_identityKey = identity.OwnershipKey;
                m_revisionKey = identity.RevisionKey;
                m_identity = identity;
                m_filePath = Path.Combine(m_rootDirectory, identity.OwnershipKey, "state.txt");
                m_allowWrite = CanPersistIdentity(identity);
                m_persistenceBlocked = m_identityBindingStatus == TajsSaveIdentityBindingStatus.IdentityAmbiguous;
                m_loadStatus = identity.IsStronglyVerified
                    ? "overclocking sidecar identity verified after first save."
                    : "overclocking sidecar identity remains unavailable after first save.";
                Save();
                return m_allowWrite;
            }

            if (string.Equals(m_identityKey, identity.OwnershipKey, StringComparison.Ordinal))
            {
                if (m_persistenceBlocked)
                {
                    return false;
                }

                m_revisionKey = identity.RevisionKey;
                m_identity = identity;
                m_allowWrite = CanPersistIdentity(identity);
                if (m_identityBindingStatus == TajsSaveIdentityBindingStatus.IdentityUsableForSessionBindingPersistenceFailed)
                {
                    m_loadStatus = "overclocking sidecar identity resolved for this session only.";
                }
                Save();
                return true;
            }

            string nextPath = Path.Combine(m_rootDirectory, identity.OwnershipKey, "state.txt");
            if (File.Exists(nextPath))
            {
                ClearLoadedState();
                m_filePath = nextPath;
                m_identityKey = identity.OwnershipKey;
                m_revisionKey = identity.RevisionKey;
                m_identity = identity;
                m_allowWrite = false;
                m_persistenceBlocked = true;
                m_loadStatus = "overclocking sidecar unavailable: save identity collision.";
                return false;
            }

            // Save-as/copy/replacement is a new ownership key. Do not carry policies keyed by
            // entity IDs into the unrelated file; leave the old sidecar untouched for recovery.
            ClearLoadedState();
            m_filePath = nextPath;
            m_identityKey = identity.OwnershipKey;
            m_revisionKey = identity.RevisionKey;
            m_identity = identity;
            m_allowWrite = CanPersistIdentity(identity);
            m_persistenceBlocked = m_identityBindingStatus == TajsSaveIdentityBindingStatus.IdentityAmbiguous;
            m_loadStatus = "overclocking sidecar reset: save identity changed.";
            return false;
        }

        internal OverclockEntityPolicy GetOrCreateEntity(int entityId)
        {
            if (!m_entities.TryGetValue(entityId, out OverclockEntityPolicy? policy))
            {
                policy = new OverclockEntityPolicy();
                m_entities[entityId] = policy;
            }

            return policy;
        }

        internal bool TryGetEntity(int entityId, out OverclockEntityPolicy? policy) => m_entities.TryGetValue(entityId, out policy);

        internal void RemoveEntity(int entityId) => m_entities.Remove(entityId);

        internal OverclockGroup? GetGroup(int groupId) => m_groups.FirstOrDefault(group => group.Id == groupId);

        internal OverclockGroup? GetGroupForEntity(int entityId) =>
            m_groups.FirstOrDefault(group => group.Members.Contains(entityId));

        internal OverclockGroup CreateGroup(string? requestedName)
        {
            string name = string.IsNullOrWhiteSpace(requestedName) ? "Group " + (m_groups.Count + 1) : CleanName(requestedName);
            var group = new OverclockGroup { Id = m_nextGroupId++, Name = name };
            m_groups.Add(group);
            m_selectedGroupId = group.Id;
            Save();
            return group;
        }

        internal bool DeleteGroup(int groupId)
        {
            int removed = m_groups.RemoveAll(group => group.Id == groupId);
            if (removed == 0)
            {
                return false;
            }

            if (m_selectedGroupId == groupId)
            {
                m_selectedGroupId = m_groups.Count == 0 ? -1 : m_groups[0].Id;
            }

            Save();
            return true;
        }

        internal bool RenameGroup(int groupId, string name)
        {
            OverclockGroup? group = GetGroup(groupId);
            if (group is null || group.Locked || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            group.Name = CleanName(name);
            Save();
            return true;
        }

        internal bool AddMember(int groupId, int entityId)
        {
            OverclockGroup? group = GetGroup(groupId);
            if (group is null || group.Locked)
            {
                return false;
            }

            foreach (OverclockGroup other in m_groups)
            {
                if (other.Id != groupId)
                {
                    other.Members.Remove(entityId);
                }
            }

            bool changed = group.Members.Add(entityId);
            if (changed)
            {
                Save();
            }

            return changed;
        }

        internal bool RemoveMember(int groupId, int entityId)
        {
            OverclockGroup? group = GetGroup(groupId);
            if (group is null || group.Locked || !group.Members.Remove(entityId))
            {
                return false;
            }

            Save();
            return true;
        }

        internal bool SetSelected(int groupId)
        {
            if (groupId != -1 && GetGroup(groupId) is null)
            {
                return false;
            }

            m_selectedGroupId = groupId;
            Save();
            return true;
        }

        internal void Prune(Func<int, bool> exists)
        {
            bool changed = false;
            foreach (int id in m_entities.Keys.Where(id => !exists(id)).ToArray())
            {
                m_entities.Remove(id);
                changed = true;
            }

            foreach (OverclockGroup group in m_groups)
            {
                int removed = group.Members.RemoveWhere(id => !exists(id));
                changed |= removed > 0;
            }

            if (changed)
            {
                Save();
            }
        }

        internal void Save()
        {
            if (m_filePath is null || !m_allowWrite || m_persistenceBlocked)
            {
                return;
            }

            string? temporaryPath = null;
            try
            {
                string? directory = Path.GetDirectoryName(m_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new List<string>
                {
                    Header,
                    "I\t" + (m_identityKey ?? string.Empty) + "\t" + (m_revisionKey ?? string.Empty),
                    "S\t" + m_selectedGroupId + "\t" + m_nextGroupId
                };
                foreach (KeyValuePair<int, OverclockEntityPolicy> pair in m_entities.OrderBy(pair => pair.Key))
                {
                    OverclockEntityPolicy policy = pair.Value;
                    lines.Add(
                        string.Join(
                            "\t",
                            "E",
                            pair.Key,
                            Bool(policy.HasManualOverride),
                            policy.ManualPercent,
                            Bool(policy.HasAutoOverride),
                            Bool(policy.Auto),
                            Bool(policy.HasBoundsOverride),
                            policy.MinPercent,
                            policy.MaxPercent));
                }

                foreach (OverclockGroup group in m_groups.OrderBy(group => group.Id))
                {
                    string members = string.Join(",", group.Members.OrderBy(id => id));
                    string name = Convert.ToBase64String(Encoding.UTF8.GetBytes(CleanName(group.Name)));
                    lines.Add(
                        string.Join(
                            "\t",
                            "G",
                            group.Id,
                            Bool(group.Locked),
                            group.ColorIndex,
                            group.HighlightAlpha,
                            group.ManualDefault,
                            Bool(group.Auto),
                            group.MinPercent,
                            group.MaxPercent,
                            name,
                            members));
                }

                temporaryPath = m_filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllLines(temporaryPath, lines);
                if (File.Exists(m_filePath))
                {
                    File.Replace(temporaryPath, m_filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, m_filePath);
                }
                temporaryPath = null;
            }
            catch
            {
                // Policy persistence is best-effort and never blocks gameplay.
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }
        }

        private void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            string[] fields = line.Split('\t');
            if (fields.Length == 0)
            {
                return;
            }

            if (fields[0] == Header || fields[0] == LegacyHeader)
            {
                return;
            }

            if (fields[0] == "I" && fields.Length >= 2)
            {
                m_loadedIdentityMatches = string.Equals(fields[1], m_identityKey, StringComparison.Ordinal);
                // The revision is informational only. Always retain the revision observed from
                // the current native file so a stale sidecar cannot make a later write claim an
                // obsolete file revision.
                return;
            }

            if (fields[0] == "S" && fields.Length >= 3 && int.TryParse(fields[1], out int selected) &&
                int.TryParse(fields[2], out int next))
            {
                m_selectedGroupId = selected;
                m_nextGroupId = Math.Max(1, next);
                return;
            }

            if (fields[0] == "E" && fields.Length == 9 && int.TryParse(fields[1], out int entityId) && entityId >= 0 &&
                TryParseBool(fields[2], out bool hasManualOverride) && int.TryParse(fields[3], out int manualPercent) &&
                TryParseBool(fields[4], out bool hasAutoOverride) && TryParseBool(fields[5], out bool auto) &&
                TryParseBool(fields[6], out bool hasBoundsOverride) && int.TryParse(fields[7], out int minPercent) &&
                int.TryParse(fields[8], out int maxPercent))
            {
                var policy = new OverclockEntityPolicy
                {
                    HasManualOverride = hasManualOverride,
                    ManualPercent = manualPercent,
                    HasAutoOverride = hasAutoOverride,
                    Auto = auto,
                    HasBoundsOverride = hasBoundsOverride,
                    MinPercent = minPercent,
                    MaxPercent = maxPercent,
                };
                m_entities[entityId] = policy;
                return;
            }

            if (fields[0] == "G" && fields.Length == 11 && int.TryParse(fields[1], out int groupId) && groupId > 0 &&
                TryParseBool(fields[2], out bool locked) && int.TryParse(fields[3], out int colorIndex) &&
                int.TryParse(fields[4], out int highlightAlpha) && int.TryParse(fields[5], out int manualDefault) &&
                TryParseBool(fields[6], out bool groupAuto) && int.TryParse(fields[7], out int groupMin) &&
                int.TryParse(fields[8], out int groupMax))
            {
                string name;
                try
                {
                    name = Encoding.UTF8.GetString(Convert.FromBase64String(fields[9]));
                }
                catch
                {
                    name = "Group " + groupId;
                }

                var group = new OverclockGroup
                {
                    Id = groupId,
                    Locked = locked,
                    ColorIndex = colorIndex,
                    HighlightAlpha = Math.Max(4, Math.Min(100, highlightAlpha)),
                    ManualDefault = manualDefault,
                    Auto = groupAuto,
                    MinPercent = groupMin,
                    MaxPercent = groupMax,
                    Name = CleanName(name),
                };
                foreach (string member in fields[10].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(member, out int id) && id >= 0)
                    {
                        group.Members.Add(id);
                    }
                }

                m_groups.RemoveAll(existing => existing.Id == groupId);
                m_groups.Add(group);
                m_nextGroupId = Math.Max(m_nextGroupId, groupId + 1);
                return;
            }

            m_parseValid = false;
        }

        private bool CanPersistIdentity(TajsSaveIdentity? identity) =>
            identity?.IsStronglyVerified == true &&
            m_identityBindingStatus != TajsSaveIdentityBindingStatus.IdentityAmbiguous;

        private static string Bool(bool value) => value ? "1" : "0";
        private static bool TryParseBool(string value, out bool result)
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static string CleanName(string? value) => (value ?? string.Empty).Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();

        private void ClearLoadedState()
        {
            m_entities.Clear();
            m_groups.Clear();
            m_nextGroupId = 1;
            m_selectedGroupId = -1;
        }

        private bool HasLegacySidecar(string saveName)
        {
            string safeName = Sanitize(saveName);
            if (safeName.Length == 0)
            {
                safeName = "current";
            }

            string path = Path.Combine(m_rootDirectory, safeName, "state.txt");
            return File.Exists(path);
        }

        private static string Sanitize(string value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }

            return result.Trim();
        }
    }
}
