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
    /// Process-lifetime shortcut metadata and effective binding resolver. The registry only
    /// retains value contracts; scene-owned callbacks belong to <see cref="ShortcutInputService"/>.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsEverything)]
    public sealed class ShortcutRegistry : IShortcutRegistry
    {
        private readonly object m_gate = new();
        private readonly Dictionary<string, ShortcutDescriptor> m_descriptors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> m_bindings =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> m_bindingIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ShortcutCombination> m_vanillaBindings = new(StringComparer.Ordinal);
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

                string? conflict = FindConflict(normalizedId, primary, secondary);
                if (conflict is not null)
                {
                    return new ShortcutSetResult(
                        ShortcutSetStatus.Conflict,
                        "Shortcut combination is already assigned to " + conflict + ".",
                        conflict);
                }

                m_bindings[normalizedId] = (primary, secondary);
                RebuildBindingIndex();
                return new ShortcutSetResult(
                    primary.IsEmpty && secondary.IsEmpty ? ShortcutSetStatus.Cleared : ShortcutSetStatus.Applied,
                    primary.IsEmpty && secondary.IsEmpty
                        ? "Shortcut cleared: " + normalizedId
                        : "Shortcut updated: " + normalizedId);
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
                    binding.Primary == descriptor.DefaultPrimary && binding.Secondary == descriptor.DefaultSecondary);
                return true;
            }
        }

        public bool TryResolveBinding(ShortcutCombination combination, out ShortcutBindingSnapshot snapshot)
        {
            lock (m_gate)
            {
                if (!combination.IsEmpty && m_bindingIndex.TryGetValue(combination.Serialized, out string? actionId) &&
                    m_descriptors.TryGetValue(actionId, out ShortcutDescriptor? descriptor) &&
                    m_bindings.TryGetValue(actionId, out (ShortcutCombination Primary, ShortcutCombination Secondary) binding))
                {
                    snapshot = new ShortcutBindingSnapshot(
                        descriptor,
                        binding.Primary,
                        binding.Secondary,
                        binding.Primary == descriptor.DefaultPrimary && binding.Secondary == descriptor.DefaultSecondary);
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
                            binding.Primary == descriptor.DefaultPrimary && binding.Secondary == descriptor.DefaultSecondary);
                    })
                    .ToArray();
            }
        }

        /// <summary>
        /// Caches vanilla bindings once. Later calls are intentionally ignored so a transient
        /// menu/scene snapshot cannot rewrite conflict diagnostics.
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
                if (lines.Length == 0 || !string.Equals(lines[0], "TajsCOIShortcutBindingsV1", StringComparison.Ordinal))
                {
                    error = "Unsupported shortcut binding schema.";
                    return false;
                }

                var rejected = new List<string>();
                for (int index = 1; index < lines.Length; index++)
                {
                    string[] fields = lines[index].Split('\t');
                    if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[0]))
                    {
                        continue;
                    }
                    if (!ShortcutCombination.TryParse(fields[1], out ShortcutCombination primary) ||
                        !ShortcutCombination.TryParse(fields[2], out ShortcutCombination secondary))
                    {
                        rejected.Add(fields[0]);
                        continue;
                    }

                    ShortcutSetResult result = TrySetBinding(fields[0], primary, secondary);
                    if (!result.Success)
                    {
                        rejected.Add(fields[0]);
                    }
                }

                if (rejected.Count > 0)
                {
                    error = "Some shortcut bindings were ignored: " + string.Join(", ", rejected.Distinct(StringComparer.Ordinal));
                    return false;
                }

                return true;
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

                var builder = new System.Text.StringBuilder("TajsCOIShortcutBindingsV1\n");
                foreach (ShortcutBindingSnapshot snapshot in GetSnapshot())
                {
                    builder.Append(snapshot.Descriptor.ActionId).Append('\t')
                        .Append(snapshot.Primary.Serialized).Append('\t')
                        .Append(snapshot.Secondary.Serialized).AppendLine();
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
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private string? FindConflict(string actionId, ShortcutCombination primary, ShortcutCombination secondary)
        {
            ShortcutCombination[] requested = new[] { primary, secondary }.Where(value => !value.IsEmpty).ToArray();
            foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in m_bindings)
            {
                if (item.Key == actionId)
                {
                    continue;
                }

                if (requested.Any(value => value == item.Value.Primary || value == item.Value.Secondary))
                {
                    return item.Key;
                }
            }

            foreach (KeyValuePair<string, ShortcutCombination> item in m_vanillaBindings)
            {
                if (requested.Any(value => value == item.Value))
                {
                    return "vanilla:" + item.Key;
                }
            }

            return null;
        }

        private void RebuildBindingIndex()
        {
            m_bindingIndex.Clear();
            foreach (KeyValuePair<string, (ShortcutCombination Primary, ShortcutCombination Secondary)> item in m_bindings)
            {
                if (!item.Value.Primary.IsEmpty)
                {
                    m_bindingIndex[item.Value.Primary.Serialized] = item.Key;
                }
                if (!item.Value.Secondary.IsEmpty)
                {
                    m_bindingIndex[item.Value.Secondary.Serialized] = item.Key;
                }
            }
        }

        private static bool DescriptorsMatch(ShortcutDescriptor left, ShortcutDescriptor right) =>
            string.Equals(left.ActionId, right.ActionId, StringComparison.Ordinal) &&
            string.Equals(left.Label, right.Label, StringComparison.Ordinal) &&
            string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
            left.DefaultPrimary == right.DefaultPrimary &&
            left.DefaultSecondary == right.DefaultSecondary &&
            left.Context == right.Context;
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
            return new ShortcutDispatchResult(true, match.Descriptor.ActionId, "dispatched");
        }

        /// <summary>
        /// Applies a combination captured by a modal keybind dialog. The existing binding is
        /// retained for the other slot, and normal conflict checks still apply.
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
