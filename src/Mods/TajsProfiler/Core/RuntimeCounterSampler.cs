// Taj's COI Mods | RuntimeCounterSampler.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;

namespace TajsCOI.Profiler.Core
{
    /// <summary>
    ///     Samples cumulative managed/Unity counters and optional Unity ProfilerRecorder values.
    ///     Unity accessors and recorder delegates are resolved once. Ordinary frame capture only
    ///     performs cheap primitive reads; memory access is rate limited and no GC is forced.
    /// </summary>
    internal sealed class RuntimeCounterSampler : IDisposable
    {
        internal const int UnityAllocated = 1 << 0;
        internal const int UnityReserved = 1 << 1;
        internal const int UnityUnusedReserved = 1 << 2;
        internal const int UnityGraphics = 1 << 3;
        internal const int MonoUsed = 1 << 4;
        internal const int MonoHeap = 1 << 5;

        private const int ProfilerGpuFrame = 1 << 0;
        private const int ProfilerMainThread = 1 << 1;
        private const int ProfilerRenderThread = 1 << 2;
        private const int ProfilerDrawCalls = 1 << 3;
        private const int ProfilerBatches = 1 << 4;
        private const int ProfilerTriangles = 1 << 5;
        private const int ProfilerVertices = 1 << 6;
        private const int ProfilerGcAlloc = 1 << 7;

        private const int ProfilerRecorderDefaultOptions = 24;
        private const double MinimumIntervalSeconds = 0.05;
        private const double MaximumIntervalSeconds = 2.0;
        private const int TimeNanosecondsUnit = 1;
        private const int BytesUnit = 2;
        private const int CountUnit = 3;

        private readonly Func<long>? m_unityAllocated;
        private readonly Func<long>? m_unityReserved;
        private readonly Func<long>? m_unityUnusedReserved;
        private readonly Func<long>? m_unityGraphics;
        private readonly Func<long>? m_monoUsed;
        private readonly Func<long>? m_monoHeap;
        private readonly ProfilerRecorderHandle? m_gpuFrame;
        private readonly ProfilerRecorderHandle? m_mainThread;
        private readonly ProfilerRecorderHandle? m_renderThread;
        private readonly ProfilerRecorderHandle? m_drawCalls;
        private readonly ProfilerRecorderHandle? m_batches;
        private readonly ProfilerRecorderHandle? m_triangles;
        private readonly ProfilerRecorderHandle? m_vertices;
        private readonly ProfilerRecorderHandle? m_gcAlloc;
        private long m_intervalTicks;
        private readonly int m_supportedUnityCounters;
        private readonly int m_supportedProfilerCounters;
        private readonly string m_supportSummary;
        private long m_lastSampleTimestamp;
        private long m_previousManagedHeapBytes = -1;
        private long m_previousUnityAllocatedBytes = -1;
        private long m_previousUnityGraphicsBytes = -1;
        private int m_previousGen0;
        private int m_previousGen1;
        private int m_previousGen2;
        private RuntimeCounterSnapshot m_lastSnapshot = RuntimeCounterSnapshot.Unavailable();
        private int m_disposed;

