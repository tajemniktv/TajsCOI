// Taj's COI Mods | LifecycleCheckpoint.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Profiler.Core
{
    internal sealed class LifecycleCheckpoint
    {
        internal LifecycleCheckpoint(
            string label,
            long sequence,
            DateTime capturedUtc,
            long processWorkingSetBytes,
            long processPrivateBytes,
            long processCpuMilliseconds,
            long managedBytes,
            long monoUsedBytes,
            long monoHeapBytes,
            long unityAllocatedBytes,
            long unityReservedBytes,
            long unityUnusedReservedBytes,
            long unityGraphicsBytes,
            int gen0Collections,
            int gen1Collections,
            int gen2Collections)
        {
            Label = label;
            Sequence = sequence;
            CapturedUtc = capturedUtc;
            ProcessWorkingSetBytes = processWorkingSetBytes;
            ProcessPrivateBytes = processPrivateBytes;
            ProcessCpuMilliseconds = processCpuMilliseconds;
            ManagedBytes = managedBytes;
            MonoUsedBytes = monoUsedBytes;
            MonoHeapBytes = monoHeapBytes;
            UnityAllocatedBytes = unityAllocatedBytes;
            UnityReservedBytes = unityReservedBytes;
            UnityUnusedReservedBytes = unityUnusedReservedBytes;
            UnityGraphicsBytes = unityGraphicsBytes;
            Gen0Collections = gen0Collections;
            Gen1Collections = gen1Collections;
            Gen2Collections = gen2Collections;
        }

        internal string Label { get; }
        internal long Sequence { get; }
        internal DateTime CapturedUtc { get; }
        internal long ProcessWorkingSetBytes { get; }
        internal long ProcessPrivateBytes { get; }
        internal long ProcessCpuMilliseconds { get; }
        internal long ManagedBytes { get; }
        internal long MonoUsedBytes { get; }
        internal long MonoHeapBytes { get; }
        internal long UnityAllocatedBytes { get; }
        internal long UnityReservedBytes { get; }
        internal long UnityUnusedReservedBytes { get; }
        internal long UnityGraphicsBytes { get; }
        internal int Gen0Collections { get; }
        internal int Gen1Collections { get; }
        internal int Gen2Collections { get; }
    }
}
