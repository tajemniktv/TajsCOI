// Taj's COI Mods | SettingValueEditor.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Globalization;

namespace TajsCOI.Common.Settings
{
    /// <summary>
    ///     The presentation state of a setting value editor. The state is kept in
    ///     the presentation layer; it does not change the typed settings service.
    /// </summary>
    public enum SettingValueEditorState
    {
        Applied,
        Dirty,
        Invalid,
        RequiresSaveReload,
        RequiresRestart,
        Unavailable,
    }

    /// <summary>
    ///     Culture-aware input parsing and canonical display formatting for
    ///     setting editors. Numeric range and step checks are delegated to the
    ///     supplied descriptor so UI code cannot accidentally create a second
    ///     validation authority.
    /// </summary>
    public static class SettingValueEditorFormatting
    {
        public static bool TryParse(
            SettingDescriptor descriptor,
            string? text,
            CultureInfo? culture,
            out object normalized,
            out string error)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            normalized = descriptor.DefaultValue;
            error = string.Empty;
            CultureInfo activeCulture = culture ?? CultureInfo.CurrentCulture;
            string input = (text ?? string.Empty).Trim();
            if (input.Length == 0)
            {
                error = "A value is required.";
                return false;
            }

            if (descriptor.ValueType != SettingValueType.Integer && descriptor.ValueType != SettingValueType.Float)
            {
                return descriptor.TryNormalize(input, out normalized, out error);
            }

