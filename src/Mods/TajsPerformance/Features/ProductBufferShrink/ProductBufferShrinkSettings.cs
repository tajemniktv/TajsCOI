// Taj's COI Mods | ProductBufferShrinkSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Performance.Features.ProductBufferShrink
{
    internal static class ProductBufferShrinkSettings
    {
        internal const string EnableConfigKey = "enable_product_buffer_shrink";
        internal const string ObservationFramesConfigKey = "product_buffer_shrink_observation_frames";

        internal static int ObservationFrames { get; private set; } = 600;

        internal static void Update(int observationFrames) =>
            ObservationFrames = Math.Max(120, Math.Min(3600, observationFrames));
    }
}
