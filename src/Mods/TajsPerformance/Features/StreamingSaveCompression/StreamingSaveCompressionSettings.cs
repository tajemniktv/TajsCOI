// Taj's COI Mods | StreamingSaveCompressionSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsCOI.Performance.Features.StreamingSaveCompression
{
    internal static class StreamingSaveCompressionSettings
    {
        internal const string EnableConfigKey = "enable_streaming_save_compression";
        internal const string SkipUncompressedChecksumConfigKey = "skip_uncompressed_save_checksum";

        internal static bool SkipUncompressedChecksum { get; private set; }

        internal static void Update(bool skipUncompressedChecksum) => SkipUncompressedChecksum = skipUncompressedChecksum;
    }
}
