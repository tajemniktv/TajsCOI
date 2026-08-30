// Taj's COI Mods | PerformanceFeatureTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Mafi.Collections;
using Mafi.Core.PathFinding;
using Mafi.Core.SaveGame;
using Mafi.Serialization;
using TajsCOI.Common.Compatibility;
using TajsCOI.Core.Runtime;
using TajsCOI.Performance.Features.LowProductTextures;
using TajsCOI.Performance.Features.PathabilityInitialization;
using TajsCOI.Performance.Features.ProductBufferShrink;
using TajsCOI.Performance.Features.RenderingLoadShedding;
using TajsCOI.Performance.Features.SaveLoadReadBuffer;
using TajsCOI.Performance.Features.StreamingSaveCompression;
using Xunit;
using DependencyResolver = Mafi.DependencyResolver;
using Tile2i = Mafi.Tile2i;

namespace TajsCOI.Tests
{
    public sealed class PerformanceFeatureTests : IDisposable
    {
        private readonly int m_saveLoadBufferKiB = SaveLoadReadBufferSettings.BufferBytes / 1024;
        private readonly bool m_skipChecksum = StreamingSaveCompressionSettings.SkipUncompressedChecksum;
        private readonly int m_mipBias = LowProductTexturesSettings.MipBias;
        private readonly int m_observationFrames = ProductBufferShrinkSettings.ObservationFrames;

        public void Dispose()
        {
            SaveLoadReadBufferSettings.Update(m_saveLoadBufferKiB);
            StreamingSaveCompressionSettings.Update(m_skipChecksum);
            LowProductTexturesSettings.Update(m_mipBias);
            ProductBufferShrinkSettings.Update(m_observationFrames);
        }

        [Fact]
        public void RenderingParticleNamesNormalizeSeparatorsAndCamelCase()
        {
            Assert.Equal(
                "heavy smoke cloud",
                ParticleNameMatcher.Normalize("HeavySmoke-Cloud"));
        }

        [Fact]
        public void DeferredPathabilityCandidateTargetsExact087bPrivateMethod()
        {
            PathabilityInitializationFeature.TargetSet? targets = PathabilityInitializationFeature.FindTargets();
            Assert.NotNull(targets);

            MethodInfo target = targets!.ComputeInitialBlocking;
            Assert.Equal("computeInitialBlocking", target.Name);
            Assert.Equal(typeof(void), target.ReturnType);
            Assert.False(target.IsStatic);
            Assert.Empty(target.GetParameters());

            Assert.Equal(
                new[] { typeof(int), typeof(DependencyResolver) },
                targets.InitSelf.GetParameters().Select(x => x.ParameterType));
            Assert.Equal(
                new[] { typeof(Tile2i), typeof(bool) },
                targets.ComputeAllPathability.GetParameters().Select(x => x.ParameterType));
            Assert.Equal(
                new[] { typeof(Tile2i), typeof(int) },
                targets.IsChunkBlocked.GetParameters().Select(x => x.ParameterType));
            Assert.Equal(
                new[]
                {
                    typeof(PfNodeInfo).MakeByRefType(),
                    typeof(int),
                    typeof(int),
                    typeof(bool),
                    typeof(Lyst<PfNodeInfo>),
                    typeof(ShipsPathFinderMode),
                    typeof(Tile2i?),
                },
                targets.GetValidNeighboursForTile.GetParameters().Select(x => x.ParameterType));
            Assert.Empty(targets.UpdateChangedTiles.GetParameters());
        }

        [Fact]
        public void RenderingParticleMatchingRequiresWholeTokens()
        {
            string normalized = ParticleNameMatcher.Normalize("industrial smoke cloud");

            Assert.True(ParticleNameMatcher.MatchesAnyToken(normalized, new[] { "smoke" }));
            Assert.True(ParticleNameMatcher.MatchesAnyToken(normalized, new[] { "cloud" }));
            Assert.False(ParticleNameMatcher.MatchesAnyToken("industrial smokestack", new[] { "smoke" }));
            Assert.False(ParticleNameMatcher.MatchesAnyToken("cloudy sky", new[] { "cloud" }));
        }

