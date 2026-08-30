// Taj's COI Mods | TajsTweaksSettingsCatalog.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using TajsCOI.Common.Settings;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     One persisted settings surface for the tweaks mod. Defaults deliberately preserve
    ///     vanilla behaviour; feature code treats malformed optional text as an empty override.
    /// </summary>
    internal static class TajsTweaksSettingsCatalog
    {
        internal const string ModId = "TajsTweaks";
        internal const string DisplayName = "Taj's Tweaks";

        internal const string LinePlacement = "line_placement";
        internal const string LinePlacementShortcut = "line_placement_shortcut";
        internal const string LinePlacementLength = "line_placement_length";
        internal const string PinnedSort = "pinned_sort";
        internal const string PinnedSortMode = "pinned_sort_mode";
        internal const string PinnedSortDirection = "pinned_sort_direction";
        internal const string PinnedHysteresis = "pinned_hysteresis_percent";
        internal const string PinnedCompact = "pinned_compact";
        internal const string PinnedBarColors = "pinned_bar_colors";
        internal const string PinnedColumns = "pinned_columns";
        internal const string PinnedAutoColumns = "pinned_auto_columns";
        internal const string PinnedRowsPerColumn = "pinned_rows_per_column";
        internal const string PinnedLowOnly = "pinned_low_only";
        internal const string PinnedLowThreshold = "pinned_low_threshold_percent";
        internal const string PinnedLowLimit = "pinned_low_limit";
        internal const string QuickRemoveOnDemolish = "quick_remove_on_demolish";
        internal const string ClassicRecipeDisplay = "classic_recipe_display";
        internal const string ResearchTreeLayout = "research_tree_layout";
        internal const string RecipePickerDensity = "recipe_picker_density";
        internal const string RecipePickerTileSize = "recipe_picker_tile_size";
        internal const string RecipePickerSpacing = "recipe_picker_spacing";
        internal const string RecipePickerColumns = "recipe_picker_columns";
        internal const string PlanningBuildingColor = "planning_building_color";
        internal const string VehicleSoundRange = "vehicle_sound_range";
        internal const string MachineSoundRange = "machine_sound_range";
        internal const string TrainSoundVolume = "train_sound_volume";
        internal const string TrainSoundRange = "train_sound_range";
        internal const string TrainTuningProfile = "train_tuning_profile";
        internal const string LocomotiveNumbering = "locomotive_numbering";

        internal const string DefaultsUnit = "defaults_unit";
        internal const string DefaultsLoose = "defaults_loose";
        internal const string DefaultsFluid = "defaults_fluid";
        internal const string DefaultsWarehouse = "defaults_warehouse";
        internal const string DefaultsMineDump = "defaults_mine_dump_products";
        internal const string DefaultsMineWarn = "defaults_mine_warning_products";

        internal const string ResourceOverlay = "resource_overlay";
        internal const string ResourceOverlayDepth = "resource_overlay_depth";
        internal const string ResourceOverlayTowerAreas = "resource_overlay_tower_areas";
        internal const string ResourceOverlayTowerLabels = "resource_overlay_tower_labels";
        internal const string ResourceOverlayLabelScale = "resource_overlay_label_scale_percent";
        internal const string ResourceOverlayLabelAlpha = "resource_overlay_label_alpha_percent";
        internal const string ResourceOverlayLabelHeight = "resource_overlay_label_height";
        internal const string ResourceTowerLineColor = "resource_tower_line_color";
        internal const string ResourceTowerLineWidth = "resource_tower_line_width";
        internal const string ResourceTowerZoomDamping = "resource_tower_zoom_damping";
        internal const string ResourceTowerZoomStart = "resource_tower_zoom_start";
        internal const string ResourceTowerAreaHeight = "resource_tower_area_height";
        internal const string ResourceTowerColors = "resource_tower_colors";
        internal const string GroundwaterPolicy = "groundwater_policy";
        internal const string GroundwaterRegenerationPercent = "groundwater_regeneration_percent";

        internal const string GroundwaterMinimumPercent = "groundwater_minimum_percent";

        // Retained for one-way migration of the former boolean setting.
        internal const string InfiniteGroundwater = "infinite_groundwater";
        internal const string AllowSteam = "allow_steam";
        internal const string AllowExhaust = "allow_exhaust";

        internal const string WorldOperations = "world_operations";
        internal const string AutoWorldDelivery = "auto_world_delivery";
        internal const string AutoExploration = "auto_exploration";
        internal const string WorldVisibilityHiddenCategories = "world_visibility_hidden_categories";
        internal const string ShipPreload = "ship_preload";
        internal const string ShipPreloadData = "ship_preload_data";
        internal const string ShipyardOutputTransport = "shipyard_output_transport";
        internal const string ShipUnloadPolicy = "ship_unload_policy";
        internal const string TerrainDesignationPriority = "terrain_designation_priority";

        internal const string RecoverTrucks = "recover_stuck_trucks";
        internal const string RecoverPeriod = "recover_stuck_trucks_period_seconds";
        internal const string StageMineTrucks = "stage_mine_trucks";
        internal const string StageMineTrucksScan = "stage_mine_trucks_scan_seconds";

        internal const string FreeCamera = "free_camera";
        internal const string UnlimitedZoom = "unlimited_zoom";
        internal const string GroundClipping = "ground_clipping";
        internal const string HudLayout = "hud_layout";
        internal const string HudDragLocked = "hud_drag_locked";
        internal const string HudScale = "hud_scale_percent";
        internal const string HudHidden = "hud_hidden_keys";
        internal const string HudPositions = "hud_positions";
        internal const string HudBackgrounds = "hud_backgrounds";
        internal const string ShowHudOnFullscreenViews = "show_hud_on_fullscreen_views";
        internal const string HudActionPolicy = "hud_action_policy";
        internal const string HudActionCollapsed = "hud_action_collapsed";
        internal const string HudActionHoverReveal = "hud_action_hover_reveal";
        internal const string HudRealWorldClock = "hud_real_world_clock";
        internal const string HudClock24Hour = "hud_clock_24_hour";

        internal const string AdaptiveTowerInspector = "adaptive_tower_inspector";
        internal const string InspectorSectionCollapsed = "inspector_section_collapsed";
        internal const string InspectorVehicleFilters = "inspector_vehicle_filters";
        internal const string InspectorVehicleVisibleRows = "inspector_vehicle_visible_rows";

        internal const string StorageOverrides = "storage_overrides";
        internal const string StorageInspectorControls = "storage_inspector_controls";
        internal const string StorageMultiplier = "storage_capacity_multiplier";
        internal const string StorageThroughputMultiplier = "storage_throughput_multiplier";
        internal const string StorageOverrideData = "storage_override_data";

        internal const string DesignationControls = "designation_controls";
        internal const string DesignationLimit = "designation_limit";
        internal const string HideDesignations = "hide_designations";

        internal const string NotificationFilter = "notification_filter";
        internal const string MutedNotifications = "muted_notification_ids";
        internal const string FarmWarnings = "filter_farm_warnings";
        internal const string FarmFullToggleAlways = "farm_full_toggle_always";
        internal const string BattleScoreOnMap = "battle_score_on_map";
        internal const string ElectricityComputingTotals = "electricity_computing_totals";
        internal const string StackerDesignationOverlay = "stacker_designation_overlay";
        internal const string TerrainGrid = "terrain_grid";
        internal const string TerrainXRay = "terrain_xray";
        internal const string TerrainXRayShortcut = "terrain_xray_shortcut";
        internal const string EfficiencyOverlay = "efficiency_overlay";
        internal const string EfficiencyOverlayMode = "efficiency_overlay_mode";
        internal const string EfficiencyOverlayBuildings = "efficiency_overlay_buildings";
        internal const string EfficiencyOverlayVehicles = "efficiency_overlay_vehicles";
        internal const string EfficiencyOverlayUpdateSeconds = "efficiency_overlay_update_seconds";
        internal const string EfficiencyOverlayRenderDistance = "efficiency_overlay_render_distance";
        internal const string EfficiencyOverlayLabelScale = "efficiency_overlay_label_scale";
        internal const string KeepFullEmptyLabelScale = "keep_full_empty_label_scale";
        internal const string ParkingHqOffloadMode = "parking_hq_offload_mode";
        internal const string DumpToShipyard = "dump_to_shipyard";
        internal const string BridgeTrussEnabled = "bridge_truss_enabled";
        internal const string BridgeCableEnabled = "bridge_cable_enabled";
        internal const string BridgeScaleMode = "bridge_scale_mode";
        internal const string CenterDriving = "center_driving";

        internal const string TransportPillarSupportRadius = "transport_pillar_support_radius";
        internal const string TransportPillarMaxHeight = "transport_pillar_max_height";
        internal const string TrainTrackPillarMaxHeight = "train_track_pillar_max_height";
        internal const string TrainTrackPillarSupportDistance = "train_track_pillar_support_distance";
        internal const string IgnorePillarRequirements = "ignore_pillar_requirements";

        internal const string FleetManager = "fleet_manager";
        internal const string FleetBatchLimit = "fleet_batch_limit";

        internal const string Overclocking = "overclocking";
        internal const string OverclockTransportCapacityCompensation = "overclock_transport_capacity_compensation";
        internal const string OverclockTransportSpacingBonus = "overclock_transport_spacing_bonus_percent";
        internal const string OverclockTransportStackBonus = "overclock_transport_stack_bonus_percent";
        internal const string OverclockMaxPercent = "overclock_max_percent";
        internal const string OverclockMinPercent = "overclock_min_percent";
        internal const string OverclockPowerCurve = "overclock_power_curve";
        internal const string OverclockWorkerCurve = "overclock_worker_curve";
        internal const string OverclockComputingCurve = "overclock_computing_curve";
        internal const string OverclockMaintenanceCurve = "overclock_maintenance_curve";
        internal const string OverclockAutoIntervalSeconds = "overclock_auto_interval_seconds";
        internal const string OverclockAutoPowerReserve = "overclock_auto_power_reserve_percent";
        internal const string OverclockAutoWorkerReserve = "overclock_auto_worker_reserve";
        internal const string OverclockAutoStepPercent = "overclock_auto_step_percent";
        internal const string OverclockAutoDeadbandPercent = "overclock_auto_deadband_percent";
        internal const string OverclockAutoMaxStepPercent = "overclock_auto_max_step_percent";
        internal const string OverclockAutoLowFill = "overclock_auto_low_fill_percent";
        internal const string OverclockAutoNeutralFill = "overclock_auto_neutral_fill_percent";
        internal const string OverclockAutoHighFill = "overclock_auto_high_fill_percent";

        // Wave 7 sandbox controls. Every switch is opt-in and defaults to vanilla behavior.
        internal const string SandboxDisableDiseaseEffects = "sandbox_disable_disease_effects";
        internal const string SandboxInfiniteFocus = "sandbox_infinite_focus";
        internal const string SandboxFocusMultiplier = "sandbox_focus_multiplier";
        internal const string SandboxDisableAirPollutionEffects = "sandbox_disable_air_pollution_effects";
        internal const string SandboxDisableAirPollutionProduction = "sandbox_disable_air_pollution_production";
        internal const string SandboxDisableWaterPollutionEffects = "sandbox_disable_water_pollution_effects";
        internal const string SandboxDisableWaterPollutionProduction = "sandbox_disable_water_pollution_production";
        internal const string SandboxDisableShipPollution = "sandbox_disable_ship_pollution";
        internal const string SandboxDisableVehiclePollution = "sandbox_disable_vehicle_pollution";
        internal const string SandboxDisableTrainPollution = "sandbox_disable_train_pollution";
        internal const string SandboxDisableFoodNeed = "sandbox_disable_food_need";
        internal const string SandboxDisableSettlementNeeds = "sandbox_disable_settlement_needs";
        internal const string SandboxDisableSolidWaste = "sandbox_disable_solid_waste";
        internal const string SandboxDisableBiowaste = "sandbox_disable_biowaste";
        internal const string SandboxDisableElectricityNeed = "sandbox_disable_electricity_need";
        internal const string SandboxDisableCleanWaterNeed = "sandbox_disable_clean_water_need";
        internal const string SandboxDisableWastewater = "sandbox_disable_wastewater";
        internal const string SandboxDisableComputingNeed = "sandbox_disable_computing_need";

        internal const string SandboxInstantCargoShip = "sandbox_instant_cargo_ship";
        internal const string SandboxDesignMode = "sandbox_design_mode";
        internal const string SandboxFreeResearch = "sandbox_free_research";
        internal const string SandboxNoConstructionCosts = "sandbox_no_construction_costs";
        internal const string SandboxFastOreSorting = "sandbox_fast_ore_sorting";
        internal const string SandboxInstantStorageEmpty = "sandbox_instant_storage_empty";
        internal const string SandboxAlwaysAllowBulldoze = "sandbox_always_allow_bulldoze";
        internal const string SandboxBulldozeWhitelist = "sandbox_bulldoze_whitelist";

        internal const string TuningShipyardCargoMultiplier = "tuning_shipyard_cargo_multiplier";
        internal const string TuningTruckLoadDurationMultiplier = "tuning_truck_load_duration_multiplier";
        internal const string TuningOreSorterBufferMultiplier = "tuning_ore_sorter_buffer_multiplier";
        internal const string TuningOreSorterThroughputMultiplier = "tuning_ore_sorter_throughput_multiplier";
        internal const string TuningShaftThroughputMultiplier = "tuning_shaft_throughput_multiplier";
        internal const string TuningThermalStorageCapacityMultiplier = "tuning_thermal_storage_capacity_multiplier";

        internal const string DiseaseScalingPolicy = "disease_scaling_policy";
        internal const string DiseaseScalingCustomFractions = "disease_scaling_custom_fractions";

        private static readonly IReadOnlyList<SettingChoice> s_modes = new[]
        {
            new SettingChoice("vanilla", "Vanilla"),
            new SettingChoice("import", "Import"),
            new SettingChoice("export", "Export"),
            new SettingChoice("both", "Import and export"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_sortModes = new[]
        {
            new SettingChoice("quantity", "Stored quantity"), new SettingChoice("fill", "Fill percentage"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_directions = new[]
        {
            new SettingChoice("descending", "High to low"), new SettingChoice("ascending", "Low to high"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_planningColors = new[]
        {
            new SettingChoice("vanilla", "Vanilla"),
            new SettingChoice("yellow", "Yellow"),
            new SettingChoice("orange", "Orange"),
            new SettingChoice("red", "Red"),
            new SettingChoice("pink", "Pink"),
            new SettingChoice("purple", "Purple"),
            new SettingChoice("green", "Green"),
            new SettingChoice("lime", "Lime"),
            new SettingChoice("white", "White"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_towerLineColors = new[]
        {
            new SettingChoice("by_tower", "By tower"),
            new SettingChoice("blue", "Blue"),
            new SettingChoice("yellow", "Yellow"),
            new SettingChoice("red", "Red"),
            new SettingChoice("green", "Green"),
            new SettingChoice("white", "White"),
            new SettingChoice("orange", "Orange"),
            new SettingChoice("purple", "Purple"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_parkingHqOffloadModes = new[]
        {
            new SettingChoice("vanilla", "Vanilla / provider default"), new SettingChoice("enabled", "Enabled"), new SettingChoice("disabled", "Disabled"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_shipUnloadPolicies = new[]
        {
            new SettingChoice("vanilla", "Vanilla order"), new SettingChoice("smallest_stack_first", "Smallest stack first"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_terrainDesignationPriorities = new[]
        {
            new SettingChoice("vanilla", "Vanilla scorer"),
            new SettingChoice("leveling_first", "Leveling first"),
            new SettingChoice("digging_first", "Digging first"),
            new SettingChoice("filling_first", "Filling first"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_trainTuningProfiles = new[]
        {
            new SettingChoice("vanilla", "Vanilla"), new SettingChoice("efficient", "Efficient fuel"), new SettingChoice("power", "Climbing power"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_locomotiveNumbering = new[]
        {
            new SettingChoice("vanilla", "Native numbering"), new SettingChoice("sequential", "Sequential"), new SettingChoice("random", "Seeded random"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_bridgeScaleModes = new[]
        {
            new SettingChoice("off", "Off"), new SettingChoice("instant", "Instant"), new SettingChoice("gradual", "Gradual"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_efficiencyOverlayModes = new[]
        {
            new SettingChoice("percentage", "Percentage"), new SettingChoice("status", "Status"), new SettingChoice("compact", "Compact marker"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_diseaseScalingPolicies = new[]
        {
            new SettingChoice("vanilla", "Vanilla distances"),
            new SettingChoice("map_scaled", "Map-scaled distances"),
            new SettingChoice("custom", "Custom fractions"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_researchTreeLayouts = new[]
        {
            new SettingChoice("vanilla", "Vanilla spacing"), new SettingChoice("compact", "Compact spacing"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_recipePickerDensities = new[]
        {
            new SettingChoice("vanilla", "Vanilla list"), new SettingChoice("compact", "Compact columns"), new SettingChoice("custom", "Custom"),
        };

        private static readonly IReadOnlyList<SettingChoice> s_groundwaterPolicies = new[]
        {
            new SettingChoice("vanilla", "Vanilla"),
            new SettingChoice("regenerate", "Regenerate"),
            new SettingChoice("maintain_minimum", "Maintain minimum"),
            new SettingChoice("infinite", "Infinite"),
        };

        internal static IReadOnlyList<SettingDescriptor> All { get; } = new SettingDescriptor[]
        {
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                LinePlacement,
                "Line placement mode",
                "Uses the game's existing line-drag placement path for single buildings when enabled.",
                false,
                "Building",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                LinePlacementShortcut,
                "Line placement shortcut",
                "Unity KeyCode name held while the first preview is anchored; the default is LeftAlt.",
                "LeftAlt",
                "Building",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: LinePlacement),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                LinePlacementLength,
                "Maximum line length",
                "Safety cap for repeated previews; invalid previews remain visible and are never submitted.",
                60,
                1,
                512,
                1,
                "Building",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: LinePlacement),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                PinnedSort,
                "Sort pinned products",
                "Sorts pinned product rows by quantity or fill percentage with a small hysteresis margin.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                PinnedSortDirection,
                "Pinned sort direction",
                "Direction used by pinned-product sorting.",
                "descending",
                s_directions,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: PinnedSort),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                PinnedSortMode,
                "Pinned sort value",
                "Sort by total stored quantity or by storage fill percentage.",
                "quantity",
                s_sortModes,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: PinnedSort),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                PinnedHysteresis,
                "Pinned sort hysteresis",
                "Minimum relative change required before a row changes order.",
                5,
                0,
                50,
                1,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: PinnedSort,
                valueFormat: SettingValueFormat.Percentage),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                PinnedCompact,
                "Compact pinned products",
                "Uses a compact row presentation without changing row interaction semantics.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                PinnedBarColors,
                "Pinned product bar colors",
                "Colors pinned-product fill bars by their current fill level; vanilla colors are restored when disabled.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                PinnedColumns,
                "Pinned product columns",
                "One column or a bounded number of columns for pinned products.",
                1,
                1,
                4,
                1,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                PinnedAutoColumns,
                "Automatic pinned-product columns",
                "Automatically chooses up to four pinned-product columns from the bounded rows-per-column setting; this takes precedence over the fixed column count.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                PinnedRowsPerColumn,
                "Pinned rows per column",
                "Maximum pinned-product rows per column when automatic columns are enabled.",
                20,
                10,
                35,
                1,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: PinnedAutoColumns),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                PinnedLowOnly,
                "Show low pinned products only",
                "Filters the pinned HUD to rows at or below the configured fill threshold.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                PinnedLowThreshold,
                "Low-stock threshold",
                "Fill percentage at or below which a pinned product is considered low stock.",
                25,
                0,
                100,
                1,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: PinnedLowOnly,
                valueFormat: SettingValueFormat.Percentage),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                PinnedLowLimit,
                "Low-stock row limit",
                "Maximum number of low-stock rows retained in the HUD.",
                20,
                1,
                100,
                1,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: PinnedLowOnly),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                QuickRemoveOnDemolish,
                "Quick-remove cargo on demolish",
                "Schedules the normal paid quick-remove command when a storage/entity demolition is requested.",
                false,
                "Building",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ClassicRecipeDisplay,
                "Classic recipe display",
                "Shows actual per-cycle recipe quantities in building panels while retaining the normalized rate in the duration tooltip.",
                false,
                "Building",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                ResearchTreeLayout,
                "Research tree layout",
                "Uses the native research tree coordinates with either vanilla or compact spacing. Connectors and hitboxes use the same coordinates.",
                "vanilla",
                s_researchTreeLayouts,
                "Research",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                RecipePickerDensity,
                "Recipe picker density",
                "Keeps the native recipe picker in vanilla mode, uses a compact two-column policy, or uses the custom tile/spacing/column values below.",
                "vanilla",
                s_recipePickerDensities,
                "Research",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                RecipePickerTileSize,
                "Recipe picker tile size",
                "Target recipe tile size in pixels; 36 matches the captured 0.8.7b product icon height.",
                36,
                24,
                72,
                1,
                "Research",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: RecipePickerDensity),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                RecipePickerSpacing,
                "Recipe picker spacing",
                "Gap between recipe cards in points; 1 point (4 pixels) matches the captured vanilla RecipesColumn gap.",
                1,
                0,
                8,
                0.25,
                "Research",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: RecipePickerDensity),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                RecipePickerColumns,
                "Recipe picker columns",
                "Number of vertical recipe columns when custom density is selected; vanilla and compact policies choose their own safe default.",
                1,
                1,
                4,
                1,
                "Research",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: RecipePickerDensity),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                PlanningBuildingColor,
                "Planning building color",
                "Presentation color for paused planned buildings; vanilla preserves the game's default blueprint color.",
                "vanilla",
                s_planningColors,
                "Building",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                VehicleSoundRange,
                "Vehicle sound range",
                "Multiplier for vehicle sound audible distance; 1 preserves vanilla behavior.",
                1,
                1,
                5,
                0.1,
                "Audio",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                MachineSoundRange,
                "Machine sound range",
                "Multiplier for machine/building sound audible distance; 1 preserves vanilla behavior.",
                1,
                1,
                5,
                0.1,
                "Audio",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                TrainSoundVolume,
                "Train sound volume",
                "Multiplier for train audio volume; 1 preserves vanilla behavior.",
                1,
                0,
                1,
                0.05,
                "Audio",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                TrainSoundRange,
                "Train sound range",
                "Multiplier for train sound audible distance; 1 preserves vanilla behavior.",
                1,
                0.1,
                1,
                0.05,
                "Audio",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                TrainTuningProfile,
                "Train tuning profile",
                "Replaces the native train slope, fuel, and pollution property values as one profile; vanilla removes the modifier.",
                "vanilla",
                s_trainTuningProfiles,
                "Trains",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                LocomotiveNumbering,
                "Locomotive numbering",
                "Assigns deterministic process-local numbers to newly created locomotives; native saved numbers remain authoritative on load.",
                "vanilla",
                s_locomotiveNumbering,
                "Trains",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                DefaultsUnit,
                "Unit storage defaults",
                "Default logistics rule for newly placed unit storages when no explicit config exists.",
                "vanilla",
                s_modes,
                "Building defaults",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                DefaultsLoose,
                "Loose storage defaults",
                "Default logistics rule for newly placed loose storages when no explicit config exists.",
                "vanilla",
                s_modes,
                "Building defaults",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                DefaultsFluid,
                "Fluid storage defaults",
                "Default logistics rule for newly placed fluid storages when no explicit config exists.",
                "vanilla",
                s_modes,
                "Building defaults",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                DefaultsWarehouse,
                "Warehouse defaults",
                "Default logistics rule for newly placed warehouses when no explicit config exists.",
                "vanilla",
                s_modes,
                "Building defaults",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                DefaultsMineDump,
                "Mine dump product IDs",
                "Comma-separated stable product IDs to use for new mine towers; empty keeps vanilla configuration.",
                string.Empty,
                "Building defaults",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                DefaultsMineWarn,
                "Mine warning product IDs",
                "Comma-separated stable product IDs that warn when a new mine tower cannot dump them.",
                string.Empty,
                "Building defaults",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ResourceOverlay,
                "Resource overlay helpers",
                "Enables bounded resource-depth and mining-area overlay helpers while the vanilla resource visualization is active. Open the native resource visualization and select products to see them; this setting does not activate it automatically.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ResourceOverlayDepth,
                "Resource depth labels",
                "Adds depth information to resource overlay labels when the compatible vanilla renderer is present.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlay),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ResourceOverlayTowerAreas,
                "Mining tower areas",
                "Shows bounded mining tower labels and controlled-area colours.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlay),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ResourceOverlayTowerLabels,
                "Mining tower labels",
                "Adds optional world-space labels to the controlled mining-tower overlay.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlayTowerAreas),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                ResourceOverlayLabelScale,
                "Overlay label scale",
                "Scale percentage for world-space resource and tower labels.",
                100,
                50,
                200,
                1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlay,
                valueFormat: SettingValueFormat.Percentage),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                ResourceOverlayLabelAlpha,
                "Overlay label opacity",
                "Opacity percentage for world-space overlay labels.",
                85,
                0,
                100,
                1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlay,
                valueFormat: SettingValueFormat.Percentage),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                ResourceOverlayLabelHeight,
                "Overlay label height",
                "World-space height offset for overlay labels.",
                0.5,
                -10,
                10,
                0.1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlay),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                ResourceTowerLineColor,
                "Mining tower line color",
                "Global color for mining tower area lines, or a deterministic color based on the tower ID.",
                "by_tower",
                s_towerLineColors,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlayTowerAreas),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                ResourceTowerLineWidth,
                "Mining tower line width",
                "World-space width for mining tower area lines; zero preserves the native width.",
                0,
                0,
                5,
                0.05,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlayTowerAreas),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                ResourceTowerZoomDamping,
                "Mining tower zoom damping",
                "Reduces mining tower label size at long camera distances.",
                0,
                0,
                1,
                0.05,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlayTowerLabels),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                ResourceTowerZoomStart,
                "Mining tower zoom start",
                "Distance at which zoom damping begins, in world units.",
                20,
                1,
                100,
                1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlayTowerLabels),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                ResourceTowerAreaHeight,
                "Mining tower label height",
                "World-space height offset for mining tower labels.",
                0.5,
                -10,
                10,
                0.1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlayTowerLabels),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                ResourceTowerColors,
                "Mining tower colors",
                "Internal bounded map of tower IDs to palette indexes; the mine-tower inspector edits this value.",
                string.Empty,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ResourceOverlayTowerAreas),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                GroundwaterPolicy,
                "Groundwater policy",
                "Vanilla keeps the native weather-driven groundwater manager. Regenerate adds a deterministic daily refill, maintain minimum tops up low deposits, and infinite fills only the missing amount to native capacity. All callbacks are gameplay-scoped and non-saveable.",
                "vanilla",
                s_groundwaterPolicies,
                "Simulation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                GroundwaterRegenerationPercent,
                "Groundwater regeneration per day",
                "Additional percentage of each deposit's configured capacity added on each in-game day in Regenerate mode.",
                18.5,
                0,
                100,
                0.5,
                "Simulation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: GroundwaterPolicy,
                valueFormat: SettingValueFormat.Percentage),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                GroundwaterMinimumPercent,
                "Groundwater minimum",
                "Minimum percentage of native capacity maintained by Maintain minimum mode.",
                25,
                0,
                100,
                1,
                "Simulation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: GroundwaterPolicy),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                InfiniteGroundwater,
                "Infinite groundwater (legacy)",
                "Compatibility alias for the former boolean setting. A saved true value is migrated once to Groundwater policy = Infinite; use Groundwater policy for new configuration.",
                false,
                "Compatibility",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                AllowSteam,
                "Allow steam in fluid logistics",
                "Allows the game's supported steam variants in fluid storage and fluid trucks. Requires a game restart and is disabled by default.",
                false,
                "Simulation",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                AllowExhaust,
                "Allow exhaust in fluid logistics",
                "Allows exhaust gas in fluid storage and fluid trucks. Requires a game restart and is disabled by default.",
                false,
                "Simulation",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                WorldOperations,
                "World operations manager",
                "Enables the read-only, save-aware world-operations command surface and ship preload list.",
                false,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                AutoWorldDelivery,
                "Automatic world delivery",
                "Optionally loads and dispatches a ship for the next eligible world-map construction operation after the normal shipyard checks pass.",
                false,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: WorldOperations),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                AutoExploration,
                "Automatic exploration dispatch",
                "Dispatches the native fleet command to the nearest reachable unexplored location when the fleet is idle at home.",
                false,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced | SettingFlags.Experimental,
                componentRequirement: WorldOperations),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ShipPreload,
                "Ship cargo preload",
                "Keeps configured ship cargo preload data available to the normal shipyard flow; no cargo is spawned directly.",
                false,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: WorldOperations),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                ShipPreloadData,
                "Ship preload data",
                "Bounded JSON-like lines of shipyard ID and product ID/quantity; invalid lines are ignored.",
                string.Empty,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: ShipPreload),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ShipyardOutputTransport,
                "Shipyard output transport unloading",
                "Moves only surplus ship cargo through connected compatible output ports. Disabled by default while the first release is validated.",
                false,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced | SettingFlags.Experimental,
                componentRequirement: WorldOperations),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                ShipUnloadPolicy,
                "Ship unload policy",
                "Keeps vanilla cargo-buffer order by default; optionally selects the smallest eligible positive stack.",
                "vanilla",
                s_shipUnloadPolicies,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced | SettingFlags.Experimental,
                componentRequirement: WorldOperations),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                TerrainDesignationPriority,
                "Terrain designation priority",
                "Prefers one ready terrain work class while retaining the native eligibility and scorer within that class.",
                "vanilla",
                s_terrainDesignationPriorities,
                "Terrain",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced | SettingFlags.Experimental,
                componentRequirement: DesignationControls),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                RecoverTrucks,
                "Recover stranded loaded trucks",
                "Allows bounded, backoff-based recovery attempts only for unassigned loaded trucks with no legitimate delivery job.",
                false,
                "Vehicles",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                DumpToShipyard,
                "Dump stranded cargo to shipyard",
                "Independently routes unassigned loaded trucks with an active cannot-deliver state to a shipyard and transfers only storable cargo on arrival.",
                false,
                "Vehicles",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                RecoverPeriod,
                "Truck recovery period",
                "Minimum seconds between recovery scans.",
                10,
                2,
                120,
                1,
                "Vehicles",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: RecoverTrucks),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                StageMineTrucks,
                "Stage mine trucks",
                "Stages eligible loaded mine trucks at a reachable assigned ore sorter when they become idle.",
                false,
                "Vehicles",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                StageMineTrucksScan,
                "Mine truck scan period",
                "Minimum seconds between mine-truck staging checks.",
                5,
                2,
                60,
                1,
                "Vehicles",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: StageMineTrucks),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                FreeCamera,
                "Free camera",
                "Removes the vanilla ground-clearance floor while retaining camera state ownership.",
                false,
                "Camera",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                UnlimitedZoom,
                "Extended camera zoom",
                "Extends camera zoom bounds independently of ground clipping.",
                false,
                "Camera",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                GroundClipping,
                "Allow ground clipping",
                "Allows the camera pivot below the vanilla ground clearance.",
                false,
                "Camera",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HudLayout,
                "Editable HUD layout",
                "Enables bounded drag, visibility, and scale controls for stable-key HUD elements; reset restores vanilla geometry.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HudDragLocked,
                "Lock HUD positions",
                "Prevents drag edits while retaining saved HUD positions and scale.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                HudScale,
                "HUD scale",
                "Global supported-HUD scale percentage.",
                100,
                75,
                150,
                1,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout,
                valueFormat: SettingValueFormat.Percentage),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                HudHidden,
                "Hidden HUD keys",
                "Comma-separated stable HUD keys to hide while editable HUD layout is enabled.",
                string.Empty,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                HudPositions,
                "HUD positions",
                "Internal normalized HUD positions; use the reset command to clear them safely.",
                string.Empty,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HudBackgrounds,
                "HUD panel backgrounds",
                "Keeps the vanilla background plates behind the notifications and research HUD panels when enabled.",
                true,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ShowHudOnFullscreenViews,
                "Show HUD on fullscreen views",
                "Keeps the normal HUD visible over world-map, research and space fullscreen views.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                WorldVisibilityHiddenCategories,
                "Persisted world visibility categories",
                "Optional comma-separated category IDs to hide when a save opens. Empty is the safe-visible default; runtime toggles are not persisted automatically.",
                string.Empty,
                "Presentation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                HudActionPolicy,
                "HUD action policy",
                "Stable action IDs with optional order, visibility, and collapsed-mode preferences.",
                string.Empty,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HudActionCollapsed,
                "Collapse status actions",
                "Shows only core status/calendar actions until the bar is revealed.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HudActionHoverReveal,
                "Reveal actions on hover",
                "Temporarily reveals collapsed status/calendar actions while the bar is hovered.",
                true,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HudRealWorldClock,
                "Real-world HUD clock",
                "Adds a presentation-only local clock to the calendar bar; it never changes game time.",
                false,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudLayout),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HudClock24Hour,
                "24-hour HUD clock",
                "Formats the optional real-world clock using a 24-hour display.",
                true,
                "HUD",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: HudRealWorldClock),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                AdaptiveTowerInspector,
                "Adaptive tower inspectors",
                "Makes mine and forestry inspector sections collapsible and bounds vehicle assignment lists.",
                false,
                "Presentation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                InspectorSectionCollapsed,
                "Inspector collapsed sections",
                "Global section IDs whose inspector bodies should start collapsed.",
                string.Empty,
                "Presentation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: AdaptiveTowerInspector),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                InspectorVehicleFilters,
                "Inspector vehicle filters",
                "Comma-separated vehicle classes shown in tower assignment sections.",
                "excavator,truck,tree_planter,tree_harvester",
                "Presentation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: AdaptiveTowerInspector),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                InspectorVehicleVisibleRows,
                "Inspector vehicle rows",
                "Maximum visible rows before a tower vehicle assignment list scrolls.",
                8,
                3,
                24,
                1,
                "Presentation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: AdaptiveTowerInspector),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                StorageOverrides,
                "Storage capacity overrides",
                "Applies opt-in capacity and throughput overrides to future prototype-backed storages; existing quantities are never discarded.",
                false,
                "Storage",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                StorageInspectorControls,
                "Advanced storage inspector controls",
                "Adds numeric logistics entry, fine-grained storage alerts, compatible-product overrides, and safe copy/paste tools to ordinary storage inspectors.",
                false,
                "Storage",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                StorageMultiplier,
                "Storage capacity multiplier",
                "Multiplier applied to vanilla storage capacity and transfer limit.",
                1,
                0.1,
                10,
                0.1,
                "Storage",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced,
                componentRequirement: StorageOverrides),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                StorageThroughputMultiplier,
                "Storage throughput multiplier",
                "Multiplier applied to storage transfer throughput while preserving the vanilla transfer duration.",
                1,
                0.1,
                10,
                0.1,
                "Storage",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced,
                componentRequirement: StorageOverrides),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                StorageOverrideData,
                "Storage prototype overrides",
                "Comma-separated stable prototype IDs with multipliers, for example StorageV1=2; invalid entries are ignored.",
                string.Empty,
                "Storage",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced,
                componentRequirement: StorageOverrides),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                DesignationControls,
                "Large-map designation controls",
                "Enables map-scale designation-area limits and independent designation-visual filtering. Large selections can be expensive.",
                false,
                "Designations",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                DesignationLimit,
                "Designation area limit",
                "Maximum edge length in tiles for supported area tools; 16,384 is the game’s maximum map dimension (about 32.8 km). Large selections may hitch or create many designations.",
                512,
                128,
                16384,
                1,
                "Designations",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: DesignationControls),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                HideDesignations,
                "Hide designation visuals",
                "Hides designation rendering without disabling designation commands or simulation.",
                false,
                "Designations",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: DesignationControls),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                NotificationFilter,
                "Notification filters",
                "Filters only explicitly listed nuisance notification IDs at the notification creation boundary.",
                false,
                "Notifications",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                MutedNotifications,
                "Muted notification IDs",
                "Comma-separated stable notification prototype IDs. The default is empty, preserving normal notifications.",
                string.Empty,
                "Notifications",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: NotificationFilter),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                FarmWarnings,
                "Filter farm warnings",
                "Adds known farm warning IDs to the filter only when explicitly enabled.",
                false,
                "Notifications",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: NotificationFilter),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                FarmFullToggleAlways,
                "Show farm inventory-full toggle when empty",
                "Keeps the existing farm inventory-full notification toggle visible before the farm has output in its buffer.",
                false,
                "Notifications",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                BattleScoreOnMap,
                "Battle score on world map",
                "Adds the current traveling-fleet battle score to the native world-map ship panel.",
                false,
                "World map",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                ElectricityComputingTotals,
                "Electricity and computing totals",
                "Adds current and maximum totals to the native electricity and computing statistics panels.",
                false,
                "Memory",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                StackerDesignationOverlay,
                "Stacker designation overlay",
                "Shows a bounded local preview of terrain designations around the selected stacker tower.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                TerrainGrid,
                "Terrain grid",
                "Keeps the game's native terrain grid visible through the dedicated toolbar toggle. The preference is remembered across gameplay-scene recreation.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                TerrainXRay,
                "Terrain X-ray",
                "Shows a bounded underground terrain section around the cursor without changing terrain simulation data.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.String(
                ModId,
                DisplayName,
                TerrainXRayShortcut,
                "Terrain X-ray shortcut",
                "Primary shortcut for the terrain X-ray tool.",
                "F7",
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                EfficiencyOverlay,
                "World efficiency overlay",
                "Shows bounded, camera-culled utilization labels above buildings and supported vehicles. The toolbar toggle is reversible and immediate.",
                false,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                EfficiencyOverlayMode,
                "Efficiency overlay display",
                "Choose percentage, short status text, or a compact colored marker.",
                "percentage",
                s_efficiencyOverlayModes,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: EfficiencyOverlay),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                EfficiencyOverlayBuildings,
                "Efficiency overlay buildings",
                "Include buildings and fixed production entities in the world overlay.",
                true,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: EfficiencyOverlay),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                EfficiencyOverlayVehicles,
                "Efficiency overlay vehicles",
                "Include trucks, excavators, harvesters, planters, and other supported vehicles with productivity history.",
                true,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: EfficiencyOverlay),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                EfficiencyOverlayUpdateSeconds,
                "Efficiency overlay update interval",
                "Minimum seconds between entity-history refreshes; rendering remains camera-culled every frame.",
                0.5,
                0.1,
                5,
                0.1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: EfficiencyOverlay),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                EfficiencyOverlayRenderDistance,
                "Efficiency overlay render distance",
                "Maximum world distance for labels. This cap protects large islands from excessive label work.",
                1500,
                100,
                2000,
                100,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: EfficiencyOverlay),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                EfficiencyOverlayLabelScale,
                "Efficiency overlay label scale",
                "Base world-space label scale before bounded distance scaling is applied.",
                1,
                0.5,
                2,
                0.1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: EfficiencyOverlay),
            SettingDescriptor.Float(
                ModId,
                DisplayName,
                KeepFullEmptyLabelScale,
                "Keep Full / Keep Empty marker scale",
                "Scale for the optional world-space marker provider when a compatible provider is installed; zero is a no-op.",
                0,
                0,
                5,
                0.1,
                "Overlays",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                ParkingHqOffloadMode,
                "Parking HQ shipyard offload",
                "Controls only the optional Gameplay++ Parking HQ offload hook. Vanilla leaves the provider's own setting untouched.",
                "vanilla",
                s_parkingHqOffloadModes,
                "Vehicles",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                BridgeTrussEnabled,
                "Bridge truss access",
                "Enables truss bridge vehicle access through the optional Gameplay++ bridge integration. Requires a game restart.",
                false,
                "Building",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                BridgeCableEnabled,
                "Bridge cable access",
                "Enables cable bridge vehicle access through the optional Gameplay++ bridge integration. Requires a game restart.",
                false,
                "Building",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Choice(
                ModId,
                DisplayName,
                BridgeScaleMode,
                "Bridge vehicle scaling",
                "Vehicle scaling mode supplied to the optional Gameplay++ bridge integration.",
                "off",
                s_bridgeScaleModes,
                "Building",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                CenterDriving,
                "Center driving on bridges",
                "Uses center-driving lane masks through the optional Gameplay++ bridge integration. Requires a game restart.",
                false,
                "Building",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                TransportPillarSupportRadius,
                "Transport pillar support radius [vanilla: 4 tiles]",
                "Maximum support spacing radius used by transport construction and pathability. Valid range is 1-16 tiles; requires a game restart.",
                4,
                1,
                TransportPillarRulesFeature.MaxConfiguredSupportRadius,
                1,
                "Transport",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                TransportPillarMaxHeight,
                "Transport pillar maximum height [vanilla: 6 tiles]",
                "Maximum ordinary transport-pillar height used by construction, preview, and validation. Valid range is 1-16 tiles; requires a game restart.",
                6,
                1,
                TransportPillarRulesFeature.MaxConfiguredPillarHeight,
                1,
                "Transport",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                TrainTrackPillarMaxHeight,
                "Train-track pillar maximum height [vanilla: 6 tiles]",
                "Maximum train-track pillar height used by construction, preview, and validation. Valid range is 1-16 tiles; requires a game restart.",
                6,
                1,
                TransportPillarRulesFeature.MaxConfiguredPillarHeight,
                1,
                "Transport",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                TrainTrackPillarSupportDistance,
                "Train-track pillar support distance [vanilla: 7 tiles]",
                "Maximum train-track span used by native support propagation. Valid range is 1-32 tiles; requires a game restart.",
                7,
                1,
                TransportPillarRulesFeature.MaxConfiguredTrainSupportDistance,
                1,
                "Transport",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                IgnorePillarRequirements,
                "Ignore pillar requirements",
                "Disables pillar requirements for transports and elevated layout entities, matching the native ignore-pillar behavior. Existing terrain and occupancy checks remain authoritative; requires a game restart.",
                false,
                "Transport",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                FleetManager,
                "Fleet order manager",
                "Enables bounded fleet status/order commands that route through normal input-command methods.",
                false,
                "Fleet",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                FleetBatchLimit,
                "Fleet batch limit",
                "Maximum vehicles considered by one bulk operation.",
                50,
                1,
                500,
                1,
                "Fleet",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: FleetManager),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                Overclocking,
                "Per-machine overclocking",
                "Enables per-entity speed policies for machines, belts, and pipes, Auto mode, group controls, and matching operating costs. Values are stored per save outside the vanilla save blob.",
                false,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                OverclockTransportCapacityCompensation,
                "Transport capacity compensation",
                "For solid belts only, gradually reduces product spacing and increases per-position stack capacity as speed rises. Fluid and molten pipes are never changed by this option.",
                true,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockTransportSpacingBonus,
                "Belt spacing compensation",
                "Maximum percentage reduction in solid-belt product spacing at the configured maximum speed.",
                100,
                0,
                300,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockTransportStackBonus,
                "Belt stack compensation",
                "Maximum percentage increase in solid-belt per-position stack capacity at the configured maximum speed.",
                200,
                0,
                500,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockMaxPercent,
                "Maximum overclock",
                "Upper bound for manual and automatic machine speed policies.",
                300,
                100,
                1000,
                5,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockMinPercent,
                "Minimum machine speed",
                "Lower bound for manual and automatic policies. 100% preserves overclock-only behaviour; lower values provide underclocking.",
                100,
                10,
                100,
                5,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockPowerCurve,
                "Power cost curve",
                "Percentage exponent used for power cost scaling (124 means rate^1.24).",
                124,
                0,
                400,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockWorkerCurve,
                "Worker cost curve",
                "Percentage exponent used for worker cost scaling (133 means rate^1.33).",
                133,
                0,
                400,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockComputingCurve,
                "Computing cost curve",
                "Percentage exponent used for computing cost scaling (112 means rate^1.12).",
                112,
                0,
                400,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockMaintenanceCurve,
                "Maintenance cost curve",
                "Percentage exponent used for maintenance cost scaling (173 means rate^1.73).",
                173,
                0,
                400,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoIntervalSeconds,
                "Auto policy interval",
                "Seconds between Auto policy decisions. Only enrolled entities are inspected.",
                2,
                1,
                30,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoPowerReserve,
                "Auto power reserve",
                "Generation reserve kept unavailable to automatic overclocking.",
                10,
                0,
                90,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoWorkerReserve,
                "Auto worker reserve",
                "Free workers kept unavailable to automatic overclocking.",
                5,
                0,
                500,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoStepPercent,
                "Auto adjustment step",
                "Quantization step used for automatic policy changes.",
                5,
                1,
                50,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoDeadbandPercent,
                "Auto deadband",
                "Demand change smaller than this percentage leaves the current automatic rate unchanged.",
                5,
                0,
                100,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoMaxStepPercent,
                "Auto maximum adjustment",
                "Maximum percentage-point change in one automatic decision.",
                25,
                1,
                500,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoLowFill,
                "Auto low fill threshold",
                "Output fill at or below this value requests the maximum rate.",
                10,
                0,
                99,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoNeutralFill,
                "Auto neutral fill threshold",
                "Output fill around this value requests 100%.",
                50,
                1,
                99,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                OverclockAutoHighFill,
                "Auto high fill threshold",
                "Output fill at or above this value requests the minimum rate.",
                90,
                1,
                100,
                1,
                "Overclocking",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced,
                componentRequirement: Overclocking),

            // Wave 7 — settlement/environment sandbox controls.
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableDiseaseEffects, "Disable disease effects", "Sandbox: suppresses disease health/mortality effects while preserving disease progression.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxInfiniteFocus, "Infinite focus points", "Sandbox: raises the native focus multiplier to a bounded high value; no new focus resource is created.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Float(ModId, DisplayName, SandboxFocusMultiplier, "Focus multiplier", "Sandbox: multiplier for focus-point production; 1 preserves vanilla and values are bounded to avoid overflow.", 1, 0, 1000, 0.1, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableAirPollutionEffects, "Disable air-pollution effects", "Sandbox: removes air-pollution health impact through the native pollution multiplier.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableAirPollutionProduction, "Disable air-pollution production", "Sandbox: reserved for a version-validated production seam; remains fail-open when unavailable.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableWaterPollutionEffects, "Disable water-pollution effects", "Sandbox: removes water-pollution health impact through the native pollution multiplier.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableWaterPollutionProduction, "Disable water-pollution production", "Sandbox: reserved for a version-validated wastewater production seam; remains fail-open when unavailable.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableShipPollution, "Disable ship pollution", "Sandbox: multiplies ship emissions at the authoritative emission calculation.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableVehiclePollution, "Disable vehicle pollution", "Sandbox: multiplies vehicle emissions at the authoritative emission calculation.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableTrainPollution, "Disable train pollution", "Sandbox: multiplies train emissions at the authoritative emission calculation.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableFoodNeed, "Disable food need", "Sandbox: removes settlement food consumption only.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableSettlementNeeds, "Disable settlement goods/services need", "Sandbox: removes settlement goods and services consumption while leaving food, electricity, water, and computing controls independent.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableSolidWaste, "Disable solid-waste generation", "Sandbox: restores only the settlement landfill accumulator after native transformation; recycling and biowaste remain native.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableBiowaste, "Disable biowaste generation", "Sandbox: restores only the settlement biowaste accumulator after native transformation; recycling and landfill remain native.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableElectricityNeed, "Disable electricity need", "Sandbox: reserved for a version-validated settlement electricity-demand seam; remains fail-open when unavailable.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableCleanWaterNeed, "Disable clean-water need", "Sandbox: reserved for a version-validated settlement water-demand seam; remains fail-open when unavailable.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableWastewater, "Disable wastewater production", "Sandbox: reserved for a version-validated wastewater production seam; remains fail-open when unavailable.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDisableComputingNeed, "Disable computing need", "Sandbox: reserved for a version-validated computing-demand seam; remains fail-open when unavailable.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),

            // Wave 7 — progression/construction sandbox controls.
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxInstantCargoShip, "Instant cargo-ship turnaround", "Sandbox: reserved for a version-validated cargo-ship turnaround seam; remains fail-open while turnaround duration is prototype-cached.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxDesignMode, "Layout design mode", "Sandbox: finalizes newly scheduled compatible construction/deconstruction after normal command processing.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxFreeResearch, "Free research", "Sandbox: sets the native research-step multiplier to zero for future research tasks.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxNoConstructionCosts, "No construction costs", "Sandbox: sets the native construction-cost multiplier to zero for future construction tasks.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxFastOreSorting, "Fast ore sorting", "Sandbox: uses the native ore-sorting property plus a bounded throughput seam when available.", false, "Sandbox", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxInstantStorageEmpty, "Instant storage empty", "Sandbox: enables the explicit destructive tajs_storage_empty <storage-id> CONFIRM command; no storage is cleared implicitly.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Boolean(ModId, DisplayName, SandboxAlwaysAllowBulldoze, "Always allow bulldoze", "Sandbox: bypasses only the soft pre-bulldoze eligibility result for whitelisted entity classes; hard invariants remain protected.", false, "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.String(ModId, DisplayName, SandboxBulldozeWhitelist, "Bulldoze whitelist", "Comma-separated exact entity type names eligible for the soft bulldoze override. Leave empty for vanilla behavior; hard-invariant classes are always rejected.", "", "Sandbox", applyMode: SettingApplyMode.Immediate, flags: SettingFlags.Advanced | SettingFlags.Experimental),

            // Wave 7 — advanced infrastructure tuning.
            SettingDescriptor.Float(ModId, DisplayName, TuningShipyardCargoMultiplier, "Shipyard cargo-buffer multiplier", "Advanced tuning: multiplier from the captured vanilla shipyard cargo buffer; requires reload to rebuild prototype buffers.", 1, 0.1, 20, 0.1, "Infrastructure", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Float(ModId, DisplayName, TuningTruckLoadDurationMultiplier, "Truck load-duration multiplier", "Advanced tuning: multiplier from the captured vanilla truck pickup duration.", 1, 0.1, 20, 0.1, "Infrastructure", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Float(ModId, DisplayName, TuningOreSorterBufferMultiplier, "Ore-sorter buffer multiplier", "Advanced tuning: multiplier from captured ore-sorter input/output buffers.", 1, 0.1, 20, 0.1, "Infrastructure", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Float(ModId, DisplayName, TuningOreSorterThroughputMultiplier, "Ore-sorter throughput multiplier", "Advanced tuning: coherent multiplier for ore-sorter quantity-per-duration and displayed rate.", 1, 0.1, 20, 0.1, "Infrastructure", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Float(ModId, DisplayName, TuningShaftThroughputMultiplier, "Shaft-throughput multiplier", "Advanced tuning: bounded mechanical-shaft throughput multiplier.", 1, 0.1, 20, 0.1, "Infrastructure", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.Float(ModId, DisplayName, TuningThermalStorageCapacityMultiplier, "Thermal-storage capacity multiplier", "Advanced tuning: bounded thermal capacity multiplier; reductions defer until stored heat discharges.", 1, 0.1, 20, 0.1, "Infrastructure", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),

            SettingDescriptor.Choice(ModId, DisplayName, DiseaseScalingPolicy, "Disease progression distance", "Selects vanilla, map-scaled, or custom disease-distance policy. Existing unlocked diseases are never relocked.", "vanilla", s_diseaseScalingPolicies, "Difficulty", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental),
            SettingDescriptor.String(ModId, DisplayName, DiseaseScalingCustomFractions, "Disease custom fractions", "Comma-separated bounded fractions (0..1) used only by the custom disease-distance policy.", "", "Difficulty", applyMode: SettingApplyMode.ReloadSave, flags: SettingFlags.Advanced | SettingFlags.Experimental, componentRequirement: DiseaseScalingPolicy),
        };

        internal static void RegisterAll(ITajsSettings settings)
        {
            foreach (SettingDescriptor descriptor in All)
            {
                settings.Register(descriptor);
            }
        }

        internal static bool IsEnabled(ITajsSettings settings, string key) => settings.Get<bool>(ModId, key);

        internal static string GetText(ITajsSettings settings, string key) => settings.Get<string>(ModId, key);
    }
}
