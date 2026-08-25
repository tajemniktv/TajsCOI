// Taj's COI Mods | RuntimeTraceExporter.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace TajsCOI.Profiler.Core
{
    internal readonly struct RuntimeTraceExportResult
    {
        internal RuntimeTraceExportResult(string path, int eventCount)
        {
            Path = path;
            EventCount = eventCount;
        }

        internal string Path { get; }
        internal int EventCount { get; }
    }

    /// <summary>
    ///     Writes a Chrome trace-event stream from value-only snapshots. Export is deliberately
    ///     command-driven; no file or JSON work occurs in the frame sampling path.
    /// </summary>
    internal static class RuntimeTraceExporter
    {
        private static readonly GameLoopTimingEvent[] s_timingEvents =
        {
            GameLoopTimingEvent.Input,
            GameLoopTimingEvent.SyncStart,
            GameLoopTimingEvent.Sync,
            GameLoopTimingEvent.SyncEnd,
            GameLoopTimingEvent.RenderAfterSync,
            GameLoopTimingEvent.Render,
            GameLoopTimingEvent.RenderEnd,
            GameLoopTimingEvent.WaitForSim,
            GameLoopTimingEvent.InputEnd,
            GameLoopTimingEvent.SimCmd,
            GameLoopTimingEvent.SimStart,
            GameLoopTimingEvent.SimUpdate,
            GameLoopTimingEvent.SimEnd,
            GameLoopTimingEvent.SimEndForUi,
            GameLoopTimingEvent.SimAfterSync,
            GameLoopTimingEvent.SimParallelStart,
            GameLoopTimingEvent.SimParallelEnd,
            GameLoopTimingEvent.SimReadState,
            GameLoopTimingEvent.SimPausedUi,
            GameLoopTimingEvent.SimCmdExtra,
        };

        internal static RuntimeTraceExportResult Export(
            string path,
            IReadOnlyList<RuntimeFrameSample> frames,
            IReadOnlyList<RuntimeTraceSpan> spans,
            IReadOnlyList<CallbackMetadataSnapshot> metadata,
            IReadOnlyList<RuntimeTraceMarker> markers)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A trace path is required.", nameof(path));
            }

            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            long baseTimestamp = FindBaseTimestamp(frames, spans, markers);
            var builder = new StringBuilder(8192);
            builder.Append("{\"traceEvents\":[");
            int eventCount = 0;
            bool first = true;

            AppendMetadata(ref first, builder, "process_name", "Captain of Industry", 0);
            eventCount++;
            AppendMetadata(ref first, builder, "thread_name", "GameLoop", 1);
            eventCount++;
            AppendMetadata(ref first, builder, "thread_name", "Simulation", 2);
            eventCount++;

            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                RuntimeFrameSample frame = frames[frameIndex];
                GameLoopTimingRanges ranges = frame.TimingRanges;
                for (int eventIndex = 0; eventIndex < s_timingEvents.Length; eventIndex++)
                {
                    GameLoopTimingEvent timingEvent = s_timingEvents[eventIndex];
                    GameLoopTimingRange range = ranges.Get(timingEvent);
                    if (!range.IsValid || range.DurationTicks <= 0)
                    {
                        continue;
                    }

                    AppendComplete(
                        ref first,
                        builder,
                        TimingName(timingEvent),
                        timingEvent >= GameLoopTimingEvent.SimCmd ? "coi.simulation" : "coi.mainloop",
                        range.StartTimestamp,
                        range.DurationTicks,
                        baseTimestamp,
                        timingEvent >= GameLoopTimingEvent.SimCmd ? 2 : 1,
                        frame.Sequence);
                    eventCount++;
                }

                AppendCounters(ref first, builder, frame, baseTimestamp);
                eventCount++;
            }

            for (int spanIndex = 0; spanIndex < spans.Count; spanIndex++)
            {
                RuntimeTraceSpan span = spans[spanIndex];
                if (span.DurationTicks <= 0)
                {
                    continue;
                }

                CallbackMetadataSnapshot callbackMetadata = FindCallbackMetadata(metadata, span.CallbackId);
                string callbackName = callbackMetadata.Id > 0
                    ? callbackMetadata.DisplayName
                    : "callback#" + span.CallbackId.ToString(CultureInfo.InvariantCulture);
                AppendComplete(
                    ref first,
                    builder,
                    callbackName,
                    "coi.callback",
                    span.StartTimestamp,
                    span.DurationTicks,
                    baseTimestamp,
                    span.ThreadId,
                    span.Sequence,
                    RuntimeTracePhase.Name(span.PhaseId),
                    callbackMetadata.AssemblyName);
                eventCount++;
            }

            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                RuntimeTraceMarker marker = markers[markerIndex];
                AppendInstant(ref first, builder, marker.Label, marker.Timestamp, baseTimestamp, marker.ThreadId);
                eventCount++;
            }

            builder.Append("],\"displayTimeUnit\":\"ms\"}");
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            return new RuntimeTraceExportResult(path, eventCount);
        }

        private static long FindBaseTimestamp(
            IReadOnlyList<RuntimeFrameSample> frames,
            IReadOnlyList<RuntimeTraceSpan> spans,
            IReadOnlyList<RuntimeTraceMarker> markers)
        {
            long result = long.MaxValue;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].CapturedTimestamp > 0)
                {
                    result = Math.Min(result, frames[i].CapturedTimestamp);
                }
                IncludeRangeStart(ref result, frames[i].TimingRanges);
            }
            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].StartTimestamp > 0)
                {
                    result = Math.Min(result, spans[i].StartTimestamp);
                }
            }
            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].Timestamp > 0)
                {
                    result = Math.Min(result, markers[i].Timestamp);
                }
            }
            return result == long.MaxValue ? Stopwatch.GetTimestamp() : result;
        }

        private static void IncludeRangeStart(ref long result, GameLoopTimingRanges ranges)
        {
            for (int i = 0; i < s_timingEvents.Length; i++)
            {
                long start = ranges.Get(s_timingEvents[i]).StartTimestamp;
                if (start > 0)
                {
                    result = Math.Min(result, start);
                }
            }
        }

        private static void AppendMetadata(ref bool first, StringBuilder builder, string name, string value, int tid)
        {
            BeginEvent(ref first, builder);
            builder.Append("\"name\":");
            AppendJsonString(builder, name);
            builder.Append(",\"ph\":\"M\",\"pid\":1,\"tid\":").Append(tid)
                .Append(",\"args\":{\"name\":");
            AppendJsonString(builder, value);
            builder.Append("}}");
        }

        private static void AppendComplete(
            ref bool first,
            StringBuilder builder,
            string name,
            string category,
            long startTimestamp,
            long durationTicks,
            long baseTimestamp,
            int threadId,
            long sequence,
            string? phase = null,
            string? assembly = null)
        {
            BeginEvent(ref first, builder);
            builder.Append("\"name\":");
            AppendJsonString(builder, name);
            builder.Append(",\"cat\":");
            AppendJsonString(builder, category);
            builder.Append(",\"ph\":\"X\",\"ts\":");
            AppendNumber(builder, ToMicroseconds(startTimestamp - baseTimestamp));
            builder.Append(",\"dur\":");
            AppendNumber(builder, ToMicroseconds(durationTicks));
            builder.Append(",\"pid\":1,\"tid\":").Append(threadId)
                .Append(",\"args\":{\"sequence\":").Append(sequence);
            if (!string.IsNullOrWhiteSpace(phase))
            {
                builder.Append(",\"phase\":");
                AppendJsonString(builder, phase);
            }
            if (!string.IsNullOrWhiteSpace(assembly))
            {
                builder.Append(",\"assembly\":");
                AppendJsonString(builder, assembly);
            }
            builder.Append("}}");
        }

        private static void AppendCounters(ref bool first, StringBuilder builder, RuntimeFrameSample frame, long baseTimestamp)
        {
            BeginEvent(ref first, builder);
            builder.Append("\"name\":\"runtime counters\",\"cat\":\"coi.counters\",\"ph\":\"C\",\"ts\":");
            AppendNumber(builder, ToMicroseconds(frame.CapturedTimestamp - baseTimestamp));
            builder.Append(",\"pid\":1,\"tid\":1,\"args\":{");
            bool firstArg = true;
            AppendCounter(builder, ref firstArg, "managedHeapBytes", frame.Counters.Available ? frame.Counters.ManagedHeapBytes : -1);
            AppendCounter(builder, ref firstArg, "unityAllocatedBytes", frame.Counters.Available ? frame.Counters.UnityAllocatedBytes : -1);
            AppendCounter(builder, ref firstArg, "unityReservedBytes", frame.Counters.Available ? frame.Counters.UnityReservedBytes : -1);
            AppendCounter(builder, ref firstArg, "unityGraphicsBytes", frame.Counters.Available ? frame.Counters.UnityGraphicsBytes : -1);
            AppendCounter(builder, ref firstArg, "gen0Delta", frame.Counters.Gen0Delta);
            AppendCounter(builder, ref firstArg, "gen1Delta", frame.Counters.Gen1Delta);
            AppendCounter(builder, ref firstArg, "gen2Delta", frame.Counters.Gen2Delta);
            AppendCounter(builder, ref firstArg, "dumpingCalls", frame.SubsystemCounters.DumpingCalls);
            AppendCounter(builder, ref firstArg, "dumpingElapsedTicks", frame.SubsystemCounters.DumpingElapsedTicks);
            AppendCounter(builder, ref firstArg, "pathEnqueues", frame.SubsystemCounters.PathEnqueues);
            AppendCounter(builder, ref firstArg, "pathSearchElapsedTicks", frame.SubsystemCounters.PathSearchElapsedTicks);
            builder.Append("}}");
        }

        private static void AppendCounter(StringBuilder builder, ref bool first, string name, long value)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            builder.Append('"').Append(name).Append("\":");
            if (value < 0)
            {
                AppendJsonString(builder, "unavailable");
            }
            else
            {
                builder.Append(value);
            }
        }

        private static void AppendInstant(ref bool first, StringBuilder builder, string name, long timestamp, long baseTimestamp, int threadId)
        {
            BeginEvent(ref first, builder);
            builder.Append("\"name\":");
            AppendJsonString(builder, name);
            builder.Append(",\"cat\":\"coi.marker\",\"ph\":\"i\",\"s\":\"t\",\"ts\":");
            AppendNumber(builder, ToMicroseconds(timestamp - baseTimestamp));
            builder.Append(",\"pid\":1,\"tid\":").Append(threadId).Append('}');
        }

        private static void BeginEvent(ref bool first, StringBuilder builder)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            builder.Append('{');
        }

        private static CallbackMetadataSnapshot FindCallbackMetadata(
            IReadOnlyList<CallbackMetadataSnapshot> metadata,
            int callbackId)
        {
            for (int i = 0; i < metadata.Count; i++)
            {
                if (metadata[i].Id == callbackId)
                {
                    return metadata[i];
                }
            }
            return default;
        }

        private static string TimingName(GameLoopTimingEvent timingEvent)
        {
            switch (timingEvent)
            {
                case GameLoopTimingEvent.Input: return "INPUT";
                case GameLoopTimingEvent.SyncStart: return "SYNC_START";
                case GameLoopTimingEvent.Sync: return "SYNC";
                case GameLoopTimingEvent.SyncEnd: return "SYNC_END";
                case GameLoopTimingEvent.RenderAfterSync: return "RENDER_AFTER_SYNC";
                case GameLoopTimingEvent.Render: return "RENDER";
                case GameLoopTimingEvent.RenderEnd: return "RENDER_END";
                case GameLoopTimingEvent.WaitForSim: return "WAIT_FOR_SIM";
                case GameLoopTimingEvent.InputEnd: return "INPUT_END";
                case GameLoopTimingEvent.SimCmd: return "SIM_CMD";
                case GameLoopTimingEvent.SimStart: return "SIM_START";
                case GameLoopTimingEvent.SimUpdate: return "SIM_UPDATE";
                case GameLoopTimingEvent.SimEnd: return "SIM_END";
                case GameLoopTimingEvent.SimEndForUi: return "SIM_END_FOR_UI";
                case GameLoopTimingEvent.SimAfterSync: return "SIM_AFTER_SYNC";
                case GameLoopTimingEvent.SimParallelStart: return "SIM_PARALLEL_START";
                case GameLoopTimingEvent.SimParallelEnd: return "SIM_PARALLEL_END";
                case GameLoopTimingEvent.SimReadState: return "SIM_READ_STATE";
                case GameLoopTimingEvent.SimPausedUi: return "SIM_PAUSED_UI";
                case GameLoopTimingEvent.SimCmdExtra: return "SIM_CMD_EXTRA";
                default: return "UNKNOWN";
            }
        }

        private static double ToMicroseconds(long ticks) =>
            ticks * 1000000.0 / Stopwatch.Frequency;

        private static void AppendNumber(StringBuilder builder, double value)
        {
            builder.Append(value.ToString("F3", CultureInfo.InvariantCulture));
        }

        private static void AppendJsonString(StringBuilder builder, string? value)
        {
            string text = value ?? string.Empty;
            builder.Append('"');
            if (text.Length > 0)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char character = text[i];
                    switch (character)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (character < 0x20)
                            {
                                builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(character);
                            }
                            break;
                    }
                }
            }
            builder.Append('"');
        }
    }
}
