// Taj's COI Mods | TajsTweaksFeatureHost.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Console;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.GameLoop;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles.Commands;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.World;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Owns the opt-in QoL patches and the bounded management command surface. Every patch
    ///     remains installed as a no-op when its setting is off so live setting changes do not
    ///     require re-registering Harmony callbacks.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class TajsTweaksFeatureHost
    {
        private const string HarmonyId = "TajsCOI.Tweaks";
        private readonly DependencyResolver m_resolver;
        private readonly ITajsSettings m_settings;
        private readonly ITajsLogger m_log;
        private Option<TajsWorldOperationsWindow> m_worldOperationsWindow;
        private Option<TajsFleetManagementWindow> m_fleetManagementWindow;
        private int m_renderTick;

        public TajsTweaksFeatureHost(DependencyResolver resolver, IGameLoopEvents gameLoop, ITajsRuntime runtime, ITajsSettings settings)
        {
            m_resolver = resolver;
            m_settings = settings;
            m_log = runtime.GetLogger(TajsTweaksSettingsCatalog.ModId, "FeatureHost");
            TajsTweaksSettingsCatalog.RegisterAll(settings);
            TajsTweaksRuntimeState.Load(settings);
            settings.Changed += OnSettingChanged;
            gameLoop.RenderUpdateEnd.AddNonSaveable(this, OnRenderUpdateEnd);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);

            TryInstall(runtime, "LinePlacement", TweaksLinePlacementFeature.Install);
            TryInstall(runtime, "PinnedProducts", TweaksPinnedProductsFeature.Install);
            TryInstall(runtime, "BuildDefaults", TweaksBuildDefaultsFeature.Install);
            TryInstall(runtime, "ResourceOverlays", TweaksResourceOverlayFeature.Install);
            TryInstall(runtime, "ResourceDepositClusters", TweaksResourceDepositFeature.Install);
            TryInstall(runtime, "ShipCargoPreload", harmony => TweaksShipPreloadFeature.Install(harmony, resolver, settings));
            TryInstall(runtime, "AutoWorldDelivery", harmony => TweaksAutoShipDeliveryFeature.Install(harmony, resolver));
            TryInstall(runtime, "CameraAndHud", TweaksCameraFeature.Install);
            TryInstall(runtime, "DesignationControls", TweaksDesignationFeature.Install);
            TryInstall(runtime, "NotificationFilter", TweaksNotificationFeature.Install);
            TryInstall(runtime, "MineTruckStaging", TweaksMineTruckStagingFeature.Install);
            TweaksMineTruckStagingFeature.SetResolver(resolver);
            TryInstall(runtime, "StuckTruckRecovery", TweaksStuckTruckRecoveryFeature.Install);
            TweaksStuckTruckRecoveryFeature.SetResolver(resolver);
            TryInstall(runtime, "StorageOverrides", harmony => TweaksStorageFeature.Install(harmony, resolver));
            TryInstallResolved(runtime, "HudLayout", () => TweaksHudLayoutFeature.Install(resolver, settings));

            runtime.ReportCompatibility(
                new CompatibilityReport(
                    TajsTweaksSettingsCatalog.ModId,
                    "FeatureHost",
                    CompatibilityState.Compatible,
                    "Typed global settings and independently fail-open feature patches",
                    TajsTweaksSettingsCatalog.All.Count + " settings; " + HarmonyId,
                    "Features are optional and retain vanilla behavior when a target is unavailable."));
        }

        private void TryInstall(ITajsRuntime runtime, string id, Action<Harmony> install)
        {
            var harmony = new Harmony(HarmonyId + "." + id);
            try
            {
                install(harmony);
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        TajsTweaksSettingsCatalog.ModId,
                        id,
                        CompatibilityState.Compatible,
                        "0.8.7b target or native fallback",
                        "Patch registration completed",
                        "The feature remains disabled unless its own setting is enabled."));
            }
            catch (Exception exception)
            {
                // Features install several patches in sequence. Roll back this feature's
                // owner if a later compatibility seam fails, so fail-open really means no
                // partial behavior remains. Each feature has its own Harmony owner for this
                // transaction; earlier features are not disturbed.
                try
                {
                    harmony.UnpatchAll(harmony.Id);
                }
                catch (Exception rollbackException)
                {
                    m_log.Exception(rollbackException, "Feature '" + id + "' rollback failed.");
                }
                m_log.Exception(exception, "Feature '" + id + "' failed open during installation.");
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        TajsTweaksSettingsCatalog.ModId,
                        id,
                        CompatibilityState.Disabled,
                        "0.8.7b target or native fallback",
                        exception.GetType().Name,
                        "No behavior was changed for this feature; vanilla behavior remains active."));
            }
        }

        private void TryInstallResolved(ITajsRuntime runtime, string id, Action install)
        {
            try
            {
                install();
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        TajsTweaksSettingsCatalog.ModId,
                        id,
                        CompatibilityState.Compatible,
                        "0.8.7b target or native fallback",
                        "Dependency-backed feature initialized",
                        "The feature remains disabled unless its own setting is enabled."));
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Feature '" + id + "' failed open during initialization.");
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        TajsTweaksSettingsCatalog.ModId,
                        id,
                        CompatibilityState.Disabled,
                        "0.8.7b target or native fallback",
                        exception.GetType().Name,
                        "No behavior was changed for this feature; vanilla behavior remains active."));
            }
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs change)
        {
            TajsTweaksRuntimeState.ApplyChange(change);
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.UnlimitedZoom)
            {
                TweaksCameraFeature.ApplyZoom();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.GroundClipping ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.FreeCamera)
            {
                TweaksCameraFeature.RefreshGroundClipping();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.ShipPreload ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.ShipPreloadData)
            {
                TweaksShipPreloadFeature.ReloadTargets();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.HudLayout ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.HudDragLocked ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.HudScale ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.HudHidden ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.HudPositions)
            {
                TweaksHudLayoutFeature.Apply(m_resolver, m_settings);
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.WorldOperations ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.AutoWorldDelivery)
            {
                TweaksAutoShipDeliveryFeature.Reset();
            }
        }

        private void OnRenderUpdateEnd(GameTime _)
        {
            if (++m_renderTick % 15 == 0)
            {
                TweaksPinnedProductsFeature.Tick();
                TweaksShipPreloadFeature.Tick();
                TweaksResourceDepositFeature.Tick(m_resolver);
                TweaksHudLayoutFeature.Apply(m_resolver, m_settings);
            }
        }

        private void OnTerminate()
        {
            m_settings.Changed -= OnSettingChanged;
            TweaksAutoShipDeliveryFeature.Reset();
            TweaksResourceDepositFeature.Dispose();
            CloseWorldOperationsWindow();
            CloseFleetManagementWindow();
        }

        [ConsoleCommand(
            documentation: "Opens the native world operations window for repairs, upgrades, settlements, and ship preload.",
            customCommandName: "tajs_world_operations")]
        public string ToggleWorldOperationsWindow()
        {
            if (!TajsTweaksRuntimeState.WorldOperations)
            {
                return "World operations manager is disabled.";
            }

            if (m_worldOperationsWindow.HasValue && m_worldOperationsWindow.Value.IsOpen)
            {
                CloseWorldOperationsWindow();
                return "World operations window: hidden";
            }

            try
            {
                var window = m_resolver.Instantiate<TajsWorldOperationsWindow>();
                window.OnCloseStart += OnWorldOperationsWindowClose;
                m_worldOperationsWindow = window;
                return "World operations window: shown";
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "World operations window failed open.");
                return "World operations window is unavailable in this scene.";
            }
        }

        [ConsoleCommand(
            documentation: "Opens the native fleet management window with grouped status and confirmed bulk actions.",
            customCommandName: "tajs_fleet_manager")]
        public string ToggleFleetManagementWindow()
        {
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }

            if (m_fleetManagementWindow.HasValue && m_fleetManagementWindow.Value.IsOpen)
            {
                CloseFleetManagementWindow();
                return "Fleet management window: hidden";
            }

            try
            {
                var window = m_resolver.Instantiate<TajsFleetManagementWindow>();
                window.OnCloseStart += OnFleetManagementWindowClose;
                m_fleetManagementWindow = window;
                return "Fleet management window: shown";
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Fleet management window failed open.");
                return "Fleet management window is unavailable in this scene.";
            }
        }

        private void OnWorldOperationsWindowClose(Window window)
        {
            if (m_worldOperationsWindow.HasValue && ReferenceEquals(m_worldOperationsWindow.Value, window))
            {
                window.OnCloseStart -= OnWorldOperationsWindowClose;
                m_worldOperationsWindow = Option<TajsWorldOperationsWindow>.None;
            }
        }

        private void OnFleetManagementWindowClose(Window window)
        {
            if (m_fleetManagementWindow.HasValue && ReferenceEquals(m_fleetManagementWindow.Value, window))
            {
                window.OnCloseStart -= OnFleetManagementWindowClose;
                m_fleetManagementWindow = Option<TajsFleetManagementWindow>.None;
            }
        }

        private void CloseWorldOperationsWindow()
        {
            if (!m_worldOperationsWindow.HasValue)
            {
                return;
            }

            TajsWorldOperationsWindow window = m_worldOperationsWindow.Value;
            window.OnCloseStart -= OnWorldOperationsWindowClose;
            window.CloseNoFade();
            m_worldOperationsWindow = Option<TajsWorldOperationsWindow>.None;
        }

        private void CloseFleetManagementWindow()
        {
            if (!m_fleetManagementWindow.HasValue)
            {
                return;
            }

            TajsFleetManagementWindow window = m_fleetManagementWindow.Value;
            window.OnCloseStart -= OnFleetManagementWindowClose;
            window.CloseNoFade();
            m_fleetManagementWindow = Option<TajsFleetManagementWindow>.None;
        }

        [ConsoleCommand(
            documentation: "Shows the save-aware world-operations and ship-preload configuration.",
            customCommandName: "tajs_world_operations_status")]
        public string WorldOperationsStatus()
        {
            if (!TajsTweaksRuntimeState.WorldOperations)
            {
                return "World operations manager is disabled.";
            }

            int preloadLines = TajsTweaksRuntimeState.ParseIds(
                TajsTweaksRuntimeState.ShipPreloadData.Replace('\n', ',')).Count;
            string pending = "unknown";
            if (m_resolver.TryResolve(out WorldMapManager worldMap))
            {
                try
                {
                    pending = worldMap.EntitiesUnderConstruction.Count(x => x is not null && x.NeedsProductsForConstruction).ToString();
                }
                catch
                {
                    pending = "unavailable";
                }
            }
            return "World operations manager: enabled; pending deliveries=" + pending + "; auto delivery=" +
                   TajsTweaksRuntimeState.AutoWorldDelivery + "; ship preload=" +
                   TajsTweaksRuntimeState.ShipPreload + "; configured preload records=" + preloadLines +
                   "; orders use the normal game flow.";
        }

        [ConsoleCommand(
            documentation: "Queues a confirmed world-map repair or upgrade through the normal input scheduler.",
            customCommandName: "tajs_world_operations_apply")]
        public string WorldOperationsApply(string? operation, string entityId, string confirmation)
        {
            if (!TajsTweaksRuntimeState.WorldOperations)
            {
                return "World operations manager is disabled.";
            }
            if (confirmation != "CONFIRM")
            {
                return "No world-map operation was queued. Repeat with confirmation=CONFIRM.";
            }
            if (!int.TryParse(entityId, out int parsedId) || !m_resolver.TryResolve(out IInputScheduler scheduler))
            {
                return "Usage: tajs_world_operations_apply <repair|cancel-repair|upgrade> <entity-id> CONFIRM";
            }
            try
            {
                var id = new EntityId(parsedId);
                switch ((operation ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "repair": scheduler.ScheduleInputCmd(new WorldMapEntityStartRepairCmd(id)); break;
                    case "cancel-repair": scheduler.ScheduleInputCmd(new WorldMapEntityCancelRepairCmd(id)); break;
                    case "upgrade": scheduler.ScheduleInputCmd(new WorldMapEntityUpgradeCmd(id)); break;
                    default: return "Usage: tajs_world_operations_apply <repair|cancel-repair|upgrade> <entity-id> CONFIRM";
                }
                return "World-map '" + operation + "' command queued through the normal input scheduler.";
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "World operation failed open.");
                return "World operation was not queued; vanilla world-map management remains active.";
            }
        }

        [ConsoleCommand(
            documentation: "Lists bounded fleet status without deleting or spawning vehicles.",
            customCommandName: "tajs_fleet_status")]
        public string FleetStatus()
        {
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }
            if (!m_resolver.TryResolve(out IEntitiesManager entities))
            {
                return "Fleet manager: the current scene has no entity manager.";
            }

            try
            {
                Vehicle[] vehicles = entities.GetAllEntitiesOfType<Vehicle>()
                    .Where(x => x is not null)
                    .ToArray();
                int loaded = vehicles.Count(x => x is Truck truck && !truck.Cargo.IsEmpty);
                int assigned = vehicles.Count(x => x.AssignedTo.HasValue);
                int pendingScrap = vehicles.Count(x => x.IsOnWayToDepotForScrap);
                int pendingReplacement = vehicles.Count(x => x.IsOnWayToDepotForReplacement || x.ReplaceQueued);
                int cannotDeliver = vehicles.Count(x => x is Truck truck && truck.IsCannotDeliverNotificationActive);
                string groups = string.Join(
                    ", ",
                    vehicles
                        .GroupBy(x => x.Prototype.Id.Value, StringComparer.Ordinal)
                        .OrderByDescending(x => x.Count())
                        .ThenBy(x => x.Key, StringComparer.Ordinal)
                        .Take(8)
                        .Select(x => x.Key + "=" + x.Count() + "/assigned:" + x.Count(v => v.AssignedTo.HasValue) +
                                     "/scrap:" + x.Count(v => v.IsOnWayToDepotForScrap) + "/replace:" +
                                     x.Count(v => v.IsOnWayToDepotForReplacement || v.ReplaceQueued)));
                int queuedBuilds = entities.GetAllEntitiesOfType<VehicleDepotBase>().Sum(x => x.BuildQueue.Count);
                int queuedReplacements = entities.GetAllEntitiesOfType<VehicleDepotBase>().Sum(x => x.ReplaceQueue.Count);
                return "Fleet manager: total=" + vehicles.Length + ", assigned=" + assigned + ", loaded=" + loaded +
                       ", cannot-deliver=" + cannotDeliver + ", pending-scrap=" + pendingScrap +
                       ", pending-replacement=" + pendingReplacement + ", queued-builds=" + queuedBuilds +
                       ", queued-replacements=" + queuedReplacements +
                       ", prototypes=[" + groups + "]" +
                       "; actions are capped at " + TajsTweaksRuntimeState.FleetBatchLimit +
                       " and require explicit normal-game command confirmation.";
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Fleet status failed open.");
                return "Fleet manager: status unavailable; vanilla vehicle management remains active.";
            }
        }

        [ConsoleCommand(
            documentation: "Creates a bounded, non-destructive fleet operation plan.",
            customCommandName: "tajs_fleet_plan")]
        public string FleetPlan(string? operation)
        {
            string normalized = (operation ?? string.Empty).Trim().ToLowerInvariant();
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }
            if (normalized != "scrap" && normalized != "replace")
            {
                return "Usage: tajs_fleet_plan <scrap|replace>";
            }
            return "Fleet plan prepared for '" + normalized + "' with a maximum of " +
                   TajsTweaksRuntimeState.FleetBatchLimit + " vehicles. No vehicle was changed; confirm through the normal vehicle manager.";
        }

        [ConsoleCommand(
            documentation: "Applies a confirmed bounded fleet operation through normal vehicle input commands.",
            customCommandName: "tajs_fleet_apply")]
        public string FleetApply(string? operation, string vehicleIds, string confirmation, string? targetPrototypeId = "")
        {
            string normalized = (operation ?? string.Empty).Trim().ToLowerInvariant();
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }
            if (confirmation != "CONFIRM")
            {
                return "No vehicle changed. Repeat with confirmation=CONFIRM after reviewing tajs_fleet_plan.";
            }
            if (normalized != "scrap" && normalized != "replace")
            {
                return "Usage: tajs_fleet_apply <scrap|replace> <comma-separated IDs> CONFIRM [target-prototype-id]";
            }
            if (!m_resolver.TryResolve(out IEntitiesManager entities) || !m_resolver.TryResolve(out IInputScheduler scheduler))
            {
                return "Fleet manager: current scene input services are unavailable.";
            }

            string[] ids = TajsTweaksRuntimeState.ParseIds(vehicleIds).Take(TajsTweaksRuntimeState.FleetBatchLimit).ToArray();
            int changed = 0;
            foreach (string text in ids)
            {
                if (!int.TryParse(text, out int id) || !entities.TryGetEntity<Vehicle>(new EntityId(id), out Vehicle vehicle))
                {
                    continue;
                }
                try
                {
                    if (normalized == "scrap")
                    {
                        scheduler.ScheduleInputCmd(new ToggleVehicleScrapCmd(vehicle.Id));
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(targetPrototypeId))
                        {
                            return "Replacement requires a target prototype ID; no vehicle changed.";
                        }
                        scheduler.ScheduleInputCmd(new ReplaceVehicleCmd(vehicle.Id, new DynamicEntityProto.ID((targetPrototypeId ?? string.Empty).Trim())));
                    }
                    changed++;
                }
                catch (Exception exception)
                {
                    m_log.Exception(exception, "Fleet input command failed for vehicle " + id + ".");
                }
            }
            return "Fleet " + normalized + " commands queued through the normal input scheduler: " + changed + ".";
        }

        [ConsoleCommand(
            documentation: "Orders a bounded number of an unlocked vehicle type through a vehicle depot build queue.",
            customCommandName: "tajs_fleet_order")]
        public string FleetOrder(string? prototypeId, string count, string confirmation)
        {
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }
            if (confirmation != "CONFIRM")
            {
                return "No vehicle ordered. Repeat with confirmation=CONFIRM after reviewing tajs_fleet_plan order.";
            }
            if (!int.TryParse(count, out int requested) || requested <= 0)
            {
                return "Usage: tajs_fleet_order <unlocked-prototype-id> <count> CONFIRM";
            }
            requested = Math.Min(requested, TajsTweaksRuntimeState.FleetBatchLimit);
            if (!m_resolver.TryResolve(out ProtosDb protos) ||
                !protos.TryGetProto<DrivingEntityProto>(new DynamicEntityProto.ID((prototypeId ?? string.Empty).Trim()), out DrivingEntityProto proto) ||
                !proto.IsUnlockedAndAvailable || !m_resolver.TryResolve(out IEntitiesManager entities) ||
                !m_resolver.TryResolve(out IInputScheduler scheduler))
            {
                return "Fleet manager: prototype is not unlocked/available or vehicle input services are unavailable.";
            }
            VehicleDepotBase? depot = entities.GetAllEntitiesOfType<VehicleDepotBase>()
                .FirstOrDefault(x => !x.IsDestroyed && x.IsEnabled && x.Prototype.BuildableEntities.Contains(proto));
            if (depot is null)
            {
                return "Fleet manager: no enabled depot can build that vehicle type.";
            }
            scheduler.ScheduleInputCmd(new AddVehicleToBuildQueueCmd(proto.Id, depot.Id, requested));
            return "Fleet order queued through the normal depot command: requested=" + requested + ", depot=" + depot.Id.Value + ".";
        }

        [ConsoleCommand(
            documentation: "Requests scrap for up to the bounded number of vehicles of one type.",
            customCommandName: "tajs_fleet_scrap_type")]
        public string FleetScrapType(string? prototypeId, string count, string confirmation, string policy = "unassigned-first")
        {
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }
            if (confirmation != "CONFIRM")
            {
                return "No vehicle changed. Repeat with confirmation=CONFIRM after reviewing tajs_fleet_plan scrap.";
            }
            if (!int.TryParse(count, out int requested) || requested <= 0 ||
                !m_resolver.TryResolve(out IEntitiesManager entities) || !m_resolver.TryResolve(out IInputScheduler scheduler))
            {
                return "Usage: tajs_fleet_scrap_type <prototype-id> <count> CONFIRM [assigned-only|unassigned-first|any]";
            }
            bool assignedOnly = string.Equals(policy, "assigned-only", StringComparison.OrdinalIgnoreCase);
            bool unassignedFirst = string.Equals(policy, "unassigned-first", StringComparison.OrdinalIgnoreCase);
            if (!assignedOnly && !unassignedFirst && !string.Equals(policy, "any", StringComparison.OrdinalIgnoreCase))
            {
                return "Usage: tajs_fleet_scrap_type <prototype-id> <count> CONFIRM [assigned-only|unassigned-first|any]";
            }
            requested = Math.Min(requested, TajsTweaksRuntimeState.FleetBatchLimit);
            Vehicle[] candidates = entities.GetAllEntitiesOfType<Vehicle>()
                .Where(x => string.Equals(x.Prototype.Id.Value, (prototypeId ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                            !x.IsOnWayToDepotForScrap && !x.IsOnWayToDepotForReplacement && (!assignedOnly || x.AssignedTo.HasValue))
                .OrderBy(x => unassignedFirst && x.AssignedTo.HasValue ? 1 : 0)
                .ThenBy(x => x.Id.Value)
                .Take(requested)
                .ToArray();
            int changed = 0;
            foreach (Vehicle vehicle in candidates)
            {
                scheduler.ScheduleInputCmd(new ToggleVehicleScrapCmd(vehicle.Id));
                changed++;
            }
            return "Fleet scrap requests queued: " + changed + "/" + requested + ". Progress is reported by tajs_fleet_status.";
        }

        [ConsoleCommand(
            documentation: "Requests bounded replacement of vehicles of one type through the normal replacement workflow.",
            customCommandName: "tajs_fleet_replace_type")]
        public string FleetReplaceType(
            string? sourcePrototypeId,
            string? targetPrototypeId,
            string count,
            string confirmation,
            string policy = "unassigned-first")
        {
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }
            if (confirmation != "CONFIRM")
            {
                return "No vehicle changed. Repeat with confirmation=CONFIRM after reviewing tajs_fleet_plan replace.";
            }
            if (!int.TryParse(count, out int requested) || requested <= 0 ||
                !m_resolver.TryResolve(out IEntitiesManager entities) || !m_resolver.TryResolve(out IInputScheduler scheduler) ||
                !m_resolver.TryResolve(out ProtosDb protos) ||
                !protos.TryGetProto<DrivingEntityProto>(new DynamicEntityProto.ID((targetPrototypeId ?? string.Empty).Trim()), out DrivingEntityProto target) ||
                !target.IsUnlockedAndAvailable)
            {
                return "Usage: tajs_fleet_replace_type <source-prototype-id> <target-prototype-id> <count> CONFIRM [assigned-only|unassigned-first|any]";
            }
            bool assignedOnly = string.Equals(policy, "assigned-only", StringComparison.OrdinalIgnoreCase);
            bool unassignedFirst = string.Equals(policy, "unassigned-first", StringComparison.OrdinalIgnoreCase);
            if (!assignedOnly && !unassignedFirst && !string.Equals(policy, "any", StringComparison.OrdinalIgnoreCase))
            {
                return "Usage: tajs_fleet_replace_type <source-prototype-id> <target-prototype-id> <count> CONFIRM [assigned-only|unassigned-first|any]";
            }
            requested = Math.Min(requested, TajsTweaksRuntimeState.FleetBatchLimit);
            Vehicle[] candidates = entities.GetAllEntitiesOfType<Vehicle>()
                .Where(x => string.Equals(x.Prototype.Id.Value, (sourcePrototypeId ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                            !x.IsOnWayToDepotForScrap && !x.IsOnWayToDepotForReplacement && !x.ReplaceQueued &&
                            (!assignedOnly || x.AssignedTo.HasValue))
                .OrderBy(x => unassignedFirst && x.AssignedTo.HasValue ? 1 : 0)
                .ThenBy(x => x.Id.Value)
                .Take(requested)
                .ToArray();
            int changed = 0;
            foreach (Vehicle vehicle in candidates)
            {
                scheduler.ScheduleInputCmd(new ReplaceVehicleCmd(vehicle.Id, target.Id));
                changed++;
            }
            return "Fleet replacement requests queued: " + changed + "/" + requested + ". Progress is reported by tajs_fleet_status.";
        }

        [ConsoleCommand(
            documentation: "Cancels bounded pending scrap or replacement requests using normal vehicle commands.",
            customCommandName: "tajs_fleet_cancel")]
        public string FleetCancel(string operation, string vehicleIds, string confirmation)
        {
            if (!TajsTweaksRuntimeState.FleetManager)
            {
                return "Fleet manager is disabled.";
            }
            if (confirmation != "CONFIRM")
            {
                return "No pending vehicle request changed. Repeat with confirmation=CONFIRM.";
            }
            if (!m_resolver.TryResolve(out IEntitiesManager entities) || !m_resolver.TryResolve(out IInputScheduler scheduler))
            {
                return "Fleet manager: current scene input services are unavailable.";
            }
            bool cancelScrap = string.Equals(operation, "scrap", StringComparison.OrdinalIgnoreCase);
            bool cancelReplacement = string.Equals(operation, "replace", StringComparison.OrdinalIgnoreCase);
            if (!cancelScrap && !cancelReplacement)
            {
                return "Usage: tajs_fleet_cancel <scrap|replace> <comma-separated IDs> CONFIRM";
            }
            int changed = 0;
            foreach (string text in TajsTweaksRuntimeState.ParseIds(vehicleIds).Take(TajsTweaksRuntimeState.FleetBatchLimit))
            {
                if (!int.TryParse(text, out int id) || !entities.TryGetEntity<Vehicle>(new EntityId(id), out Vehicle vehicle))
                {
                    continue;
                }
                if (cancelScrap && vehicle.IsOnWayToDepotForScrap)
                {
                    scheduler.ScheduleInputCmd(new ToggleVehicleScrapCmd(vehicle.Id));
                    changed++;
                }
                else if (cancelReplacement && (vehicle.IsOnWayToDepotForReplacement || vehicle.ReplaceQueued))
                {
                    scheduler.ScheduleInputCmd(new CancelReplaceVehicleCmd(vehicle.Id));
                    changed++;
                }
            }
            return "Fleet cancellation commands queued: " + changed + ".";
        }

        [ConsoleCommand(
            documentation: "Shows the current bounded HUD layout state.",
            customCommandName: "tajs_hud_status")]
        public string HudStatus() => TweaksHudLayoutFeature.Status();

        [ConsoleCommand(
            documentation: "Clears saved HUD positions and restores the vanilla geometry.",
            customCommandName: "tajs_hud_reset")]
        public string HudReset()
        {
            if (!TajsTweaksRuntimeState.HudLayout)
            {
                return "HUD layout is disabled.";
            }
            return TweaksHudLayoutFeature.Reset(m_resolver, m_settings);
        }
    }
}
