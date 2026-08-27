// Taj's COI Mods | TweaksAutoShipDeliveryFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.World;
using Mafi.Core.World.Entities;
using TajsCOI.Tweaks.Features.World;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Opt-in automation for the existing shipyard world-operation flow. It only requests
    ///     ordinary cargo loading and dispatches through the ship/path APIs already used by the
    ///     game; it never creates cargo, repairs, or mutates world-map state directly.
    /// </summary>
    internal static class TweaksAutoShipDeliveryFeature
    {
        private sealed class ShipState
        {
            internal bool CargoWasLoading;
            internal bool ShipSent;
            internal WorldMapLocId? SentToLocation;
        }

        private static readonly Dictionary<int, ShipState> s_states = new();
        private static readonly WorldShipOrderArbiter s_orderArbiter = WorldShipOrderArbiter.Shared;
        private static int s_simTick;
        private static WeakReference<DependencyResolver>? s_resolver;
        private static WeakReference<WorldMapManager>? s_worldMap;
        private static WeakReference<TravelingFleetManager>? s_fleet;
        private static FieldInfo? s_worldEntityConstructBuffersField;
        private static FieldInfo? s_battleShipPathFinderField;
        private static MethodInfo? s_sendShipHomeMethod;

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            s_resolver = new WeakReference<DependencyResolver>(resolver);
            MethodInfo target = typeof(Shipyard).GetInterfaceMap(typeof(IEntityWithSimUpdate)).TargetMethods
                                    .FirstOrDefault(x => x.Name.IndexOf("SimUpdate", StringComparison.Ordinal) >= 0)
                                ?? throw new MissingMethodException(typeof(Shipyard).FullName, "SimUpdate");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(TweaksAutoShipDeliveryFeature), nameof(SimUpdatePostfix)));
            s_worldEntityConstructBuffersField = typeof(Shipyard).GetField("m_worldEntityConstructBuffers", BindingFlags.Instance | BindingFlags.NonPublic);
            s_battleShipPathFinderField = typeof(BattleShip).GetField("m_pathFinder", BindingFlags.Instance | BindingFlags.NonPublic);
            s_sendShipHomeMethod = typeof(BattleShip).GetMethod("sendShipHome", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        internal static void Reset()
        {
            lock (s_states)
            {
                s_states.Clear();
            }
            s_orderArbiter.Clear();
            s_simTick = 0;
            s_worldMap = null;
            s_fleet = null;
        }

        private static void SimUpdatePostfix(Shipyard? __instance)
        {
            if (++s_simTick % 10 != 0)
            {
                return;
            }
            if (__instance is null || !TajsTweaksRuntimeState.WorldOperations || !TajsTweaksRuntimeState.AutoWorldDelivery ||
                !TryResolveDependencies(out WorldMapManager? worldMap, out TravelingFleetManager? fleet) || worldMap is null || fleet is null)
            {
                return;
            }

            try
            {
                ShipState state = GetState(__instance.Id.Value);
                if (__instance.AssignedShip.IsNone)
                {
                    s_orderArbiter.Release(__instance.Id.Value, WorldShipOrderOwner.AutoDelivery);
                    s_orderArbiter.Release(__instance.Id.Value, WorldShipOrderOwner.AutoReturn);
                    state.CargoWasLoading = false;
                    state.ShipSent = false;
                    state.SentToLocation = null;
                    return;
                }

                BattleShip ship = __instance.AssignedShip.Value;
                if (!ship.IsDocked)
                {
                    state.CargoWasLoading = false;
                    // A path that did not originate from this helper is a manual/native order.
                    // Manual ownership is sticky until the ship is docked again.
                    if (!state.ShipSent && ship.HasWorldMapPath &&
                        s_orderArbiter.GetOwner(ship.Id.Value) == WorldShipOrderOwner.None)
                    {
                        s_orderArbiter.SetManualOrder(ship.Id.Value, true);
                        return;
                    }
                    if (state.SentToLocation.HasValue && !ship.HasWorldMapPath && ship.CurrentLocationId.HasValue &&
                        ship.CurrentLocationId.Value == state.SentToLocation.Value && s_sendShipHomeMethod is not null)
                    {
                        state.SentToLocation = null;
                        state.ShipSent = false;
                        if (s_orderArbiter.TryClaim(ship.Id.Value, WorldShipOrderOwner.AutoReturn))
                        {
                            s_sendShipHomeMethod.Invoke(ship, null);
                        }
                    }
                    return;
                }

                if (s_orderArbiter.ManualOrderActive(ship.Id.Value))
                {
                    s_orderArbiter.SetManualOrder(ship.Id.Value, false);
                }
                s_orderArbiter.Release(ship.Id.Value, WorldShipOrderOwner.AutoReturn);

                // Repairing and manual/other modification orders always win over this helper.
                if (state.ShipSent || !ship.CanOperate || __instance.IsRepairing ||
                    __instance.ModificationState != ShipModificationState.None)
                {
                    return;
                }

                if (__instance.WorldEntityToConstruct.HasValue)
                {
                    if (!s_orderArbiter.TryClaim(ship.Id.Value, WorldShipOrderOwner.AutoDelivery))
                    {
                        return;
                    }
                    if (!state.CargoWasLoading)
                    {
                        state.CargoWasLoading = true;
                    }
                    else if (IsCargoLoadingComplete(__instance) &&
                             TrySendShipToLocation(ship, __instance.WorldEntityToConstruct.Value.Location.Id, fleet))
                    {
                        state.ShipSent = true;
                        state.CargoWasLoading = false;
                        state.SentToLocation = __instance.WorldEntityToConstruct.Value.Location.Id;
                    }
                    return;
                }

                state.CargoWasLoading = false;
                IWorldMapRepairableEntity? next = worldMap.EntitiesUnderConstruction
                    .FirstOrDefault(x => x is not null && x.NeedsProductsForConstruction);
                if (next is null)
                {
                    return;
                }

                if (!s_orderArbiter.TryClaim(ship.Id.Value, WorldShipOrderOwner.AutoDelivery))
                {
                    return;
                }

                // ToggleCargoLoadFor is the game's own save-aware command path. It also keeps
                // fuel, capacity, existing manual orders, and construction requirements intact.
                __instance.ToggleCargoLoadFor(next);
            }
            catch
            {
                // A changed world/ship private seam leaves the ordinary shipyard update active.
            }
        }

        private static bool TryResolveDependencies(out WorldMapManager? worldMap, out TravelingFleetManager? fleet)
        {
            worldMap = null;
            fleet = null;
            if (s_resolver is null || !s_resolver.TryGetTarget(out DependencyResolver? resolver))
            {
                return false;
            }
            if (s_worldMap is not null && s_worldMap.TryGetTarget(out WorldMapManager? cachedWorld) &&
                s_fleet is not null && s_fleet.TryGetTarget(out TravelingFleetManager? cachedFleet))
            {
                worldMap = cachedWorld;
                fleet = cachedFleet;
                return true;
            }
            if (!resolver.TryResolve(out worldMap) || !resolver.TryResolve(out fleet) || worldMap is null || fleet is null)
            {
                return false;
            }
            s_worldMap = new WeakReference<WorldMapManager>(worldMap);
            s_fleet = new WeakReference<TravelingFleetManager>(fleet);
            return true;
        }

        private static ShipState GetState(int shipyardId)
        {
            lock (s_states)
            {
                if (!s_states.TryGetValue(shipyardId, out ShipState? state))
                {
                    state = new ShipState();
                    s_states[shipyardId] = state;
                }
                if (s_states.Count > 128)
                {
                    int first = s_states.Keys.First();
                    s_states.Remove(first);
                }
                return state;
            }
        }

        private static bool IsCargoLoadingComplete(Shipyard shipyard)
        {
            if (s_worldEntityConstructBuffersField?.GetValue(shipyard) is not Lyst<ProductBuffer> buffers || buffers.Count == 0)
            {
                return false;
            }
            for (int index = 0; index < buffers.Count; index++)
            {
                if (!buffers[index].Capacity.IsZero)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TrySendShipToLocation(BattleShip ship, WorldMapLocId locationId, TravelingFleetManager fleet)
        {
            if (s_battleShipPathFinderField?.GetValue(ship) is not IWorldMapPathFinder pathFinder)
            {
                return false;
            }
            if (!fleet.ComputeRoundtripPathAndCosts(locationId, out _, out _, out Quantity fuelCost) || fuelCost > ship.FuelQuantity)
            {
                return false;
            }
            var path = new Lyst<WorldMapLocId>();
            if (!ship.FindPathTo(locationId, pathFinder, path) || path.Count == 0 || !ship.TryLeaveToWorld())
            {
                return false;
            }
            ship.SetPath(path, LocationVisitReason.DeliverCargo);
            return true;
        }
    }
}
