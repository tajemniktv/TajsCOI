// Taj's COI Mods | ShipUnloadPolicyFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Entities.Static;
using Mafi.Core.Products;
using Mafi.Core.World;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Optional selector for the exact 0.8.7b BattleShip.TryUnloadCargo seam. It preserves
    ///     native eligibility/skip arguments and only changes which eligible buffer is removed.
    /// </summary>
    internal static class ShipUnloadPolicyFeature
    {
        private static MethodInfo? s_removeEmpty;
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            MethodInfo method = AccessTools.Method(typeof(BattleShip), "TryUnloadCargo", new[] { typeof(Quantity), typeof(IReadOnlySet<ProductProto>) }) ??
                                throw new MissingMethodException(typeof(BattleShip).FullName, "TryUnloadCargo");
            s_removeEmpty = AccessTools.Method(typeof(BattleShip), "removeBufferIfEmpty");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ShipUnloadPolicyFeature), nameof(TryUnloadCargoPrefix)));
            s_installed = true;
        }

        internal static void Reset()
        {
            s_installed = false;
            s_removeEmpty = null;
        }

        private static bool TryUnloadCargoPrefix(
            BattleShip __instance,
            Quantity maxQuantity,
            IReadOnlySet<ProductProto> productsToSkip,
            ref ProductQuantity __result)
        {
            if (!string.Equals(TajsTweaksRuntimeState.ShipUnloadPolicy, "smallest_stack_first", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            try
            {
                KeyValuePair<ProductProto, IProductBuffer>? selected = __instance.Cargo
                    .Where(pair => !productsToSkip.Contains(pair.Key) &&
                                   !TweaksShipPreloadFeature.IsProductReserved(__instance.AssignedDock.ValueOrNull, pair.Key))
                    .Where(pair => pair.Value is not null && pair.Value.Quantity.IsPositive)
                    .OrderBy(pair => pair.Value.Quantity.Value)
                    .ThenBy(pair => pair.Key.Id.Value, StringComparer.Ordinal)
                    .Cast<KeyValuePair<ProductProto, IProductBuffer>?>()
                    .FirstOrDefault();
                if (!selected.HasValue)
                {
                    __result = ProductQuantity.None;
                    return false;
                }
                IProductBuffer buffer = selected.Value.Value;
                Quantity quantity = buffer.RemoveAsMuchAs(maxQuantity);
                s_removeEmpty?.Invoke(__instance, new object[] { buffer });
                __result = quantity.IsPositive ? new ProductQuantity(selected.Value.Key, quantity) : ProductQuantity.None;
                return false;
            }
            catch
            {
                // A changed private cargo shape leaves the entirely native method active.
                return true;
            }
        }
    }
}
