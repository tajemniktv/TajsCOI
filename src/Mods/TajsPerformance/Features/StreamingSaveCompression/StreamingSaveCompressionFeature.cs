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
        private const int CurrentSaveVersion = 328;

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
            if (target is null || s_writerField is null || s_compressionField is null || s_durationSetter is null || s_mainHeaderField is null)
            {
                throw new MissingMemberException("The 0.8.7a GameSaver streaming-save contract was not found.");
            }

            s_log = log;
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

            var writerOption = (Option<MemoryBlobWriter>)s_writerField!.GetValue(__instance);
            SaveCompressionType compression = (SaveCompressionType)s_compressionField!.GetValue(__instance);
            MemoryBlobWriter? writer = writerOption.ValueOrNull;
            if (writer is null || compression != SaveCompressionType.Gzip)
            {
                return true;
            }

            Stopwatch timer = Stopwatch.StartNew();
            ulong mainHeader = (ulong)s_mainHeaderField!.GetValue(null);
            StreamingSaveResult result = StreamingSaveWriter.Write(
                writer.FinalizeAndReturnStream(),
                outputStream,
                mainHeader,
                CurrentSaveVersion,
                (int)compression,
                StreamingSaveCompressionSettings.SkipUncompressedChecksum);
            timer.Stop();
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
