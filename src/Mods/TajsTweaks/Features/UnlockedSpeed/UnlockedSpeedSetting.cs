// Taj's COI Mods | UnlockedSpeedSetting.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using TajsCOI.Common.Settings;

namespace TajsCOI.Tweaks.Features.UnlockedSpeed
{
    internal static class UnlockedSpeedSetting
    {
        internal const string ModId = "TajsTweaks";
        internal const string Key = "unlocked_speed_max";
        internal const string SequenceModeKey = "unlocked_speed_sequence_mode";
        internal const string CustomSequenceKey = "unlocked_speed_sequence";
        internal const string ResumeOnSelectKey = "unlocked_speed_resume_on_select";

        internal const string VanillaSequenceMode = "vanilla";
        internal const string EveryIntegerSequenceMode = "every_integer";
        internal const string CustomSequenceMode = "custom";

        internal static readonly IReadOnlyList<SettingChoice> SequenceModes = new[]
        {
            new SettingChoice(VanillaSequenceMode, "Vanilla speeds"),
            new SettingChoice(EveryIntegerSequenceMode, "Every integer"),
            new SettingChoice(CustomSequenceMode, "Custom sequence"),
        };

        internal static readonly SettingDescriptor Descriptor = SettingDescriptor.Integer(
            ModId,
            "Tweaks",
            Key,
            "Maximum unlocked speed",
            "Maximum value accepted by the unlocked simulation speed command.",
            defaultValue: 100,
            minimum: 20,
            maximum: 500,
            step: 1,
            category: "Simulation",
            scope: SettingScope.Global,
            applyMode: SettingApplyMode.Immediate);

        internal static readonly SettingDescriptor SequenceModeDescriptor = SettingDescriptor.Choice(
            ModId,
            "Tweaks",
            SequenceModeKey,
            "Speed-step sequence",
            "Controls the values used by the normal increase/decrease speed shortcuts. Vanilla keeps the standard 1x, 2x, 3x and 12x steps.",
            VanillaSequenceMode,
            SequenceModes,
            category: "Simulation",
            scope: SettingScope.Global,
            applyMode: SettingApplyMode.Immediate);

        internal static readonly SettingDescriptor CustomSequenceDescriptor = SettingDescriptor.String(
            ModId,
            "Tweaks",
            CustomSequenceKey,
            "Custom speed sequence",
            "Comma-separated positive speed multipliers. Values outside the configured maximum are ignored; 1x and the maximum are always available in custom mode.",
            "1,2,3,12,20,50,100",
            category: "Simulation",
            scope: SettingScope.Global,
            applyMode: SettingApplyMode.Immediate);

        internal static readonly SettingDescriptor ResumeOnSelectDescriptor = SettingDescriptor.Boolean(
            ModId,
            "Tweaks",
            ResumeOnSelectKey,
            "Resume when selecting a speed",
            "When enabled, selecting a speed while paused resumes the simulation through the normal game-speed command path.",
            true,
            category: "Simulation",
            scope: SettingScope.Global,
            applyMode: SettingApplyMode.Immediate);

        internal static IReadOnlyList<SettingDescriptor> All { get; } = new[]
        {
            Descriptor,
            SequenceModeDescriptor,
            CustomSequenceDescriptor,
            ResumeOnSelectDescriptor,
        };
    }
}
