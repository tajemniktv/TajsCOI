// Taj's COI Mods | GameLoopTimingSnapshot.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace TajsCOI.Profiler.Core
{
    internal enum GameLoopTimingEvent
    {
        Input,
        SyncStart,
        Sync,
        SyncEnd,
        RenderAfterSync,
        Render,
        RenderEnd,
        WaitForSim,
        InputEnd,
        SimCmd,
        SimStart,
        SimUpdate,
        SimEnd,
        SimEndForUi,
        SimAfterSync,
        SimParallelStart,
        SimParallelEnd,
        SimReadState,
        SimPausedUi,
        SimCmdExtra,
    }

    internal readonly struct GameLoopTimingSnapshot
    {
        internal GameLoopTimingSnapshot(
            long inputTicks = 0,
            long syncStartTicks = 0,
            long syncTicks = 0,
            long syncEndTicks = 0,
            long renderAfterSyncTicks = 0,
            long renderTicks = 0,
            long renderEndTicks = 0,
            long waitForSimTicks = 0,
            long inputEndTicks = 0,
            long simCmdTicks = 0,
            long simStartTicks = 0,
            long simUpdateTicks = 0,
            long simEndTicks = 0,
            long simEndForUiTicks = 0,
            long simAfterSyncTicks = 0,
            long simParallelStartTicks = 0,
            long simParallelEndTicks = 0,
            long simReadStateTicks = 0,
            long simPausedUiTicks = 0,
            long simCmdExtraTicks = 0)
        {
            InputTicks = inputTicks;
            SyncStartTicks = syncStartTicks;
            SyncTicks = syncTicks;
            SyncEndTicks = syncEndTicks;
            RenderAfterSyncTicks = renderAfterSyncTicks;
            RenderTicks = renderTicks;
            RenderEndTicks = renderEndTicks;
            WaitForSimTicks = waitForSimTicks;
            InputEndTicks = inputEndTicks;
            SimCmdTicks = simCmdTicks;
            SimStartTicks = simStartTicks;
            SimUpdateTicks = simUpdateTicks;
            SimEndTicks = simEndTicks;
            SimEndForUiTicks = simEndForUiTicks;
            SimAfterSyncTicks = simAfterSyncTicks;
            SimParallelStartTicks = simParallelStartTicks;
            SimParallelEndTicks = simParallelEndTicks;
            SimReadStateTicks = simReadStateTicks;
            SimPausedUiTicks = simPausedUiTicks;
            SimCmdExtraTicks = simCmdExtraTicks;
        }

        internal GameLoopTimingSnapshot(GameLoopTimingRanges ranges)
            : this(
                ranges.Input.DurationTicks,
                ranges.SyncStart.DurationTicks,
                ranges.Sync.DurationTicks,
                ranges.SyncEnd.DurationTicks,
                ranges.RenderAfterSync.DurationTicks,
                ranges.Render.DurationTicks,
                ranges.RenderEnd.DurationTicks,
                ranges.WaitForSim.DurationTicks,
                ranges.InputEnd.DurationTicks,
                ranges.SimCmd.DurationTicks,
                ranges.SimStart.DurationTicks,
                ranges.SimUpdate.DurationTicks,
                ranges.SimEnd.DurationTicks,
                ranges.SimEndForUi.DurationTicks,
                ranges.SimAfterSync.DurationTicks,
                ranges.SimParallelStart.DurationTicks,
                ranges.SimParallelEnd.DurationTicks,
                ranges.SimReadState.DurationTicks,
                ranges.SimPausedUi.DurationTicks,
                ranges.SimCmdExtra.DurationTicks)
        {
        }

        internal long InputTicks { get; }
        internal long SyncStartTicks { get; }
        internal long SyncTicks { get; }
        internal long SyncEndTicks { get; }
        internal long RenderAfterSyncTicks { get; }
        internal long RenderTicks { get; }
        internal long RenderEndTicks { get; }
        internal long WaitForSimTicks { get; }
        internal long InputEndTicks { get; }
        internal long SimCmdTicks { get; }
        internal long SimStartTicks { get; }
        internal long SimUpdateTicks { get; }
        internal long SimEndTicks { get; }
        internal long SimEndForUiTicks { get; }
        internal long SimAfterSyncTicks { get; }
        internal long SimParallelStartTicks { get; }
        internal long SimParallelEndTicks { get; }
        internal long SimReadStateTicks { get; }
        internal long SimPausedUiTicks { get; }
        internal long SimCmdExtraTicks { get; }

        internal bool HasAnySample =>
            InputTicks > 0 || SyncStartTicks > 0 || SyncTicks > 0 || SyncEndTicks > 0 ||
            RenderAfterSyncTicks > 0 || RenderTicks > 0 || RenderEndTicks > 0 || WaitForSimTicks > 0 ||
            InputEndTicks > 0 || SimCmdTicks > 0 || SimStartTicks > 0 || SimUpdateTicks > 0 ||
            SimEndTicks > 0 || SimEndForUiTicks > 0 || SimAfterSyncTicks > 0 ||
            SimParallelStartTicks > 0 || SimParallelEndTicks > 0 || SimReadStateTicks > 0 ||
            SimPausedUiTicks > 0 || SimCmdExtraTicks > 0;

        internal long MainPhaseTicks =>
            RuntimeTraceMath.SaturatingAdd(
                RuntimeTraceMath.SaturatingAdd(
                    RuntimeTraceMath.SaturatingAdd(
                        RuntimeTraceMath.SaturatingAdd(InputTicks, InputEndTicks),
                        RuntimeTraceMath.SaturatingAdd(SyncStartTicks, SyncTicks)),
                    SyncEndTicks),
                RuntimeTraceMath.SaturatingAdd(
                    RuntimeTraceMath.SaturatingAdd(RenderAfterSyncTicks, RenderTicks),
                    RenderEndTicks));

        internal long RenderPhaseTicks => RuntimeTraceMath.SaturatingAdd(
            RuntimeTraceMath.SaturatingAdd(RenderAfterSyncTicks, RenderTicks),
            RenderEndTicks);

        internal long SimulationPhaseTicks =>
            RuntimeTraceMath.SaturatingAdd(
                RuntimeTraceMath.SaturatingAdd(
                    RuntimeTraceMath.SaturatingAdd(
                        RuntimeTraceMath.SaturatingAdd(SimCmdTicks, SimCmdExtraTicks),
                        RuntimeTraceMath.SaturatingAdd(SimAfterSyncTicks, SimStartTicks)),
                    RuntimeTraceMath.SaturatingAdd(SimParallelStartTicks, SimUpdateTicks)),
                RuntimeTraceMath.SaturatingAdd(
                    RuntimeTraceMath.SaturatingAdd(
                        RuntimeTraceMath.SaturatingAdd(SimParallelEndTicks, SimEndTicks),
                        RuntimeTraceMath.SaturatingAdd(SimReadStateTicks, SimEndForUiTicks)),
                    SimPausedUiTicks));

        internal long GetTicks(GameLoopTimingEvent eventId)
        {
            switch (eventId)
            {
                case GameLoopTimingEvent.Input: return InputTicks;
                case GameLoopTimingEvent.SyncStart: return SyncStartTicks;
                case GameLoopTimingEvent.Sync: return SyncTicks;
                case GameLoopTimingEvent.SyncEnd: return SyncEndTicks;
                case GameLoopTimingEvent.RenderAfterSync: return RenderAfterSyncTicks;
                case GameLoopTimingEvent.Render: return RenderTicks;
                case GameLoopTimingEvent.RenderEnd: return RenderEndTicks;
                case GameLoopTimingEvent.WaitForSim: return WaitForSimTicks;
                case GameLoopTimingEvent.InputEnd: return InputEndTicks;
                case GameLoopTimingEvent.SimCmd: return SimCmdTicks;
                case GameLoopTimingEvent.SimStart: return SimStartTicks;
                case GameLoopTimingEvent.SimUpdate: return SimUpdateTicks;
                case GameLoopTimingEvent.SimEnd: return SimEndTicks;
                case GameLoopTimingEvent.SimEndForUi: return SimEndForUiTicks;
                case GameLoopTimingEvent.SimAfterSync: return SimAfterSyncTicks;
                case GameLoopTimingEvent.SimParallelStart: return SimParallelStartTicks;
                case GameLoopTimingEvent.SimParallelEnd: return SimParallelEndTicks;
                case GameLoopTimingEvent.SimReadState: return SimReadStateTicks;
                case GameLoopTimingEvent.SimPausedUi: return SimPausedUiTicks;
                case GameLoopTimingEvent.SimCmdExtra: return SimCmdExtraTicks;
                default: return 0;
            }
        }
    }

    internal readonly struct GameLoopTimingRingReadWindow
    {
        internal GameLoopTimingRingReadWindow(int startLogicalIndex, int count, long droppedEntries)
        {
            StartLogicalIndex = startLogicalIndex;
            Count = count;
            DroppedEntries = droppedEntries;
        }

        internal int StartLogicalIndex { get; }
        internal int Count { get; }
        internal long DroppedEntries { get; }
    }

    /// <summary>
    ///     Tracks one independently-produced vanilla ring. The game exposes a logical write
    ///     counter, even though entry lookup masks it to the 2048-slot backing array. A reader
    ///     therefore consumes the interval exactly once and can detect when more than one full
    ///     retention window elapsed between polls.
    /// </summary>
    internal sealed class GameLoopTimingRingCursor
    {
        private readonly int m_capacity;
        private bool m_initialized;
        private int m_nextLogicalIndex;

        internal GameLoopTimingRingCursor(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            m_capacity = capacity;
        }

        internal GameLoopTimingRingReadWindow Advance(int writeIndex)
        {
            if (!m_initialized)
            {
                m_initialized = true;
                // writeIndex points at the entry currently being filled. The previous logical
                // slot is the first safe candidate, but the range validator below rejects the
                // empty slot when the reader starts before the first producer write.
                m_nextLogicalIndex = writeIndex != 0 ? unchecked(writeIndex - 1) : writeIndex;
            }

            uint unsignedDelta = unchecked((uint)(writeIndex - m_nextLogicalIndex));
            if (unsignedDelta == 0)
            {
                return default;
            }

            long delta = unsignedDelta;
            long dropped = Math.Max(0, delta - m_capacity);
            int count = (int)Math.Min(m_capacity, delta);
            int start = dropped > 0
                ? unchecked(writeIndex - m_capacity)
                : m_nextLogicalIndex;
            m_nextLogicalIndex = writeIndex;
            return new GameLoopTimingRingReadWindow(start, count, dropped);
        }
    }

    /// <summary>
    /// Reads the private 0.8.7b GameLoopTimings ring without reflection on the frame path.
    /// The dynamic accessors are built only after the expected enum, fields, and buffer shape
    /// have been validated. A missing or changed game surface disables only this reader.
    /// </summary>
    internal sealed class GameLoopTimingsAccess
    {
        private static readonly string[] s_expectedEventNames =
        {
            "INPUT",
            "SYNC_START",
            "SYNC",
            "SYNC_END",
            "RENDER_AFTER_SYNC",
            "RENDER",
            "RENDER_END",
            "WAIT_FOR_SIM",
            "INPUT_END",
            "SIM_CMD",
            "SIM_START",
            "SIM_UPDATE",
            "SIM_END",
            "SIM_END_FOR_UI",
            "SIM_AFTER_SYNC",
            "SIM_PARALLEL_START",
            "SIM_PARALLEL_END",
            "SIM_READ_STATE",
            "SIM_PAUSED_UI",
            "SIM_CMD_EXTRA",
        };

        private readonly int[] m_writeIndices;
        private readonly Func<int, int, long> m_readStart;
        private readonly Func<int, int, long> m_readEnd;
        private readonly int m_bufferMask;
        private readonly GameLoopTimingRingCursor[] m_cursors;
        private readonly long[] m_completedDurations;
        private readonly GameLoopTimingRange[] m_latestRanges;
        private long m_droppedEntries;

        private GameLoopTimingsAccess(
            int[] writeIndices,
            Func<int, int, long> readStart,
            Func<int, int, long> readEnd,
            int bufferMask)
        {
            m_writeIndices = writeIndices;
            m_readStart = readStart;
            m_readEnd = readEnd;
            m_bufferMask = bufferMask;
            BufferSize = bufferMask + 1;
            m_cursors = Enumerable.Range(0, s_expectedEventNames.Length)
                .Select(_ => new GameLoopTimingRingCursor(BufferSize))
                .ToArray();
            m_completedDurations = new long[s_expectedEventNames.Length];
            m_latestRanges = new GameLoopTimingRange[s_expectedEventNames.Length];
        }

        internal int BufferSize { get; }
        internal bool IsAvailable => true;
        internal long DroppedEntries => m_droppedEntries;

        internal static bool TryCreate(out GameLoopTimingsAccess? access, out string reason)
        {
            access = null;
            reason = string.Empty;
            try
            {
                Assembly coreAssembly = typeof(Mafi.Core.GameLoop.IGameLoopEvents).Assembly;
                Type? timingsType = coreAssembly.GetType("Mafi.Core.GameLoop.GameLoopTimings", false);
                if (timingsType is null)
                {
                    reason = "Mafi.Core.GameLoop.GameLoopTimings was not found.";
                    return false;
                }

                Type? eventType = timingsType.GetNestedType("Event", BindingFlags.Public | BindingFlags.NonPublic);
                Type? entryType = timingsType.GetNestedType("Entry", BindingFlags.Public | BindingFlags.NonPublic);
                if (eventType is null || !eventType.IsEnum || entryType is null)
                {
                    reason = "GameLoopTimings is missing its Event or Entry type.";
                    return false;
                }

                string[] eventNames = Enum.GetNames(eventType);
                if (!eventNames.SequenceEqual(s_expectedEventNames, StringComparer.Ordinal))
                {
                    reason = "GameLoopTimings.Event names or order changed.";
                    return false;
                }

                FieldInfo? entriesField = timingsType.GetField("m_entries", BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo? writeIndexField = timingsType.GetField("m_writeIndex", BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo? bufferSizeField = timingsType.GetField("BUFFER_SIZE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo? bufferMaskField = timingsType.GetField("BUFFER_SIZE_MASK", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo? startField = entryType.GetField("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo? endField = entryType.GetField("End", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (entriesField is null || writeIndexField is null || bufferSizeField is null ||
                    bufferMaskField is null || startField is null || endField is null ||
                    startField.FieldType != typeof(long) || endField.FieldType != typeof(long) ||
                    writeIndexField.FieldType != typeof(int[]))
                {
                    reason = "GameLoopTimings backing fields do not match the supported shape.";
                    return false;
                }

                int bufferSize = Convert.ToInt32(bufferSizeField.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture);
                int bufferMask = Convert.ToInt32(bufferMaskField.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture);
                if (bufferSize <= 1 || bufferMask != bufferSize - 1)
                {
                    reason = "GameLoopTimings buffer constants are invalid.";
                    return false;
                }

                Array? entries = entriesField.GetValue(null) as Array;
                int[]? writeIndices = writeIndexField.GetValue(null) as int[];
                if (entries is null || writeIndices is null || entries.Length != s_expectedEventNames.Length ||
                    writeIndices.Length != s_expectedEventNames.Length)
                {
                    reason = "GameLoopTimings ring arrays do not match the supported shape.";
                    return false;
                }

                for (int index = 0; index < entries.Length; index++)
                {
                    if (entries.GetValue(index) is not Array ring || ring.Length != bufferSize)
                    {
                        reason = "GameLoopTimings ring capacity changed.";
                        return false;
                    }
                }

                MethodInfo? getWriteIndex = timingsType.GetMethod(
                    "GetWriteIndex",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { eventType },
                    null);
                MethodInfo? getEntry = timingsType.GetMethod(
                    "GetEntry",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { eventType, typeof(int) },
                    null);
                if (getWriteIndex is null || getEntry is null || getWriteIndex.ReturnType != typeof(int) ||
                    getEntry.ReturnType != entryType.MakeByRefType())
                {
                    reason = "GameLoopTimings access methods are missing or changed.";
                    return false;
                }

                Func<int, int, long> readStart = CreateEntryFieldReader(
                    entriesField,
                    startField,
                    entryType,
                    bufferMask,
                    "ReadGameLoopTimingStart");
                Func<int, int, long> readEnd = CreateEntryFieldReader(
                    entriesField,
                    endField,
                    entryType,
                    bufferMask,
                    "ReadGameLoopTimingEnd");

                access = new GameLoopTimingsAccess(writeIndices, readStart, readEnd, bufferMask);
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        internal GameLoopTimingSnapshot ReadLatest()
        {
            return ReadLatest(out _);
        }

        internal GameLoopTimingSnapshot ReadLatest(out GameLoopTimingRanges ranges)
        {
            ranges = new GameLoopTimingRanges(
                ReadLatestRange(GameLoopTimingEvent.Input),
                ReadLatestRange(GameLoopTimingEvent.SyncStart),
                ReadLatestRange(GameLoopTimingEvent.Sync),
                ReadLatestRange(GameLoopTimingEvent.SyncEnd),
                ReadLatestRange(GameLoopTimingEvent.RenderAfterSync),
                ReadLatestRange(GameLoopTimingEvent.Render),
                ReadLatestRange(GameLoopTimingEvent.RenderEnd),
                ReadLatestRange(GameLoopTimingEvent.WaitForSim),
                ReadLatestRange(GameLoopTimingEvent.InputEnd),
                ReadLatestRange(GameLoopTimingEvent.SimCmd),
                ReadLatestRange(GameLoopTimingEvent.SimStart),
                ReadLatestRange(GameLoopTimingEvent.SimUpdate),
                ReadLatestRange(GameLoopTimingEvent.SimEnd),
                ReadLatestRange(GameLoopTimingEvent.SimEndForUi),
                ReadLatestRange(GameLoopTimingEvent.SimAfterSync),
                ReadLatestRange(GameLoopTimingEvent.SimParallelStart),
                ReadLatestRange(GameLoopTimingEvent.SimParallelEnd),
                ReadLatestRange(GameLoopTimingEvent.SimReadState),
                ReadLatestRange(GameLoopTimingEvent.SimPausedUi),
                ReadLatestRange(GameLoopTimingEvent.SimCmdExtra));
            return new GameLoopTimingSnapshot(ranges);
        }

        internal bool ReadCompleted(out GameLoopTimingSnapshot snapshot, out GameLoopTimingRanges ranges)
        {
            bool hasSample = false;
            for (int eventIndex = 0; eventIndex < m_completedDurations.Length; eventIndex++)
            {
                m_completedDurations[eventIndex] = 0;
                m_latestRanges[eventIndex] = default;

                int writeIndex = Volatile.Read(ref m_writeIndices[eventIndex]);
                GameLoopTimingRingReadWindow window = m_cursors[eventIndex].Advance(writeIndex);
                m_droppedEntries = RuntimeTraceMath.SaturatingAdd(m_droppedEntries, window.DroppedEntries);
                for (int offset = 0; offset < window.Count; offset++)
                {
                    int logicalIndex = unchecked(window.StartLogicalIndex + offset);
                    int physicalIndex = logicalIndex & m_bufferMask;
                    long start = m_readStart(eventIndex, physicalIndex);
                    long end = m_readEnd(eventIndex, physicalIndex);
                    GameLoopTimingRange range = new GameLoopTimingRange(start, end);
                    if (!range.IsValid)
                    {
                        continue;
                    }

                    m_completedDurations[eventIndex] = RuntimeTraceMath.SaturatingAdd(
                        m_completedDurations[eventIndex],
                        range.DurationTicks);
                    m_latestRanges[eventIndex] = range;
                    hasSample = true;
                }
            }

            ranges = CreateRanges(m_latestRanges);
            snapshot = CreateSnapshot(m_completedDurations);
            return hasSample;
        }

        private GameLoopTimingRange ReadLatestRange(GameLoopTimingEvent eventId)
        {
            int eventIndex = (int)eventId;
            int writeIndex = Volatile.Read(ref m_writeIndices[eventIndex]);
            if (writeIndex <= 0)
            {
                return default;
            }

            // End() increments the write index before filling the slot. Leave the newest slot
            // untouched so a concurrent writer cannot expose a half-written pair of timestamps.
            int safeIndex = unchecked(writeIndex - 1);
            long start = m_readStart(eventIndex, safeIndex & m_bufferMask);
            long end = m_readEnd(eventIndex, safeIndex & m_bufferMask);
            return new GameLoopTimingRange(start, end);
        }

        private static GameLoopTimingRanges CreateRanges(GameLoopTimingRange[] ranges) => new(
            ranges[(int)GameLoopTimingEvent.Input],
            ranges[(int)GameLoopTimingEvent.SyncStart],
            ranges[(int)GameLoopTimingEvent.Sync],
            ranges[(int)GameLoopTimingEvent.SyncEnd],
            ranges[(int)GameLoopTimingEvent.RenderAfterSync],
            ranges[(int)GameLoopTimingEvent.Render],
            ranges[(int)GameLoopTimingEvent.RenderEnd],
            ranges[(int)GameLoopTimingEvent.WaitForSim],
            ranges[(int)GameLoopTimingEvent.InputEnd],
            ranges[(int)GameLoopTimingEvent.SimCmd],
            ranges[(int)GameLoopTimingEvent.SimStart],
            ranges[(int)GameLoopTimingEvent.SimUpdate],
            ranges[(int)GameLoopTimingEvent.SimEnd],
            ranges[(int)GameLoopTimingEvent.SimEndForUi],
            ranges[(int)GameLoopTimingEvent.SimAfterSync],
            ranges[(int)GameLoopTimingEvent.SimParallelStart],
            ranges[(int)GameLoopTimingEvent.SimParallelEnd],
            ranges[(int)GameLoopTimingEvent.SimReadState],
            ranges[(int)GameLoopTimingEvent.SimPausedUi],
            ranges[(int)GameLoopTimingEvent.SimCmdExtra]);

        private static GameLoopTimingSnapshot CreateSnapshot(long[] ticks) => new(
            ticks[(int)GameLoopTimingEvent.Input],
            ticks[(int)GameLoopTimingEvent.SyncStart],
            ticks[(int)GameLoopTimingEvent.Sync],
            ticks[(int)GameLoopTimingEvent.SyncEnd],
            ticks[(int)GameLoopTimingEvent.RenderAfterSync],
            ticks[(int)GameLoopTimingEvent.Render],
            ticks[(int)GameLoopTimingEvent.RenderEnd],
            ticks[(int)GameLoopTimingEvent.WaitForSim],
            ticks[(int)GameLoopTimingEvent.InputEnd],
            ticks[(int)GameLoopTimingEvent.SimCmd],
            ticks[(int)GameLoopTimingEvent.SimStart],
            ticks[(int)GameLoopTimingEvent.SimUpdate],
            ticks[(int)GameLoopTimingEvent.SimEnd],
            ticks[(int)GameLoopTimingEvent.SimEndForUi],
            ticks[(int)GameLoopTimingEvent.SimAfterSync],
            ticks[(int)GameLoopTimingEvent.SimParallelStart],
            ticks[(int)GameLoopTimingEvent.SimParallelEnd],
            ticks[(int)GameLoopTimingEvent.SimReadState],
            ticks[(int)GameLoopTimingEvent.SimPausedUi],
            ticks[(int)GameLoopTimingEvent.SimCmdExtra]);

        private static Func<int, int, long> CreateEntryFieldReader(
            FieldInfo entriesField,
            FieldInfo valueField,
            Type entryType,
            int bufferMask,
            string name)
        {
            DynamicMethod method = new DynamicMethod(
                name,
                typeof(long),
                new[] { typeof(int), typeof(int) },
                typeof(GameLoopTimingsAccess).Module,
                true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, entriesField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, bufferMask);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Ldelema, entryType);
            il.Emit(OpCodes.Ldfld, valueField);
            il.Emit(OpCodes.Ret);
            return (Func<int, int, long>)method.CreateDelegate(typeof(Func<int, int, long>));
        }
    }
}
