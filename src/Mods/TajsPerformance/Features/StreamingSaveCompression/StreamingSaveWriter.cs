// Taj's COI Mods | StreamingSaveWriter.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TajsCOI.Performance.Features.StreamingSaveCompression
{
    internal static class StreamingSaveWriter
    {
        internal const int HeaderSize = 40;

        internal static StreamingSaveResult Write(
            Stream uncompressedInput,
            Stream output,
            ulong fileHeader,
            int saveVersion,
            int compressionType,
            bool skipUncompressedChecksum,
            Func<Stream, Stream>? createCompressor = null)
        {
            if (uncompressedInput is null)
            {
                throw new ArgumentNullException(nameof(uncompressedInput));
            }
            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            if (!uncompressedInput.CanRead || !uncompressedInput.CanSeek)
            {
                throw new ArgumentException("The uncompressed save snapshot must be readable and seekable.", nameof(uncompressedInput));
            }
            if (!output.CanWrite || !output.CanSeek)
            {
                throw new ArgumentException("Streaming save output must be writable and seekable.", nameof(output));
            }

            long originalPosition = uncompressedInput.Position;
            try
            {
                uncompressedInput.Position = 0;
                long uncompressedBytes = uncompressedInput.Length;
                uint uncompressedChecksum = 0;
                if (!skipUncompressedChecksum)
                {
                    uncompressedChecksum = Crc32Calculator.Compute(uncompressedInput, out long scannedBytes);
                    if (scannedBytes != uncompressedBytes)
                    {
                        throw new EndOfStreamException($"Expected {uncompressedBytes} uncompressed bytes, scanned {scannedBytes}.");
                    }
                    uncompressedInput.Position = 0;
                }

                long headerPosition = output.Position;
                WriteHeader(output, fileHeader, saveVersion, compressionType, 0, 0, uncompressedBytes, uncompressedChecksum);

                var crcOutput = new Crc32ForwardingWriteStream(output);
                using (Stream gzip = createCompressor is null
                    ? new GZipStream(crcOutput, CompressionLevel.Optimal, leaveOpen: true)
                    : createCompressor(crcOutput))
                {
                    uncompressedInput.CopyTo(gzip, 64 * 1024);
                }

                long endPosition = output.Position;
                output.Position = headerPosition;
                WriteHeader(
                    output,
                    fileHeader,
                    saveVersion,
                    compressionType,
                    crcOutput.BytesWritten,
                    crcOutput.Checksum,
                    uncompressedBytes,
                    uncompressedChecksum);
                output.Position = endPosition;

                return new StreamingSaveResult(
                    crcOutput.BytesWritten,
                    crcOutput.Checksum,
                    uncompressedBytes,
                    uncompressedChecksum);
            }
            finally
            {
                uncompressedInput.Position = originalPosition;
            }
        }

        private static void WriteHeader(
            Stream output,
            ulong fileHeader,
            int saveVersion,
            int compressionType,
            long compressedBytes,
            uint compressedChecksum,
            long uncompressedBytes,
            uint uncompressedChecksum)
        {
            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            writer.Write(fileHeader);
            writer.Write(saveVersion);
            writer.Write(compressionType);
            writer.Write(compressedBytes);
            writer.Write(compressedChecksum);
            writer.Write(uncompressedBytes);
            writer.Write(uncompressedChecksum);
        }
    }

    internal readonly struct StreamingSaveResult
    {
        internal StreamingSaveResult(long compressedBytes, uint compressedChecksum, long uncompressedBytes, uint uncompressedChecksum)
        {
            CompressedBytes = compressedBytes;
            CompressedChecksum = compressedChecksum;
            UncompressedBytes = uncompressedBytes;
            UncompressedChecksum = uncompressedChecksum;
        }

        internal long CompressedBytes { get; }
        internal uint CompressedChecksum { get; }
        internal long UncompressedBytes { get; }
        internal uint UncompressedChecksum { get; }
    }
}
