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
using Mafi.Core.Factory.Transports;
using Mafi.Core.Game;
using Mafi.Core.GameLoop;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.Vehicles.Commands;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.World;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Diagnostics;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;
using TajsCOI.Tweaks.Features.Difficulty;
using TajsCOI.Tweaks.Features.Overclocking;
using TajsCOI.Tweaks.Features.Storage;

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
        private const string OverclockingUnavailableMessage = "Per-machine overclocking is unavailable in this scene.";
        private readonly DependencyResolver m_resolver;
        private readonly ITajsRuntime m_runtime;
        private readonly ITajsSettings m_settings;
        private readonly ITajsLogger m_log;
        private readonly TweaksInfiniteGroundwaterFeature m_infiniteGroundwater;
        private TajsOverclockingFeature? m_overclocking;
        private TajsDifficultyFeature? m_difficulty;
        private bool m_overclockingInitializationAttempted;
        private Option<TajsWorldOperationsWindow> m_worldOperationsWindow;
        private Option<TajsFleetManagementWindow> m_fleetManagementWindow;
        private int m_renderTick;

        public TajsTweaksFeatureHost(DependencyResolver resolver, IGameLoopEvents gameLoop, ITajsRuntime runtime, ITajsSettings settings)
        {
            m_resolver = resolver;
            m_runtime = runtime;
            m_settings = settings;
            m_log = runtime.GetLogger(TajsTweaksSettingsCatalog.ModId, "FeatureHost");
            m_infiniteGroundwater = new TweaksInfiniteGroundwaterFeature(resolver, gameLoop, m_log);
            TajsTweaksSettingsCatalog.RegisterAll(settings);
            TajsTweaksRuntimeState.Load(settings);
            TransportPillarRulesFeature.Initialize();
            settings.Changed += OnSettingChanged;
            gameLoop.RenderUpdateEnd.AddNonSaveable(this, OnRenderUpdateEnd);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);
            gameLoop.RegisterInitState(this, InitializeDeferredFeatures);

            TryInstall(runtime, "LinePlacement", TweaksLinePlacementFeature.Install);
            TryInstall(runtime, "PinnedProducts", TweaksPinnedProductsFeature.Install);
            TryInstall(runtime, "QuickRemoveOnDemolish", TweaksQuickRemoveFeature.Install);
            TryInstall(runtime, "ClassicRecipeDisplay", TweaksClassicRecipeFeature.Install);
            TryInstall(runtime, "PlanningBuildingColor", TweaksPlanningColorFeature.Install);
            TryInstall(runtime, "AudioControls", TweaksAudioFeature.Install);
            TryInstall(runtime, "BuildDefaults", TweaksBuildDefaultsFeature.Install);
            TryInstall(runtime, "ResourceOverlays", TweaksResourceOverlayFeature.Install);
            TryInstall(runtime, "MiningTowerColors", harmony => TweaksMiningTowerColorFeature.Install(harmony, settings));
            TryInstall(runtime, "StatisticsTotals", TweaksStatisticsTotalsFeature.Install);
            TryInstall(runtime, "ResourceDepositClusters", TweaksResourceDepositFeature.Install);
            TryInstallResolved(runtime, "InfiniteGroundwater", m_infiniteGroundwater.Install);
            TryInstall(runtime, "ShipCargoPreload", harmony => TweaksShipPreloadFeature.Install(harmony, resolver, settings));
            TryInstall(runtime, "AutoWorldDelivery", harmony => TweaksAutoShipDeliveryFeature.Install(harmony, resolver));
            TryInstall(runtime, "BattleScoreOnMap", TweaksBattleScoreFeature.Install);
            TryInstall(runtime, "FarmFullToggle", TweaksFarmAlertFeature.Install);
            TryInstall(runtime, "CameraAndHud", TweaksCameraFeature.Install);
            bool designationInstalled = TryInstall(runtime, "DesignationControls", TweaksDesignationFeature.Install);
            RegisterDesignationIntegration(runtime, designationInstalled);
            TryInstall(runtime, "NotificationFilter", TweaksNotificationFeature.Install);
            TryInstall(runtime, "MineTruckStaging", TweaksMineTruckStagingFeature.Install);
            TweaksMineTruckStagingFeature.SetResolver(resolver);
            TryInstall(runtime, "StuckTruckRecovery", TweaksStuckTruckRecoveryFeature.Install);
            TweaksStuckTruckRecoveryFeature.SetResolver(resolver);
            TryInstall(runtime, "TransportThroughput", TweaksTransportThroughputFeature.Install);
            TryInstall(runtime, "TransportPillarRules", TransportPillarRulesFeature.Install);
            TryInstall(runtime, "ParkingHqOffload", TweaksParkingHqOffloadFeature.Install);
            TweaksParkingHqOffloadFeature.SetResolver(resolver);
            TryInstall(runtime, "KeepFullEmptyMarkers", TweaksKeepFullEmptyMarkerFeature.Install);
            TryInstall(runtime, "FullscreenHud", TweaksFullscreenHudFeature.Install);
            TryInstall(runtime, "SimulationSpeedDisplay", TweaksSimulationSpeedDisplayFeature.Install);
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

        private static void RegisterDesignationIntegration(ITajsRuntime runtime, bool designationInstalled)
        {
            const string capabilityId = "TajsTweaks.TweaksPlusPlusDesignationVisual";
            const string harmonyOwner = HarmonyId + ".DesignationControls";
            bool expectedOwnerInstalled = designationInstalled &&
                                          TweaksDesignationFeature.HasExpectedHarmonyOwner(harmonyOwner);
            bool optionalTargetAvailable = TweaksDesignationFeature.HasTweaksPlusPlusDesignationVisualIntegration();
            bool available = expectedOwnerInstalled && optionalTargetAvailable;
            runtime.RegisterCapability(
                new RuntimeCapabilityDescriptor(
                    capabilityId,
                    TajsTweaksSettingsCatalog.ModId,
                    "DesignationControls",
                    available ? RuntimeCapabilityState.Available : RuntimeCapabilityState.Unavailable,
                    string.Empty,
                    "Optional Tweaks++ designation-renderer compatibility seam.",
                    available
                        ? string.Empty
                        : !designationInstalled
                            ? "DesignationControls failed installation and its Harmony owner was rolled back."
                            : !expectedOwnerInstalled
                                ? "DesignationControls completed without an observable Tajs Harmony owner."
                                : "Tweaks++ DesignationVisualPatch.RenderPrefix was not found.",
                    RuntimeComponentLifetime.GameplayScene));
            runtime.RegisterComponent(
                new RuntimeComponentDescriptor(
                    TajsTweaksSettingsCatalog.ModId,
                    "DesignationControls",
                    RuntimeComponentLifetime.GameplayScene,
                    "TerrainDesignationsRenderer.renderUpdate and optional Tweaks++ DesignationVisualPatch.RenderPrefix",
                    new[] { harmonyOwner },
                    Array.Empty<string>(),
                    new[] { capabilityId }));
        }

        private bool TryInstall(ITajsRuntime runtime, string id, Action<Harmony> install)
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
                return true;
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
                return false;
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
                change.Descriptor.Key == TajsTweaksSettingsCatalog.HudPositions ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.HudBackgrounds ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.ShowHudOnFullscreenViews)
            {
                TweaksHudLayoutFeature.Apply(m_resolver, m_settings);
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.WorldOperations ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.AutoWorldDelivery)
            {
                TweaksAutoShipDeliveryFeature.Reset();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.InfiniteGroundwater)
            {
                m_infiniteGroundwater.RefreshFromSettings();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.KeepFullEmptyLabelScale)
            {
                TweaksKeepFullEmptyMarkerFeature.Apply();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.EfficiencyOverlay ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.EfficiencyOverlayMode ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.EfficiencyOverlayBuildings ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.EfficiencyOverlayVehicles ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.EfficiencyOverlayUpdateSeconds ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.EfficiencyOverlayRenderDistance ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.EfficiencyOverlayLabelScale)
            {
                TweaksEfficiencyOverlayFeature.ApplySettings();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.TerrainGrid)
            {
                TweaksTerrainGridFeature.ApplySettings();
            }
            if (change.Descriptor.Key == TajsTweaksSettingsCatalog.Overclocking ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockTransportCapacityCompensation ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockTransportSpacingBonus ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockTransportStackBonus ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockMaxPercent ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockMinPercent ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoIntervalSeconds ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoPowerReserve ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoWorkerReserve ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoStepPercent ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoDeadbandPercent ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoMaxStepPercent ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoLowFill ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoNeutralFill ||
                change.Descriptor.Key == TajsTweaksSettingsCatalog.OverclockAutoHighFill)
            {
                m_overclocking?.RefreshSettings();
            }
        }

        private void OnRenderUpdateEnd(GameTime _)
        {
            EnsureOverclockingFeature();
            m_overclocking?.UpdateSelectionInput();
            if (++m_renderTick % 15 == 0)
            {
                TweaksPinnedProductsFeature.Tick();
                TweaksFarmAlertFeature.Tick();
                TweaksShipPreloadFeature.Tick();
                TweaksResourceDepositFeature.Tick(m_resolver);
                TweaksKeepFullEmptyMarkerFeature.Apply();
                TweaksHudLayoutFeature.Apply(m_resolver, m_settings);
                TweaksSimulationSpeedDisplayFeature.Apply(m_resolver);
                m_overclocking?.Tick();
            }
        }

        private void InitializeDeferredFeatures()
        {
            // TajsTweaksFeatureHost is constructed while DependencyResolver is instantiating
            // global dependencies. Resolver-backed feature setup must wait until InitState,
            // after InstantiateAllAndLock has completed, otherwise the resolver rejects the
            // nested TryResolve call as a recursive dependency resolution.
            TryInstall(m_runtime, "StackerDesignationOverlay", harmony => TweaksStackerDesignationFeature.Install(harmony, m_resolver));
            TryInstallResolved(m_runtime, "TerrainGrid", () => TweaksTerrainGridFeature.Install(m_resolver, m_settings, m_log));
            TryInstallResolved(m_runtime, "EfficiencyOverlay", () => TweaksEfficiencyOverlayFeature.Install(m_resolver));
            TryInstall(m_runtime, "SteamAndExhaustStorage", harmony => TweaksSteamStorageFeature.Install(harmony, m_resolver));
            TryInstall(m_runtime, "StorageOverrides", harmony => TweaksStorageFeature.Install(harmony, m_resolver));
            TryInstall(m_runtime, "StorageInspectorControls", harmony => TajsStorageAdvancedFeature.Install(harmony, m_resolver));
            TryInstall(m_runtime, "GameplayPlusPlusBridge", harmony => TweaksGameplayPlusPlusFeature.Install(harmony, m_resolver));
            TryInstallResolved(
                m_runtime,
                "PillarConstraintOverrides",
                () =>
                {
                    if (TajsTweaksRuntimeState.IgnorePillarRequirements && m_resolver.TryResolve(out ProtosDb protosDb))
                    {
                        TransportPillarRulesFeature.ApplyPillarConstraintOverrides(protosDb);
                    }
                });
            TryInstallResolved(m_runtime, TajsDifficultyFeature.ComponentId, InitializeDifficulty);
        }

        private void InitializeDifficulty()
        {
            if (m_difficulty is not null)
            {
                return;
            }
            if (!m_resolver.TryResolve(out GameDifficultyApplier applier) ||
                !m_resolver.TryResolve(out IInputScheduler scheduler) ||
                !m_resolver.TryResolve(out ISaveManager saveManager))
            {
                throw new InvalidOperationException("The active scene does not expose the native difficulty applier, input scheduler, and save manager.");
            }

            m_resolver.TryResolve(out GameNameConfig? gameNameConfig);
            string saveName = string.IsNullOrWhiteSpace(saveManager.GameName) ? "current" : saveManager.GameName;
            m_difficulty = new TajsDifficultyFeature(
                applier,
                scheduler,
                gameNameConfig,
                saveManager,
                saveName,
                m_runtime.GetLogger(TajsTweaksSettingsCatalog.ModId, TajsDifficultyFeature.ComponentId));

            var unsupportedPercent = TajsDifficultyOptionCatalog.UnsupportedPercentMembers;
            m_runtime.ReportCompatibility(
                new CompatibilityReport(
                    TajsTweaksSettingsCatalog.ModId,
                    "DifficultyMetadata",
                    unsupportedPercent.Count == 0 ? CompatibilityState.Compatible : CompatibilityState.Degraded,
                    "Only explicitly audited DiffSettingInfo<Percent> members receive extended options",
                    unsupportedPercent.Count == 0 ? "All native percent members are audited" : string.Join(", ", unsupportedPercent),
                    unsupportedPercent.Count == 0
                        ? "Native difficulty option arrays retain their vanilla values and receive audited Tajs extensions."
                        : "Unknown native percent members retain vanilla options until their semantics are audited."));
        }

        private void EnsureOverclockingFeature()
        {
            if (m_overclockingInitializationAttempted)
            {
                return;
            }

            // The host itself is constructed while DependencyResolver is locking. Resolving
            // IEntitiesManager from the overclocking constructor at that point recursively
            // enters the resolver and aborts the whole game initialization. Defer this optional
            // feature until the first render callback, after global dependency construction has
            // completed, and keep it fail-open if the scene has no compatible seam.
            m_overclockingInitializationAttempted = true;
            try
            {
                var feature = new TajsOverclockingFeature(m_resolver, m_settings, m_runtime);
                if (TryInstall(m_runtime, "Overclocking", feature.Install))
                {
                    m_overclocking = feature;
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Feature 'Overclocking' failed during deferred setup.");
                m_runtime.ReportCompatibility(
                    new CompatibilityReport(
                        TajsTweaksSettingsCatalog.ModId,
                        "Overclocking",
                        CompatibilityState.Disabled,
                        "0.8.7b target or native fallback",
                        exception.GetType().Name,
                        "No behavior was changed for this feature; vanilla behavior remains active."));
            }
        }

        private void OnTerminate()
        {
            m_settings.Changed -= OnSettingChanged;
            TweaksAutoShipDeliveryFeature.Reset();
            m_infiniteGroundwater.Dispose();
            m_overclocking?.Dispose();
            m_difficulty?.Dispose();
            TweaksResourceDepositFeature.Dispose();
            TweaksStackerDesignationFeature.Dispose();
            TweaksTerrainGridFeature.Dispose();
            TweaksEfficiencyOverlayFeature.Dispose();
            TweaksStuckTruckRecoveryFeature.ClearDestinations();
            TweaksKeepFullEmptyMarkerFeature.Reset();
            TajsStorageAdvancedFeature.Reset();
            TweaksHudLayoutFeature.ClearFullscreenState();
            CloseWorldOperationsWindow();
            CloseFleetManagementWindow();
        }

        [ConsoleCommand(
            documentation: "Explains where to open the native difficulty editor for the active save.",
            customCommandName: "tajs_difficulty")]
        public string DifficultyEditorHelp()
        {
            if (m_difficulty is null)
            {
                return "Native difficulty settings are unavailable in this scene.";
            }

            return "Use COI's native Difficulty Settings window from the game menu. TajsDifficulty extends its audited options there.";
        }

        [ConsoleCommand(
            documentation: "Shows supported difficulty values and lifecycle classifications.",
            customCommandName: "tajs_difficulty_status")]
        public string DifficultyStatus() => m_difficulty?.Status() ?? "Native difficulty settings are unavailable in this scene.";

        [ConsoleCommand(
            documentation: "Queues one runtime-safe difficulty value. Extreme values require CONFIRM.",
            customCommandName: "tajs_difficulty_set")]
        public string SetDifficulty(string? memberName, string? value, string? confirmation = null)
        {
            if (m_difficulty is null)
            {
                return "Native difficulty settings are unavailable in this scene.";
            }
            if (string.IsNullOrWhiteSpace(memberName) || string.IsNullOrWhiteSpace(value))
            {
                return "Usage: tajs_difficulty_set <GameDifficultyConfig member> <value> [CONFIRM]";
            }
            return m_difficulty.Set(memberName!, value, string.Equals(confirmation, "CONFIRM", StringComparison.OrdinalIgnoreCase));
        }

        [ConsoleCommand(
            documentation: "Queues a reset of runtime-safe difficulty values to original save or vanilla values.",
            customCommandName: "tajs_difficulty_reset")]
        public string ResetDifficulty(string? target, string? confirmation = null)
        {
            if (m_difficulty is null)
            {
                return "Native difficulty settings are unavailable in this scene.";
            }
            return m_difficulty.Reset(target, string.Equals(confirmation, "CONFIRM", StringComparison.OrdinalIgnoreCase));
        }

        [ConsoleCommand(
            documentation: "Shows the current per-entity overclock rate and effective policy.",
            customCommandName: "tajs_overclock_status")]
        public string OverclockStatus(string entityId)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(entityId, out int parsedId))
            {
                return "Usage: tajs_overclock_status <entity-id>";
            }

            return m_overclocking.Status(new EntityId(parsedId));
        }

        [ConsoleCommand(
            documentation: "Lists supported overclocking entities with optional type/state/group filters and deterministic sorting.",
            customCommandName: "tajs_overclock_list")]
        public string OverclockList(string? type = null, string? state = null, string? groupId = null, string? sort = null)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            int? parsedGroup = null;
            if (groupId is not null)
            {
                if (!int.TryParse(groupId, out int value))
                {
                    return "Usage: tajs_overclock_list [all|machine|ore|office|waste] [all|auto|manual|boosted|default|group] [group-id] [id|rate|type|state]";
                }

                parsedGroup = value;
            }

            return m_overclocking.ListEntities(type, state, parsedGroup, sort);
        }

        [ConsoleCommand(
            documentation: "Queues a bounded per-entity overclock command through the normal input scheduler.",
            customCommandName: "tajs_overclock_set")]
        public string OverclockSet(string entityId, string percent)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(entityId, out int parsedId) || !int.TryParse(percent, out int parsedPercent))
            {
                return "Usage: tajs_overclock_set <entity-id> <percent>";
            }

            return m_overclocking.QueueSetManual(new EntityId(parsedId), parsedPercent, out string message) ? message : "Not queued: " + message;
        }

        [ConsoleCommand(
            documentation: "Enables or disables per-entity demand-based Auto mode; optional min/max bounds override the group.",
            customCommandName: "tajs_overclock_auto")]
        public string OverclockAuto(string entityId, string enabled, string? minimum = null, string? maximum = null)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            int parsedMinimum = 0;
            int parsedMaximum = 0;
            if (!int.TryParse(entityId, out int parsedId) || !bool.TryParse(enabled, out bool parsedEnabled) ||
                minimum is not null && !int.TryParse(minimum, out parsedMinimum) ||
                maximum is not null && !int.TryParse(maximum, out parsedMaximum))
            {
                return "Usage: tajs_overclock_auto <entity-id> <true|false> [min-percent] [max-percent]";
            }

            int? min = minimum is null ? null : parsedMinimum;
            int? max = maximum is null ? null : parsedMaximum;
            return m_overclocking.QueueSetAuto(new EntityId(parsedId), parsedEnabled, min, max, out string message) ? message : "Not queued: " + message;
        }

        [ConsoleCommand(
            documentation: "Returns one supported entity to its group or global overclock policy.",
            customCommandName: "tajs_overclock_reset")]
        public string OverclockReset(string entityId)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(entityId, out int parsedId))
            {
                return "Usage: tajs_overclock_reset <entity-id>";
            }

            return m_overclocking.QueueReset(new EntityId(parsedId), out string message) ? message : "Not queued: " + message;
        }

        [ConsoleCommand(
            documentation: "Creates a save-scoped named overclock group.",
            customCommandName: "tajs_overclock_group_create")]
        public string OverclockGroupCreate(string? name = null)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            OverclockGroup group = m_overclocking.CreateGroup(name);
            return "Created overclock group " + group.Id + " ('" + group.Name + "').";
        }

        [ConsoleCommand(
            documentation: "Lists save-scoped overclock groups and their members.",
            customCommandName: "tajs_overclock_group_list")]
        public string OverclockGroupList()
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (m_overclocking.Groups.Count == 0)
            {
                return "No overclock groups exist.";
            }

            return string.Join(
                " | ",
                m_overclocking.Groups.Select(group =>
                    group.Id + ":" + group.Name + " members=" + group.Members.Count + " locked=" + group.Locked +
                    " default=" + (group.ManualDefault == 0 ? "global" : group.ManualDefault + "%") + " auto=" + group.Auto));
        }

        [ConsoleCommand(
            documentation: "Renames a save-scoped overclock group.",
            customCommandName: "tajs_overclock_group_rename")]
        public string OverclockGroupRename(string groupId, string name)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            return int.TryParse(groupId, out int parsedGroup) && m_overclocking.RenameGroup(parsedGroup, name)
                ? "Renamed overclock group " + parsedGroup + "."
                : "Group is missing, locked, or the name is empty.";
        }

        [ConsoleCommand(
            documentation: "Deletes a save-scoped overclock group without changing entity policies.",
            customCommandName: "tajs_overclock_group_delete")]
        public string OverclockGroupDelete(string groupId)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(groupId, out int parsedGroup))
            {
                return "Usage: tajs_overclock_group_delete <group-id>";
            }

            return m_overclocking.QueueDeleteGroup(parsedGroup, out string deleteMessage)
                ? deleteMessage
                : "Not queued: " + deleteMessage;
        }

        [ConsoleCommand(
            documentation: "Starts a screen-space rectangle picker for a save-scoped overclock group.",
            customCommandName: "tajs_overclock_group_pick")]
        public string OverclockGroupPick(string groupId)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            return int.TryParse(groupId, out int parsedGroup)
                ? m_overclocking.StartGroupSelection(parsedGroup)
                : "Usage: tajs_overclock_group_pick <group-id>";
        }

        [ConsoleCommand(
            documentation: "Highlights all supported members of an overclock group in the world.",
            customCommandName: "tajs_overclock_group_show")]
        public string OverclockGroupShow(string groupId)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            return int.TryParse(groupId, out int parsedGroup) && m_overclocking.ShowGroup(parsedGroup)
                ? "Showing overclock group " + parsedGroup + "."
                : "Group is missing or world highlighting is unavailable.";
        }

        [ConsoleCommand(
            documentation: "Clears overclock group world highlights.",
            customCommandName: "tajs_overclock_group_hide")]
        public string OverclockGroupHide()
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            m_overclocking.ClearHighlights();
            return "Overclock group highlights cleared.";
        }

        [ConsoleCommand(
            documentation: "Locks or unlocks a group so bulk and rectangle operations cannot change it while locked.",
            customCommandName: "tajs_overclock_group_lock")]
        public string OverclockGroupLock(string groupId, string locked)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            return int.TryParse(groupId, out int parsedGroup) && bool.TryParse(locked, out bool parsedLocked) &&
                   m_overclocking.SetGroupLocked(parsedGroup, parsedLocked)
                ? "Group " + parsedGroup + " locked=" + parsedLocked + "."
                : "Usage: tajs_overclock_group_lock <group-id> <true|false>";
        }

        [ConsoleCommand(
            documentation: "Sets the highlight color index for a group (0 through 8).",
            customCommandName: "tajs_overclock_group_color")]
        public string OverclockGroupColor(string groupId, string colorIndex)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            return int.TryParse(groupId, out int parsedGroup) && int.TryParse(colorIndex, out int parsedColor) &&
                   m_overclocking.SetGroupColor(parsedGroup, parsedColor)
                ? "Group " + parsedGroup + " color updated."
                : "Usage: tajs_overclock_group_color <group-id> <0-8>";
        }

        [ConsoleCommand(
            documentation: "Adds one supported entity to a named overclock group.",
            customCommandName: "tajs_overclock_group_add")]
        public string OverclockGroupAdd(string groupId, string entityId)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(groupId, out int parsedGroup) || !int.TryParse(entityId, out int parsedEntity))
            {
                return "Usage: tajs_overclock_group_add <group-id> <entity-id>";
            }

            return m_overclocking.QueueAddToGroup(parsedGroup, new EntityId(parsedEntity), out string addMessage)
                ? addMessage
                : "Not queued: " + addMessage;
        }

        [ConsoleCommand(
            documentation: "Removes one entity from a named overclock group.",
            customCommandName: "tajs_overclock_group_remove")]
        public string OverclockGroupRemove(string groupId, string entityId)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(groupId, out int parsedGroup) || !int.TryParse(entityId, out int parsedEntity))
            {
                return "Usage: tajs_overclock_group_remove <group-id> <entity-id>";
            }

            return m_overclocking.QueueRemoveFromGroup(parsedGroup, new EntityId(parsedEntity), out string removeMessage)
                ? removeMessage
                : "Not queued: " + removeMessage;
        }

        [ConsoleCommand(
            documentation: "Sets a group's default rate for members without an entity override.",
            customCommandName: "tajs_overclock_group_default")]
        public string OverclockGroupDefault(string groupId, string percent)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(groupId, out int parsedGroup) || !int.TryParse(percent, out int parsedPercent))
            {
                return "Usage: tajs_overclock_group_default <group-id> <percent>";
            }

            return m_overclocking.QueueSetGroupDefault(parsedGroup, parsedPercent, out string message) ? message : "Not queued: " + message;
        }

        [ConsoleCommand(
            documentation: "Applies a group rate as an explicit entity override to every supported member.",
            customCommandName: "tajs_overclock_group_apply")]
        public string OverclockGroupApply(string groupId, string percent)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            if (!int.TryParse(groupId, out int parsedGroup) || !int.TryParse(percent, out int parsedPercent))
            {
                return "Usage: tajs_overclock_group_apply <group-id> <percent>";
            }

            return m_overclocking.QueueApplyGroupToMembers(parsedGroup, parsedPercent, out string message) ? message : "Not queued: " + message;
        }

        [ConsoleCommand(
            documentation: "Enables or disables Auto mode for a named group; optional min/max bounds apply to members without overrides.",
            customCommandName: "tajs_overclock_group_auto")]
        public string OverclockGroupAuto(string groupId, string enabled, string? minimum = null, string? maximum = null)
        {
            if (m_overclocking is null)
            {
                return OverclockingUnavailableMessage;
            }

            int parsedMinimum = 0;
            int parsedMaximum = 0;
            if (!int.TryParse(groupId, out int parsedGroup) || !bool.TryParse(enabled, out bool parsedEnabled) ||
                minimum is not null && !int.TryParse(minimum, out parsedMinimum) ||
                maximum is not null && !int.TryParse(maximum, out parsedMaximum))
            {
                return "Usage: tajs_overclock_group_auto <group-id> <true|false> [min-percent] [max-percent]";
            }

            int? min = minimum is null ? null : parsedMinimum;
            int? max = maximum is null ? null : parsedMaximum;
            return m_overclocking.QueueSetGroupAuto(parsedGroup, parsedEnabled, min, max, out string message) ? message : "Not queued: " + message;
        }

        [ConsoleCommand(
            documentation: "Shows the configured transport and train pillar rules, including their vanilla values.",
            customCommandName: "tajs_transport_pillars_status")]
        public string TransportPillarsStatus() => TransportPillarRulesFeature.Describe();

        [ConsoleCommand(
            documentation: "Restores all transport and train pillar settings to their vanilla values; restart the game to apply them.",
            customCommandName: "tajs_transport_pillars_reset")]
        public string ResetTransportPillarRules()
        {
            SettingSetResult[] results =
            {
                m_settings.TrySet(
                    TajsTweaksSettingsCatalog.ModId,
                    TajsTweaksSettingsCatalog.TransportPillarSupportRadius,
                    TransportPillarRulesFeature.VanillaTransportSupportRadius),
                m_settings.TrySet(
                    TajsTweaksSettingsCatalog.ModId,
                    TajsTweaksSettingsCatalog.TransportPillarMaxHeight,
                    TransportPillarRulesFeature.VanillaTransportPillarHeight),
                m_settings.TrySet(
                    TajsTweaksSettingsCatalog.ModId,
                    TajsTweaksSettingsCatalog.TrainTrackPillarMaxHeight,
                    TransportPillarRulesFeature.VanillaTrainPillarHeight),
                m_settings.TrySet(
                    TajsTweaksSettingsCatalog.ModId,
                    TajsTweaksSettingsCatalog.TrainTrackPillarSupportDistance,
                    TransportPillarRulesFeature.VanillaTrainSupportDistance),
                m_settings.TrySet(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.IgnorePillarRequirements, false),
            };
            return results.All(x => x.Success)
                ? "Transport and train pillar settings reset to vanilla. Restart the game to apply them."
                : "Pillar reset was incomplete: " + string.Join("; ", results.Where(x => !x.Success).Select(x => x.Error));
        }

        [ConsoleCommand(
            documentation: "Queues a bounded rectangular transport pillar add/remove operation through the native manager.",
            customCommandName: "tajs_transport_pillars_area")]
        public string TransportPillarsArea(string? operation, string minX, string minY, string maxX, string maxY, string? confirmation = null)
        {
            if (!int.TryParse(minX, out int parsedMinX) || !int.TryParse(minY, out int parsedMinY) ||
                !int.TryParse(maxX, out int parsedMaxX) || !int.TryParse(maxY, out int parsedMaxY) ||
                !m_resolver.TryResolve(out TransportsManager manager) || !m_resolver.TryResolve(out IInputScheduler scheduler))
            {
                return "Usage: tajs_transport_pillars_area <add|remove> <min-x> <min-y> <max-x> <max-y> [CONFIRM]";
            }

            return TransportPillarRulesFeature.ApplyTransportArea(
                manager,
                scheduler,
                operation,
                parsedMinX,
                parsedMinY,
                parsedMaxX,
                parsedMaxY,
                confirmation);
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
