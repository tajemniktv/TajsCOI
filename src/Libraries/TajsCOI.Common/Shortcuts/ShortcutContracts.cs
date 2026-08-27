// Taj's COI Mods | ShortcutContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Common.Shortcuts
{
    public enum ShortcutActivationContext
    {
        Global,
        Gameplay,
        Ui,
        Tool,
        Modal,
    }

    public enum ShortcutRegistrationStatus
    {
        Added,
        Updated,
        AlreadyRegistered,
        Rejected,
    }

    public enum ShortcutSetStatus
    {
        Applied,
        Cleared,
        Rejected,
        Conflict,
        UnknownAction,
    }

    /// <summary>
    /// A culture-independent, serializable keyboard/mouse combination. The value is
    /// normalized once so conflict checks never depend on the casing used by a caller.
    /// </summary>
    public readonly struct ShortcutCombination : IEquatable<ShortcutCombination>
    {
        public ShortcutCombination(string serialized)
        {
            if (!TryParse(serialized, out string normalized))
            {
                throw new ArgumentException("Shortcut combination must contain a key or button.", nameof(serialized));
            }

            Serialized = normalized;
        }

        public string Serialized { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Serialized);

        public static ShortcutCombination Empty => default;

        public static bool TryParse(string? serialized, out ShortcutCombination combination)
        {
            if (!TryParse(serialized, out string normalized))
            {
                combination = default;
                return false;
            }

            combination = new ShortcutCombination(normalized, alreadyNormalized: true);
            return true;
        }

        public bool Equals(ShortcutCombination other) =>
            string.Equals(Serialized, other.Serialized, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ShortcutCombination other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Serialized ?? string.Empty);

        public override string ToString() => Serialized ?? string.Empty;

        public static bool operator ==(ShortcutCombination left, ShortcutCombination right) => left.Equals(right);

        public static bool operator !=(ShortcutCombination left, ShortcutCombination right) => !left.Equals(right);

        private ShortcutCombination(string normalized, bool alreadyNormalized)
        {
            Serialized = normalized;
        }

        private static bool TryParse(string? serialized, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return true;
            }

            string[] tokens = serialized!
                .Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length > 0)
                .Select(token => token.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (tokens.Length == 0)
            {
                return true;
            }

            string[] modifiers = { "CTRL", "CONTROL", "ALT", "SHIFT", "META", "CMD", "COMMAND" };
            string[] modifierTokens = tokens
                .Where(token => modifiers.Contains(token, StringComparer.Ordinal))
                .Select(NormalizeModifier)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(token => ModifierOrder(token))
                .ToArray();
            string[] keyTokens = tokens.Where(token => !modifiers.Contains(token, StringComparer.Ordinal)).ToArray();
            if (keyTokens.Length != 1 || keyTokens[0].Length == 0)
            {
                return false;
            }

            normalized = string.Join("+", modifierTokens.Concat(new[] { keyTokens[0] }));
            return true;
        }

        private static string NormalizeModifier(string token) =>
            token == "CONTROL" ? "CTRL" :
            token == "COMMAND" || token == "CMD" ? "META" : token;

        private static int ModifierOrder(string token) =>
            token == "CTRL" ? 0 : token == "ALT" ? 1 : token == "SHIFT" ? 2 : 3;
    }

    public sealed class ShortcutDescriptor
    {
        public ShortcutDescriptor(
            string actionId,
            string label,
            string category,
            ShortcutCombination defaultPrimary,
            ShortcutCombination defaultSecondary,
            ShortcutActivationContext context)
        {
            ActionId = RequireId(actionId, nameof(actionId));
            Label = RequireText(label, nameof(label));
            Category = RequireText(category, nameof(category));
            DefaultPrimary = defaultPrimary;
            DefaultSecondary = defaultSecondary;
            Context = context;
        }

        public string ActionId { get; }
        public string Label { get; }
        public string Category { get; }
        public ShortcutCombination DefaultPrimary { get; }
        public ShortcutCombination DefaultSecondary { get; }
        public ShortcutActivationContext Context { get; }

        private static string RequireText(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Shortcut text cannot be empty.", parameter)
                : value.Trim();

        private static string RequireId(string value, string parameter)
        {
            string result = RequireText(value, parameter);
            if (result.Any(character => !(char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.')))
            {
                throw new ArgumentException("Shortcut IDs may contain only letters, digits, '.', '_' and '-'.", parameter);
            }

            return result;
        }
    }

    public sealed class ShortcutBindingSnapshot
    {
        public ShortcutBindingSnapshot(
            ShortcutDescriptor descriptor,
            ShortcutCombination primary,
            ShortcutCombination secondary,
            bool isDefault)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Primary = primary;
            Secondary = secondary;
            IsDefault = isDefault;
        }

        public ShortcutDescriptor Descriptor { get; }
        public ShortcutCombination Primary { get; }
        public ShortcutCombination Secondary { get; }
        public bool IsDefault { get; }
    }

    public sealed class ShortcutRegistrationResult
    {
        public ShortcutRegistrationResult(ShortcutRegistrationStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public ShortcutRegistrationStatus Status { get; }
        public string Message { get; }
        public bool IsSuccess => Status != ShortcutRegistrationStatus.Rejected;
    }

    public sealed class ShortcutSetResult
    {
        public ShortcutSetResult(ShortcutSetStatus status, string message, string? conflictingActionId = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            ConflictingActionId = conflictingActionId ?? string.Empty;
        }

        public ShortcutSetStatus Status { get; }
        public string Message { get; }
        public string ConflictingActionId { get; }
        public bool Success => Status == ShortcutSetStatus.Applied || Status == ShortcutSetStatus.Cleared;
    }

    public interface IShortcutRegistry
    {
        ShortcutRegistrationResult Register(ShortcutDescriptor descriptor);

        ShortcutSetResult TrySetBinding(string actionId, ShortcutCombination primary, ShortcutCombination secondary);

        bool TryGet(string actionId, out ShortcutBindingSnapshot snapshot);

        /// <summary>
        /// Resolves one already-normalized combination without enumerating all bindings. Input
        /// dispatch uses this indexed path so key events do not allocate a full snapshot.
        /// </summary>
        bool TryResolveBinding(ShortcutCombination combination, out ShortcutBindingSnapshot snapshot);

        IReadOnlyList<ShortcutBindingSnapshot> GetSnapshot();

        void CacheVanillaBindings(IEnumerable<KeyValuePair<string, ShortcutCombination>> bindings);

        IReadOnlyDictionary<string, ShortcutCombination> GetVanillaBindingsSnapshot();

        bool TryLoad(string path, out string error);

        bool TrySave(string path, out string error);
    }

    public interface IShortcutDispatchGate
    {
        bool HasTextFieldFocus { get; }
        bool ModalCapturesInput { get; }
        bool ToolOwnsInput { get; }
        bool UiCapturesInput { get; }
        bool IsContextActive(ShortcutActivationContext context);
    }

    public sealed class ShortcutDispatchResult
    {
        public ShortcutDispatchResult(bool handled, string actionId, string reason)
        {
            Handled = handled;
            ActionId = actionId ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public bool Handled { get; }
        public string ActionId { get; }
        public string Reason { get; }
    }

    public interface IShortcutInputService
    {
        IDisposable RegisterHandler(string actionId, Action handler);

        ShortcutDispatchResult TryDispatch(ShortcutCombination combination, IShortcutDispatchGate gate);

        ShortcutSetResult CaptureBinding(string actionId, ShortcutCombination combination, bool secondary);
    }
}
