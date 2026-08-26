// Taj's COI Mods | RuntimeTraceExporter.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TajsCOI.Common.Diagnostics;

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
            IReadOnlyList<RuntimeTraceMarker> markers,
            IReadOnlyList<RuntimeTelemetryEventSnapshot>? telemetryEvents = null,
            HarmonyInspectionSnapshot? harmony = null,
            IReadOnlyList<RuntimeCapabilityDescriptor>? capabilities = null,
            IReadOnlyList<RuntimeComponentDescriptor>? components = null,
            IReadOnlyList<LoadedModSnapshot>? loadedMods = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A trace path is required.", nameof(path));
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            long baseTimestamp = FindBaseTimestamp(frames, spans, markers, telemetryEvents);
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

            if (telemetryEvents is not null)
            {
                for (int eventIndex = 0; eventIndex < telemetryEvents.Count; eventIndex++)
                {
                    RuntimeTelemetryEventSnapshot telemetryEvent = telemetryEvents[eventIndex];
                    AppendTelemetryEvent(ref first, builder, telemetryEvent, baseTimestamp);
                    eventCount++;
                }
            }

            builder.Append("],\"displayTimeUnit\":\"ms\"");
            if (harmony is not null || capabilities is not null || components is not null || loadedMods is not null)
            {
                AppendRuntimeDiagnostics(builder, harmony, capabilities, components, loadedMods);
            }
            builder.Append('}');
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            return new RuntimeTraceExportResult(path, eventCount);
        }

        private static void AppendRuntimeDiagnostics(
            StringBuilder builder,
            HarmonyInspectionSnapshot? harmony,
            IReadOnlyList<RuntimeCapabilityDescriptor>? capabilities,
            IReadOnlyList<RuntimeComponentDescriptor>? components,
            IReadOnlyList<LoadedModSnapshot>? loadedMods)
        {
            builder.Append(",\"tajsDiagnostics\":{");
            bool first = true;
            if (harmony is not null)
            {
                AppendJsonPropertyName(builder, ref first, "harmony");
                builder.Append("{\"capturedUtc\":");
                AppendJsonString(builder, harmony.CapturedUtc.ToString("O", CultureInfo.InvariantCulture));
                builder.Append(",\"available\":").Append(harmony.IsAvailable ? "true" : "false")
                    .Append(",\"error\":");
                AppendJsonString(builder, harmony.Error);
                builder.Append(",\"tajsPatchedTargets\":").Append(harmony.TajsPatchedTargetCount)
                    .Append(",\"sharedTargets\":").Append(harmony.SharedTargetCount)
                    .Append(",\"attention\":").Append(harmony.AttentionCount)
                    .Append(",\"tajsPatches\":").Append(harmony.TajsPatchCount)
                    .Append(",\"targets\":[");
                for (int index = 0; index < harmony.Targets.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }
                    HarmonyTargetSnapshot target = harmony.Targets[index];
                    builder.Append("{\"assembly\":");
                    AppendJsonString(builder, target.OriginalAssembly);
                    builder.Append(",\"type\":");
                    AppendJsonString(builder, target.OriginalType);
                    builder.Append(",\"method\":");
                    AppendJsonString(builder, target.OriginalMethod);
                    builder.Append(",\"signature\":");
                    AppendJsonString(builder, target.OriginalSignature);
                    builder.Append(",\"risk\":");
                    AppendJsonString(builder, target.Risk.ToString());
                    builder.Append(",\"riskReason\":");
                    AppendJsonString(builder, target.RiskReason);
                    builder.Append(",\"nonTajsOwners\":");
                    AppendJsonStringArray(builder, target.NonTajsOwners);
                    builder.Append(",\"patches\":[");
                    for (int patchIndex = 0; patchIndex < target.Patches.Count; patchIndex++)
                    {
                        if (patchIndex > 0)
                        {
                            builder.Append(',');
                        }
                        HarmonyPatchSnapshot patch = target.Patches[patchIndex];
                        builder.Append("{\"kind\":");
                        AppendJsonString(builder, patch.Kind.ToString());
                        builder.Append(",\"owner\":");
                        AppendJsonString(builder, patch.OwnerId);
                        builder.Append(",\"method\":");
                        AppendJsonString(builder, patch.PatchMethod);
                        builder.Append(",\"priority\":").Append(patch.Priority)
                            .Append(",\"before\":");
                        AppendJsonStringArray(builder, patch.Before);
                        builder.Append(",\"after\":");
                        AppendJsonStringArray(builder, patch.After);
                        builder.Append(",\"tajsOwned\":").Append(patch.IsTajsOwned ? "true" : "false").Append('}');
                    }
                    builder.Append("]}");
                }
                builder.Append("]}");
            }

            if (capabilities is not null)
            {
                AppendJsonPropertyName(builder, ref first, "capabilities");
                builder.Append('[');
                for (int index = 0; index < capabilities.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }
                    RuntimeCapabilityDescriptor capability = capabilities[index];
                    builder.Append("{\"id\":");
                    AppendJsonString(builder, capability.CapabilityId);
                    builder.Append(",\"mod\":");
                    AppendJsonString(builder, capability.ModId);
                    builder.Append(",\"component\":");
                    AppendJsonString(builder, capability.ComponentId);
                    builder.Append(",\"state\":");
                    AppendJsonString(builder, capability.State.ToString());
                    builder.Append(",\"version\":");
                    AppendJsonString(builder, capability.Version);
                    builder.Append(",\"details\":");
                    AppendJsonString(builder, capability.Details);
                    builder.Append(",\"reason\":");
                    AppendJsonString(builder, capability.Reason);
                    builder.Append(",\"lifetime\":");
                    AppendJsonString(builder, capability.Lifetime.ToString());
                    builder.Append('}');
                }
                builder.Append(']');
            }

            if (components is not null)
            {
                AppendJsonPropertyName(builder, ref first, "components");
                builder.Append('[');
                for (int index = 0; index < components.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }
                    RuntimeComponentDescriptor component = components[index];
                    builder.Append("{\"mod\":");
                    AppendJsonString(builder, component.ModId);
                    builder.Append(",\"id\":");
                    AppendJsonString(builder, component.ComponentId);
                    builder.Append(",\"lifetime\":");
                    AppendJsonString(builder, component.Lifetime.ToString());
                    builder.Append(",\"expectedSeam\":");
                    AppendJsonString(builder, component.ExpectedSeam);
                    builder.Append(",\"harmonyOwners\":");
                    AppendJsonStringArray(builder, component.HarmonyOwnerIds);
                    builder.Append(",\"requiredCapabilities\":");
                    AppendJsonStringArray(builder, component.RequiredCapabilityIds);
                    builder.Append(",\"optionalCapabilities\":");
                    AppendJsonStringArray(builder, component.OptionalCapabilityIds);
                    builder.Append('}');
                }
                builder.Append(']');
            }

            if (loadedMods is not null)
            {
                AppendJsonPropertyName(builder, ref first, "loadedMods");
                builder.Append('[');
                for (int index = 0; index < loadedMods.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }
                    LoadedModSnapshot mod = loadedMods[index];
                    builder.Append("{\"id\":");
                    AppendJsonString(builder, mod.Id);
                    builder.Append(",\"displayName\":");
                    AppendJsonString(builder, mod.DisplayName);
                    builder.Append(",\"version\":");
                    AppendJsonString(builder, mod.Version);
                    builder.Append(",\"loadSucceeded\":").Append(mod.LoadSucceeded ? "true" : "false")
                        .Append(",\"loadError\":");
                    AppendJsonString(builder, mod.LoadError);
                    builder.Append('}');
                }
                builder.Append(']');
            }
            builder.Append('}');
        }

        private static void AppendJsonPropertyName(StringBuilder builder, ref bool first, string name)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            AppendJsonString(builder, name);
            builder.Append(':');
        }

        private static void AppendJsonStringArray(StringBuilder builder, IReadOnlyList<string> values)
        {
            builder.Append('[');
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                AppendJsonString(builder, values[index]);
            }
            builder.Append(']');
        }

        /// <summary>
        ///     Writes the broad flight-recorder samples as a spreadsheet-friendly CSV. This is
        ///     deliberately a command/export path; no text formatting is performed while samples
        ///     are captured.
        /// </summary>
        internal static RuntimeTraceExportResult ExportCsv(
            string path,
            IReadOnlyList<RuntimeFrameSample> frames)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A CSV path is required.", nameof(path));
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder(8192);
            AppendCsvHeader(builder);
            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                AppendCsvFrame(builder, frames[frameIndex]);
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            return new RuntimeTraceExportResult(path, frames.Count);
        }

        private static void AppendCsvHeader(StringBuilder builder)
        {
            builder.Append("sequence,timestamp,paused,speed,simSteps,budgetedSimSteps,overtime,")
                .Append("frameMs,renderMs,waitForSimMs,simMs,classification,")
                .Append("managedHeapBytes,managedHeapDeltaBytes,unityAllocatedBytes,unityReservedBytes,")
                .Append("unityUnusedReservedBytes,unityGraphicsBytes,gpuFrameMs,mainThreadMs,renderThreadMs,")
                .Append("drawCalls,batches,triangles,vertices,gcAllocatedBytes,gen0Delta,gen1Delta,gen2Delta");
            for (int index = 0; index < s_timingEvents.Length; index++)
            {
                builder.Append(',').Append("phase_").Append(TimingName(s_timingEvents[index])).Append("_ms");
            }
            builder.Append('\n');
        }

        private static void AppendCsvFrame(StringBuilder builder, RuntimeFrameSample frame)
        {
            AppendCsvLong(builder, frame.Sequence);
            AppendCsvLong(builder, frame.CapturedTimestamp);
            AppendCsvBool(builder, frame.SimPaused);
            AppendCsvInt(builder, frame.SimSpeedMult);
            AppendCsvInt(builder, frame.SimStepsPerUpdate);
            AppendCsvInt(builder, frame.BudgetedSimSteps);
            AppendCsvBool(builder, frame.Runner.WasOvertime);
            AppendCsvTicks(builder, frame.FrameTicks);
            AppendCsvTicks(builder, frame.RenderTicks);
            AppendCsvTicks(builder, frame.Timings.WaitForSimTicks);
            AppendCsvTicks(builder, frame.SimTicks);
            AppendCsvString(builder, frame.SimPaused ? "paused" : frame.Classification.ToString());
            RuntimeCounterSnapshot counters = frame.Counters;
            AppendCsvOptionalLong(builder, counters.Available ? counters.ManagedHeapBytes : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.ManagedHeapDeltaBytes : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.UnityAllocatedBytes : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.UnityReservedBytes : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.UnityUnusedReservedBytes : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.UnityGraphicsBytes : -1);
            AppendCsvOptionalTicks(builder, counters.HasGpuTelemetry ? counters.GpuFrameTicks : -1);
            AppendCsvOptionalTicks(builder, counters.Available ? counters.MainThreadTicks : -1);
            AppendCsvOptionalTicks(builder, counters.Available ? counters.RenderThreadTicks : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.DrawCalls : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.Batches : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.Triangles : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.Vertices : -1);
            AppendCsvOptionalLong(builder, counters.Available ? counters.GcAllocatedBytes : -1);
            AppendCsvOptionalInt(builder, counters.Available ? counters.Gen0Delta : -1);
            AppendCsvOptionalInt(builder, counters.Available ? counters.Gen1Delta : -1);
            AppendCsvOptionalInt(builder, counters.Available ? counters.Gen2Delta : -1);
            for (int index = 0; index < s_timingEvents.Length; index++)
            {
                AppendCsvTicks(builder, frame.TimingRanges.Get(s_timingEvents[index]).DurationTicks);
            }
            builder.Append('\n');
        }

        private static void AppendCsvLong(StringBuilder builder, long value)
        {
            AppendCsvSeparator(builder);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendCsvInt(StringBuilder builder, int value)
        {
            AppendCsvSeparator(builder);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendCsvOptionalInt(StringBuilder builder, int value)
        {
            if (value < 0)
            {
                AppendCsvString(builder, null);
                return;
            }
            AppendCsvInt(builder, value);
        }

        private static void AppendCsvOptionalLong(StringBuilder builder, long value)
        {
            if (value < 0)
            {
                AppendCsvString(builder, null);
                return;
            }
            AppendCsvLong(builder, value);
        }

        private static void AppendCsvTicks(StringBuilder builder, long ticks) =>
            AppendCsvOptionalTicks(builder, ticks);

        private static void AppendCsvOptionalTicks(StringBuilder builder, long ticks)
        {
            if (ticks < 0)
            {
                AppendCsvString(builder, null);
                return;
            }
            AppendCsvSeparator(builder);
            builder.Append((ticks * 1000.0 / Stopwatch.Frequency).ToString("F4", CultureInfo.InvariantCulture));
        }

        private static void AppendCsvBool(StringBuilder builder, bool value) =>
            AppendCsvString(builder, value ? "true" : "false");

        private static void AppendCsvString(StringBuilder builder, string? value)
        {
            AppendCsvSeparator(builder);
            string text = value ?? string.Empty;
            if (text.Length == 0)
            {
                return;
            }
            bool quoted = text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (quoted)
            {
                builder.Append('"');
            }
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (character == '"')
                {
                    builder.Append("\"\"");
                }
                else
                {
                    builder.Append(character);
                }
            }
            if (quoted)
            {
                builder.Append('"');
            }
        }

        private static void AppendCsvSeparator(StringBuilder builder)
        {
            if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
            {
                builder.Append(',');
            }
        }

        private static long FindBaseTimestamp(
            IReadOnlyList<RuntimeFrameSample> frames,
            IReadOnlyList<RuntimeTraceSpan> spans,
            IReadOnlyList<RuntimeTraceMarker> markers,
            IReadOnlyList<RuntimeTelemetryEventSnapshot>? events)
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
            if (events is not null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Timestamp > 0)
                    {
                        result = Math.Min(result, events[i].Timestamp);
                    }
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
            AppendCounter(builder, ref firstArg, "monoUsedBytes", frame.Counters.Available ? frame.Counters.MonoUsedBytes : -1);
            AppendCounter(builder, ref firstArg, "monoHeapBytes", frame.Counters.Available ? frame.Counters.MonoHeapBytes : -1);
            AppendCounter(builder, ref firstArg, "unityAllocatedBytes", frame.Counters.Available ? frame.Counters.UnityAllocatedBytes : -1);
            AppendCounter(builder, ref firstArg, "unityReservedBytes", frame.Counters.Available ? frame.Counters.UnityReservedBytes : -1);
            AppendCounter(builder, ref firstArg, "unityUnusedReservedBytes", frame.Counters.Available ? frame.Counters.UnityUnusedReservedBytes : -1);
            AppendCounter(builder, ref firstArg, "unityGraphicsBytes", frame.Counters.Available ? frame.Counters.UnityGraphicsBytes : -1);
            AppendTimingCounter(builder, ref firstArg, "mainThreadMs", frame.Counters.MainThreadTicks);
            AppendTimingCounter(builder, ref firstArg, "renderThreadMs", frame.Counters.RenderThreadTicks);
            AppendCounter(builder, ref firstArg, "drawCalls", frame.Counters.DrawCalls);
            AppendCounter(builder, ref firstArg, "batches", frame.Counters.Batches);
            AppendCounter(builder, ref firstArg, "triangles", frame.Counters.Triangles);
            AppendCounter(builder, ref firstArg, "vertices", frame.Counters.Vertices);
            AppendCounter(builder, ref firstArg, "gcAllocatedBytes", frame.Counters.GcAllocatedBytes);
            AppendCounter(builder, ref firstArg, "gen0Delta", frame.Counters.Gen0Delta);
            AppendCounter(builder, ref firstArg, "gen1Delta", frame.Counters.Gen1Delta);
            AppendCounter(builder, ref firstArg, "gen2Delta", frame.Counters.Gen2Delta);
            for (int index = 0; index < frame.Telemetry.Count; index++)
            {
                AppendTelemetryCounter(
                    builder,
                    ref firstArg,
                    RuntimeTelemetry.CounterName(index),
                    RuntimeTelemetry.CounterUnit(index),
                    frame.Telemetry.Get(index));
                AppendTelemetryOwner(
                    builder,
                    ref firstArg,
                    RuntimeTelemetry.CounterName(index),
                    RuntimeTelemetry.CounterOwner(index));
            }
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

        private static void AppendTimingCounter(StringBuilder builder, ref bool first, string name, long ticks)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            AppendJsonString(builder, name);
            builder.Append(':');
            if (ticks < 0)
            {
                AppendJsonString(builder, "unavailable");
            }
            else
            {
                AppendNumber(builder, ticks * 1000.0 / Stopwatch.Frequency);
            }
        }

        private static void AppendTelemetryCounter(
            StringBuilder builder,
            ref bool first,
            string name,
            RuntimeTelemetryUnit unit,
            long value)
        {
            if (unit == RuntimeTelemetryUnit.StopwatchTicks)
            {
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                AppendJsonString(builder, name);
                builder.Append(':');
                AppendNumber(builder, value * 1000.0 / Stopwatch.Frequency);
                return;
            }

            AppendCounter(builder, ref first, name, value);
        }

        private static void AppendTelemetryOwner(
            StringBuilder builder,
            ref bool first,
            string name,
            string owner)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            AppendJsonString(builder, name + ".owner");
            builder.Append(':');
            AppendJsonString(builder, owner);
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

        private static void AppendTelemetryEvent(
            ref bool first,
            StringBuilder builder,
            RuntimeTelemetryEventSnapshot telemetryEvent,
            long baseTimestamp)
        {
            BeginEvent(ref first, builder);
            builder.Append("\"name\":");
            AppendJsonString(builder, telemetryEvent.Name);
            builder.Append(",\"cat\":\"coi.telemetry\",\"ph\":\"i\",\"s\":\"t\",\"ts\":");
            AppendNumber(builder, ToMicroseconds(telemetryEvent.Timestamp - baseTimestamp));
            builder.Append(",\"pid\":1,\"tid\":").Append(telemetryEvent.ThreadId)
                .Append(",\"args\":{\"sequence\":").Append(telemetryEvent.Sequence)
                .Append(",\"phase\":");
            AppendJsonString(builder, RuntimeTracePhase.Name(telemetryEvent.PhaseId));
            builder.Append("}}");
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

        private static void AppendNumber(StringBuilder builder, double value) => builder.Append(value.ToString("F3", CultureInfo.InvariantCulture));

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
