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

        public bool IsProcessPatchInstalled()
        {
            MethodInfo? target = FindTarget();
            MethodInfo? patchMethod = AccessTools.Method(typeof(StreamingSaveCompressionFeature), nameof(WriteStreaming));
            return target is not null && patchMethod is not null &&
                   ProcessHarmonyPatchOwnership.HasExpected(
                       Harmony.GetPatchInfo(target)?.Prefixes,
                       HarmonyId,
                       patchMethod);
        }

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            Type? gameSaver = typeof(SaveLoadFileUtils).Assembly.GetType("Mafi.Core.SaveGame.GameSaver", false);
            MethodInfo? target = FindTarget();
            // Harmony callbacks are static. Installation validates and publishes these fixed 0.8.7a
            // bindings once for the process-lifetime patch owner.
#pragma warning disable S2696
            s_writerField = gameSaver?.GetField("m_mainWriter", BindingFlags.Instance | BindingFlags.NonPublic);
            s_compressionField = gameSaver?.GetField("m_compressionType", BindingFlags.Instance | BindingFlags.NonPublic);
            s_durationSetter = gameSaver?.GetProperty("LastSaveFinalizeDuration", BindingFlags.Instance | BindingFlags.Public)
                ?.GetSetMethod(true);
            Type? saveHeaders = typeof(SaveLoadFileUtils).Assembly.GetType("Mafi.Core.SaveGame.SaveHeaders", false);
            s_mainHeaderField = saveHeaders?.GetField("HEADER_MAIN_LE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
#pragma warning restore S2696
            if (target is null || s_writerField?.FieldType != typeof(Option<MemoryBlobWriter>) ||
                s_compressionField?.FieldType != typeof(SaveCompressionType) ||
                s_durationSetter is null || s_durationSetter.IsStatic || s_durationSetter.ReturnType != typeof(void) ||
                s_durationSetter.GetParameters().Length != 1 ||
                s_durationSetter.GetParameters()[0].ParameterType != typeof(TimeSpan) ||
                s_mainHeaderField is null || !s_mainHeaderField.IsStatic || s_mainHeaderField.FieldType != typeof(ulong))
            {
                throw new MissingMemberException("The 0.8.7a GameSaver streaming-save contract was not found.");
            }

#pragma warning disable S2696
            s_log = log;
#pragma warning restore S2696
            // Target: GameSaver.FinishSaveWriteToStream(Stream). This behavior-changing prefix replaces
            // only gzip finalization. Before writer consumption it can return to vanilla; afterward it
            // invokes vanilla SaveDataWithHeaders directly on the retained stream if streaming fails.
            MethodInfo patchMethod = AccessTools.Method(typeof(StreamingSaveCompressionFeature), nameof(WriteStreaming))!;
            lock (s_installGate)
            {
                Patches? patches = Harmony.GetPatchInfo(target);
                if (ProcessHarmonyPatchOwnership.HasExpected(patches?.Prefixes, HarmonyId, patchMethod))
                {
                    log.Info("Already installed / compatible; the process-lifetime streaming-save patch was not applied again.");
                    runtime.ReportCompatibility(
                        new CompatibilityReport(
                            "TajsPerformance",
                            Id,
                            CompatibilityState.Compatible,
                            "Existing process-lifetime Harmony owner and prefix method",
                            "Already installed / compatible",
                            "The validated 0.8.7a streaming-save patch remains active; no duplicate prefix was registered."));
                    return;
                }

                if (ProcessHarmonyPatchOwnership.HasOwner(patches?.Prefixes, HarmonyId))
                {
                    throw new InvalidOperationException(
                        $"Existing Harmony owner '{HarmonyId}' has an unexpected streaming-save prefix ({ProcessHarmonyPatchOwnership.Describe(patches)}).");
                }

                var harmony = new Harmony(HarmonyId);
                try
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(patchMethod));
                }
                catch
                {
                    harmony.Unpatch(target, HarmonyPatchType.Prefix, HarmonyId);
                    throw;
                }
            }

            runtime.ReportCompatibility(
                new CompatibilityReport(
                    "TajsPerformance",
                    Id,
                    CompatibilityState.Compatible,
                    "0.8.7a GameSaver snapshot/compression fields and seekable temporary-file output",
                    "Streaming gzip prefix installed",
                    StreamingSaveCompressionSettings.SkipUncompressedChecksum
                        ? "Compressed CRC and post-write validation remain active; uncompressed CRC is explicitly disabled."
                        : "Both compressed and uncompressed CRCs plus post-write validation remain active."));
        }

        private static readonly object s_installGate = new();

        private static MethodInfo? FindTarget()
        {
            Type? gameSaver = typeof(SaveLoadFileUtils).Assembly.GetType("Mafi.Core.SaveGame.GameSaver", false);
            return gameSaver?.GetMethod(
                "FinishSaveWriteToStream",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(Stream) },
                null);
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

            long outputStartPosition;
            try
            {
                outputStartPosition = outputStream.Position;
                if (outputStartPosition != outputStream.Length)
                {
                    s_log?.WarningOnce("Streaming save requires append-positioned output; vanilla finalization will run.");
                    return true;
                }
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Streaming save could not validate the output position; vanilla finalization will run.");
                return true;
            }

            ISaveCompressor compressor;
            try
            {
                compressor = SaveLoadFileUtils.GetCompressorOrThrow(compression).Value;
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Streaming save could not resolve the vanilla compressor; vanilla finalization will run.");
                return true;
            }

            Stream uncompressedInput;
            try
            {
                // This consumes m_mainWriter. No path below this point may return true and let
                // GameSaver call FinalizeAndReturnStream() on the same writer a second time.
                uncompressedInput = writer.FinalizeAndReturnStream();
            }
            catch (Exception exception)
            {
                throw new IOException("Streaming save could not finalize the serialized snapshot.", exception);
            }

            var timer = Stopwatch.StartNew();
            StreamingSaveResult? result = WriteStreamingOrVanilla(
                uncompressedInput,
                outputStream,
                mainHeader,
                SaveVersion.CURRENT_SAVE_VERSION,
                compression,
                StreamingSaveCompressionSettings.SkipUncompressedChecksum,
                compressor.CreateCompressingStream,
                out Exception? streamingFailure);
            timer.Stop();

            try
            {
                s_durationSetter!.Invoke(__instance, new object[] { timer.Elapsed });
                if (result is StreamingSaveResult streamed)
                {
                    s_log?.Info(
                        $"Streamed save payload {streamed.UncompressedBytes} -> {streamed.CompressedBytes} bytes in {timer.Elapsed.TotalMilliseconds:F1} ms.");
                }
                else
                {
                    if (streamingFailure is Exception failure)
                    {
                        s_log?.Exception(
                            failure,
                            $"Streaming save failed cleanly; vanilla SaveDataWithHeaders completed from the retained snapshot in {timer.Elapsed.TotalMilliseconds:F1} ms.");
                    }
                }
            }
            catch (Exception exception)
            {
                s_log?.Exception(exception, "Streaming save completed, but finalize-duration diagnostics could not be updated.");
            }
            return false;
        }

        // This mirrors the fixed vanilla save-header contract and additionally accepts the streaming
        // compressor seam plus a failure result; grouping those values would obscure this boundary.
