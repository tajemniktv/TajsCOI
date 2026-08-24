// Taj's COI Mods | StreamingSaveCompressionFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.SaveGame;
using Mafi.Serialization;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance.Features.StreamingSaveCompression
{
    internal sealed class StreamingSaveCompressionFeature : IPerformanceFeature
    {
        private const string HarmonyId = "TajsCOI.Performance.StreamingSaveCompression";

        private static FieldInfo? s_writerField;
        private static FieldInfo? s_compressionField;
        private static MethodInfo? s_durationSetter;
        private static FieldInfo? s_mainHeaderField;
        private static ITajsLogger? s_log;

        public string Id => "StreamingSaveCompression";
        public string ConfigKey => StreamingSaveCompressionSettings.EnableConfigKey;

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            Type? gameSaver = typeof(SaveLoadFileUtils).Assembly.GetType("Mafi.Core.SaveGame.GameSaver", false);
            MethodInfo? target = gameSaver?.GetMethod(
                "FinishSaveWriteToStream",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Stream) },
                null);
            s_writerField = gameSaver?.GetField("m_mainWriter", BindingFlags.Instance | BindingFlags.NonPublic);
            s_compressionField = gameSaver?.GetField("m_compressionType", BindingFlags.Instance | BindingFlags.NonPublic);
            s_durationSetter = gameSaver?.GetProperty("LastSaveFinalizeDuration", BindingFlags.Instance | BindingFlags.Public)
                ?.GetSetMethod(true);
            Type? saveHeaders = typeof(SaveLoadFileUtils).Assembly.GetType("Mafi.Core.SaveGame.SaveHeaders", false);
            s_mainHeaderField = saveHeaders?.GetField("HEADER_MAIN_LE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target is null || s_writerField?.FieldType != typeof(Option<MemoryBlobWriter>) ||
                s_compressionField?.FieldType != typeof(SaveCompressionType) ||
                s_durationSetter is null || s_durationSetter.IsStatic || s_durationSetter.ReturnType != typeof(void) ||
                s_durationSetter.GetParameters().Length != 1 ||
                s_durationSetter.GetParameters()[0].ParameterType != typeof(TimeSpan) ||
                s_mainHeaderField is null || !s_mainHeaderField.IsStatic || s_mainHeaderField.FieldType != typeof(ulong))
            {
                throw new MissingMemberException("The 0.8.7a GameSaver streaming-save contract was not found.");
            }

            s_log = log;
            // Target: GameSaver.FinishSaveWriteToStream(Stream). This behavior-changing prefix replaces
            // only gzip finalization, returns true on any recoverable failure so vanilla runs, and is
            // owned for the process lifetime by this feature-specific Harmony ID.
            new Harmony(HarmonyId).Patch(
                target,
                prefix: new HarmonyMethod(typeof(StreamingSaveCompressionFeature), nameof(WriteStreaming)));

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                Id,
                CompatibilityState.Compatible,
                "0.8.7a GameSaver snapshot/compression fields and seekable temporary-file output",
                "Streaming gzip prefix installed",
                StreamingSaveCompressionSettings.SkipUncompressedChecksum
                    ? "Compressed CRC and post-write validation remain active; uncompressed CRC is explicitly disabled."
                    : "Both compressed and uncompressed CRCs plus post-write validation remain active."));
        }

        private static bool WriteStreaming(object __instance, Stream outputStream)
        {
            if (!outputStream.CanSeek || !outputStream.CanWrite)
            {
                return true;
            }

            if (s_writerField?.GetValue(__instance) is not Option<MemoryBlobWriter> writerOption ||
                s_compressionField?.GetValue(__instance) is not SaveCompressionType compression ||
                s_mainHeaderField?.GetValue(null) is not ulong mainHeader)
            {
                s_log?.ErrorOnce("Streaming save fields no longer match the validated 0.8.7a contract; vanilla finalization will run.");
                return true;
            }
            MemoryBlobWriter? writer = writerOption.ValueOrNull;
            if (writer is null || compression != SaveCompressionType.Gzip)
            {
                return true;
            }

            long outputStartPosition = outputStream.Position;
            Stopwatch timer = Stopwatch.StartNew();
            StreamingSaveResult result;
            try
            {
                ISaveCompressor compressor = SaveLoadFileUtils.GetCompressorOrThrow(compression).Value;
                result = StreamingSaveWriter.Write(
                    writer.FinalizeAndReturnStream(),
                    outputStream,
                    mainHeader,
                    SaveVersion.CURRENT_SAVE_VERSION,
                    (int)compression,
                    StreamingSaveCompressionSettings.SkipUncompressedChecksum,
                    compressor.CreateCompressingStream);
                timer.Stop();
            }
            catch (Exception exception)
            {
                try
                {
                    outputStream.SetLength(outputStartPosition);
                    outputStream.Position = outputStartPosition;
                }
                catch (Exception rollbackException)
                {
                    throw new IOException(
                        "Streaming save failed and the partial output could not be rolled back; vanilla finalization was not attempted.",
                        new AggregateException(exception, rollbackException));
                }
                s_log?.Exception(exception, "Streaming save failed after a clean rollback; vanilla finalization will run.");
                return true;
            }

            try
            {
                s_durationSetter!.Invoke(__instance, new object[] { timer.Elapsed });
                s_log?.Info(
                    $"Streamed save payload {result.UncompressedBytes} -> {result.CompressedBytes} bytes in {timer.Elapsed.TotalMilliseconds:F1} ms.");
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Streaming save completed, but finalize-duration diagnostics could not be updated.");
            }
            return false;
        }
    }
}
