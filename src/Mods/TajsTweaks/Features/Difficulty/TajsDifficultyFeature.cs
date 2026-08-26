// Taj's COI Mods | TajsDifficultyFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core.Game;
using Mafi.Core.Input;
using TajsCOI.Common.Logging;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    internal sealed class TajsDifficultyRow
    {
        internal TajsDifficultyRow(
            TajsDifficultyDefinition definition,
            PropertyInfo? property,
            IDiffSettingInfo? info,
            string current,
            string original,
            string vanilla)
        {
            Definition = definition;
            Property = property;
            Info = info;
            Current = current;
            Original = original;
            Vanilla = vanilla;
        }

        internal TajsDifficultyDefinition Definition { get; }
        internal PropertyInfo? Property { get; }
        internal IDiffSettingInfo? Info { get; }
        internal string Current { get; }
        internal string Original { get; }
        internal string Vanilla { get; }
        internal bool Supported => Property is not null && Info is not null && Definition.ApplyMode != TajsDifficultyApplyMode.Unsupported;
    }

    /// <summary>
    ///     Adapts the native difficulty config to a safe editor. All actual changes go through
    ///     the game's ChangeGameDifficultyCmd, which updates native property modifiers, change
    ///     history, and save serialization consistently.
    /// </summary>
    internal sealed class TajsDifficultyFeature : IDisposable
    {
        internal const string ComponentId = "DifficultyEditor";

        private readonly GameDifficultyApplier m_applier;
        private readonly IInputScheduler m_inputScheduler;
        private readonly ITajsLogger m_log;
        private readonly TajsDifficultyStateStore m_store = new();
        private readonly Dictionary<string, PropertyInfo> m_properties = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IDiffSettingInfo> m_infos = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TajsDifficultyDefinition> m_definitions = new(StringComparer.Ordinal);
        private bool m_disposed;

        internal TajsDifficultyFeature(
            GameDifficultyApplier applier,
            IInputScheduler inputScheduler,
            string saveName,
            ITajsLogger log)
        {
            m_applier = applier ?? throw new ArgumentNullException(nameof(applier));
            m_inputScheduler = inputScheduler ?? throw new ArgumentNullException(nameof(inputScheduler));
            m_log = log ?? throw new ArgumentNullException(nameof(log));

            foreach (TajsDifficultyDefinition definition in TajsDifficultyOptionCatalog.Definitions)
            {
                m_definitions[definition.MemberName] = definition;
            }

            try
            {
                foreach (IDiffSettingInfo info in GameDifficultyConfig.AllOptions)
                {
                    if (info is null || string.IsNullOrWhiteSpace(info.ValueMemberName))
                    {
                        continue;
                    }

                    m_infos[info.ValueMemberName] = info;
                    m_properties[info.ValueMemberName] = info.Property;
                    if (!m_definitions.ContainsKey(info.ValueMemberName))
                    {
                        m_definitions[info.ValueMemberName] = TajsDifficultyOptionCatalog.CreateDiscovered(
                            info.ValueMemberName,
                            info.Property.PropertyType);
                    }
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Difficulty metadata enumeration failed open.");
            }

            m_store.LoadOrCapture(saveName, m_applier.DifficultyConfig, m_properties);
        }

        internal IReadOnlyList<TajsDifficultyRow> Snapshot()
        {
            var rows = new List<TajsDifficultyRow>(m_definitions.Count);
            foreach (TajsDifficultyDefinition definition in m_definitions.Values
                         .OrderBy(value => value.Category, StringComparer.Ordinal)
                         .ThenBy(value => value.DisplayName, StringComparer.Ordinal))
            {
                m_properties.TryGetValue(definition.MemberName, out PropertyInfo? property);
                m_infos.TryGetValue(definition.MemberName, out IDiffSettingInfo? info);
                object? current = TryGetPropertyValue(property, m_applier.DifficultyConfig);
                object? original = property is not null && m_store.TryGetOriginal(definition.MemberName, property, out object? stored)
                    ? stored
                    : null;
                object? vanilla = property is not null ? GetVanillaConfigValue(property) : null;
                rows.Add(
                    new TajsDifficultyRow(
                        definition,
                        property,
                        info,
                        FormatValue(current, info),
                        FormatValue(original, info),
                        FormatValue(vanilla, info)));
            }
            return rows;
        }

        private static object? TryGetPropertyValue(PropertyInfo? property, GameDifficultyConfig config)
        {
            try
            {
                return property?.GetValue(config);
            }
            catch
            {
                return null;
            }
        }

        internal string Status()
        {
            IReadOnlyList<TajsDifficultyRow> rows = Snapshot();
            int supported = rows.Count(row => row.Supported);
            int unsupported = rows.Count - supported;
            string unsupportedNames = string.Join(
                ", ",
                rows.Where(row => !row.Supported).Select(row => row.Definition.MemberName));
            return "TajsDifficulty: " + supported + " supported, " + unsupported + " unsupported; " +
                   "original-save values are stored separately from vanilla defaults." +
                   (unsupported == 0 ? string.Empty : " Unsupported: " + unsupportedNames + ".");
        }

        internal string Set(string memberName, string? rawValue, bool confirmed)
        {
            if (!TryFind(memberName, out TajsDifficultyDefinition? definition, out PropertyInfo? property, out IDiffSettingInfo? info))
            {
                return "Unknown or unsupported difficulty setting '" + (memberName ?? string.Empty) + "'.";
            }
            if (definition.ApplyMode is TajsDifficultyApplyMode.NewGameOnly or TajsDifficultyApplyMode.ReloadSave)
            {
                return definition.DisplayName + " is " + ApplyModeText(definition.ApplyMode) + "; the active save was not changed.";
            }

            if (!TryParseValue(rawValue, property.PropertyType, definition, info, out object? value, out string error))
            {
                return error;
            }
            if (NeedsConfirmation(value) && !confirmed)
            {
                return "The requested value is unusually extreme. Repeat with CONFIRM to apply it.";
            }

            GameDifficultyConfig updated = m_applier.DifficultyConfig.Clone();
            property.SetValue(updated, value);
            if (m_applier.DifficultyConfig.IsSameAs(updated))
            {
                return definition.DisplayName + " is already " + FormatValue(value, info) + ".";
            }

            m_inputScheduler.ScheduleInputCmd(new ChangeGameDifficultyCmd(updated));
            return definition.DisplayName + " queued at " + FormatValue(value, info) + " (" + ApplyModeText(definition.ApplyMode) + ").";
        }

        internal string Reset(string? target, bool confirmed)
        {
            if (!confirmed)
            {
                return "Resetting difficulty values changes the active save. Repeat with CONFIRM.";
            }

            string normalized = string.IsNullOrWhiteSpace(target) ? "original" : target!.Trim();
            bool useVanilla;
            if (string.Equals(normalized, "original", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "save", StringComparison.OrdinalIgnoreCase))
            {
                useVanilla = false;
            }
            else if (string.Equals(normalized, "vanilla", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase))
            {
                useVanilla = true;
            }
            else
            {
                return "Usage: tajs_difficulty_reset <original|vanilla> CONFIRM";
            }

            GameDifficultyConfig updated = m_applier.DifficultyConfig.Clone();
            int changed = 0;
            foreach (TajsDifficultyDefinition definition in m_definitions.Values)
            {
                if (definition.ApplyMode is TajsDifficultyApplyMode.NewGameOnly or TajsDifficultyApplyMode.ReloadSave ||
                    !m_properties.TryGetValue(definition.MemberName, out PropertyInfo? property) ||
                    !m_infos.ContainsKey(definition.MemberName))
                {
                    continue;
                }

                object? value;
                if (useVanilla)
                {
                    value = GetVanillaConfigValue(property);
                }
                else if (!m_store.TryGetOriginal(definition.MemberName, property, out value))
                {
                    continue;
                }

                if (value is null)
                {
                    continue;
                }
                property.SetValue(updated, value);
                changed++;
            }

            if (changed == 0 || m_applier.DifficultyConfig.IsSameAs(updated))
            {
                return "No runtime-safe difficulty values needed resetting.";
            }

            m_inputScheduler.ScheduleInputCmd(new ChangeGameDifficultyCmd(updated));
            return "Queued runtime-safe difficulty reset to " + (useVanilla ? "vanilla defaults" : "original save values") + ".";
        }

        internal string RestoreMember(string memberName, bool useVanilla, bool confirmed)
        {
            if (!confirmed)
            {
                return "Restoring a difficulty value changes the active save. Repeat with CONFIRM.";
            }
            if (!TryFind(memberName, out TajsDifficultyDefinition? definition, out PropertyInfo? property, out IDiffSettingInfo? info))
            {
                return "Unknown or unsupported difficulty setting '" + memberName + "'.";
            }
            if (definition.ApplyMode is TajsDifficultyApplyMode.NewGameOnly or TajsDifficultyApplyMode.ReloadSave)
            {
                return definition.DisplayName + " is " + ApplyModeText(definition.ApplyMode) + "; the active save was not changed.";
            }

            object? value = useVanilla
                ? GetVanillaConfigValue(property)
                : m_store.TryGetOriginal(definition.MemberName, property, out object? original)
                    ? original
                    : null;
            if (value is null)
            {
                return "No " + (useVanilla ? "vanilla" : "original-save") + " value is available for " + definition.DisplayName + ".";
            }

            GameDifficultyConfig updated = m_applier.DifficultyConfig.Clone();
            property.SetValue(updated, value);
            if (m_applier.DifficultyConfig.IsSameAs(updated))
            {
                return definition.DisplayName + " is already " + FormatValue(value, info) + ".";
            }
            m_inputScheduler.ScheduleInputCmd(new ChangeGameDifficultyCmd(updated));
            return "Queued " + definition.DisplayName + " reset to " + FormatValue(value, info) + ".";
        }

        internal string ApplyModeText(TajsDifficultyApplyMode mode) =>
            mode == TajsDifficultyApplyMode.Immediate ? "safe to change now" :
            mode == TajsDifficultyApplyMode.FutureCalculations ? "takes effect on future calculations" :
            mode == TajsDifficultyApplyMode.ReloadSave ? "requires reload" :
            mode == TajsDifficultyApplyMode.NewGameOnly ? "new-game only" :
            "unsupported in this game version";

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_store.Save();
        }

        private bool TryFind(
            string memberName,
            out TajsDifficultyDefinition definition,
            out PropertyInfo property,
            out IDiffSettingInfo info)
        {
            definition = null!;
            property = null!;
            info = null!;
            string requested = memberName?.Trim() ?? string.Empty;
            if (!m_definitions.TryGetValue(requested, out definition!) ||
                !m_properties.TryGetValue(requested, out property!) ||
                !m_infos.TryGetValue(requested, out info!))
            {
                return false;
            }
            return true;
        }

        private object? GetVanillaConfigValue(PropertyInfo property)
        {
            try
            {
                GameDifficultyConfig vanilla = m_applier.DifficultyConfig.OriginalPreset.HasValue
                    ? GameDifficultyConfig.CreateConfigFor(m_applier.DifficultyConfig.OriginalPreset.Value, true)
                    : GameDifficultyConfig.Normal(true);
                return property.GetValue(vanilla);
            }
            catch (Exception exception)
            {
                m_log.Warning("Vanilla value unavailable for " + property.Name + ": " + exception.GetType().Name);
                return null;
            }
        }

        private static bool TryParseValue(
            string? rawValue,
            Type targetType,
            TajsDifficultyDefinition definition,
            IDiffSettingInfo info,
            out object? value,
            out string error)
        {
            value = null;
            error = "";
            string input = rawValue?.Trim() ?? string.Empty;
            if (targetType == typeof(Percent))
            {
                if (string.Equals(input, "unlimited", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(definition.MemberName, "WorldMinesReservesDiff", StringComparison.Ordinal))
                {
                    value = Percent.MaxValue;
                    return true;
                }

                if (input.EndsWith("%", StringComparison.Ordinal))
                {
                    input = input.Substring(0, input.Length - 1).Trim();
                }
                if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
                {
                    error = "Enter a whole-number percentage, for example -25, 0, or 150.";
                    return false;
                }
                if (definition.Range is null || percent < definition.Range.Minimum || percent > definition.Range.Maximum)
                {
                    error = "Value is outside the validated range " + definition.Range?.Minimum + ".." + definition.Range?.Maximum + ".";
                    return false;
                }
                value = percent.Percent();
                return true;
            }

            if (targetType.IsEnum)
            {
                try
                {
                    if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
                    {
                        if (!Enum.IsDefined(targetType, numeric))
                        {
                            error = "That enum value is not supported by this game version.";
                            return false;
                        }
                        value = Enum.ToObject(targetType, numeric);
                        return true;
                    }

                    value = Enum.Parse(targetType, input, true);
                    return true;
                }
                catch (ArgumentException)
                {
                    // The native UI often displays localized labels (for example "Consume if
                    // can") rather than enum member names. Accept either representation.
                    foreach (object candidate in Enum.GetValues(targetType))
                    {
                        try
                        {
                            if (string.Equals(info.ConvertValueToString(candidate), input, StringComparison.OrdinalIgnoreCase))
                            {
                                value = candidate;
                                return true;
                            }
                        }
                        catch
                        {
                        }
                    }
                    error = "Unknown option '" + input + "'. Use the value shown in the difficulty window.";
                    return false;
                }
            }

            error = "This difficulty setting has an unsupported value type.";
            return false;
        }

        private static bool NeedsConfirmation(object? value)
        {
            if (value is Percent percent)
            {
                return percent == Percent.MaxValue || Math.Abs(percent.ToIntPercentRounded()) > 200;
            }
            return false;
        }

        private static string FormatValue(object? value, IDiffSettingInfo? info)
        {
            if (value is null)
            {
                return "unsupported";
            }
            if (value is Percent percent)
            {
                return percent == Percent.MaxValue
                    ? "Unlimited"
                    : percent.ToIntPercentRounded().ToString(CultureInfo.InvariantCulture) + "%";
            }
            try
            {
                return info?.ConvertValueToString(value) ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
    }
}
