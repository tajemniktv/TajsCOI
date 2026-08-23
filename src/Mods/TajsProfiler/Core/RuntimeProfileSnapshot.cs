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
            DateTime capturedUtc,
            long managedBytes,
            long unityAllocatedBytes,
            long unityReservedBytes,
            long unityGraphicsBytes,
            ProductRendererMetric products,
            IReadOnlyDictionary<string, StageMetric> stages)
        {
            Label = label;
            Sequence = sequence;
            CapturedUtc = capturedUtc;
            ManagedBytes = managedBytes;
            UnityAllocatedBytes = unityAllocatedBytes;
            UnityReservedBytes = unityReservedBytes;
            UnityGraphicsBytes = unityGraphicsBytes;
            Products = products;
            Stages = stages;
        }

        internal string Label { get; }
        internal long Sequence { get; }
        internal DateTime CapturedUtc { get; }
        internal long ManagedBytes { get; }
        internal long UnityAllocatedBytes { get; }
        internal long UnityReservedBytes { get; }
        internal long UnityGraphicsBytes { get; }
        internal ProductRendererMetric Products { get; }
        internal IReadOnlyDictionary<string, StageMetric> Stages { get; }
    }

    internal readonly struct ProductRendererMetric
    {
        internal ProductRendererMetric(
            bool available,
            int instances,
            int gpuInstances,
            int usedSlots,
            int capacitySlots,
            int fragmentedSlots,
            int freeRangeCount,
            int largestFreeRange,
            long gpuBytes,
            string reason)
        {
            Available = available;
            Instances = instances;
            GpuInstances = gpuInstances;
            UsedSlots = usedSlots;
            CapacitySlots = capacitySlots;
            FragmentedSlots = fragmentedSlots;
            FreeRangeCount = freeRangeCount;
            LargestFreeRange = largestFreeRange;
            GpuBytes = gpuBytes;
            Reason = reason;
        }

        internal bool Available { get; }
        internal int Instances { get; }
        internal int GpuInstances { get; }
        internal int UsedSlots { get; }
        internal int CapacitySlots { get; }
        internal int FragmentedSlots { get; }
        internal int FreeRangeCount { get; }
        internal int LargestFreeRange { get; }
        internal long GpuBytes { get; }
        internal string Reason { get; }
        internal int UnusedSlots => Math.Max(0, CapacitySlots - UsedSlots);
        internal double Utilization => CapacitySlots > 0 ? UsedSlots * 100.0 / CapacitySlots : 0.0;
    }
}
