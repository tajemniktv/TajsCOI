// Taj's COI Mods | TajsDifficultyCommandsProcessor.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Core.Game;
using Mafi.Core.Input;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    /// <summary>
    ///     Processes Tajs console mutations on the simulation command path. Commands carry a
    ///     member/value request, not a full config snapshot, and invoke the native applier only
    ///     after rebasing on its current config.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class TajsDifficultyCommandsProcessor :
        ICommandProcessor<TajsDifficultySetCmd>,
        IAction<TajsDifficultySetCmd>,
        ICommandProcessor<TajsDifficultyResetCmd>,
        IAction<TajsDifficultyResetCmd>
    {
        private readonly GameDifficultyApplier m_applier;

        public TajsDifficultyCommandsProcessor(GameDifficultyApplier applier)
        {
            m_applier = applier ?? throw new ArgumentNullException(nameof(applier));
        }

        public void Invoke(TajsDifficultySetCmd command)
        {
            if (!TryFind(command.MemberName, out TajsDifficultyDefinition? definition, out PropertyInfo? property, out IDiffSettingInfo? info))
            {
                command.SetResultError("Unknown or unsupported difficulty setting '" + command.MemberName + "'.");
                return;
            }

            if (!CanChangeAtRuntime(definition))
            {
                command.SetResultError(
                    definition.DisplayName + " is " +
                    TajsDifficultyApplyModeText(definition.ApplyMode) + "; the active save was not changed.");
                return;
            }

            if (!TajsDifficultyFeature.TryParseValue(
                    command.RawValue,
                    property.PropertyType,
                    definition,
                    info,
                    out object? value,
                    out string error))
            {
                command.SetResultError(error);
                return;
            }

            if (TajsDifficultyFeature.NeedsConfirmation(value) && !command.Confirmed)
            {
                command.SetResultError("The requested value is unusually extreme. Repeat with CONFIRM to apply it.");
                return;
            }

            GameDifficultyConfig updated = m_applier.DifficultyConfig.Clone();
            property.SetValue(updated, value);
            ApplyNative(command, updated);
        }

        public void Invoke(TajsDifficultyResetCmd command)
        {
            if (command.MemberNames is null || command.EncodedValues is null ||
                command.MemberNames.Length == 0 ||
                command.MemberNames.Length != command.EncodedValues.Length)
            {
                command.SetResultError("The difficulty reset transaction was malformed and was not applied.");
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            GameDifficultyConfig updated = m_applier.DifficultyConfig.Clone();
            for (int index = 0; index < command.MemberNames.Length; index++)
            {
                string memberName = command.MemberNames[index]?.Trim() ?? string.Empty;
                if (!seen.Add(memberName) ||
                    !TryFind(memberName, out TajsDifficultyDefinition? definition, out PropertyInfo? property, out IDiffSettingInfo? info))
                {
                    command.SetResultError("The difficulty reset transaction contains an unknown or duplicate setting.");
                    return;
                }

                if (!CanChangeAtRuntime(definition))
                {
                    command.SetResultError(
                        definition.DisplayName + " is " +
                        TajsDifficultyApplyModeText(definition.ApplyMode) + "; the reset was not applied.");
                    return;
                }

                if (!TajsDifficultyFeature.TryParseValue(
                        command.EncodedValues[index],
                        property.PropertyType,
                        definition,
                        info,
                        out object? value,
                        out string error))
                {
                    command.SetResultError(error);
                    return;
                }

                property.SetValue(updated, value);
            }

            // No native command is invoked until every member has validated. GameDifficultyApplier
            // then checks cooldowns for the complete diff before changing any value.
            ApplyNative(command, updated);
        }

        private void ApplyNative(InputCommand command, GameDifficultyConfig updated)
        {
            try
            {
                var nativeCommand = new ChangeGameDifficultyCmd(updated);
                m_applier.Invoke(nativeCommand);
                if (!nativeCommand.ResultSet)
                {
                    command.SetResultError("Native difficulty application did not produce a result.");
                }
                else if (nativeCommand.HasError)
                {
                    command.SetResultError(
                        string.IsNullOrEmpty(nativeCommand.ErrorMessage)
                            ? "Native difficulty application rejected the change."
                            : nativeCommand.ErrorMessage);
                }
                else
                {
                    command.SetResultSuccess();
                }
            }
            catch (Exception exception)
            {
                command.SetResultError("Native difficulty application failed: " + exception.Message);
            }
        }

        private static bool TryFind(
            string memberName,
            out TajsDifficultyDefinition definition,
            out PropertyInfo property,
            out IDiffSettingInfo info)
        {
            definition = null!;
            property = null!;
            info = null!;
            string requested = memberName?.Trim() ?? string.Empty;
            TajsDifficultyDefinition? catalogDefinition = TajsDifficultyOptionCatalog.Find(requested);
            IDiffSettingInfo? nativeInfo = GameDifficultyConfig.AllOptions.FirstOrDefault(value =>
                value is not null && string.Equals(value.ValueMemberName, requested, StringComparison.Ordinal));
            if (catalogDefinition is null || nativeInfo is null)
            {
                return false;
            }

            definition = catalogDefinition;
            property = nativeInfo.Property;
            info = nativeInfo;
            return true;
        }

        private static bool CanChangeAtRuntime(TajsDifficultyDefinition definition)
        {
            return definition.ApplyMode != TajsDifficultyApplyMode.Unsupported &&
                   definition.ApplyMode != TajsDifficultyApplyMode.NewGameOnly &&
                   definition.ApplyMode != TajsDifficultyApplyMode.ReloadSave;
        }

        private static string TajsDifficultyApplyModeText(TajsDifficultyApplyMode mode) =>
            mode == TajsDifficultyApplyMode.ReloadSave ? "requires reload" :
            mode == TajsDifficultyApplyMode.NewGameOnly ? "new-game only" :
            "unsupported in this game version";
    }
}