        internal RuntimeCounterSampler(double intervalSeconds = 0.25)
        {
            m_intervalTicks = ToIntervalTicks(intervalSeconds);
            // Establish the baseline before the first frame callback so the first sample reports
            // collections observed by the profiler, not the process lifetime total.
            m_previousGen0 = GC.CollectionCount(0);
            m_previousGen1 = GC.CollectionCount(1);
            m_previousGen2 = GC.CollectionCount(2);

            Type? profiler = FindType("UnityEngine.Profiling.Profiler", "UnityEngine.CoreModule");
            m_unityAllocated = CreateLongGetter(profiler, "GetTotalAllocatedMemoryLong");
            m_unityReserved = CreateLongGetter(profiler, "GetTotalReservedMemoryLong");
            m_unityUnusedReserved = CreateLongGetter(profiler, "GetTotalUnusedReservedMemoryLong");
            m_unityGraphics = CreateLongGetter(profiler, "GetAllocatedMemoryForGraphicsDriver");
            m_monoUsed = CreateLongGetter(profiler, "GetMonoUsedSizeLong");
            m_monoHeap = CreateLongGetter(profiler, "GetMonoHeapSizeLong");

            m_supportedUnityCounters =
                (m_unityAllocated is null ? 0 : UnityAllocated) |
                (m_unityReserved is null ? 0 : UnityReserved) |
                (m_unityUnusedReserved is null ? 0 : UnityUnusedReserved) |
                (m_unityGraphics is null ? 0 : UnityGraphics) |
                (m_monoUsed is null ? 0 : MonoUsed) |
                (m_monoHeap is null ? 0 : MonoHeap);

            Type? recorderType = FindType("Unity.Profiling.ProfilerRecorder", "UnityEngine.CoreModule");
            Type? recorderOptionsType = FindType("Unity.Profiling.ProfilerRecorderOptions", "UnityEngine.CoreModule");
            m_gpuFrame = TryCreateRecorder(recorderType, recorderOptionsType, "Render", "GPU Frame Time", TimeNanosecondsUnit);
            m_mainThread = TryCreateRecorder(recorderType, recorderOptionsType, "Internal", "Main Thread", TimeNanosecondsUnit);
            m_renderThread = TryCreateRecorder(recorderType, recorderOptionsType, "Internal", "Render Thread", TimeNanosecondsUnit);
            m_drawCalls = TryCreateRecorder(recorderType, recorderOptionsType, "Render", "Draw Calls Count", CountUnit);
            m_batches = TryCreateRecorder(recorderType, recorderOptionsType, "Render", "Batches Count", CountUnit);
            m_triangles = TryCreateRecorder(recorderType, recorderOptionsType, "Render", "Triangles Count", CountUnit);
            m_vertices = TryCreateRecorder(recorderType, recorderOptionsType, "Render", "Vertices Count", CountUnit);
            m_gcAlloc = TryCreateRecorder(recorderType, recorderOptionsType, "Internal", "GC.Alloc", BytesUnit);

            m_supportedProfilerCounters =
                (m_gpuFrame is null ? 0 : ProfilerGpuFrame) |
                (m_mainThread is null ? 0 : ProfilerMainThread) |
                (m_renderThread is null ? 0 : ProfilerRenderThread) |
                (m_drawCalls is null ? 0 : ProfilerDrawCalls) |
                (m_batches is null ? 0 : ProfilerBatches) |
                (m_triangles is null ? 0 : ProfilerTriangles) |
                (m_vertices is null ? 0 : ProfilerVertices) |
                (m_gcAlloc is null ? 0 : ProfilerGcAlloc);
            m_supportSummary = BuildSupportSummary();
        }

        internal int SupportedUnityCounters => m_supportedUnityCounters;
        internal int SupportedProfilerCounters => m_supportedProfilerCounters;
        internal string SupportSummary => m_supportSummary;

        internal string GpuTelemetryStatus => m_gpuFrame is null
            ? "unavailable: no trusted player-build GPU time counter"
            : "available: Unity ProfilerRecorder Render/GPU Frame Time";

        internal double IntervalMilliseconds => Volatile.Read(ref m_intervalTicks) * 1000.0 / Stopwatch.Frequency;

        internal void UpdateIntervalSeconds(double intervalSeconds) => Interlocked.Exchange(ref m_intervalTicks, ToIntervalTicks(intervalSeconds));

