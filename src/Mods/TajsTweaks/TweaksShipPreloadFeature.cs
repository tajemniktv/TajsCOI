// Taj's COI Mods | TweaksShipPreloadFeature.cs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Entities.Static;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Promotes configured shipyard cargo buffers through the normal vehicle-buffer registry.
    ///     It never stores cargo directly, so fuel, pathing, capacity, and manual orders remain
    ///     owned by the game's ordinary logistics flow.
    /// </summary>
    internal static class TweaksShipPreloadFeature
    {
        private sealed class PreloadPriority : IInputBufferPriorityProvider
        {
            private readonly int m_target;

            internal PreloadPriority(int target) => m_target = target;

            public BufferStrategy GetInputPriority(IProductBuffer buffer, Quantity pendingQuantity)
            {
                int remaining = Math.Max(0, m_target - buffer.Quantity.Value - pendingQuantity.Value);
                return remaining == 0 ? BufferStrategy.Ignore : new BufferStrategy(8, new Quantity(remaining));
            }
        }

        private static readonly List<WeakReference<Shipyard>> s_shipyards = new List<WeakReference<Shipyard>>();
        private static readonly Dictionary<int, Dictionary<string, int>> s_targets = new Dictionary<int, Dictionary<string, int>>();
        private static WeakReference<DependencyResolver>? s_resolver;
        private static FieldInfo? s_registryField;
        private static FieldInfo? s_cargoField;
        private static MethodInfo? s_getBufferMethod;

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            s_resolver = new WeakReference<DependencyResolver>(resolver);
            ConstructorInfo? constructor = typeof(Shipyard).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault();
            if (constructor is null)
            {
                throw new MissingMethodException(typeof(Shipyard).FullName, ".ctor");
            }
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TweaksShipPreloadFeature), nameof(ShipyardCreated)));
            s_registryField = typeof(Shipyard).GetField("m_vehicleBuffersRegistry", BindingFlags.Instance | BindingFlags.NonPublic);
            s_cargoField = typeof(Shipyard).GetField("m_cargo", BindingFlags.Instance | BindingFlags.NonPublic);
            s_getBufferMethod = AccessTools.Method(typeof(Shipyard), "getOrCreateCargoBufferFor");
            ReloadTargets();
        }

        internal static void ReloadTargets()
        {
            s_targets.Clear();
            if (!TajsTweaksRuntimeState.WorldOperations || !TajsTweaksRuntimeState.ShipPreload)
            {
                return;
            }
            foreach (string entry in (TajsTweaksRuntimeState.ShipPreloadData ?? string.Empty)
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Take(256))
            {
                string[] pair = entry.Trim().Split(new[] { '=' }, 2);
                if (pair.Length != 2 || !int.TryParse(pair[1].Trim(), out int target) || target <= 0)
                {
                    continue;
                }
                string[] key = pair[0].Trim().Split(new[] { '|' }, 2);
                if (key.Length != 2 || !int.TryParse(key[0], out int shipyardId) || shipyardId < 0 || key[1].Length == 0)
                {
                    continue;
                }
                if (!s_targets.TryGetValue(shipyardId, out Dictionary<string, int>? products))
                {
                    products = new Dictionary<string, int>(StringComparer.Ordinal);
                    s_targets[shipyardId] = products;
                }
                products[key[1]] = Math.Min(target, 1_000_000);
            }
        }

        internal static void Tick()
        {
            s_shipyards.RemoveAll(x => !x.TryGetTarget(out _));
            foreach (WeakReference<Shipyard> reference in s_shipyards.ToArray())
            {
                if (reference.TryGetTarget(out Shipyard? shipyard))
                {
                    Reconcile(shipyard);
                }
            }
        }

        private static void ShipyardCreated(Shipyard __instance)
        {
            s_shipyards.Add(new WeakReference<Shipyard>(__instance));
            Reconcile(__instance);
        }

        private static void Reconcile(Shipyard shipyard)
        {
            if (!TajsTweaksRuntimeState.WorldOperations || !TajsTweaksRuntimeState.ShipPreload || s_registryField is null || s_cargoField is null || s_getBufferMethod is null ||
                !s_targets.TryGetValue(shipyard.Id.Value, out Dictionary<string, int>? targets))
            {
                return;
            }
            try
            {
                if (s_registryField.GetValue(shipyard) is not IVehicleBuffersRegistry registry || s_cargoField.GetValue(shipyard) is not IEnumerable cargo)
                {
                    return;
                }
                var reconciledProducts = new HashSet<string>(StringComparer.Ordinal);
                foreach (object entry in cargo)
                {
                    object? product = ReadMember(entry, "Key");
                    object? buffer = ReadMember(entry, "Value");
                    if (product is not ProductProto proto || buffer is not IProductBuffer productBuffer ||
                        !targets.TryGetValue(proto.Id.Value, out int target))
                    {
                        continue;
                    }
                    reconciledProducts.Add(proto.Id.Value);
                    PromoteBuffer(registry, shipyard, productBuffer, target);
                }

                if (s_resolver is not null && s_resolver.TryGetTarget(out DependencyResolver? resolver) &&
                    resolver.TryResolve(out ProtosDb protosDb))
                {
                    foreach (string productId in targets.Keys)
                    {
                        if (reconciledProducts.Contains(productId))
                        {
                            continue;
                        }
                        ProductProto? proto = protosDb.All<ProductProto>().FirstOrDefault(x => x.Id.Value == productId);
                        if (proto is null || s_getBufferMethod.Invoke(shipyard, new object[] { proto }) is not IProductBuffer productBuffer ||
                            !targets.TryGetValue(productId, out int target))
                        {
                            continue;
                        }
                        PromoteBuffer(registry, shipyard, productBuffer, target);
                    }
                }
            }
            catch
            {
                // A private registry change must leave the vanilla shipyard buffers untouched.
            }
        }

        private static void PromoteBuffer(IVehicleBuffersRegistry registry, Shipyard shipyard, IProductBuffer buffer, int target)
        {
            registry.TryUnregisterInputBuffer(buffer);
            registry.TryRegisterInputBuffer(shipyard, buffer, new PreloadPriority(target), alwaysEnabled: true);
        }

        private static object? ReadMember(object value, string name)
        {
            Type type = value.GetType();
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) ??
                type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        }
    }
}
