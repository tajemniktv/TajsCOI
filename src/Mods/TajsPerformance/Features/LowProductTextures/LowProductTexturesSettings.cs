// Taj's COI Mods | LowProductTexturesSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Threading;

namespace TajsCOI.Performance.Features.LowProductTextures
{
    internal static class LowProductTexturesSettings
    {
        internal const string EnableConfigKey = "enable_low_product_textures";
        internal const string MipBiasConfigKey = "product_texture_mip_bias";

        private static int s_mipBias = 3;

        internal static int MipBias => Volatile.Read(ref s_mipBias);

        internal static void Update(int mipBias) =>
            Volatile.Write(ref s_mipBias, mipBias < 3 ? 3 : mipBias > 4 ? 4 : mipBias);
    }
}
