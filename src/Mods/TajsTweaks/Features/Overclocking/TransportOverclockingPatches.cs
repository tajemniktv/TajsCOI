// Taj's COI Mods | TransportOverclockingPatches.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Ports;
using Mafi.Core.Products;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    ///     Adds the transport-specific seams for issue #146. Belts and pipes share COI's Transport
    ///     entity, but only solid transports receive the optional inventory-density compensation.
    /// </summary>
    internal static class TransportOverclockingPatches
    {
        private static readonly ConditionalWeakTable<TransportTrajectory, Transport> s_trajectoryOwners = new();
        private static FieldInfo? s_spacingField;
        private static FieldInfo? s_stackField;
        private static MethodInfo? s_quantityMin;
        private static MethodInfo? s_effectiveSpacing;
        private static MethodInfo? s_effectiveStackMin;
        private static MethodInfo? s_effectiveStackValue;

        internal static bool SpeedSeamAvailable { get; private set; }

        internal static void Install(Harmony harmony)
        {
            try
            {
                ConstructorInfo? constructor = typeof(Transport).GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault();
                if (constructor is not null)
                {
                    harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TransportOverclockingPatches), nameof(TransportConstructedPostfix)));
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo? simUpdate = FindInterfaceImplementation(typeof(Transport), typeof(IEntityWithSimUpdate), "SimUpdate");
                if (simUpdate is not null)
                {
                    harmony.Patch(simUpdate, postfix: new HarmonyMethod(typeof(TransportOverclockingPatches), nameof(TransportSimUpdatePostfix)));
                    SpeedSeamAvailable = true;
                }
            }
            catch
            {
            }

            InstallCapacityPatches(harmony);
        }

        internal static void RegisterTransport(Transport transport)
        {
            try
            {
                TransportTrajectory trajectory = transport.Trajectory;
                s_trajectoryOwners.Remove(trajectory);
                s_trajectoryOwners.Add(trajectory, transport);
            }
            catch
            {
            }
        }

        internal static void TransportConstructedPostfix(Transport __instance) => RegisterTransport(__instance);

        internal static void TransportSimUpdatePostfix(Transport __instance) => TajsOverclockingFeature.Current?.AdvanceExtraCycles(__instance);

        internal static bool IsBelt(Transport transport)
        {
            try
            {
                ProductType allowed = transport.Prototype.PortsShape.AllowedProductType;
                return !allowed.Equals(FluidProductProto.ProductType) && !allowed.Equals(MoltenProductProto.ProductType);
            }
            catch
            {
                return false;
            }
        }

        internal static int EffectiveSpacing(int vanillaSpacing, Transport transport)
        {
            if (!CanCompensate(transport) || vanillaSpacing <= 1)
            {
                return vanillaSpacing;
            }

            int percent = TajsOverclockingFeature.GetPercentFor(transport);
            int maxPercent = Math.Max(100, TajsTweaksRuntimeState.OverclockMaxPercent);
            return OverclockingMath.RampedCapacityValue(
                vanillaSpacing,
                percent,
                maxPercent,
                TajsTweaksRuntimeState.OverclockTransportSpacingBonus,
                increase: false);
        }

        internal static Quantity EffectiveStackMin(ref Quantity productMax, Quantity protoMax, Transport transport)
        {
            Quantity vanilla = productMax.Value <= protoMax.Value ? productMax : protoMax;
            return EffectiveStackValue(vanilla, transport);
        }

        internal static Quantity EffectiveStackValue(Quantity protoMax, Transport transport)
        {
            if (!CanCompensate(transport) || protoMax.Value <= 0)
            {
                return protoMax;
            }

            int percent = TajsOverclockingFeature.GetPercentFor(transport);
            int maxPercent = Math.Max(100, TajsTweaksRuntimeState.OverclockMaxPercent);
            int value = OverclockingMath.RampedCapacityValue(
                protoMax.Value,
                percent,
                maxPercent,
                TajsTweaksRuntimeState.OverclockTransportStackBonus,
                increase: true);
            return value <= protoMax.Value ? protoMax : new Quantity(value);
        }

        internal static int EffectiveStackFor(Transport transport) =>
            EffectiveStackValue(transport.Prototype.MaxQuantityPerTransportedProduct, transport).Value;

        internal static string DescribeCapacity(Transport transport)
        {
            if (!IsBelt(transport))
            {
                return "transport capacity: pipe semantics (no belt compensation)";
            }

            int vanillaSpacing = transport.Prototype.ProductSpacingWaypoints;
            int effectiveSpacing = EffectiveSpacing(vanillaSpacing, transport);
            int effectiveStack = EffectiveStackFor(transport);
            int maxProducts = transport.Trajectory.MaxProducts;
            return "belt capacity: spacing " + effectiveSpacing + "/" + vanillaSpacing +
                   ", stack " + effectiveStack + ", trajectory slots " + maxProducts;
        }

        internal static void MaxProductsPostfix(TransportTrajectory __instance, ref int __result)
        {
            try
            {
                if (!s_trajectoryOwners.TryGetValue(__instance, out Transport? transport) || !CanCompensate(transport))
                {
                    return;
                }

                int vanillaSpacing = transport.Prototype.ProductSpacingWaypoints;
                int spacing = EffectiveSpacing(vanillaSpacing, transport);
                if (spacing >= 1 && spacing < vanillaSpacing)
                {
                    __result = (__instance.Waypoints.Length + spacing - 1) / spacing;
                }
            }
            catch
            {
                // Capacity reporting is optional; a seam failure leaves vanilla capacity intact.
            }
        }

        private static void InstallCapacityPatches(Harmony harmony)
        {
            BindingFlags instancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
            BindingFlags instanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            s_spacingField = typeof(TransportProto).GetField("ProductSpacingWaypoints", instanceAll);
            s_stackField = typeof(TransportProto).GetField("MaxQuantityPerTransportedProduct", instanceAll);
            s_quantityMin = typeof(Quantity).GetMethod("Min", instanceAll, null, new[] { typeof(Quantity) }, null);
            s_effectiveSpacing = typeof(TransportOverclockingPatches).GetMethod(
                nameof(EffectiveSpacing),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            s_effectiveStackMin = typeof(TransportOverclockingPatches).GetMethod(
                nameof(EffectiveStackMin),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            s_effectiveStackValue = typeof(TransportOverclockingPatches).GetMethod(
                nameof(EffectiveStackValue),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            MethodInfo? moveProducts = typeof(Transport).GetMethod("tryMoveProducts", instancePrivate);
            TryPatchTranspiler(harmony, moveProducts);
            TryPatchTranspiler(harmony, FindInterfaceImplementation(typeof(Transport), typeof(IEntityWithPorts), "ReceiveAsMuchAsFromPort"));
            TryPatchTranspiler(harmony, FindInterfaceImplementation(typeof(Transport), typeof(IEntityWithPortsEarlyExit), "CouldReceiveFromPortEarlyExit"));

            try
            {
                MethodInfo? maxProducts = typeof(TransportTrajectory).GetProperty("MaxProducts", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
                if (maxProducts is not null)
                {
                    harmony.Patch(maxProducts, postfix: new HarmonyMethod(typeof(TransportOverclockingPatches), nameof(MaxProductsPostfix)));
                }
            }
            catch
            {
            }
        }

        private static void TryPatchTranspiler(Harmony harmony, MethodInfo? target)
        {
            if (target is null || s_spacingField is null || s_effectiveSpacing is null)
            {
                return;
            }

            try
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(TransportOverclockingPatches), nameof(CapacityTranspiler)));
            }
            catch
            {
                // The speed seam remains useful when a future version changes one capacity method.
            }
        }

        private static IEnumerable<CodeInstruction> CapacityTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> source = new(instructions);
            if (s_spacingField is null || s_effectiveSpacing is null)
            {
                return source;
            }

            var result = new List<CodeInstruction>(source.Count + 16);
            for (int index = 0; index < source.Count; index++)
            {
                CodeInstruction instruction = source[index];
                result.Add(instruction);
                if (instruction.opcode != OpCodes.Ldfld || instruction.operand is not FieldInfo field)
                {
                    continue;
                }

                if (field == s_spacingField)
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, s_effectiveSpacing));
                    continue;
                }

                if (s_stackField is null || field != s_stackField)
                {
                    continue;
                }

                bool followedByMin = s_quantityMin is not null && index + 1 < source.Count &&
                                     source[index + 1].opcode == OpCodes.Call && source[index + 1].operand as MethodInfo == s_quantityMin;
                if (followedByMin && s_effectiveStackMin is not null)
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    source[index + 1].opcode = OpCodes.Call;
                    source[index + 1].operand = s_effectiveStackMin;
                }
                else if (!followedByMin && s_effectiveStackValue is not null)
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, s_effectiveStackValue));
                }
            }

            return result;
        }

        private static bool CanCompensate(Transport transport) =>
            TajsTweaksRuntimeState.Overclocking &&
            TajsTweaksRuntimeState.OverclockTransportCapacityCompensation &&
            IsBelt(transport) &&
            TajsOverclockingFeature.Current is not null;

        private static MethodInfo? FindInterfaceImplementation(Type type, Type interfaceType, string methodName)
        {
            try
            {
                if (!interfaceType.IsAssignableFrom(type))
                {
                    return null;
                }

                InterfaceMapping map = type.GetInterfaceMap(interfaceType);
                for (int index = 0; index < map.InterfaceMethods.Length; index++)
                {
                    if (map.InterfaceMethods[index].Name == methodName)
                    {
                        return map.TargetMethods[index];
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