            if (!TryNormalizeNumericText(descriptor, input, activeCulture, out string invariant, out error) ||
                !double.TryParse(
                    invariant,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double number))
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "Expected a finite number.";
                }
                return false;
            }

            return descriptor.TryNormalize(number, out normalized, out error);
        }

        public static string Format(
            SettingDescriptor descriptor,
            object value,
            CultureInfo? culture = null)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            CultureInfo activeCulture = culture ?? CultureInfo.CurrentCulture;
            string formatted = descriptor.ValueType switch
            {
                SettingValueType.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(activeCulture),
                SettingValueType.Float => Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("G", activeCulture),
                _ => Convert.ToString(value, activeCulture) ?? string.Empty,
            };
            return descriptor.ValueFormat == SettingValueFormat.Percentage &&
                   (descriptor.ValueType == SettingValueType.Integer || descriptor.ValueType == SettingValueType.Float)
                ? formatted + "%"
                : formatted;
        }

        private static bool TryNormalizeNumericText(
            SettingDescriptor descriptor,
            string input,
            CultureInfo culture,
            out string invariant,
            out string error)
        {
            invariant = string.Empty;
            error = string.Empty;
            bool hasPercent = input.EndsWith("%", StringComparison.Ordinal);
            if (hasPercent)
            {
                if (descriptor.ValueFormat != SettingValueFormat.Percentage)
                {
                    error = "A percentage suffix is not supported for this setting.";
                    return false;
                }
                input = input.Substring(0, input.Length - 1).TrimEnd();
            }
            else if (input.IndexOf('%') >= 0)
            {
                error = "The percentage suffix must be at the end of the value.";
                return false;
            }

            if (input.Length == 0)
            {
                error = "A number is required before the percentage suffix.";
                return false;
            }

            string cultureSeparator = culture.NumberFormat.NumberDecimalSeparator;
            if (cultureSeparator.Length != 1 || cultureSeparator[0] != '.' && cultureSeparator[0] != ',')
            {
                cultureSeparator = ".";
            }
            char cultureDecimal = cultureSeparator[0];
            bool hasDot = false;
            bool hasCultureSeparator = false;
            int decimalSeparators = 0;
            int digitCount = 0;
            for (int index = 0; index < input.Length; index++)
            {
                char character = input[index];
                if (char.IsDigit(character))
                {
                    digitCount++;
                    continue;
                }
                if (character == '+' || character == '-')
                {
                    if (index != 0)
                    {
                        error = "A sign is only valid at the start of the value.";
                        return false;
                    }
                    continue;
                }
                if (character == '.' || character == ',')
                {
                    if (character == '.')
                    {
                        hasDot = true;
                    }
                    if (character == cultureDecimal)
                    {
                        hasCultureSeparator = true;
                    }
                    decimalSeparators++;
                    if (decimalSeparators > 1)
                    {
                        error = "Use one decimal separator; grouped or mixed separators are not accepted.";
                        return false;
                    }
                    continue;
                }

                error = "The value contains an invalid character.";
                return false;
            }

            if (digitCount == 0)
            {
                error = "Expected a finite number.";
                return false;
            }
            if (cultureDecimal != '.' && hasDot && hasCultureSeparator)
            {
                error = "Mixed decimal separators are not accepted.";
                return false;
            }
            invariant = cultureDecimal == ',' ? input.Replace(',', '.') : input;
            return true;
        }
    }

    /// <summary>
    ///     State machine shared by UI controls. A control supplies an authoritative
    ///     value writer through its apply delegate and can then use the same
    ///     commit/revert behavior in dashboard and feature-specific inspectors.
    /// </summary>
    public sealed class SettingValueEditorModel
    {
        private readonly SettingDescriptor m_descriptor;
        private readonly Func<object, SettingSetResult> m_apply;
        private object m_authoritativeValue;
        private SettingApplyMode m_lastApplyMode;
        private bool m_isAvailable = true;
        private string m_unavailableReason = string.Empty;
        private string m_text;
        private string m_error = string.Empty;
        private bool m_dirty;

        public SettingValueEditorModel(
            SettingDescriptor descriptor,
            object authoritativeValue,
            Func<object, SettingSetResult> apply,
            CultureInfo? culture = null)
        {
            m_descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            m_apply = apply ?? throw new ArgumentNullException(nameof(apply));
            if (!descriptor.TryNormalize(authoritativeValue, out m_authoritativeValue, out string error))
            {
                throw new ArgumentException("The initial value is invalid: " + error, nameof(authoritativeValue));
            }
            Culture = culture ?? CultureInfo.CurrentCulture;
            m_lastApplyMode = SettingApplyMode.Immediate;
            m_text = SettingValueEditorFormatting.Format(m_descriptor, m_authoritativeValue, Culture);
        }

        public SettingDescriptor Descriptor => m_descriptor;
        public CultureInfo Culture { get; }
        public string Text => m_text;
        public object AuthoritativeValue => m_authoritativeValue;
        public string Error => m_error;
        public string UnavailableReason => m_unavailableReason;
        public bool IsDirty => m_dirty;
        public bool IsAvailable => m_isAvailable;

        public SettingValueEditorState State =>
            !m_isAvailable ? SettingValueEditorState.Unavailable :
            !string.IsNullOrEmpty(m_error) ? SettingValueEditorState.Invalid :
            m_dirty ? SettingValueEditorState.Dirty :
            m_lastApplyMode == SettingApplyMode.ReloadSave ? SettingValueEditorState.RequiresSaveReload :
            m_lastApplyMode == SettingApplyMode.RestartGame ? SettingValueEditorState.RequiresRestart :
            SettingValueEditorState.Applied;

        public void SetInput(string? text)
        {
            m_text = text ?? string.Empty;
            m_error = string.Empty;
            m_dirty = !string.Equals(
                m_text,
                SettingValueEditorFormatting.Format(m_descriptor, m_authoritativeValue, Culture),
                StringComparison.Ordinal);
        }

        public bool TryCommit(out SettingSetResult result)
        {
            if (!m_isAvailable)
            {
                result = SettingSetResult.Rejected(
                    string.IsNullOrEmpty(m_unavailableReason) ? "This setting is unavailable." : m_unavailableReason);
                return false;
            }
            if (!SettingValueEditorFormatting.TryParse(
                    m_descriptor,
                    m_text,
                    Culture,
                    out object normalized,
                    out string parseError))
            {
                m_error = parseError;
                m_dirty = true;
                result = SettingSetResult.Rejected(parseError);
                return false;
            }

            try
            {
                result = m_apply(normalized);
            }
            catch (Exception exception)
            {
                string applyError = "The value could not be applied: " + exception.Message;
                m_error = applyError;
                m_dirty = true;
                result = SettingSetResult.Rejected(applyError);
                return false;
            }
            if (!result.Success)
            {
                m_error = result.Error;
                m_dirty = true;
                return false;
            }

            object appliedValue = result.Value ?? normalized;
            if (!m_descriptor.TryNormalize(appliedValue, out object canonicalValue, out string authoritativeError))
            {
                m_error = "The owner returned an invalid value: " + authoritativeError;
                m_dirty = true;
                result = SettingSetResult.Rejected(m_error);
                return false;
            }
            m_authoritativeValue = canonicalValue;
            m_lastApplyMode = result.ApplyMode;
            m_error = string.Empty;
            m_dirty = false;
            m_text = SettingValueEditorFormatting.Format(m_descriptor, m_authoritativeValue, Culture);
            return true;
        }

        public void Revert()
        {
            m_text = SettingValueEditorFormatting.Format(m_descriptor, m_authoritativeValue, Culture);
            m_error = string.Empty;
            m_dirty = false;
        }

        public void Refresh(object authoritativeValue, SettingApplyMode applyMode = SettingApplyMode.Immediate)
        {
            if (!m_descriptor.TryNormalize(authoritativeValue, out object normalized, out string error))
            {
                throw new ArgumentException("The refreshed value is invalid: " + error, nameof(authoritativeValue));
            }

            m_authoritativeValue = normalized;
            m_lastApplyMode = applyMode;
            if (!m_dirty)
            {
                m_text = SettingValueEditorFormatting.Format(m_descriptor, m_authoritativeValue, Culture);
                m_error = string.Empty;
            }
        }

        public void SetAvailable(bool available, string? reason = null)
        {
            m_isAvailable = available;
            m_unavailableReason = available ? string.Empty : reason ?? "This setting is unavailable.";
            if (available)
            {
                m_error = string.Empty;
            }
        }
    }
}
