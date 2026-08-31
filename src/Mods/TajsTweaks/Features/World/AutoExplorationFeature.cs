// Taj's COI Mods | AutoExplorationFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Input;
using Mafi.Core.World;

namespace TajsCOI.Tweaks.Features.World
{
    /// <summary>
    ///     Opt-in exploration dispatcher. It only schedules the native GoToLocationCmd after a
    ///     bounded candidate check; no path, cargo, or world-map state is mutated directly.
    /// </summary>
    internal static class AutoExplorationFeature
    {
        private static WeakReference<DependencyResolver>? s_resolver;
        private static readonly WorldShipOrderArbiter s_arbiter = WorldShipOrderArbiter.Shared;
        private static MethodInfo? s_target;
        private static int s_tick;
        private static bool s_installed;

        internal static void Install(Harmony harmony, DependencyResolver resolver)
        {
            if (s_installed)
            {
                return;
            }
            s_resolver = new WeakReference<DependencyResolver>(resolver);
            s_target = AccessTools.Method(typeof(TravelingFleetManager), "simUpdate")
                       ?? throw new MissingMethodException(typeof(TravelingFleetManager).FullName, "simUpdate");
            harmony.Patch(s_target, postfix: new HarmonyMethod(typeof(AutoExplorationFeature), nameof(SimUpdatePostfix)));
            s_installed = true;
        }

        internal static void Reset()
        {
            s_resolver = null;
            s_target = null;
            s_arbiter.Clear();
            s_tick = 0;
            s_installed = false;
        }

        private static void SimUpdatePostfix(TravelingFleetManager __instance)
        {
            if (++s_tick % 30 != 0 || !TajsTweaksRuntimeState.WorldOperations ||
                !TajsTweaksRuntimeState.AutoExploration || __instance is null || !__instance.HasFleet ||
                s_resolver is null || !s_resolver.TryGetTarget(out DependencyResolver? resolver) ||
                !resolver.TryResolve(out IInputScheduler scheduler) ||
                !resolver.TryResolve(out WorldMapManager mapManager))
            {
                return;
            }
            try
            {
                BattleShip ship = __instance.TravelingFleet;
                WorldShipOrderOwner owner = s_arbiter.GetOwner(ship.Id.Value);
                if (owner == WorldShipOrderOwner.AutoExploration && !ship.IsAtHomeCell &&
                    !ship.HasWorldMapPath && !ship.IsExploring && !ship.InBattle)
                {
                    s_arbiter.Release(ship.Id.Value, WorldShipOrderOwner.AutoExploration);
                    if (s_arbiter.TryClaim(ship.Id.Value, WorldShipOrderOwner.AutoReturn))
                    {
                        scheduler.ScheduleInputCmd(new GoToLocationCmd(mapManager.Map.HomeLocation.Id, LocationVisitReason.General));
                    }
                    return;
                }
                if (owner == WorldShipOrderOwner.AutoReturn && ship.IsAtHomeCell && ship.IsDocked)
                {
                    s_arbiter.Release(ship.Id.Value, WorldShipOrderOwner.AutoReturn);
                    return;
                }
                if (!ExplorationCandidatePolicy.IsReady(
                        ship.IsDocked,
                        ship.IsAtHomeCell,
                        ship.InBattle,
                        ship.AssignedDock.HasValue && ship.AssignedDock.Value.IsRepairing,
                        ship.Cargo.Any(pair => pair.Value is not null && pair.Value.Quantity.IsPositive),
                        ship.FuelQuantity.Value >= ship.FuelBuffer.Capacity.Value / 4) ||
                    ship.IsExploring || !ship.CanOperate || s_arbiter.ManualOrderActive(ship.Id.Value) ||
                    owner != WorldShipOrderOwner.None)
                {
                    return;
                }

                int? playerStrength = null;
                try
                {
                    // BattleFleet.GetBattleScore is the native score shown by the fleet UI.
                    playerStrength = ship.BattleFleet.GetBattleScore();
                }
                catch
                {
                    // An unavailable score is unknown combat data and is handled fail-safe below.
                }

                ExplorationCandidate[] candidates = mapManager.Map.Locations
                    .Where(location => location is not null && location != mapManager.Map.HomeLocation &&
                                       location.State == WorldMapLocationState.NotExplored)
                    .Select(location =>
                    {
                        bool reachable = __instance.ComputeRoundtripPathAndCosts(
                            location.Id,
                            out int distance,
                            out _,
                            out Quantity fuel);
                        bool fuelSufficient = reachable && fuel <= __instance.TravelingFleet.FuelQuantity;
                        bool enemyPresenceKnown = location.IsEnemyKnown;
                        bool hasEnemy = location.Enemy.HasValue;
                        int? enemyStrength = null;
                        if (enemyPresenceKnown && hasEnemy)
                        {
                            try
                            {
                                // The location's BattleFleet is the native authoritative enemy fleet.
                                enemyStrength = location.Enemy.Value.GetBattleScore();
                            }
                            catch
                            {
                                // An unavailable score remains unknown and cannot pass the default policy.
                            }
                        }

                        bool combatSafe = ExplorationCandidatePolicy.IsCombatSafe(
                            enemyPresenceKnown,
                            hasEnemy,
                            playerStrength,
                            enemyStrength,
                            TajsTweaksRuntimeState.AutoExplorationSafetyMarginPercent,
                            TajsTweaksRuntimeState.AutoExplorationAllowUnknownStrength);
                        double enemyMargin = playerStrength.HasValue && enemyStrength.HasValue
                            ? playerStrength.Value - enemyStrength.Value
                            : double.NaN;
                        return new ExplorationCandidate(
                            location.Id.Value,
                            distance,
                            fuelSufficient && combatSafe,
                            enemyPresenceKnown && hasEnemy,
                            enemyMargin);
                    })
                    .ToArray();
                ExplorationCandidate? selected = ExplorationCandidatePolicy.ChooseNearest(candidates);
                if (!selected.HasValue || !s_arbiter.TryClaim(ship.Id.Value, WorldShipOrderOwner.AutoExploration))
                {
                    return;
                }
                scheduler.ScheduleInputCmd(new GoToLocationCmd(new WorldMapLocId(selected.Value.LocationId), LocationVisitReason.General));
            }
            catch
            {
                // Release only this helper's claim; native fleet behavior remains active.
                s_arbiter.Release(__instance.HasFleet ? __instance.TravelingFleet.Id.Value : 0, WorldShipOrderOwner.AutoExploration);
            }
        }
    }
}
