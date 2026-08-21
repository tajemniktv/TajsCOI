// Taj's COI Mods | UnlockedSpeedSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsTweaks.Features.UnlockedSpeed;

internal static class UnlockedSpeedSettings
{
    internal const string ConfigKey = "unlocked_speed_max";
    internal const int DefaultMaxSpeed = 100;
    internal const int MinMaxSpeed = 20;
    internal const int MaxMaxSpeed = 500;

    internal static int MaxSpeed { get; private set; } = DefaultMaxSpeed;

    internal static void Update(int configuredValue)
    {
        if (configuredValue < MinMaxSpeed)
            MaxSpeed = MinMaxSpeed;
        else if (configuredValue > MaxMaxSpeed)
            MaxSpeed = MaxMaxSpeed;
        else
            MaxSpeed = configuredValue;
    }
}