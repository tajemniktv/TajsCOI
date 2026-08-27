// Taj's COI Mods | SettingEnums.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Common.Settings
{
    public enum SettingValueType
    {
        Boolean,
        Integer,
        Float,
        Choice,
        String,
    }

    /// <summary>
    /// Describes how a typed setting value should be presented by a reusable
    /// editor. This is presentation metadata only; the descriptor's numeric
    /// bounds and validator remain authoritative.
    /// </summary>
    public enum SettingValueFormat
    {
        Default,
        Percentage,
        ColorComponent,
    }

    public enum SettingScope
    {
        Global,
        PerSave,
    }

    public enum SettingApplyMode
    {
        Immediate,
        ReloadSave,
        RestartGame,
    }

    [Flags]
    public enum SettingFlags
    {
        None = 0,
        Advanced = 1 << 0,
        Experimental = 1 << 1,
        Dangerous = 1 << 2,
    }
}
