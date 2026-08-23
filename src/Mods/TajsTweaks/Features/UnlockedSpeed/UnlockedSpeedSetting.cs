// Taj's COI Mods | UnlockedSpeedSetting.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Common.Settings;

namespace TajsCOI.Tweaks.Features.UnlockedSpeed
{
    internal static class UnlockedSpeedSetting
    {
        internal const string ModId = "TajsTweaks";
        internal const string Key = "unlocked_speed_max";

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
    }
}
