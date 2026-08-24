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
        internal const int MinMipBias = 3;
        internal const int MaxMipBias = 4;

        private static int s_mipBias = MinMipBias;

        internal static int MipBias => Volatile.Read(ref s_mipBias);

        internal static void Update(int mipBias)
        {
            int clamped = mipBias < MinMipBias ? MinMipBias : mipBias > MaxMipBias ? MaxMipBias : mipBias;
            Volatile.Write(ref s_mipBias, clamped);
        }
    }
}
