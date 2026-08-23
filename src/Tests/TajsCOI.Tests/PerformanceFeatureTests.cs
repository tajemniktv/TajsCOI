// Taj's COI Mods | PerformanceFeatureTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.IO;
using System.IO.Compression;
using System.Text;
using HarmonyLib;
using Mafi.Core.SaveGame;
using TajsCOI.Performance.Features.SaveLoadReadBuffer;
using TajsCOI.Performance.Features.StreamingSaveCompression;
using TajsCOI.Performance.Features.LowProductTextures;
using TajsCOI.Core.Runtime;
using TajsCOI.Common.Compatibility;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class PerformanceFeatureTests
    {
        [Fact]
        public void SaveLoadBufferTranspilerReplacesExactlyTheVanillaConstant()
        {
            SaveLoadReadBufferSettings.Update(64);
            var input = new List<CodeInstruction>
            {
                new(OpCodes.Nop),
                new(OpCodes.Ldc_I4, SaveLoadReadBufferSettings.VanillaBufferBytes),
                new(OpCodes.Ldc_I4, 123),
            };

            List<CodeInstruction> output = SaveLoadReadBufferFeature.ReplaceBufferSize(input).ToList();

            Assert.Equal(3, output.Count);
            Assert.Equal(64 * 1024, output[1].operand);
            Assert.Equal(123, output[2].operand);
        }

        [Fact]
        public void SaveLoadBufferSettingClampsToSupportedRange()
        {
            SaveLoadReadBufferSettings.Update(1);
            Assert.Equal(16 * 1024, SaveLoadReadBufferSettings.BufferBytes);

            SaveLoadReadBufferSettings.Update(999);
            Assert.Equal(256 * 1024, SaveLoadReadBufferSettings.BufferBytes);
        }

        [Fact]
        public void SaveLoadBufferFeaturePatchesConfigured087aConstructor()
        {
            SaveLoadReadBufferSettings.Update(64);
            var runtime = new TajsRuntime();
            var feature = new SaveLoadReadBufferFeature();

            try
            {
                feature.Install(runtime, runtime.GetLogger("TajsPerformance", "SaveLoadReadBuffer"));
                CompatibilityReport report = Assert.Single(runtime.GetCompatibilitySnapshot());
                Assert.Equal(CompatibilityState.Compatible, report.State);
                Assert.Equal("SaveLoadReadBuffer", report.ComponentId);
            }
            finally
            {
                new Harmony("TajsCOI.Performance.SaveLoadReadBuffer")
                    .UnpatchAll("TajsCOI.Performance.SaveLoadReadBuffer");
            }
        }

        [Fact]
        public void Crc32MatchesStandardKnownVector()
        {
            byte[] bytes = Encoding.ASCII.GetBytes("123456789");
            using var stream = new MemoryStream(bytes);

            uint crc = Crc32Calculator.Compute(stream, out long count);

            Assert.Equal(9, count);
            Assert.Equal(0xcbf43926u, crc);
        }

        [Fact]
        public void StreamingSaveWriterProducesRoundTrippableHeaderAndPayload()
        {
            byte[] payload = Enumerable.Range(0, 200_000).Select(x => (byte)(x * 31)).ToArray();
            using var input = new MemoryStream(payload);
            using var output = new MemoryStream();

            StreamingSaveResult result = StreamingSaveWriter.Write(input, output, 0x1122334455667788, 328, 1, false);

            output.Position = 0;
            SaveChecksumValidationResults validation = SaveLoadFileUtils.ValidateChecksum(
                output,
                out SaveHeader _,
                out Mafi.Option<System.Exception> validationException);
            Assert.Equal(SaveChecksumValidationResults.Success, validation);
            Assert.False(validationException.HasValue);

            output.Position = 0;
            using var reader = new BinaryReader(output, Encoding.UTF8, leaveOpen: true);
            Assert.Equal(0x1122334455667788ul, reader.ReadUInt64());
            Assert.Equal(328, reader.ReadInt32());
            Assert.Equal(1, reader.ReadInt32());
            Assert.Equal(result.CompressedBytes, reader.ReadInt64());
            Assert.Equal(result.CompressedChecksum, reader.ReadUInt32());
            Assert.Equal(payload.LongLength, reader.ReadInt64());
            Assert.Equal(result.UncompressedChecksum, reader.ReadUInt32());

            using var gzip = new GZipStream(output, CompressionMode.Decompress, leaveOpen: true);
            using var restored = new MemoryStream();
            gzip.CopyTo(restored);
            Assert.Equal(payload, restored.ToArray());
        }

        [Fact]
        public void StreamingSaveWriterCanSkipOnlyTheUncompressedChecksum()
        {
            byte[] payload = Encoding.UTF8.GetBytes("The compressed checksum must remain active.");
            using var input = new MemoryStream(payload);
            input.Position = 7;
            using var output = new MemoryStream();

            StreamingSaveResult result = StreamingSaveWriter.Write(input, output, 7, 328, 1, true);

            Assert.Equal(7, input.Position);
            Assert.Equal(0u, result.UncompressedChecksum);
            Assert.NotEqual(0u, result.CompressedChecksum);
            output.Position = 0;
            Assert.Equal(
                SaveChecksumValidationResults.Success,
                SaveLoadFileUtils.ValidateChecksum(output, out SaveHeader _, out Mafi.Option<System.Exception> _));
        }

        [Fact]
        public void StreamingSaveWriterRestoresInputPositionAfterOutputFailure()
        {
            byte[] payload = Enumerable.Range(0, 100_000).Select(x => (byte)x).ToArray();
            using var input = new MemoryStream(payload);
            input.Position = 123;
            using var output = new ThrowingWriteStream(StreamingSaveWriter.HeaderSize + 100);

            Assert.Throws<IOException>(() =>
                StreamingSaveWriter.Write(input, output, 7, 328, 1, false));
            Assert.Equal(123, input.Position);
        }

        [Fact]
        public void StreamingSaveWriterUsesProvidedGameCompressorFactory()
        {
            bool invoked = false;
            byte[] payload = Encoding.UTF8.GetBytes("factory-backed gzip");
            using var input = new MemoryStream(payload);
            using var output = new MemoryStream();

            StreamingSaveWriter.Write(
                input,
                output,
                7,
                328,
                1,
                false,
                stream =>
                {
                    invoked = true;
                    return new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
                });

            Assert.True(invoked);
            output.Position = StreamingSaveWriter.HeaderSize;
            using var gzip = new GZipStream(output, CompressionMode.Decompress, leaveOpen: true);
            using var restored = new MemoryStream();
            gzip.CopyTo(restored);
            Assert.Equal(payload, restored.ToArray());
        }

        [Fact]
        public void StreamingSaveWriterChecksumDetectsCompressedCorruption()
        {
            byte[] payload = Enumerable.Range(0, 50_000).Select(x => (byte)(x * 17)).ToArray();
            using var input = new MemoryStream(payload);
            using var output = new MemoryStream();
            StreamingSaveResult result = StreamingSaveWriter.Write(input, output, 7, 328, 1, false);
            byte[] file = output.ToArray();

            using var compressed = new MemoryStream(file, StreamingSaveWriter.HeaderSize, (int)result.CompressedBytes, writable: false);
            uint valid = Crc32Calculator.Compute(compressed, out long count);
            Assert.Equal(result.CompressedBytes, count);
            Assert.Equal(result.CompressedChecksum, valid);

            file[file.Length - 1] ^= 0x40;
            using var corrupted = new MemoryStream(file, StreamingSaveWriter.HeaderSize, (int)result.CompressedBytes, writable: false);
            uint invalid = Crc32Calculator.Compute(corrupted, out _);
            Assert.NotEqual(result.CompressedChecksum, invalid);
        }

        [Fact]
        public void StreamingSaveFeatureResolvesConfigured087aContract()
        {
            StreamingSaveCompressionSettings.Update(false);
            var runtime = new TajsRuntime();
            var feature = new StreamingSaveCompressionFeature();

            try
            {
                feature.Install(runtime, runtime.GetLogger("TajsPerformance", "StreamingSaveCompression"));
                CompatibilityReport report = Assert.Single(runtime.GetCompatibilitySnapshot());
                Assert.Equal(CompatibilityState.Compatible, report.State);
                Assert.Equal("StreamingSaveCompression", report.ComponentId);
            }
            finally
            {
                new Harmony("TajsCOI.Performance.StreamingSaveCompression")
                    .UnpatchAll("TajsCOI.Performance.StreamingSaveCompression");
            }
        }

        [Fact]
        public void LowProductTextureSettingExposesOnlyBiasThreeOrFour()
        {
            LowProductTexturesSettings.Update(2);
            Assert.Equal(3, LowProductTexturesSettings.MipBias);
            LowProductTexturesSettings.Update(4);
            Assert.Equal(4, LowProductTexturesSettings.MipBias);
            LowProductTexturesSettings.Update(9);
            Assert.Equal(4, LowProductTexturesSettings.MipBias);
        }

        private sealed class ThrowingWriteStream : MemoryStream
        {
            private readonly long m_limit;

            internal ThrowingWriteStream(long limit)
            {
                m_limit = limit;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Position + count > m_limit)
                {
                    throw new IOException("Deliberate write failure.");
                }
                base.Write(buffer, offset, count);
            }
        }
    }
}