        [Fact]
        public void SaveLoadBufferTranspilerReplacesExactlyTheVanillaConstant()
        {
            SaveLoadReadBufferSettings.Update(64);
            ConstructorInfo bufferedReader = typeof(BlobReader).Assembly
                .GetType("Mafi.Serialization.BufferedReadStream")!
                .GetConstructor(new[] { typeof(Stream), typeof(int), typeof(bool) })!;
            var input = new List<CodeInstruction>
            {
                new(OpCodes.Ldc_I4, SaveLoadReadBufferSettings.VanillaBufferBytes), new(OpCodes.Ldc_I4_1), new(OpCodes.Newobj, bufferedReader),
            };

            List<CodeInstruction> output = SaveLoadReadBufferFeature.ReplaceBufferSize(input).ToList();

            Assert.Equal(3, output.Count);
            Assert.Equal(64 * 1024, output[0].operand);
            Assert.Equal(OpCodes.Ldc_I4_1, output[1].opcode);
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
        public void SaveLoadBufferTranspilerRejectsMissingOrDuplicateVanillaConstants()
        {
            Assert.Throws<InvalidOperationException>(() =>
                SaveLoadReadBufferFeature.ReplaceBufferSize(new[] { new CodeInstruction(OpCodes.Nop) }).ToList());
            ConstructorInfo bufferedReader = typeof(BlobReader).Assembly
                .GetType("Mafi.Serialization.BufferedReadStream")!
                .GetConstructor(new[] { typeof(Stream), typeof(int), typeof(bool) })!;
            Assert.Throws<InvalidOperationException>(() =>
                SaveLoadReadBufferFeature.ReplaceBufferSize(
                    new[]
                    {
                        new CodeInstruction(OpCodes.Ldc_I4, SaveLoadReadBufferSettings.VanillaBufferBytes),
                        new CodeInstruction(OpCodes.Ldc_I4_1),
                        new CodeInstruction(OpCodes.Newobj, bufferedReader),
                        new CodeInstruction(OpCodes.Ldc_I4, SaveLoadReadBufferSettings.VanillaBufferBytes),
                        new CodeInstruction(OpCodes.Ldc_I4_1),
                        new CodeInstruction(OpCodes.Newobj, bufferedReader),
                    }).ToList());
        }

        [Fact]
        public void SaveLoadBufferTranspilerRejectsChangedSemanticConstant()
        {
            ConstructorInfo bufferedReader = typeof(BlobReader).Assembly
                .GetType("Mafi.Serialization.BufferedReadStream")!
                .GetConstructor(new[] { typeof(Stream), typeof(int), typeof(bool) })!;

            Assert.Throws<InvalidOperationException>(() =>
                SaveLoadReadBufferFeature.ReplaceBufferSize(
                    new[]
                    {
                        new CodeInstruction(OpCodes.Ldc_I4, 8192),
                        new CodeInstruction(OpCodes.Ldc_I4_1),
                        new CodeInstruction(OpCodes.Newobj, bufferedReader),
                    }).ToList());
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
                feature.Install(runtime, runtime.GetLogger("TajsPerformance", "SaveLoadReadBuffer"));
                CompatibilityReport report = Assert.Single(runtime.GetCompatibilitySnapshot());
                Assert.Equal(CompatibilityState.Compatible, report.State);
                Assert.Equal("SaveLoadReadBuffer", report.ComponentId);
                ConstructorInfo target = typeof(BlobReader).GetConstructor(
                    new[] { typeof(Stream), typeof(int), typeof(Mafi.Collections.ImmutableCollections.ImmutableArray<ISpecialSerializerFactory>) })!;
                Assert.Single(
                    Harmony.GetPatchInfo(target)!.Transpilers,
                    x => x.owner == "TajsCOI.Performance.SaveLoadReadBuffer");
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
                out Mafi.Option<Exception> validationException);
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
                SaveLoadFileUtils.ValidateChecksum(output, out SaveHeader _, out Mafi.Option<Exception> _));
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
            Assert.Equal(0, output.Position);
            Assert.Equal(0, output.Length);
        }

        [Fact]
        public void StreamingSaveFailureFallsBackToVanillaUsingRetainedSnapshot()
        {
            byte[] payload = Enumerable.Range(0, 100_000).Select(x => (byte)(x * 13)).ToArray();
            using var input = new MemoryStream(payload);
            input.Position = 123;
            using var output = new MemoryStream();

            StreamingSaveResult? result = StreamingSaveCompressionFeature.WriteStreamingOrVanilla(
                input,
                output,
                0x1122334455667788,
                Mafi.SaveVersion.CURRENT_SAVE_VERSION,
                SaveCompressionType.Gzip,
                skipUncompressedChecksum: false,
                _ => throw new InvalidOperationException("Deliberate streaming compressor failure."),
                out Exception? streamingFailure);

            Assert.Null(result);
            Assert.IsType<InvalidOperationException>(streamingFailure);
            output.Position = 0;
            Assert.Equal(
                SaveChecksumValidationResults.Success,
                SaveLoadFileUtils.ValidateChecksum(output, out SaveHeader _, out Mafi.Option<Exception> _));

            output.Position = StreamingSaveWriter.HeaderSize;
            using var gzip = new GZipStream(output, CompressionMode.Decompress, leaveOpen: true);
            using var restored = new MemoryStream();
            gzip.CopyTo(restored);
            Assert.Equal(payload, restored.ToArray());
        }

        [Fact]
        public void StreamingSaveWriterRejectsNonAppendOutputWithoutMutation()
        {
            byte[] payload = Encoding.UTF8.GetBytes("serialized payload");
            byte[] existing = Encoding.UTF8.GetBytes("existing output tail");
            using var input = new MemoryStream(payload);
            input.Position = 4;
            using var output = new MemoryStream();
            output.Write(existing, 0, existing.Length);
            output.Position = 3;

            Assert.Throws<ArgumentException>(() =>
                StreamingSaveWriter.Write(input, output, 7, 328, 1, false));

            Assert.Equal(4, input.Position);
            Assert.Equal(3, output.Position);
            Assert.Equal(existing, output.ToArray());
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
