// Taj's COI Mods | ProductBufferShrinkFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi.Unity.InstancedRendering.Products;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using UnityEngine;

namespace TajsCOI.Performance.Features.ProductBufferShrink
{
    internal sealed class ProductBufferShrinkFeature : IPerformanceFeature
    {
        private const string HarmonyId = "TajsCOI.Performance.ProductBufferShrink";
        private static readonly ConditionalWeakTable<ProductsRenderer, RendererShrinkState> s_states = new();
        private static ITajsLogger? s_log;

        public string Id => "ProductBufferShrink";
        public string ConfigKey => ProductBufferShrinkSettings.EnableConfigKey;

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            MethodInfo? target = AccessTools.Method(typeof(ProductsRenderer), "uploadFrame", Type.EmptyTypes);
            if (target is null || target.IsStatic || target.ReturnType != typeof(void))
            {
                throw new MissingMethodException(typeof(ProductsRenderer).FullName, "uploadFrame()");
            }

            s_log = log;
            new Harmony(HarmonyId).Patch(
                target,
                prefix: new HarmonyMethod(typeof(ProductBufferShrinkFeature), nameof(BeforeUploadFrame)));

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                Id,
                CompatibilityState.Compatible,
                "0.8.7a ProductsRenderer live/reserve buffers and dirty upload path",
                $"Sustained under-utilization observer installed ({ProductBufferShrinkSettings.ObservationFrames} frames)",
                "Only remappable instance buffers can shrink; owner and persistent-slot identity buffers are untouched."));
        }

        private static void BeforeUploadFrame(
            ProductsRenderer __instance,
            ref GraphicsBuffer? ___m_liveBuffer,
            ref GraphicsBuffer? ___m_reserveBuffer,
            ref bool ___m_liveDirty,
            ref bool ___m_reserveDirty,
            int ___m_liveCountDraw,
            int ___m_reserveCount)
        {
            RendererShrinkState state = s_states.GetValue(
                __instance,
                _ => new RendererShrinkState(ProductBufferShrinkSettings.ObservationFrames));

            if (state.Live.Observe(___m_liveCountDraw, ___m_liveBuffer?.count ?? 0))
            {
                GraphicsBuffer liveBuffer = ___m_liveBuffer!;
                int oldCapacity = liveBuffer.count;
                liveBuffer.Release();
                ___m_liveBuffer = null;
                ___m_liveDirty = true;
                s_log?.Info($"Released under-utilized live product buffer ({___m_liveCountDraw}/{oldCapacity}); vanilla will rebuild it.");
            }

            if (state.Reserve.Observe(___m_reserveCount, ___m_reserveBuffer?.count ?? 0))
            {
                GraphicsBuffer reserveBuffer = ___m_reserveBuffer!;
                int oldCapacity = reserveBuffer.count;
                reserveBuffer.Release();
                ___m_reserveBuffer = null;
                ___m_reserveDirty = true;
                s_log?.Info($"Released under-utilized reserve product buffer ({___m_reserveCount}/{oldCapacity}); vanilla will rebuild it.");
            }
        }

        private sealed class RendererShrinkState
        {
            internal RendererShrinkState(int observationFrames)
            {
                Live = new BufferShrinkTracker(observationFrames);
                Reserve = new BufferShrinkTracker(observationFrames);
            }

            internal BufferShrinkTracker Live { get; }
            internal BufferShrinkTracker Reserve { get; }
        }
    }
}
