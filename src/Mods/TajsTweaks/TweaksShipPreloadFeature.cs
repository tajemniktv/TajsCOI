// Taj's COI Mods | TweaksShipPreloadFeature.cs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Entities.Static;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles;
using TajsCOI.Common.Settings;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Promotes configured shipyard cargo buffers through the normal vehicle-buffer registry.
    ///     It never stores cargo directly, so fuel, pathing, capacity, and manual orders remain
    ///     owned by the game's ordinary logistics flow.
    /// </summary>
    internal static class TweaksShipPreloadFeature
    {
        internal sealed class PendingEntry
        {
            internal ProductProto Product = null!;
            internal int Delivered;
            internal int Target;
        }

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

        private sealed class PromotionMarker
        {
            internal int Target;
        }

        private static readonly List<WeakReference<Shipyard>> s_shipyards = new List<WeakReference<Shipyard>>();
        private static readonly Dictionary<int, Dictionary<string, int>> s_targets = new Dictionary<int, Dictionary<string, int>>();
        private static readonly ConditionalWeakTable<IProductBuffer, PromotionMarker> s_promotedBuffers = new ConditionalWeakTable<IProductBuffer, PromotionMarker>();
        private static WeakReference<DependencyResolver>? s_resolver;
        private static WeakReference<ITajsSettings>? s_settings;
        private static FieldInfo? s_registryField;
        private static FieldInfo? s_cargoField;
        private static MethodInfo? s_getBufferMethod;

        internal static void Install(Harmony harmony, DependencyResolver resolver, ITajsSettings settings)
        {
            s_resolver = new WeakReference<DependencyResolver>(resolver);
            s_settings = new WeakReference<ITajsSettings>(settings);
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
            RestorePromotedBuffers();
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

        internal static IReadOnlyList<Shipyard> GetShipyards()
        {
            List<Shipyard> result = new List<Shipyard>();
            s_shipyards.RemoveAll(x => !x.TryGetTarget(out _));
            foreach (WeakReference<Shipyard> reference in s_shipyards.ToArray())
            {
                if (reference.TryGetTarget(out Shipyard? shipyard) && shipyard is not null && !shipyard.IsDestroyed)
                {
                    result.Add(shipyard);
                }
            }

            result.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
            return result;
        }

        internal static IReadOnlyList<PendingEntry> ReadPending(Shipyard shipyard)
        {
            List<PendingEntry> result = new List<PendingEntry>();
            if (shipyard is null || !s_targets.TryGetValue(shipyard.Id.Value, out Dictionary<string, int>? targets))
            {
                return result;
            }

            Dictionary<string, IProductBuffer> buffers = GetCargoBuffers(shipyard)
                .Where(x => x.Product is not null)
                .ToDictionary(x => x.Product.Id.Value, StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> target in targets)
            {
                if (buffers.TryGetValue(target.Key, out IProductBuffer? buffer))
                {
                    result.Add(new PendingEntry
                    {
                        Product = buffer.Product,
                        Delivered = buffer.Quantity.Value,
                        Target = target.Value,
                    });
                }
            }

            return result;
        }

        internal static IReadOnlyList<KeyValuePair<ProductProto, int>> ReadCargo(Shipyard shipyard)
        {
            return GetCargoBuffers(shipyard)
                .Where(x => x.Product is not null && x.Quantity.Value > 0)
                .Select(x => new KeyValuePair<ProductProto, int>(x.Product, x.Quantity.Value))
                .ToArray();
        }

        internal static bool HasReservation(Shipyard shipyard, ProductProto product) =>
            shipyard is not null && product is not null &&
            s_targets.TryGetValue(shipyard.Id.Value, out Dictionary<string, int>? targets) &&
            targets.ContainsKey(product.Id.Value);

        internal static bool RequestDelivery(Shipyard shipyard, ProductProto product, int quantity)
        {
            if (shipyard is null || product is null || quantity <= 0 || s_getBufferMethod is null)
            {
                return false;
            }

            try
            {
                if (s_getBufferMethod.Invoke(shipyard, new object[] { product }) is not IProductBuffer buffer)
                {
                    return false;
                }

                if (!s_targets.TryGetValue(shipyard.Id.Value, out Dictionary<string, int>? targets))
                {
                    targets = new Dictionary<string, int>(StringComparer.Ordinal);
                    s_targets[shipyard.Id.Value] = targets;
                }

                int current = targets.TryGetValue(product.Id.Value, out int target) ? target : buffer.Quantity.Value;
                targets[product.Id.Value] = Math.Min(1_000_000, Math.Max(current, buffer.Quantity.Value) + quantity);
                Reconcile(shipyard);
                PersistTargets();
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void Release(Shipyard shipyard, ProductProto product)
        {
            if (shipyard is null || product is null || !s_targets.TryGetValue(shipyard.Id.Value, out Dictionary<string, int>? targets))
            {
                return;
            }

            targets.Remove(product.Id.Value);
            if (targets.Count == 0)
            {
                s_targets.Remove(shipyard.Id.Value);
            }

            Reconcile(shipyard);
            PersistTargets();
        }

        internal static void CancelOrder(Shipyard shipyard, ProductProto product)
        {
            if (shipyard is null || product is null || !s_targets.TryGetValue(shipyard.Id.Value, out Dictionary<string, int>? targets) ||
                !targets.ContainsKey(product.Id.Value))
            {
                return;
            }

            int delivered = GetCargoBuffers(shipyard)
                .Where(x => x.Product is not null && x.Product.Id.Value == product.Id.Value)
                .Select(x => x.Quantity.Value)
                .FirstOrDefault();
            if (delivered > 0)
            {
                targets[product.Id.Value] = delivered;
            }
            else
            {
                targets.Remove(product.Id.Value);
                if (targets.Count == 0)
                {
                    s_targets.Remove(shipyard.Id.Value);
                }
            }

            Reconcile(shipyard);
            PersistTargets();
        }

        private static void ShipyardCreated(Shipyard __instance)
        {
            s_shipyards.Add(new WeakReference<Shipyard>(__instance));
            Reconcile(__instance);
        }

        private static void Reconcile(Shipyard shipyard)
        {
            if (s_registryField is null || s_cargoField is null || s_getBufferMethod is null)
            {
                return;
            }
            try
            {
                if (s_registryField.GetValue(shipyard) is not IVehicleBuffersRegistry registry || s_cargoField.GetValue(shipyard) is not IEnumerable cargo)
                {
                    return;
                }
                if (!TajsTweaksRuntimeState.WorldOperations || !TajsTweaksRuntimeState.ShipPreload ||
                    !s_targets.TryGetValue(shipyard.Id.Value, out Dictionary<string, int>? targets))
                {
                    RestoreBuffers(registry, shipyard, cargo);
                    return;
                }

                var reconciledProducts = new HashSet<string>(StringComparer.Ordinal);
                foreach (object entry in cargo)
                {
                    object? product = ReadMember(entry, "Key");
                    object? buffer = ReadMember(entry, "Value");
                    if (product is not ProductProto proto || buffer is not IProductBuffer productBuffer)
                    {
                        continue;
                    }
                    if (!targets.TryGetValue(proto.Id.Value, out int target))
                    {
                        RestoreBuffer(registry, shipyard, productBuffer);
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
            if (s_promotedBuffers.TryGetValue(buffer, out PromotionMarker? marker) && marker.Target == target)
            {
                return;
            }

            registry.TryUnregisterInputBuffer(buffer);
            if (registry.TryRegisterInputBuffer(shipyard, buffer, new PreloadPriority(target), alwaysEnabled: true))
            {
                if (!s_promotedBuffers.TryGetValue(buffer, out marker))
                {
                    marker = new PromotionMarker();
                    s_promotedBuffers.Add(buffer, marker);
                }
                marker.Target = target;
            }
        }

        private static void RestoreBuffer(IVehicleBuffersRegistry registry, Shipyard shipyard, IProductBuffer buffer)
        {
            if (!s_promotedBuffers.TryGetValue(buffer, out _))
            {
                return;
            }

            registry.TryUnregisterInputBuffer(buffer);
            registry.TryRegisterInputBuffer(
                shipyard,
                buffer,
                StaticPriorityProvider.Ignore,
                alwaysEnabled: true,
                isFallbackOnly: true);
            s_promotedBuffers.Remove(buffer);
        }

        private static void RestoreBuffers(IVehicleBuffersRegistry registry, Shipyard shipyard, IEnumerable cargo)
        {
            foreach (object entry in cargo)
            {
                if (ReadMember(entry, "Value") is IProductBuffer buffer)
                {
                    RestoreBuffer(registry, shipyard, buffer);
                }
            }
        }

        private static void RestorePromotedBuffers()
        {
            foreach (WeakReference<Shipyard> reference in s_shipyards.ToArray())
            {
                if (!reference.TryGetTarget(out Shipyard? shipyard) || shipyard is null || s_registryField is null || s_cargoField is null ||
                    s_registryField.GetValue(shipyard) is not IVehicleBuffersRegistry registry || s_cargoField.GetValue(shipyard) is not IEnumerable cargo)
                {
                    continue;
                }

                RestoreBuffers(registry, shipyard, cargo);
            }
        }

        private static object? ReadMember(object value, string name)
        {
            Type type = value.GetType();
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) ??
                type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        }

        private static IEnumerable<IProductBuffer> GetCargoBuffers(Shipyard shipyard)
        {
            if (s_cargoField?.GetValue(shipyard) is not object cargo)
            {
                return Array.Empty<IProductBuffer>();
            }

            IEnumerable? values = cargo.GetType().GetProperty("Values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(cargo) as IEnumerable;
            IEnumerable entries = values ?? (cargo as IEnumerable ?? Array.Empty<object>());
            List<IProductBuffer> result = new List<IProductBuffer>();
            foreach (object entry in entries)
            {
                if (entry is IProductBuffer direct)
                {
                    result.Add(direct);
                }
                else if (ReadMember(entry, "Value") is IProductBuffer value)
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static void PersistTargets()
        {
            if (s_settings is null || !s_settings.TryGetTarget(out ITajsSettings? settings))
            {
                return;
            }

            string value = string.Join(",", s_targets
                .OrderBy(x => x.Key)
                .SelectMany(x => x.Value.OrderBy(y => y.Key)
                    .Select(y => x.Key + "|" + y.Key + "=" + y.Value)));
            settings.TrySet(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ShipPreloadData, value);
        }
    }
}
