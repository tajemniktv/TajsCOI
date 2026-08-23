// Taj's COI Mods | ManualAssetTrimSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsCOI.Performance.Features.ManualAssetTrim
{
    internal static class ManualAssetTrimSettings
    {
        internal const string EnableConfigKey = "enable_manual_asset_trim";

        internal static bool Enabled { get; private set; }

        internal static void Update(bool enabled) => Enabled = enabled;
    }
}