        internal RuntimeCounterSnapshot Read(long timestamp, bool force = false)
        {
            try
            {
                int gen0 = GC.CollectionCount(0);
                int gen1 = GC.CollectionCount(1);
                int gen2 = GC.CollectionCount(2);
                RecorderSnapshot recorder = ReadProfilerRecorders();
                bool readMemory = force || m_lastSampleTimestamp <= 0 ||
                                  timestamp - m_lastSampleTimestamp >= Volatile.Read(ref m_intervalTicks);
                if (!readMemory)
                {
                    RuntimeCounterSnapshot interval = WithGcDeltas(timestamp, gen0, gen1, gen2, recorder);
                    m_previousGen0 = gen0;
                    m_previousGen1 = gen1;
                    m_previousGen2 = gen2;
                    m_lastSnapshot = interval;
                    return interval;
                }

                long managedHeap = NormalizeBytes(GC.GetTotalMemory(false));
                long unityAllocated = ReadBytes(m_unityAllocated, zeroMeansUnavailable: true);
                long unityReserved = ReadBytes(m_unityReserved, zeroMeansUnavailable: true);
                long unityUnusedReserved = ReadBytes(m_unityUnusedReserved, zeroMeansUnavailable: true);
                // Unity's graphics-driver getter is not dedicated VRAM. In a number of player/API
                // combinations it returns zero even though graphics allocations exist, so zero is
                // explicitly classified as unavailable instead of being reported as 0 B.
                long unityGraphics = ReadBytes(m_unityGraphics, zeroMeansUnavailable: true);
                long monoUsed = ReadBytes(m_monoUsed, zeroMeansUnavailable: true);
                long monoHeap = ReadBytes(m_monoHeap, zeroMeansUnavailable: true);

                var current = new RuntimeCounterSnapshot(
                    true,
                    timestamp,
                    managedHeap,
                    unityAllocated,
                    unityReserved,
                    unityUnusedReserved,
                    unityGraphics,
                    monoUsed,
                    monoHeap,
                    CounterDelta(gen0, m_previousGen0),
                    CounterDelta(gen1, m_previousGen1),
                    CounterDelta(gen2, m_previousGen2),
                    Delta(managedHeap, m_previousManagedHeapBytes),
                    Delta(unityAllocated, m_previousUnityAllocatedBytes),
                    Delta(unityGraphics, m_previousUnityGraphicsBytes),
                    recorder.GpuFrameTicks,
                    recorder.GpuFrameTrusted,
                    m_supportedUnityCounters,
                    recorder.MainThreadTicks,
                    recorder.RenderThreadTicks,
                    recorder.DrawCalls,
                    recorder.Batches,
                    recorder.Triangles,
                    recorder.Vertices,
                    recorder.GcAllocatedBytes);

                m_previousManagedHeapBytes = managedHeap;
                m_previousUnityAllocatedBytes = unityAllocated;
                m_previousUnityGraphicsBytes = unityGraphics;
                m_previousGen0 = gen0;
                m_previousGen1 = gen1;
                m_previousGen2 = gen2;
                m_lastSampleTimestamp = timestamp;
                m_lastSnapshot = current;
                return current;
            }
            catch
            {
                m_lastSampleTimestamp = timestamp;
                m_lastSnapshot = RuntimeCounterSnapshot.Unavailable(timestamp);
                return m_lastSnapshot;
            }
        }

        internal long MeasureOverhead(int iterations)
        {
            long start = Stopwatch.GetTimestamp();
            long timestamp = start;
            for (int index = 0; index < iterations; index++)
            {
                Read(timestamp++, force: false);
            }
            return Math.Max(0, Stopwatch.GetTimestamp() - start);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_gpuFrame?.Dispose();
            m_mainThread?.Dispose();
            m_renderThread?.Dispose();
            m_drawCalls?.Dispose();
            m_batches?.Dispose();
            m_triangles?.Dispose();
            m_vertices?.Dispose();
            m_gcAlloc?.Dispose();
        }

        private RuntimeCounterSnapshot WithGcDeltas(
            long timestamp,
            int gen0,
            int gen1,
            int gen2,
            RecorderSnapshot recorder) =>
            new(
                m_lastSnapshot.Available,
                timestamp,
                m_lastSnapshot.ManagedHeapBytes,
                m_lastSnapshot.UnityAllocatedBytes,
                m_lastSnapshot.UnityReservedBytes,
                m_lastSnapshot.UnityUnusedReservedBytes,
                m_lastSnapshot.UnityGraphicsBytes,
                m_lastSnapshot.MonoUsedBytes,
                m_lastSnapshot.MonoHeapBytes,
                CounterDelta(gen0, m_previousGen0),
                CounterDelta(gen1, m_previousGen1),
                CounterDelta(gen2, m_previousGen2),
                0,
                0,
                0,
                recorder.GpuFrameTicks,
                recorder.GpuFrameTrusted,
                m_lastSnapshot.SupportedUnityCounters,
                recorder.MainThreadTicks,
                recorder.RenderThreadTicks,
                recorder.DrawCalls,
                recorder.Batches,
                recorder.Triangles,
                recorder.Vertices,
                recorder.GcAllocatedBytes);

