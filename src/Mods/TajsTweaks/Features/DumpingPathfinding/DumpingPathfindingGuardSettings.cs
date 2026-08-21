// Taj's COI Mods | DumpingPathfindingGuardSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsTweaks.Features.DumpingPathfinding;

internal static class DumpingPathfindingGuardSettings
{
    internal const string PerTruckConfigKey = "dumping_pf_guard_searches_per_truck_per_tick";
    internal const string TotalConfigKey = "dumping_pf_guard_searches_total_per_tick";

    internal const int DefaultPerTruckLimit = 4;
    internal const int DefaultTotalLimit = 64;
    internal const int MaxConfigurableLimit = 512;

    internal static int SearchesPerTruckPerTick { get; private set; } = DefaultPerTruckLimit;
    internal static int TotalSearchesPerTick { get; private set; } = DefaultTotalLimit;

    internal static void UpdatePerTruckLimit(int configuredValue)
    {
        SearchesPerTruckPerTick = clamp(configuredValue);
    }

    internal static void UpdateTotalLimit(int configuredValue)
    {
        TotalSearchesPerTick = clamp(configuredValue);
    }

    private static int clamp(int configuredValue)
    {
        if (configuredValue <= 0)
            return 0;

        if (configuredValue > MaxConfigurableLimit)
            return MaxConfigurableLimit;

        return configuredValue;
    }
}
