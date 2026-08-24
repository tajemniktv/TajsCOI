// Taj's COI Mods | SaveLoadReadBufferSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Threading;

namespace TajsCOI.Performance.Features.SaveLoadReadBuffer
{
    internal static class SaveLoadReadBufferSettings
    {
        internal const string EnableConfigKey = "enable_large_save_load_buffer";
        internal const string SizeConfigKey = "save_load_buffer_kib";
        internal const int VanillaBufferBytes = 4 * 1024;
        internal const int DefaultBufferKiB = 64;

        private static int s_bufferBytes = DefaultBufferKiB * 1024;

        internal static int BufferBytes => Volatile.Read(ref s_bufferBytes);

        internal static void Update(int kibibytes)
        {
            int clamped = kibibytes < 16 ? 16 : kibibytes > 256 ? 256 : kibibytes;
            Volatile.Write(ref s_bufferBytes, clamped * 1024);
        }
    }
}
