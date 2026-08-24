// Taj's COI Mods | SettingDescriptor.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace TajsCOI.Common.Settings
{
    public sealed class SettingDescriptor
    {
        private SettingDescriptor(
            string modId,
            string modDisplayName,
            string key,
            string displayName,
            string description,
            string category,
            SettingValueType valueType,
            object defaultValue,
            double? minimum,
            double? maximum,
            double? step,
            IReadOnlyList<SettingChoice>? choices,
            SettingScope scope,
            SettingApplyMode applyMode,
            SettingFlags flags,
            string? componentRequirement)
        {
            ModId = RequireId(modId, nameof(modId));
            ModDisplayName = RequireText(modDisplayName, nameof(modDisplayName));
            Key = RequireId(key, nameof(key));
            DisplayName = RequireText(displayName, nameof(displayName));
            Description = RequireText(description, nameof(description));
            Category = RequireText(category, nameof(category));
            ValueType = valueType;
            Minimum = minimum;
            Maximum = maximum;
            Step = step;
            Choices = new ReadOnlyCollection<SettingChoice>((choices ?? Array.Empty<SettingChoice>()).ToArray());
            Scope = scope;
            ApplyMode = applyMode;
            Flags = flags;
            ComponentRequirement = string.IsNullOrWhiteSpace(componentRequirement) ? null : componentRequirement!.Trim();

            ValidateShape();
            if (!TryNormalize(defaultValue, out object normalized, out string error))
            {
                throw new ArgumentException("Invalid setting default: " + error, nameof(defaultValue));
            }
            DefaultValue = normalized;
        }

        public string ModId { get; }
        public string ModDisplayName { get; }
        public string Key { get; }
        public string StableId => ModId + "." + Key;
        public string DisplayName { get; }
        public string Description { get; }
        public string Category { get; }
        public SettingValueType ValueType { get; }
        public object DefaultValue { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }
        public double? Step { get; }
        public IReadOnlyList<SettingChoice> Choices { get; }
        public SettingScope Scope { get; }
        public SettingApplyMode ApplyMode { get; }
        public SettingFlags Flags { get; }
        public string? ComponentRequirement { get; }

        public static SettingDescriptor Boolean(
            string modId, string modDisplayName, string key, string displayName, string description,
            bool defaultValue, string category = "General", SettingScope scope = SettingScope.Global,
            SettingApplyMode applyMode = SettingApplyMode.Immediate, SettingFlags flags = SettingFlags.None,
            string? componentRequirement = null) =>
            new(modId, modDisplayName, key, displayName, description, category, SettingValueType.Boolean,
                defaultValue, null, null, null, null, scope, applyMode, flags, componentRequirement);

        public static SettingDescriptor Integer(
            string modId, string modDisplayName, string key, string displayName, string description,
            int defaultValue, int minimum, int maximum, int step = 1, string category = "General",
            SettingScope scope = SettingScope.Global, SettingApplyMode applyMode = SettingApplyMode.Immediate,
            SettingFlags flags = SettingFlags.None, string? componentRequirement = null) =>
            new(modId, modDisplayName, key, displayName, description, category, SettingValueType.Integer,
                defaultValue, minimum, maximum, step, null, scope, applyMode, flags, componentRequirement);

        public static SettingDescriptor Float(
            string modId, string modDisplayName, string key, string displayName, string description,
            double defaultValue, double minimum, double maximum, double step, string category = "General",
            SettingScope scope = SettingScope.Global, SettingApplyMode applyMode = SettingApplyMode.Immediate,
            SettingFlags flags = SettingFlags.None, string? componentRequirement = null) =>
            new(modId, modDisplayName, key, displayName, description, category, SettingValueType.Float,
                defaultValue, minimum, maximum, step, null, scope, applyMode, flags, componentRequirement);

        public static SettingDescriptor Choice(
            string modId, string modDisplayName, string key, string displayName, string description,
            string defaultValue, IReadOnlyList<SettingChoice> choices, string category = "General",
            SettingScope scope = SettingScope.Global, SettingApplyMode applyMode = SettingApplyMode.Immediate,
            SettingFlags flags = SettingFlags.None, string? componentRequirement = null) =>
            new(modId, modDisplayName, key, displayName, description, category, SettingValueType.Choice,
                defaultValue, null, null, null, choices, scope, applyMode, flags, componentRequirement);

        public static SettingDescriptor String(
            string modId, string modDisplayName, string key, string displayName, string description,
            string defaultValue, string category = "General", SettingScope scope = SettingScope.Global,
            SettingApplyMode applyMode = SettingApplyMode.Immediate, SettingFlags flags = SettingFlags.None,
            string? componentRequirement = null) =>
            new(modId, modDisplayName, key, displayName, description, category, SettingValueType.String,
                defaultValue, null, null, null, null, scope, applyMode, flags, componentRequirement);

        public bool TryNormalize(object? input, out object normalized, out string error)
        {
            normalized = DefaultFor(ValueType);
            error = string.Empty;
            try
            {
                switch (ValueType)
                {
                    case SettingValueType.Boolean:
                        return TryNormalizeBoolean(input, out normalized, out error);

                    case SettingValueType.Integer:
                        return TryNormalizeInteger(input, out normalized, out error);

                    case SettingValueType.Float:
                        return TryNormalizeFloat(input, out normalized, out error);

                    case SettingValueType.Choice:
                        return TryNormalizeChoice(input, out normalized, out error);

                    case SettingValueType.String:
                        return TryNormalizeString(input, out normalized, out error);

                    default:
                        error = "Unsupported setting type.";
                        return false;
                }
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException || exception is ArgumentNullException)
            {
                error = "Value could not be converted to " + ValueType + ".";
                return false;
            }
        }

        private static bool TryNormalizeBoolean(object? input, out object normalized, out string error)
        {
            if (input is bool boolean || input is string text && bool.TryParse(text, out boolean))
            {
                normalized = boolean;
                error = string.Empty;
                return true;
            }
            normalized = false;
            error = "Expected true or false.";
            return false;
        }

        private bool TryNormalizeInteger(object? input, out object normalized, out string error)
        {
            normalized = 0;
            if (input is null)
            {
                error = "Expected a whole 32-bit integer.";
                return false;
            }
            double number = Convert.ToDouble(input, CultureInfo.InvariantCulture);
            // Exact comparison is intentional: integer settings reject every fractional value.
#pragma warning disable S1244
            bool hasFraction = number != Math.Truncate(number);
#pragma warning restore S1244
            if (!IsFinite(number) || hasFraction || number < int.MinValue || number > int.MaxValue)
            {
                error = "Expected a whole 32-bit integer.";
                return false;
            }
            return ValidateNumber((int)number, out normalized, out error);
        }

        private bool TryNormalizeFloat(object? input, out object normalized, out string error)
        {
            normalized = 0d;
            if (input is null)
            {
                error = "Expected a finite number.";
                return false;
            }
            double number = Convert.ToDouble(input, CultureInfo.InvariantCulture);
            if (!IsFinite(number))
            {
                error = "Expected a finite number.";
                return false;
            }
            return ValidateNumber(number, out normalized, out error);
        }

        private bool TryNormalizeChoice(object? input, out object normalized, out string error)
        {
            string choice = Convert.ToString(input, CultureInfo.InvariantCulture) ?? string.Empty;
            if (Choices.Any(x => string.Equals(x.Value, choice, StringComparison.Ordinal)))
            {
                normalized = choice;
                error = string.Empty;
                return true;
            }
            normalized = string.Empty;
            error = "Expected one of: " + string.Join(", ", Choices.Select(x => x.Value)) + ".";
            return false;
        }

        private static bool TryNormalizeString(object? input, out object normalized, out string error)
        {
            if (input is string text)
            {
                normalized = text;
                error = string.Empty;
                return true;
            }
            normalized = string.Empty;
            error = "Expected text.";
            return false;
        }

        private bool ValidateNumber(double number, out object normalized, out string error)
        {
            normalized = ValueType == SettingValueType.Integer ? (object)(int)number : number;
            if (Minimum.HasValue && number < Minimum.Value || Maximum.HasValue && number > Maximum.Value)
            {
                error = $"Expected a value between {Minimum} and {Maximum}.";
                return false;
            }
            if (Step.HasValue && Minimum.HasValue)
            {
                double steps = (number - Minimum.Value) / Step.Value;
                if (Math.Abs(steps - Math.Round(steps)) > 1e-7)
                {
                    error = $"Expected increments of {Step.Value.ToString(CultureInfo.InvariantCulture)}.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private void ValidateShape()
        {
            ValidateNumericShape();
            ValidateChoiceShape();
        }

        private void ValidateNumericShape()
        {
            if (Minimum.HasValue && !IsFinite(Minimum.Value) ||
                Maximum.HasValue && !IsFinite(Maximum.Value) ||
                Step.HasValue && !IsFinite(Step.Value))
            {
                throw new ArgumentOutOfRangeException("numericMetadata", "Numeric bounds and step must be finite.");
            }
            if (Minimum.HasValue != Maximum.HasValue)
            {
                throw new ArgumentException("Numeric settings require an ordered minimum and maximum.");
            }
            if (Minimum is double minimum && Maximum is double maximum && minimum > maximum)
            {
                throw new ArgumentException("Numeric settings require an ordered minimum and maximum.");
            }
            if (Step.HasValue && Step.Value <= 0)
            {
                throw new ArgumentOutOfRangeException("step", "Setting step must be positive.");
            }
            if ((ValueType == SettingValueType.Integer || ValueType == SettingValueType.Float) && !Minimum.HasValue)
            {
                throw new ArgumentException("Numeric settings require bounds.");
            }
        }

        private void ValidateChoiceShape()
        {
            if (ValueType == SettingValueType.Choice && Choices.Count == 0)
            {
                throw new ArgumentException("Choice settings require at least one option.");
            }
            if (ValueType == SettingValueType.Choice &&
                Choices.Select(x => x.Value).Distinct(StringComparer.Ordinal).Count() != Choices.Count)
            {
                throw new ArgumentException("Choice setting values must be unique.");
            }
            if (ValueType != SettingValueType.Choice && Choices.Count != 0)
            {
                throw new ArgumentException("Only choice settings can declare options.");
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static object DefaultFor(SettingValueType type) =>
            type == SettingValueType.Boolean ? (object)false :
            type == SettingValueType.Integer ? 0 :
            type == SettingValueType.Float ? 0d : string.Empty;

        private static string RequireText(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Setting text cannot be empty.", parameter)
                : value.Trim();

        private static string RequireId(string value, string parameter)
        {
            string result = RequireText(value, parameter);
            if (result.Any(character => !(char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-')))
            {
                throw new ArgumentException("Setting IDs may contain only letters, digits, '.', '_' and '-'.", parameter);
            }
            return result;
        }
    }
}
