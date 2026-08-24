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

        public bool IsProcessPatchInstalled()
        {
            MethodInfo? target = FindTarget();
            MethodInfo? patchMethod = AccessTools.Method(typeof(ProductBufferShrinkFeature), nameof(BeforeUploadFrame));
            return target is not null && patchMethod is not null &&
                ProcessHarmonyPatchOwnership.HasExpected(
                    Harmony.GetPatchInfo(target)?.Prefixes,
                    HarmonyId,
                    patchMethod);
        }

        public void Install(ITajsRuntime runtime, ITajsLogger log)
        {
            MethodInfo? target = FindTarget();
            if (target is null || target.IsStatic || target.ReturnType != typeof(void))
            {
                throw new MissingMethodException(typeof(ProductsRenderer).FullName, "uploadFrame()");
            }

            // Harmony callbacks are static, so the process-lifetime feature logger is intentionally
            // published to static callback state from this one-time installation method.
#pragma warning disable S2696
            s_log = log;
#pragma warning restore S2696
            // Target: ProductsRenderer.uploadFrame(). This behavior-changing prefix releases only
            // validated remappable buffers after a sustained low-utilization window; a missing target
            // disables installation, and the feature-specific Harmony owner lives for the process.
            MethodInfo patchMethod = AccessTools.Method(typeof(ProductBufferShrinkFeature), nameof(BeforeUploadFrame))!;
            lock (s_installGate)
            {
                Patches? patches = Harmony.GetPatchInfo(target);
                if (ProcessHarmonyPatchOwnership.HasExpected(patches?.Prefixes, HarmonyId, patchMethod))
                {
                    log.Info("Already installed / compatible; the process-lifetime buffer patch was not applied again.");
                    runtime.ReportCompatibility(new CompatibilityReport(
                        "TajsPerformance", Id, CompatibilityState.Compatible,
                        "Existing process-lifetime Harmony owner and prefix method",
                        "Already installed / compatible",
                        "The validated 0.8.7a buffer patch remains active; no duplicate prefix was registered."));
                    return;
                }

                if (ProcessHarmonyPatchOwnership.HasOwner(patches?.Prefixes, HarmonyId))
                {
                    throw new InvalidOperationException(
                        $"Existing Harmony owner '{HarmonyId}' has an unexpected buffer prefix ({ProcessHarmonyPatchOwnership.Describe(patches)}).");
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

            runtime.ReportCompatibility(new CompatibilityReport(
                "TajsPerformance",
                Id,
                CompatibilityState.Compatible,
                "0.8.7a ProductsRenderer live/reserve buffers and dirty upload path",
                $"Sustained under-utilization observer installed ({ProductBufferShrinkSettings.ObservationFrames} frames)",
                "Only remappable instance buffers can shrink; owner and persistent-slot identity buffers are untouched."));
        }

        private static readonly object s_installGate = new();

        private static MethodInfo? FindTarget() => AccessTools.Method(typeof(ProductsRenderer), "uploadFrame", Type.EmptyTypes);

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

            GraphicsBuffer? liveBuffer = ___m_liveBuffer;
            bool shrinkLive = state.Live.Observe(___m_liveCountDraw, liveBuffer?.count ?? 0);
            if (liveBuffer is not null && shrinkLive)
            {
                int oldCapacity = liveBuffer.count;
                liveBuffer.Release();
                ___m_liveBuffer = null;
                ___m_liveDirty = true;
                s_log?.Info($"Released under-utilized live product buffer ({___m_liveCountDraw}/{oldCapacity}); vanilla will rebuild it.");
            }

            GraphicsBuffer? reserveBuffer = ___m_reserveBuffer;
            bool shrinkReserve = state.Reserve.Observe(___m_reserveCount, reserveBuffer?.count ?? 0);
            if (reserveBuffer is not null && shrinkReserve)
            {
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