        private RecorderSnapshot ReadProfilerRecorders()
        {
            return new RecorderSnapshot(
                ReadTimeTicks(m_gpuFrame, trusted: true, out bool gpuTrusted),
                gpuTrusted,
                ReadTimeTicks(m_mainThread, trusted: false, out _),
                ReadTimeTicks(m_renderThread, trusted: false, out _),
                ReadCount(m_drawCalls),
                ReadCount(m_batches),
                ReadCount(m_triangles),
                ReadCount(m_vertices),
                ReadBytes(m_gcAlloc, zeroMeansUnavailable: false));
        }

        private static long ReadTimeTicks(ProfilerRecorderHandle? recorder, bool trusted, out bool trustedResult)
        {
            trustedResult = false;
            if (recorder is null || !recorder.TryRead(out long nanoseconds) || nanoseconds <= 0)
            {
                return -1;
            }

            long ticks = NanosecondsToStopwatchTicks(nanoseconds);
            trustedResult = trusted && ticks >= 0;
            return ticks;
        }

        private static long ReadCount(ProfilerRecorderHandle? recorder) =>
            recorder is null || !recorder.TryRead(out long value) || value < 0 ? -1 : value;

        private static long ReadBytes(ProfilerRecorderHandle? recorder, bool zeroMeansUnavailable) =>
            recorder is null || !recorder.TryRead(out long value)
                ? -1
                : NormalizeBytes(value, zeroMeansUnavailable);

        private static long ReadBytes(Func<long>? getter, bool zeroMeansUnavailable)
        {
            if (getter is null)
            {
                return -1;
            }
            try
            {
                return NormalizeBytes(getter(), zeroMeansUnavailable);
            }
            catch
            {
                return -1;
            }
        }

        private static long NormalizeBytes(long value, bool zeroMeansUnavailable = false) =>
            value > 0 || !zeroMeansUnavailable && value == 0 ? value : -1;

        private static long Delta(long current, long previous) =>
            current < 0 || previous < 0 ? 0 : current - previous;

        private static int CounterDelta(int current, int previous) =>
            previous <= current ? current - previous : 0;

        private static long NanosecondsToStopwatchTicks(long nanoseconds)
        {
            double ticks = nanoseconds * (double)Stopwatch.Frequency / 1_000_000_000.0;
            return ticks >= long.MaxValue ? long.MaxValue : Math.Max(0, (long)Math.Round(ticks, MidpointRounding.AwayFromZero));
        }

        private ProfilerRecorderHandle? TryCreateRecorder(
            Type? recorderType,
            Type? optionsType,
            string category,
            string statName,
            int expectedUnit)
        {
            if (recorderType is null || optionsType is null)
            {
                return null;
            }

            try
            {
                ConstructorInfo? constructor = recorderType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string), typeof(string), typeof(int), optionsType },
                    null);
                if (constructor is null)
                {
                    return null;
                }

