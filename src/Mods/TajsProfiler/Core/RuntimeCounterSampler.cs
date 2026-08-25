// Taj's COI Mods | RuntimeCounterSampler.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace TajsCOI.Profiler.Core
{
    /// <summary>
    ///     Samples only cumulative, supported player counters. Unity accessors are resolved once
    ///     and the sampler is rate limited so ordinary frame capture never performs reflection or
    ///     a forced collection.
    /// </summary>
    internal sealed class RuntimeCounterSampler
    {
        internal const int UnityAllocated = 1 << 0;
        internal const int UnityReserved = 1 << 1;
        internal const int UnityUnusedReserved = 1 << 2;
        internal const int UnityGraphics = 1 << 3;
        internal const int MonoUsed = 1 << 4;
        internal const int MonoHeap = 1 << 5;

        private readonly Func<long>? m_unityAllocated;
        private readonly Func<long>? m_unityReserved;
        private readonly Func<long>? m_unityUnusedReserved;
        private readonly Func<long>? m_unityGraphics;
        private readonly Func<long>? m_monoUsed;
        private readonly Func<long>? m_monoHeap;
        private readonly long m_intervalTicks;
        private readonly int m_supportedUnityCounters;
        private long m_lastSampleTimestamp;
        private long m_previousManagedHeapBytes = -1;
        private long m_previousUnityAllocatedBytes = -1;
        private long m_previousUnityGraphicsBytes = -1;
        private int m_previousGen0;
        private int m_previousGen1;
        private int m_previousGen2;
        private RuntimeCounterSnapshot m_lastSnapshot = RuntimeCounterSnapshot.Unavailable();

        internal RuntimeCounterSampler(double intervalSeconds = 0.25)
        {
            m_intervalTicks = Math.Max(1, (long)Math.Round(
                Math.Max(0.01, intervalSeconds) * Stopwatch.Frequency,
                MidpointRounding.AwayFromZero));
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
        }

        internal int SupportedUnityCounters => m_supportedUnityCounters;
        internal string GpuTelemetryStatus => "GPU frame timing unavailable: no trusted player-build counter was found.";

        internal RuntimeCounterSnapshot Read(long timestamp, bool force = false)
        {
            try
            {
                int gen0 = GC.CollectionCount(0);
                int gen1 = GC.CollectionCount(1);
                int gen2 = GC.CollectionCount(2);
                bool readMemory = force || m_lastSampleTimestamp <= 0 ||
                    timestamp - m_lastSampleTimestamp >= m_intervalTicks;
                if (!readMemory)
                {
                    RuntimeCounterSnapshot interval = WithGcDeltas(timestamp, gen0, gen1, gen2);
                    m_previousGen0 = gen0;
                    m_previousGen1 = gen1;
                    m_previousGen2 = gen2;
                    m_lastSnapshot = interval;
                    return interval;
                }

                long managedHeap = GC.GetTotalMemory(false);
                long unityAllocated = Read(m_unityAllocated);
                long unityReserved = Read(m_unityReserved);
                long unityUnusedReserved = Read(m_unityUnusedReserved);
                long unityGraphics = Read(m_unityGraphics);
                long monoUsed = Read(m_monoUsed);
                long monoHeap = Read(m_monoHeap);

                RuntimeCounterSnapshot current = new(
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
                    -1,
                    false,
                    m_supportedUnityCounters);

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

        private RuntimeCounterSnapshot WithGcDeltas(long timestamp, int gen0, int gen1, int gen2) =>
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
                m_lastSnapshot.GpuFrameTicks,
                m_lastSnapshot.GpuFrameTrusted,
                m_lastSnapshot.SupportedUnityCounters);

        private static int CounterDelta(int current, int previous) =>
            previous <= current ? current - previous : 0;

        private static long Delta(long current, long previous) =>
            current < 0 || previous < 0 ? 0 : current - previous;

        private static long Read(Func<long>? getter)
        {
            if (getter is null)
            {
                return -1;
            }
            try
            {
                return getter();
            }
            catch
            {
                return -1;
            }
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

                DynamicMethod getter = new DynamicMethod(
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
            Type? type = Type.GetType(fullName + ", " + assemblyName, false);
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
    }
}
