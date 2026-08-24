// Taj's COI Mods | ManualAssetTrimSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Threading;

namespace TajsCOI.Performance.Features.ManualAssetTrim
{
    internal static class ManualAssetTrimSettings
    {
        internal const string EnableConfigKey = "enable_manual_asset_trim";

        private static int s_enabled;

        internal static bool Enabled => Volatile.Read(ref s_enabled) != 0;

        internal static void Update(bool enabled) => Volatile.Write(ref s_enabled, enabled ? 1 : 0);
    }
}