                // ReSharper disable once RedundantCast
                object recorder = constructor.Invoke(new[] { (object)category, statName, 1, Enum.ToObject(optionsType, ProfilerRecorderDefaultOptions) });
                var handle = ProfilerRecorderHandle.Create(recorder, recorderType, expectedUnit);
                if (handle is null || !handle.IsValid)
                {
                    handle?.Dispose();
                    return null;
                }
                return handle;
            }
            catch
            {
                return null;
            }
        }

        private string BuildSupportSummary()
        {
            StringBuilder builder = new StringBuilder(768)
                .Append("unity: allocated=").Append(UnityStatus(m_unityAllocated, "Profiler.GetTotalAllocatedMemoryLong"))
                .Append(", reserved=").Append(UnityStatus(m_unityReserved, "Profiler.GetTotalReservedMemoryLong"))
                .Append(", unused-reserved=").Append(UnityStatus(m_unityUnusedReserved, "Profiler.GetTotalUnusedReservedMemoryLong"))
                .Append(", graphics-driver=").Append(UnityStatus(m_unityGraphics, "Profiler.GetAllocatedMemoryForGraphicsDriver"))
                .Append(", mono-used=").Append(UnityStatus(m_monoUsed, "Profiler.GetMonoUsedSizeLong"))
                .Append(", mono-heap=").Append(UnityStatus(m_monoHeap, "Profiler.GetMonoHeapSizeLong"))
                .Append("; profiler: gpu-frame=").Append(RecorderStatus(m_gpuFrame, "Render/GPU Frame Time"))
                .Append(", main-thread=").Append(RecorderStatus(m_mainThread, "Internal/Main Thread"))
                .Append(", render-thread=").Append(RecorderStatus(m_renderThread, "Internal/Render Thread"))
                .Append(", draw-calls=").Append(RecorderStatus(m_drawCalls, "Render/Draw Calls Count"))
                .Append(", batches=").Append(RecorderStatus(m_batches, "Render/Batches Count"))
                .Append(", triangles=").Append(RecorderStatus(m_triangles, "Render/Triangles Count"))
                .Append(", vertices=").Append(RecorderStatus(m_vertices, "Render/Vertices Count"))
                .Append(", gc-alloc=").Append(RecorderStatus(m_gcAlloc, "Internal/GC.Alloc"));
            return builder.ToString();
        }

        private static string UnityStatus(Func<long>? getter, string name) =>
            getter is null ? "unavailable" : name + " (bytes; zero=unavailable)";

        private static string RecorderStatus(ProfilerRecorderHandle? recorder, string name) =>
            recorder is null ? "unavailable" : name;

        private static long ToIntervalTicks(double intervalSeconds)
        {
            if (double.IsNaN(intervalSeconds) || double.IsInfinity(intervalSeconds))
            {
                intervalSeconds = 0.25;
            }
            return Math.Max(
                1,
                (long)Math.Round(
                    Math.Max(MinimumIntervalSeconds, Math.Min(MaximumIntervalSeconds, intervalSeconds)) * Stopwatch.Frequency,
                    MidpointRounding.AwayFromZero));
        }

        private static Func<long>? CreateLongGetter(Type? type, string methodName)
        {
            try
            {
                MethodInfo? method = type?.GetMethod(
                    methodName,
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method is null || !method.IsStatic || method.ReturnType == typeof(void))
                {
                    return null;
                }

                var getter = new DynamicMethod(
                    "TajsProfiler" + methodName,
                    typeof(long),
                    Type.EmptyTypes,
                    typeof(RuntimeCounterSampler).Module,
                    true);
                ILGenerator il = getter.GetILGenerator();
                il.Emit(OpCodes.Call, method);
                if (method.ReturnType != typeof(long))
                {
                    il.Emit(OpCodes.Conv_I8);
                }
                il.Emit(OpCodes.Ret);
                return (Func<long>)getter.CreateDelegate(typeof(Func<long>));
            }
            catch
            {
                return null;
            }
        }

        private static Type? FindType(string fullName, string assemblyName)
        {
            var type = Type.GetType(fullName + ", " + assemblyName, false);
            return type ?? FindLoadedType(fullName, assemblyName);
        }

        private static Type? FindLoadedType(string fullName, string assemblyName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                {
                    return assembly.GetType(fullName, false);
                }
            }
            return null;
        }

        private readonly struct RecorderSnapshot
        {
            internal RecorderSnapshot(
                long gpuFrameTicks,
                bool gpuFrameTrusted,
                long mainThreadTicks,
                long renderThreadTicks,
                long drawCalls,
                long batches,
                long triangles,
                long vertices,
                long gcAllocatedBytes)
            {
                GpuFrameTicks = gpuFrameTicks;
                GpuFrameTrusted = gpuFrameTrusted;
                MainThreadTicks = mainThreadTicks;
                RenderThreadTicks = renderThreadTicks;
                DrawCalls = drawCalls;
                Batches = batches;
                Triangles = triangles;
                Vertices = vertices;
                GcAllocatedBytes = gcAllocatedBytes;
            }

            internal long GpuFrameTicks { get; }
            internal bool GpuFrameTrusted { get; }
            internal long MainThreadTicks { get; }
            internal long RenderThreadTicks { get; }
            internal long DrawCalls { get; }
            internal long Batches { get; }
            internal long Triangles { get; }
            internal long Vertices { get; }
            internal long GcAllocatedBytes { get; }
        }

        private sealed class ProfilerRecorderHandle : IDisposable
        {
            private readonly object m_recorder;
            private readonly Func<object, bool> m_valid;
            private readonly Func<object, long> m_lastValue;
            private readonly Action<object> m_dispose;
            private int m_disposed;

            private ProfilerRecorderHandle(
                object recorder,
                Func<object, bool> valid,
                Func<object, long> lastValue,
                Action<object> dispose)
            {
                m_recorder = recorder;
                m_valid = valid;
                m_lastValue = lastValue;
                m_dispose = dispose;
            }

            internal bool IsValid
            {
                get
                {
                    try
                    {
                        return Volatile.Read(ref m_disposed) == 0 && m_valid(m_recorder);
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            internal bool TryRead(out long value)
            {
                value = -1;
                if (!IsValid)
                {
                    return false;
                }
                try
                {
                    value = m_lastValue(m_recorder);
                    return value >= 0;
                }
                catch
                {
                    value = -1;
                    return false;
                }
            }

            internal static ProfilerRecorderHandle? Create(object recorder, Type recorderType, int expectedUnit)
            {
                MethodInfo? validGetter = recorderType.GetProperty("Valid", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetGetMethod(true);
                MethodInfo? lastValueGetter = recorderType.GetProperty("LastValue", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetGetMethod(true);
                MethodInfo? unitGetter = recorderType.GetProperty("UnitType", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetGetMethod(true);
                MethodInfo? dispose = recorderType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public);
                if (validGetter is null || lastValueGetter is null || unitGetter is null || dispose is null ||
                    lastValueGetter.ReturnType != typeof(long))
                {
                    return null;
                }

                Func<object, bool>? valid = CreateBoolGetter(recorderType, validGetter);
                Func<object, long>? lastValue = CreateLongGetter(recorderType, lastValueGetter);
                Func<object, int>? unit = CreateIntGetter(recorderType, unitGetter);
                Action<object>? release = CreateDisposer(recorderType, dispose);
                if (valid is null || lastValue is null || unit is null || release is null)
                {
                    release?.Invoke(recorder);
                    return null;
                }

                int actualUnit;
                try
                {
                    actualUnit = unit(recorder);
                }
                catch
                {
                    release(recorder);
                    return null;
                }
                if (actualUnit != expectedUnit)
                {
                    release(recorder);
                    return null;
                }

                return new ProfilerRecorderHandle(recorder, valid, lastValue, release);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                try
                {
                    m_dispose(m_recorder);
                }
                catch
                {
                    // Counter handles are optional diagnostic resources.
                }
            }

            private static Func<object, bool>? CreateBoolGetter(Type owner, MethodInfo getter) =>
                CreateGetter<bool>(owner, getter, emitConversion: null);

            private static Func<object, long>? CreateLongGetter(Type owner, MethodInfo getter) =>
                CreateGetter<long>(owner, getter, emitConversion: getter.ReturnType == typeof(long) ? null : OpCodes.Conv_I8);

            private static Func<object, int>? CreateIntGetter(Type owner, MethodInfo getter) =>
                CreateGetter<int>(owner, getter, emitConversion: getter.ReturnType == typeof(int) ? null : OpCodes.Conv_I4);

            private static Func<object, T>? CreateGetter<T>(Type owner, MethodInfo getter, OpCode? emitConversion)
            {
                try
                {
                    var method = new DynamicMethod(
                        "ReadTajsProfilerRecorder",
                        typeof(T),
                        new[] { typeof(object) },
                        typeof(RuntimeCounterSampler).Module,
                        true);
                    ILGenerator il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Unbox_Any, owner);
                    il.Emit(getter.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, getter);
                    if (emitConversion.HasValue)
                    {
                        il.Emit(emitConversion.Value);
                    }
                    il.Emit(OpCodes.Ret);
                    return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
                }
                catch
                {
                    return null;
                }
            }

            private static Action<object>? CreateDisposer(Type owner, MethodInfo dispose)
            {
                try
                {
                    var method = new DynamicMethod(
                        "DisposeTajsProfilerRecorder",
                        typeof(void),
                        new[] { typeof(object) },
                        typeof(RuntimeCounterSampler).Module,
                        true);
                    ILGenerator il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Unbox_Any, owner);
                    il.Emit(dispose.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, dispose);
                    il.Emit(OpCodes.Ret);
                    return (Action<object>)method.CreateDelegate(typeof(Action<object>));
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
