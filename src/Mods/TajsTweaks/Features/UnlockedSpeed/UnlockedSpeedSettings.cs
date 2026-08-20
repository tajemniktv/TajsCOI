// Taj's Game | UnlockedSpeedSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsTweaks.Features.UnlockedSpeed;

internal static class UnlockedSpeedSettings
{
    internal const string ConfigKey = "unlocked_speed_max";
    internal const int DefaultMaxSpeed = 100;
    internal const int MinMaxSpeed = 20;
    internal const int MaxMaxSpeed = 500;

    private static int _maxSpeed = DefaultMaxSpeed;

    internal static int MaxSpeed => _maxSpeed;

    internal static void Update(int configuredValue)
    {
        if (configuredValue < MinMaxSpeed)
            _maxSpeed = MinMaxSpeed;
        else if (configuredValue > MaxMaxSpeed)
            _maxSpeed = MaxMaxSpeed;
        else
            _maxSpeed = configuredValue;
    }
}
