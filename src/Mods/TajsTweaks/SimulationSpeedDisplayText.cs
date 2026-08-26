// Taj's COI Mods | SimulationSpeedDisplayText.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Tweaks
{
    internal static class SimulationSpeedDisplayText
    {
        internal static string Format(int speed) => Math.Max(0, speed) + "x";
    }
}
