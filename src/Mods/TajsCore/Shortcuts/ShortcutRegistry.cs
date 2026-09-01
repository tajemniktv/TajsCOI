// Taj's COI Mods | ShortcutRegistry.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mafi;
using TajsCOI.Common.Shortcuts;

namespace TajsCOI.Core.Shortcuts
{
    /// <summary>
    ///     Process-lifetime shortcut metadata and effective binding resolver. The registry only
    ///     retains value contracts; scene-owned callbacks belong to <see cref="ShortcutInputService" />.
    ///     Accidental conflicts are rejected. Explicitly approved conflicts remain visible and
    ///     resolve to the ordinal-first action ID at dispatch time.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsEverything)]
    public sealed class ShortcutRegistry : IShortcutRegistry
    {
        private readonly object m_gate = new();
        private readonly Dictionary<string, ShortcutDescriptor> m_descriptors = new(StringComparer.Ordinal);

        private readonly Dictionary<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> m_bindings =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, List<string>> m_bindingIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ShortcutCombination> m_vanillaBindings = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_conflictApprovals = new(StringComparer.Ordinal);
        private bool m_vanillaCached;

        public ShortcutRegistrationResult Register(ShortcutDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            lock (m_gate)
            {
                if (!m_descriptors.TryGetValue(descriptor.ActionId, out ShortcutDescriptor? previous))
                {
                    string? conflict = FindConflict(descriptor.ActionId, descriptor.DefaultPrimary, descriptor.DefaultSecondary);
                    if (conflict is not null)
                    {
                        return new ShortcutRegistrationResult(
                            ShortcutRegistrationStatus.Rejected,
                            "Shortcut default conflicts with " + conflict + ".");
                    }

                    m_descriptors.Add(descriptor.ActionId, descriptor);
                    m_bindings[descriptor.ActionId] = (descriptor.DefaultPrimary, descriptor.DefaultSecondary);
                    RebuildBindingIndex();
                    return new ShortcutRegistrationResult(
                        ShortcutRegistrationStatus.Added,
                        "Shortcut registered: " + descriptor.ActionId);
                }

                if (!DescriptorsMatch(previous, descriptor))
                {
                    return new ShortcutRegistrationResult(
                        ShortcutRegistrationStatus.Rejected,
                        "Shortcut '" + descriptor.ActionId + "' was registered with incompatible metadata.");
                }

                return new ShortcutRegistrationResult(
                    ShortcutRegistrationStatus.AlreadyRegistered,
                    "Shortcut already registered: " + descriptor.ActionId);
            }
        }

        public ShortcutSetResult TrySetBinding(
            string actionId,
            ShortcutCombination primary,
            ShortcutCombination secondary)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return new ShortcutSetResult(ShortcutSetStatus.UnknownAction, "Shortcut action ID cannot be empty.");
            }

