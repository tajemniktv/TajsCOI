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
        internal const string PinnedLowOnly = "pinned_low_only";
        internal const string PinnedLowThreshold = "pinned_low_threshold_percent";
        internal const string PinnedLowLimit = "pinned_low_limit";

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
        internal const string InfiniteGroundwater = "infinite_groundwater";

        internal const string WorldOperations = "world_operations";
        internal const string AutoWorldDelivery = "auto_world_delivery";
        internal const string ShipPreload = "ship_preload";
        internal const string ShipPreloadData = "ship_preload_data";

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

        internal const string StorageOverrides = "storage_overrides";
        internal const string StorageMultiplier = "storage_capacity_multiplier";
        internal const string StorageThroughputMultiplier = "storage_throughput_multiplier";
        internal const string StorageOverrideData = "storage_override_data";

        internal const string DesignationControls = "designation_controls";
        internal const string DesignationLimit = "designation_limit";
        internal const string HideDesignations = "hide_designations";

        internal const string NotificationFilter = "notification_filter";
        internal const string MutedNotifications = "muted_notification_ids";
        internal const string FarmWarnings = "filter_farm_warnings";

        internal const string FleetManager = "fleet_manager";
        internal const string FleetBatchLimit = "fleet_batch_limit";

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
                componentRequirement: PinnedSort),
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
                componentRequirement: PinnedLowOnly),
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
                componentRequirement: ResourceOverlay),
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
                componentRequirement: ResourceOverlay),
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
            SettingDescriptor.Boolean(
                ModId,
                DisplayName,
                InfiniteGroundwater,
                "Infinite ground water",
                "Refills virtual ground-water deposits to their configured capacity at game initialization and at the start of each in-game day. Uses non-saveable lifecycle callbacks and is disabled by default.",
                false,
                "Simulation",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
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
                RecoverTrucks,
                "Recover stranded loaded trucks",
                "Allows bounded, backoff-based recovery attempts only for unassigned loaded trucks with no legitimate delivery job.",
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
                componentRequirement: HudLayout),
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
                StorageOverrides,
                "Storage capacity overrides",
                "Applies opt-in capacity and throughput overrides to future prototype-backed storages; existing quantities are never discarded.",
                false,
                "Storage",
                applyMode: SettingApplyMode.RestartGame,
                flags: SettingFlags.Advanced | SettingFlags.Experimental),
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
                "Enables bounded designation-area limits and independent designation-visual filtering.",
                false,
                "Designations",
                applyMode: SettingApplyMode.Immediate,
                flags: SettingFlags.Advanced),
            SettingDescriptor.Integer(
                ModId,
                DisplayName,
                DesignationLimit,
                "Designation area limit",
                "Maximum edge length for supported area tools; bounded to prevent unreviewed whole-map scans.",
                512,
                128,
                2048,
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
