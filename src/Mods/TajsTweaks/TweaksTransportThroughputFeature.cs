// Taj's COI Mods | TweaksTransportThroughputFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Factory.Lifts;
using Mafi.Core.Factory.Zippers;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Extends the existing storage-throughput policy to the transport components that
    ///     calculate their delay from connected-port throughput. The game recalculates a vanilla
    ///     delay first; the postfix then applies the multiplier once, so repeated recomputations
    ///     cannot compound the adjustment.
    /// </summary>
    internal static class TweaksTransportThroughputFeature
    {
        internal static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(Zipper), "recomputeBufferSizeAndThresholds", nameof(ZipperPostfix));
            Patch(harmony, typeof(MiniZipper), "recomputeBufferSizeAndThresholds", nameof(MiniZipperPostfix));
            Patch(harmony, typeof(Lift), "recomputeBufferSizeAndDelay", nameof(LiftPostfix));
        }

        private static void Patch(Harmony harmony, Type type, string methodName, string postfixName)
        {
            FieldInfo? delay = type.GetField("m_delay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? method = AccessTools.Method(type, methodName);
            if (delay is null || method is null)
            {
                throw new MissingMethodException(type.FullName, methodName + "/m_delay");
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(TweaksTransportThroughputFeature), postfixName));
        }

        private static void ZipperPostfix(Zipper __instance) => ScaleDelay(__instance, typeof(Zipper));

        private static void MiniZipperPostfix(MiniZipper __instance) => ScaleDelay(__instance, typeof(MiniZipper));

        private static void LiftPostfix(Lift __instance) => ScaleDelay(__instance, typeof(Lift));

        private static void ScaleDelay(object instance, Type type)
        {
            double multiplier = TajsTweaksRuntimeState.StorageOverrides ? TajsTweaksRuntimeState.StorageThroughputMultiplier : 1d;
            if (multiplier <= 1d)
            {
                return;
            }
            try
            {
                FieldInfo? delay = type.GetField("m_delay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (delay?.GetValue(instance) is Duration duration)
                {
                    int ticks = Math.Max(1, (int)Math.Round(duration.Ticks / multiplier, MidpointRounding.AwayFromZero));
                    delay.SetValue(instance, new Duration(ticks));
                }
            }
            catch
            {
                // Transport throughput is optional; a changed private field leaves vanilla timing.
            }
        }
    }
}