            string normalizedId = actionId.Trim();
            lock (m_gate)
            {
                if (!m_descriptors.ContainsKey(normalizedId))
                {
                    return new ShortcutSetResult(ShortcutSetStatus.UnknownAction, "Unknown shortcut: " + normalizedId);
                }

                if (!primary.IsEmpty && primary == secondary)
                {
                    secondary = ShortcutCombination.Empty;
                }

                List<ConflictRecord> conflicts = FindConflicts(normalizedId, primary, secondary, m_bindings);
                ConflictRecord? unapproved = conflicts.FirstOrDefault(conflict =>
                    !m_conflictApprovals.Contains(ApprovalKey(conflict.Combination, normalizedId, conflict.TargetId)));
                if (unapproved is not null)
                {
                    return new ShortcutSetResult(
                        ShortcutSetStatus.Conflict,
                        "Shortcut combination is already assigned to " + unapproved.TargetId +
                        "; explicitly accept this conflict before keeping it.",
                        unapproved.TargetId);
                }

                m_bindings[normalizedId] = (primary, secondary);
                PruneConflictApprovals();
                RebuildBindingIndex();
                return new ShortcutSetResult(
                    primary.IsEmpty && secondary.IsEmpty ? ShortcutSetStatus.Cleared : ShortcutSetStatus.Applied,
                    primary.IsEmpty && secondary.IsEmpty
                        ? "Shortcut cleared: " + normalizedId
                        : "Shortcut updated: " + normalizedId);
            }
        }

        public ShortcutSetResult TryResetBinding(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return new ShortcutSetResult(ShortcutSetStatus.UnknownAction, "Shortcut action ID cannot be empty.");
            }

            ShortcutDescriptor? descriptor;
            lock (m_gate)
            {
                if (!m_descriptors.TryGetValue(actionId.Trim(), out descriptor))
                {
                    return new ShortcutSetResult(ShortcutSetStatus.UnknownAction, "Unknown shortcut: " + actionId.Trim());
                }
            }

            // Route through the same conflict/approval path as a user rebind. Defaults that
            // collide with another active binding remain safe/rejecting unless explicitly
            // approved for this concrete combination.
            return TrySetBinding(descriptor.ActionId, descriptor.DefaultPrimary, descriptor.DefaultSecondary);
        }

        public ShortcutSetResult TryAcceptConflict(
            string actionId,
            ShortcutCombination combination,
            string conflictingActionId)
        {
            if (string.IsNullOrWhiteSpace(actionId) || string.IsNullOrWhiteSpace(conflictingActionId))
            {
                return new ShortcutSetResult(ShortcutSetStatus.UnknownAction, "Shortcut action IDs cannot be empty.");
            }

            string normalizedId = actionId.Trim();
            string targetId = conflictingActionId.Trim();
            if (combination.IsEmpty)
            {
                return new ShortcutSetResult(ShortcutSetStatus.Rejected, "An empty combination cannot conflict.");
            }

            lock (m_gate)
            {
                if (!m_descriptors.ContainsKey(normalizedId))
                {
                    return new ShortcutSetResult(ShortcutSetStatus.UnknownAction, "Unknown shortcut: " + normalizedId);
                }

                if (!IsActiveConflict(normalizedId, combination, targetId, m_bindings))
                {
                    return new ShortcutSetResult(
                        ShortcutSetStatus.Rejected,
                        "The requested shortcut conflict is not active.",
                        targetId);
                }

                m_conflictApprovals.Add(ApprovalKey(combination, normalizedId, targetId));
                return new ShortcutSetResult(
                    ShortcutSetStatus.Applied,
                    "Accepted conflict between " + normalizedId + " and " + targetId + ".",
                    targetId);
            }
        }

        public IReadOnlyList<ShortcutConflictSnapshot> GetConflictSnapshot()
        {
            lock (m_gate)
            {
                var combinations = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in m_bindings)
                {
                    AddConflictParticipant(combinations, item.Value.Primary, item.Key);
                    AddConflictParticipant(combinations, item.Value.Secondary, item.Key);
                }

                foreach (KeyValuePair<string, ShortcutCombination> item in m_vanillaBindings)
                {
                    AddConflictParticipant(combinations, item.Value, "vanilla:" + item.Key);
                }

                return combinations
                    .Where(item => item.Value.Count > 1)
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item =>
                    {
                        string[] participants = item.Value.OrderBy(value => value, StringComparer.Ordinal).ToArray();
                        string[] actions = participants.Where(value => !value.StartsWith("vanilla:", StringComparison.Ordinal)).ToArray();
                        string[] vanilla = participants.Where(value => value.StartsWith("vanilla:", StringComparison.Ordinal)).ToArray();
                        ShortcutCombination combination = new ShortcutCombination(item.Key);
                        bool accepted = actions
                            .SelectMany((left, index) => actions.Skip(index + 1).Select(right => ApprovalKey(combination, left, right)))
                            .Concat(actions.SelectMany(action => vanilla.Select(vanillaAction => ApprovalKey(combination, action, vanillaAction))))
                            .All(m_conflictApprovals.Contains);
                        return new ShortcutConflictSnapshot(item.Key, actions, vanilla, accepted);
                    })
                    .ToArray();
            }
        }

        public bool TryGet(string actionId, out ShortcutBindingSnapshot snapshot)
        {
            lock (m_gate)
            {
                if (!m_descriptors.TryGetValue(actionId?.Trim() ?? string.Empty, out ShortcutDescriptor? descriptor) ||
                    !m_bindings.TryGetValue(descriptor.ActionId, out (ShortcutCombination Primary, ShortcutCombination Secondary) binding))
                {
                    snapshot = null!;
                    return false;
                }

                snapshot = new ShortcutBindingSnapshot(
                    descriptor,
                    binding.Primary,
                    binding.Secondary,
                    binding.Primary == descriptor.DefaultPrimary && binding.Secondary == descriptor.DefaultSecondary,
                    isConflict: FindConflicts(descriptor.ActionId, binding.Primary, binding.Secondary, m_bindings).Count != 0);
                return true;
            }
        }

        public bool TryResolveBinding(ShortcutCombination combination, out ShortcutBindingSnapshot snapshot)
        {
            lock (m_gate)
            {
                if (!combination.IsEmpty && m_bindingIndex.TryGetValue(combination.Serialized, out List<string>? actionIds) &&
                    actionIds.Count > 0 &&
                    IsDispatchable(combination, actionIds) &&
                    m_descriptors.TryGetValue(actionIds[0], out ShortcutDescriptor? descriptor) &&
                    m_bindings.TryGetValue(actionIds[0], out (ShortcutCombination Primary, ShortcutCombination Secondary) binding))
                {
                    snapshot = new ShortcutBindingSnapshot(
                        descriptor,
                        binding.Primary,
                        binding.Secondary,
                        binding.Primary == descriptor.DefaultPrimary && binding.Secondary == descriptor.DefaultSecondary,
                        isConflict: actionIds.Count > 1 || HasVanillaConflict(combination));
                    return true;
                }

                snapshot = null!;
                return false;
            }
        }

        public IReadOnlyList<ShortcutBindingSnapshot> GetSnapshot()
        {
            lock (m_gate)
            {
                return m_descriptors.Values
                    .OrderBy(descriptor => descriptor.ActionId, StringComparer.Ordinal)
                    .Select(descriptor =>
                    {
                        (ShortcutCombination Primary, ShortcutCombination Secondary) binding = m_bindings[descriptor.ActionId];
                        return new ShortcutBindingSnapshot(
                            descriptor,
                            binding.Primary,
                            binding.Secondary,
                            binding.Primary == descriptor.DefaultPrimary && binding.Secondary == descriptor.DefaultSecondary,
                            isConflict: FindConflicts(descriptor.ActionId, binding.Primary, binding.Secondary, m_bindings).Count != 0);
                    })
                    .ToArray();
            }
        }

        /// <summary>
        ///     Caches vanilla bindings once. Later calls are intentionally ignored so a transient
        ///     menu/scene snapshot cannot rewrite conflict diagnostics.
        /// </summary>
        public void CacheVanillaBindings(IEnumerable<KeyValuePair<string, ShortcutCombination>> bindings)
        {
            if (bindings is null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            lock (m_gate)
            {
                if (m_vanillaCached)
                {
                    return;
                }

                m_vanillaCached = true;

                foreach (KeyValuePair<string, ShortcutCombination> item in bindings)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key) && !item.Value.IsEmpty)
                    {
                        m_vanillaBindings[item.Key.Trim()] = item.Value;
                    }
                }
                // Persistence can load before the game exposes its native shortcut table. Keep
                // deferred approvals until this first cache pass can prove which vanilla entries
                // still exist, then discard obsolete approvals.
                PruneConflictApprovals();
            }
        }

        public IReadOnlyDictionary<string, ShortcutCombination> GetVanillaBindingsSnapshot()
        {
            lock (m_gate)
            {
                return new Dictionary<string, ShortcutCombination>(m_vanillaBindings, StringComparer.Ordinal);
            }
        }

        public bool TryLoad(string path, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Shortcut binding path cannot be empty.";
                    return false;
                }
                if (!File.Exists(path))
                {
                    error = "Shortcut binding file was not found.";
                    return false;
                }

                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 ||
                    (!string.Equals(lines[0], "TajsCOIShortcutBindingsV1", StringComparison.Ordinal) &&
                     !string.Equals(lines[0], "TajsCOIShortcutBindingsV2", StringComparison.Ordinal)))
                {
                    error = "Unsupported shortcut binding schema.";
                    return false;
                }

                bool version2 = string.Equals(lines[0], "TajsCOIShortcutBindingsV2", StringComparison.Ordinal);
                var candidateLines = new List<BindingRecord>();
                var approvalLines = new List<ApprovalRecord>();
                for (int index = 1; index < lines.Length; index++)
                {
                    if (string.IsNullOrWhiteSpace(lines[index]))
                    {
                        continue;
                    }

                    string[] fields = lines[index].Split('\t');
                    if (version2 && fields.Length == 4 && string.Equals(fields[0], "C", StringComparison.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(fields[1]) || string.IsNullOrWhiteSpace(fields[3]) ||
                            !ShortcutCombination.TryParse(fields[2], out ShortcutCombination approvalCombination) ||
                            approvalCombination.IsEmpty)
                        {
                            error = "Malformed shortcut conflict approval at line " + (index + 1) + ".";
                            return false;
                        }

                        approvalLines.Add(new ApprovalRecord(fields[1].Trim(), approvalCombination, fields[3].Trim(), index + 1));
                        continue;
                    }

                    if (version2 && (fields.Length != 4 || !string.Equals(fields[0], "B", StringComparison.Ordinal)))
                    {
                        error = "Malformed shortcut record at line " + (index + 1) + ".";
                        return false;
                    }

                    int offset = version2 ? 1 : 0;
                    if (fields.Length - offset != 3 || string.IsNullOrWhiteSpace(fields[offset]) ||
                        !ShortcutCombination.TryParse(fields[offset + 1], out ShortcutCombination primary) ||
                        !ShortcutCombination.TryParse(fields[offset + 2], out ShortcutCombination secondary))
                    {
                        error = "Malformed shortcut binding at line " + (index + 1) + ".";
                        return false;
                    }

                    candidateLines.Add(new BindingRecord(fields[offset].Trim(), primary, secondary, index + 1));
                }

                lock (m_gate)
                {
                    var candidateBindings = new Dictionary<string, (ShortcutCombination Primary, ShortcutCombination Secondary)>(m_bindings, StringComparer.Ordinal);
                    // A file is a complete candidate layout. Approvals not present in it must not
                    // leak in from the currently active state.
                    var candidateApprovals = new HashSet<string>(StringComparer.Ordinal);
                    var seenBindings = new HashSet<string>(StringComparer.Ordinal);

                    foreach (BindingRecord record in candidateLines)
                    {
                        if (!m_descriptors.ContainsKey(record.ActionId))
                        {
                            error = "Unknown shortcut action at line " + record.Line + ": " + record.ActionId + ".";
                            return false;
                        }
                        if (!seenBindings.Add(record.ActionId))
                        {
                            error = "Duplicate shortcut binding at line " + record.Line + ": " + record.ActionId + ".";
                            return false;
                        }

                        if (!record.Primary.IsEmpty && record.Primary == record.Secondary)
                        {
                            candidateBindings[record.ActionId] = (record.Primary, ShortcutCombination.Empty);
                        }
                        else
                        {
                            candidateBindings[record.ActionId] = (record.Primary, record.Secondary);
                        }
                    }

                    foreach (ApprovalRecord record in approvalLines)
                    {
                        if (!m_descriptors.ContainsKey(record.ActionId) ||
                            (!record.TargetId.StartsWith("vanilla:", StringComparison.Ordinal) && !m_descriptors.ContainsKey(record.TargetId)))
                        {
                            error = "Unknown shortcut conflict action at line " + record.Line + ".";
                            return false;
                        }
                        bool deferredVanillaApproval = record.TargetId.StartsWith("vanilla:", StringComparison.Ordinal) &&
                                                       !m_vanillaBindings.ContainsKey(record.TargetId.Substring("vanilla:".Length)) &&
                                                       IsBoundToCombination(record.ActionId, record.Combination, candidateBindings);
                        if (!IsActiveConflict(record.ActionId, record.Combination, record.TargetId, candidateBindings) &&
                            !deferredVanillaApproval)
                        {
                            error = "Inactive shortcut conflict approval at line " + record.Line + ".";
                            return false;
                        }
                        candidateApprovals.Add(ApprovalKey(record.Combination, record.ActionId, record.TargetId));
                    }

                    List<string> validationErrors = ValidateCandidate(candidateBindings, candidateApprovals);
                    if (validationErrors.Count != 0)
                    {
                        error = "Shortcut layout rejected: " + string.Join(" | ", validationErrors.Take(4));
                        return false;
                    }

                    m_bindings.Clear();
                    foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in candidateBindings)
                    {
                        m_bindings.Add(item.Key, item.Value);
                    }
                    m_conflictApprovals.Clear();
                    m_conflictApprovals.UnionWith(candidateApprovals);
                    PruneConflictApprovals();
                    RebuildBindingIndex();
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public bool TrySave(string path, out string error)
        {
            error = string.Empty;
            string tempPath = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    error = "Shortcut binding path cannot be empty.";
                    return false;
                }

                string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var builder = new System.Text.StringBuilder("TajsCOIShortcutBindingsV2\n");
                foreach (ShortcutBindingSnapshot snapshot in GetSnapshot())
                {
                    builder.Append("B\t").Append(snapshot.Descriptor.ActionId).Append('\t')
                        .Append(snapshot.Primary.Serialized).Append('\t')
                        .Append(snapshot.Secondary.Serialized).AppendLine();
                }

                lock (m_gate)
                {
                    foreach (string approval in m_conflictApprovals.OrderBy(value => value, StringComparer.Ordinal))
                    {
                        if (TryParseApprovalKey(approval, out string combination, out string left, out string right))
                        {
                            // Keep the action in the first field even when canonical ordering puts
                            // a vanilla target before it lexically. The approval key remains
                            // symmetric internally, so load order cannot change its identity.
                            string actionId = left.StartsWith("vanilla:", StringComparison.Ordinal) ? right : left;
                            string targetId = left.StartsWith("vanilla:", StringComparison.Ordinal) ? left : right;
                            builder.Append("C\t").Append(actionId).Append('\t').Append(combination).Append('\t').Append(targetId).AppendLine();
                        }
                    }
                }

                tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempPath, builder.ToString(), new System.Text.UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, path + ".bak", true);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private string? FindConflict(string actionId, ShortcutCombination primary, ShortcutCombination secondary)
        {
            return FindConflicts(actionId, primary, secondary, m_bindings)
                .Select(conflict => conflict.TargetId)
                .FirstOrDefault();
        }

        private void RebuildBindingIndex()
        {
            m_bindingIndex.Clear();
            foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in m_bindings.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                AddBindingIndex(item.Value.Primary, item.Key);
                AddBindingIndex(item.Value.Secondary, item.Key);
            }
        }

        private void AddBindingIndex(ShortcutCombination combination, string actionId)
        {
            if (combination.IsEmpty)
            {
                return;
            }

            if (!m_bindingIndex.TryGetValue(combination.Serialized, out List<string>? actionIds))
            {
                actionIds = new List<string>();
                m_bindingIndex.Add(combination.Serialized, actionIds);
            }
            if (!actionIds.Contains(actionId, StringComparer.Ordinal))
            {
                actionIds.Add(actionId);
            }
        }

        private bool IsDispatchable(ShortcutCombination combination, IReadOnlyList<string> actionIds)
        {
            if (actionIds.Count > 1)
            {
                for (int leftIndex = 0; leftIndex < actionIds.Count; leftIndex++)
                {
                    for (int rightIndex = leftIndex + 1; rightIndex < actionIds.Count; rightIndex++)
                    {
                        if (!m_conflictApprovals.Contains(
                                ApprovalKey(combination, actionIds[leftIndex], actionIds[rightIndex])))
                        {
                            return false;
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, ShortcutCombination> vanilla in m_vanillaBindings)
            {
                if (vanilla.Value != combination)
                {
                    continue;
                }

                foreach (string actionId in actionIds)
                {
                    if (!m_conflictApprovals.Contains(
                            ApprovalKey(combination, actionId, "vanilla:" + vanilla.Key)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool HasVanillaConflict(ShortcutCombination combination)
        {
            foreach (ShortcutCombination vanilla in m_vanillaBindings.Values)
            {
                if (vanilla == combination)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddConflictParticipant(
            IDictionary<string, HashSet<string>> combinations,
            ShortcutCombination combination,
            string participant)
        {
            if (combination.IsEmpty)
            {
                return;
            }

            if (!combinations.TryGetValue(combination.Serialized, out HashSet<string>? participants))
            {
                participants = new HashSet<string>(StringComparer.Ordinal);
                combinations.Add(combination.Serialized, participants);
            }
            participants.Add(participant);
        }

        private List<ConflictRecord> FindConflicts(
            string actionId,
            ShortcutCombination primary,
            ShortcutCombination secondary,
            IReadOnlyDictionary<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> bindings)
        {
            var result = new List<ConflictRecord>();
            ShortcutCombination[] requested = new[] { primary, secondary }
                .Where(value => !value.IsEmpty)
                .Distinct()
                .ToArray();
            foreach (ShortcutCombination combination in requested)
            {
                foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in bindings.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (item.Key == actionId || (item.Value.Primary != combination && item.Value.Secondary != combination))
                    {
                        continue;
                    }
                    result.Add(new ConflictRecord(combination, item.Key));
                }

                foreach (KeyValuePair<string, ShortcutCombination> item in m_vanillaBindings.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (item.Value == combination)
                    {
                        result.Add(new ConflictRecord(combination, "vanilla:" + item.Key));
                    }
                }
            }

            return result
                .GroupBy(conflict => conflict.Combination.Serialized + "\u0000" + conflict.TargetId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(conflict => conflict.Combination.Serialized, StringComparer.Ordinal)
                .ThenBy(conflict => conflict.TargetId, StringComparer.Ordinal)
                .ToList();
        }

        private bool IsActiveConflict(
            string actionId,
            ShortcutCombination combination,
            string targetId,
            IReadOnlyDictionary<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> bindings)
        {
            if (!IsBoundToCombination(actionId, combination, bindings))
            {
                return false;
            }

            if (targetId.StartsWith("vanilla:", StringComparison.Ordinal))
            {
                string vanillaId = targetId.Substring("vanilla:".Length);
                return m_vanillaBindings.TryGetValue(vanillaId, out ShortcutCombination vanilla) && vanilla == combination;
            }

            return bindings.TryGetValue(targetId, out (ShortcutCombination Primary, ShortcutCombination Secondary) binding) &&
                   targetId != actionId && (binding.Primary == combination || binding.Secondary == combination);
        }

        private static bool IsBoundToCombination(
            string actionId,
            ShortcutCombination combination,
            IReadOnlyDictionary<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> bindings)
        {
            return bindings.TryGetValue(actionId, out (ShortcutCombination Primary, ShortcutCombination Secondary) binding) &&
                   (binding.Primary == combination || binding.Secondary == combination);
        }

        private List<string> ValidateCandidate(
            IReadOnlyDictionary<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> bindings,
            ISet<string> approvals)
        {
            var errors = new List<string>();
            foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in bindings.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                foreach (ConflictRecord conflict in FindConflicts(item.Key, item.Value.Primary, item.Value.Secondary, bindings))
                {
                    if (!approvals.Contains(ApprovalKey(conflict.Combination, item.Key, conflict.TargetId)))
                    {
                        errors.Add(item.Key + " conflicts with " + conflict.TargetId + " on " + conflict.Combination.Serialized);
                    }
                }
            }

            return errors
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
        }

        private void PruneConflictApprovals()
        {
            if (m_conflictApprovals.Count == 0)
            {
                return;
            }

            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in m_bindings)
            {
                foreach (ConflictRecord conflict in FindConflicts(item.Key, item.Value.Primary, item.Value.Secondary, m_bindings))
                {
                    string key = ApprovalKey(conflict.Combination, item.Key, conflict.TargetId);
                    if (m_conflictApprovals.Contains(key))
                    {
                        active.Add(key);
                    }
                }
            }

            m_conflictApprovals.RemoveWhere(value =>
            {
                if (active.Contains(value))
                {
                    return false;
                }

                // Preserve a vanilla approval whose native table has not been cached yet. The
                // first CacheVanillaBindings call resolves it and prunes it if the target is gone.
                return !TryParseApprovalKey(value, out string serializedCombination, out string left, out string right) ||
                       !IsDeferredVanillaApprovalStillRelevant(serializedCombination, left, right);
            });
        }

        private bool IsDeferredVanillaTarget(string participant) =>
            participant.StartsWith("vanilla:", StringComparison.Ordinal) &&
            !m_vanillaBindings.ContainsKey(participant.Substring("vanilla:".Length));

        private bool IsDeferredVanillaApprovalStillRelevant(
            string serializedCombination,
            string left,
            string right)
        {
            string actionId;
            if (left.StartsWith("vanilla:", StringComparison.Ordinal))
            {
                if (right.StartsWith("vanilla:", StringComparison.Ordinal))
                {
                    return false;
                }
                actionId = right;
            }
            else if (right.StartsWith("vanilla:", StringComparison.Ordinal))
            {
                actionId = left;
            }
            else
            {
                return false;
            }

            if ((!IsDeferredVanillaTarget(left) && !IsDeferredVanillaTarget(right)) ||
                !ShortcutCombination.TryParse(serializedCombination, out ShortcutCombination combination) ||
                combination.IsEmpty)
            {
                return false;
            }

            return IsBoundToCombination(actionId, combination, m_bindings);
        }

        private static string ApprovalKey(ShortcutCombination combination, string left, string right)
        {
            string first = string.CompareOrdinal(left, right) <= 0 ? left : right;
            string second = string.CompareOrdinal(left, right) <= 0 ? right : left;
            return combination.Serialized + "\u0000" + first + "\u0000" + second;
        }

        private static bool TryParseApprovalKey(string key, out string combination, out string left, out string right)
        {
            string[] fields = key.Split(new[] { '\u0000' }, StringSplitOptions.None);
            if (fields.Length == 3 && !string.IsNullOrWhiteSpace(fields[0]) &&
                !string.IsNullOrWhiteSpace(fields[1]) && !string.IsNullOrWhiteSpace(fields[2]))
            {
                combination = fields[0];
                left = fields[1];
                right = fields[2];
                return true;
            }

            combination = string.Empty;
            left = string.Empty;
            right = string.Empty;
            return false;
        }

        private sealed class ConflictRecord
        {
            internal ConflictRecord(ShortcutCombination combination, string targetId)
            {
                Combination = combination;
                TargetId = targetId;
            }

            internal ShortcutCombination Combination { get; }
            internal string TargetId { get; }
        }

        private sealed class BindingRecord
        {
            internal BindingRecord(string actionId, ShortcutCombination primary, ShortcutCombination secondary, int line)
            {
                ActionId = actionId;
                Primary = primary;
                Secondary = secondary;
                Line = line;
            }

            internal string ActionId { get; }
            internal ShortcutCombination Primary { get; }
            internal ShortcutCombination Secondary { get; }
            internal int Line { get; }
        }

        private sealed class ApprovalRecord
        {
            internal ApprovalRecord(string actionId, ShortcutCombination combination, string targetId, int line)
            {
                ActionId = actionId;
                Combination = combination;
                TargetId = targetId;
                Line = line;
            }

            internal string ActionId { get; }
            internal ShortcutCombination Combination { get; }
            internal string TargetId { get; }
            internal int Line { get; }
        }

        private static bool DescriptorsMatch(ShortcutDescriptor left, ShortcutDescriptor right) =>
            string.Equals(left.ActionId, right.ActionId, StringComparison.Ordinal) &&
            string.Equals(left.Label, right.Label, StringComparison.Ordinal) &&
            string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
            left.DefaultPrimary == right.DefaultPrimary &&
            left.DefaultSecondary == right.DefaultSecondary &&
            left.Context == right.Context &&
            string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
            left.BindingType == right.BindingType;
    }

    [GlobalDependency(RegistrationMode.AsEverything)]
    public sealed class ShortcutInputService : IShortcutInputService
    {
        private readonly IShortcutRegistry m_registry;
        private readonly object m_gate = new();
        private readonly Dictionary<string, Action> m_handlers = new(StringComparer.Ordinal);

        public ShortcutInputService(IShortcutRegistry registry)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IDisposable RegisterHandler(string actionId, Action handler)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("Shortcut action ID cannot be empty.", nameof(actionId));
            }
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            string normalizedId = actionId.Trim();
            lock (m_gate)
            {
                m_handlers[normalizedId] = handler;
            }

            return new Registration(this, normalizedId, handler);
        }

        public ShortcutDispatchResult TryDispatch(ShortcutCombination combination, IShortcutDispatchGate gate)
        {
            if (combination.IsEmpty)
            {
                return new ShortcutDispatchResult(false, string.Empty, "empty combination");
            }
            if (gate is null)
            {
                throw new ArgumentNullException(nameof(gate));
            }
            if (gate.HasTextFieldFocus || gate.ModalCapturesInput || gate.ToolOwnsInput || gate.UiCapturesInput)
            {
                return new ShortcutDispatchResult(false, string.Empty, "input is captured");
            }

            if (!m_registry.TryResolveBinding(combination, out ShortcutBindingSnapshot match) ||
                !gate.IsContextActive(match.Descriptor.Context))
            {
                return new ShortcutDispatchResult(false, string.Empty, "no active binding");
            }

            Action? handler;
            lock (m_gate)
            {
                m_handlers.TryGetValue(match.Descriptor.ActionId, out handler);
            }
            if (handler is null)
            {
                return new ShortcutDispatchResult(false, match.Descriptor.ActionId, "no handler registered");
            }

            handler();
            return new ShortcutDispatchResult(
                true,
                match.Descriptor.ActionId,
                match.IsConflict ? "dispatched (accepted conflict; deterministic resolution)" : "dispatched");
        }

        /// <summary>
        ///     Applies a combination captured by a modal keybind dialog. The existing binding is
        ///     retained for the other slot, and normal conflict checks still apply.
        /// </summary>
        public ShortcutSetResult CaptureBinding(string actionId, ShortcutCombination combination, bool secondary)
        {
            if (!m_registry.TryGet(actionId, out ShortcutBindingSnapshot snapshot))
            {
                return new ShortcutSetResult(ShortcutSetStatus.UnknownAction, "Unknown shortcut: " + actionId);
            }

            return m_registry.TrySetBinding(
                actionId,
                secondary ? snapshot.Primary : combination,
                secondary ? combination : snapshot.Secondary);
        }

        private void Remove(string actionId, Action handler)
        {
            lock (m_gate)
            {
                if (m_handlers.TryGetValue(actionId, out Action? current) && ReferenceEquals(current, handler))
                {
                    m_handlers.Remove(actionId);
                }
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly ShortcutInputService m_owner;
            private readonly string m_actionId;
            private readonly Action m_handler;
            private bool m_disposed;

            internal Registration(ShortcutInputService owner, string actionId, Action handler)
            {
                m_owner = owner;
                m_actionId = actionId;
                m_handler = handler;
            }

            public void Dispose()
            {
                if (m_disposed)
                {
                    return;
                }

                m_disposed = true;
                m_owner.Remove(m_actionId, m_handler);
            }
        }
    }
}