#pragma warning disable S107
        internal static StreamingSaveResult? WriteStreamingOrVanilla(
            Stream uncompressedInput,
            Stream outputStream,
            ulong mainHeader,
            int saveVersion,
            SaveCompressionType compression,
            bool skipUncompressedChecksum,
            Func<Stream, Stream> createCompressor,
            out Exception? streamingFailure)
        {
            long outputStartPosition = outputStream.Position;
            streamingFailure = null;
            Exception streamingException;
            try
            {
                return StreamingSaveWriter.Write(
                    uncompressedInput,
                    outputStream,
                    mainHeader,
                    saveVersion,
                    (int)compression,
                    skipUncompressedChecksum,
                    createCompressor);
            }
            catch (Exception exception)
            {
                streamingException = exception;
                streamingFailure = streamingException;
                RollbackOutputOrThrow(outputStream, outputStartPosition, exception);
            }

            try
            {
                uncompressedInput.Position = 0;
                SaveLoadFileUtils.SaveDataWithHeaders(
                    mainHeader,
                    saveVersion,
                    uncompressedInput,
                    outputStream,
                    compression);
                return null;
            }
            catch (Exception fallbackException)
            {
                RollbackOutputOrThrow(
                    outputStream,
                    outputStartPosition,
                    new AggregateException(streamingException, fallbackException));
                throw new IOException(
                    "Both streaming and vanilla save finalization failed after consuming the serialized snapshot.",
                    new AggregateException(streamingException, fallbackException));
            }
        }
#pragma warning restore S107

        private static void RollbackOutputOrThrow(Stream output, long position, Exception originalFailure)
        {
            try
            {
                output.SetLength(position);
                output.Position = position;
            }
            catch (Exception rollbackException)
            {
                throw new IOException(
                    "Save finalization failed and the partial output could not be rolled back.",
                    new AggregateException(originalFailure, rollbackException));
            }
        }
    }
}
