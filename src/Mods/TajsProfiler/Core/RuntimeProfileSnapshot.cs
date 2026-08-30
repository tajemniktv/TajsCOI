// Taj's COI Mods | RuntimeProfileSnapshot.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace TajsCOI.Profiler.Core
{
    internal sealed class RuntimeProfileSnapshot
    {
        internal RuntimeProfileSnapshot(
            string label,
            long sequence,
            long resetGeneration,
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
            ProductRendererMetric products,
            IReadOnlyDictionary<string, StageMetric> stages,
            IReadOnlyList<GcPassMetric> gcPasses,
            long gcPassSequence)
        {
            Label = label;
            Sequence = sequence;
            ResetGeneration = resetGeneration;
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
            Products = products;
            Stages = stages;
            GcPasses = gcPasses;
            GcPassSequence = gcPassSequence;
        }

        internal string Label { get; }
        internal long Sequence { get; }
        internal long ResetGeneration { get; }
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
        internal ProductRendererMetric Products { get; }
        internal IReadOnlyDictionary<string, StageMetric> Stages { get; }
        internal IReadOnlyList<GcPassMetric> GcPasses { get; }
        internal long GcPassSequence { get; }
    }

    internal readonly struct ProductRendererMetric
    {
        internal ProductRendererMetric(
            bool available,
            int instances,
            int gpuInstances,
            int liveSlots,
            int highWaterSlots,
            int capacitySlots,
            int fragmentedSlots,
            int freeRangeCount,
            int largestFreeRange,
            int liveBufferUsed,
            int liveBufferCapacity,
            int reserveBufferUsed,
            int reserveBufferCapacity,
            long instancesBytes,
            long staticOwnersBytes,
            long dynamicOwnersBytes,
            long slotsBytes,
            long texturesBytes,
            string reason)
        {
            Available = available;
            Instances = instances;
            GpuInstances = gpuInstances;
            LiveSlots = liveSlots;
            HighWaterSlots = highWaterSlots;
            CapacitySlots = capacitySlots;
            FragmentedSlots = fragmentedSlots;
            FreeRangeCount = freeRangeCount;
            LargestFreeRange = largestFreeRange;
            LiveBufferUsed = liveBufferUsed;
            LiveBufferCapacity = liveBufferCapacity;
            ReserveBufferUsed = reserveBufferUsed;
            ReserveBufferCapacity = reserveBufferCapacity;
            InstancesBytes = instancesBytes;
            StaticOwnersBytes = staticOwnersBytes;
            DynamicOwnersBytes = dynamicOwnersBytes;
            SlotsBytes = slotsBytes;
            TexturesBytes = texturesBytes;
            Reason = reason;
        }

        internal bool Available { get; }
        internal int Instances { get; }
        internal int GpuInstances { get; }
        internal int LiveSlots { get; }
        internal int HighWaterSlots { get; }
        internal int CapacitySlots { get; }
        internal int FragmentedSlots { get; }
        internal int FreeRangeCount { get; }
        internal int LargestFreeRange { get; }
        internal int LiveBufferUsed { get; }
        internal int LiveBufferCapacity { get; }
        internal int ReserveBufferUsed { get; }
        internal int ReserveBufferCapacity { get; }
        internal long InstancesBytes { get; }
        internal long StaticOwnersBytes { get; }
        internal long DynamicOwnersBytes { get; }
        internal long SlotsBytes { get; }
        internal long TexturesBytes { get; }
        internal long GpuBytes => InstancesBytes + StaticOwnersBytes + DynamicOwnersBytes + SlotsBytes + TexturesBytes;
        internal string Reason { get; }
        internal int UnusedCapacitySlots => Math.Max(0, CapacitySlots - HighWaterSlots);
        internal int TotalFreeSlots => Math.Max(0, CapacitySlots - LiveSlots);
        internal double Utilization => CapacitySlots > 0 ? LiveSlots * 100.0 / CapacitySlots : 0.0;
    }

    internal readonly struct GcPassMetric
    {
        internal GcPassMetric(long sequence, long elapsedTicks, long beforeBytes, long afterBytes, int gen0, int gen1, int gen2)
            : this(sequence, 0, elapsedTicks, 0, beforeBytes, afterBytes, gen0, gen1, gen2)
        {
        }

        internal GcPassMetric(
            long sequence,
            int passNumber,
            long elapsedTicks,
            long finalizerDrainElapsedTicks,
            long beforeBytes,
            long afterBytes,
            int gen0,
            int gen1,
            int gen2)
        {
            Sequence = sequence;
            PassNumber = passNumber;
            ElapsedTicks = elapsedTicks;
            FinalizerDrainElapsedTicks = finalizerDrainElapsedTicks;
            BeforeBytes = beforeBytes;
            AfterBytes = afterBytes;
            Gen0Collections = gen0;
            Gen1Collections = gen1;
            Gen2Collections = gen2;
        }

        internal long Sequence { get; }
        internal int PassNumber { get; }
        internal long ElapsedTicks { get; }
        internal long FinalizerDrainElapsedTicks { get; }
        internal long BeforeBytes { get; }
        internal long AfterBytes { get; }
        internal long ReclaimedBytes => BeforeBytes - AfterBytes;
        internal int Gen0Collections { get; }
        internal int Gen1Collections { get; }
        internal int Gen2Collections { get; }
        internal double ElapsedMilliseconds => ElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        internal double FinalizerDrainElapsedMilliseconds =>
            FinalizerDrainElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }
}
