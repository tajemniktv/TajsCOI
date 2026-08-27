// Taj's COI Mods | ShipyardOutputTransportFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Ports;
using Mafi.Core.Ports.Io;
using Mafi.Core.Products;
using TajsCOI.Tweaks.Features.Ships;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Optional shipyard output bridge. It removes only what a native connected port accepts,
    ///     protects preload reservations, and never discards blocked cargo.
    /// </summary>
    internal static class ShipyardOutputTransportFeature
    {
        private sealed class PortCache
        {
            internal string Signature = string.Empty;
            internal IoPort[] Ports = Array.Empty<IoPort>();
            internal int Cursor;
        }

        private static readonly ConditionalWeakTable<Shipyard, PortCache> s_caches = new();
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            MethodInfo target = typeof(Shipyard).GetInterfaceMap(typeof(IEntityWithSimUpdate)).TargetMethods
                .FirstOrDefault(method => method.Name.IndexOf("SimUpdate", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? throw new MissingMethodException(typeof(Shipyard).FullName, "SimUpdate");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ShipyardOutputTransportFeature), nameof(SimUpdatePostfix)));
            s_installed = true;
        }

        internal static void Reset() => s_installed = false;

        private static void SimUpdatePostfix(Shipyard __instance)
        {
            if (!TajsTweaksRuntimeState.WorldOperations || !TajsTweaksRuntimeState.ShipPreload ||
                !TajsTweaksRuntimeState.ShipyardOutputTransport || __instance is null || __instance.IsDestroyed)
            {
                return;
            }
            try
            {
                if (__instance is not IEntityWithPorts withPorts)
                {
                    return;
                }
                IoPort[] ports = GetConnectedOutputPorts(__instance, withPorts);
                if (ports.Length == 0)
                {
                    return;
                }
                Dictionary<string, int> reserved = TweaksShipPreloadFeature.ReadReservedCargo(__instance)
                    .ToDictionary(item => item.ProductId, item => item.Amount, StringComparer.Ordinal);
                IProductBuffer[] buffers = TweaksShipPreloadFeature.GetCargoBuffers(__instance)
                    .Where(buffer => buffer is not null && buffer.Quantity.IsPositive)
                    .ToArray();
                if (buffers.Length == 0)
                {
                    return;
                }

                PortCache cache = s_caches.GetValue(__instance, _ => new PortCache());
                for (int offset = 0; offset < buffers.Length * ports.Length; offset++)
                {
                    int index = (cache.Cursor + offset) % (buffers.Length * ports.Length);
                    IProductBuffer buffer = buffers[index % buffers.Length];
                    int protectedAmount = reserved.TryGetValue(buffer.Product.Id.Value, out int requested)
                        ? Math.Min(buffer.Quantity.Value, Math.Max(0, requested))
                        : 0;
                    int surplus = buffer.Quantity.Value - protectedAmount;
                    if (surplus <= 0)
                    {
                        continue;
                    }
                    IoPort port = ports[index % ports.Length];
                    IoPortData portData = new IoPortData(port);
                    if (!portData.AllowedProductType.Matches(buffer.Product.Type))
                    {
                        continue;
                    }
                    Quantity remaining = portData.SendAsMuchAs(buffer.Product.WithQuantity(new Quantity(surplus)));
                    Quantity sent = new Quantity(surplus) - remaining;
                    if (sent.IsPositive)
                    {
                        buffer.RemoveAsMuchAs(sent);
                        cache.Cursor = (index + 1) % (buffers.Length * ports.Length);
                        return;
                    }
                }
            }
            catch
            {
                // A private/port seam change must leave the native shipyard update untouched.
            }
        }

        private static IoPort[] GetConnectedOutputPorts(Shipyard shipyard, IEntityWithPorts withPorts)
        {
            PortCache cache = s_caches.GetValue(shipyard, _ => new PortCache());
            IoPort[] ports = withPorts.Ports
                .Where(port => port is not null && port.IsConnected &&
                               (port.Type == IoPortType.Output || port.Type == IoPortType.Any))
                .OrderBy(port => port.PortIndex)
                .ToArray();
            string signature = string.Join(";", ports.Select(port => port.Id.ToString() + ":" + port.ConnectedPort.ValueOrNull?.Id.ToString()));
            if (!string.Equals(signature, cache.Signature, StringComparison.Ordinal))
            {
                cache.Signature = signature;
                cache.Ports = ports;
                cache.Cursor = 0;
            }
            return cache.Ports;
        }
    }
}
