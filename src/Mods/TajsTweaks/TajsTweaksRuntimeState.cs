// Taj's COI Mods | TajsTweaksRuntimeState.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TajsCOI.Common.Settings;

namespace TajsCOI.Tweaks
{
    internal static class TajsTweaksRuntimeState
    {
        private static readonly object s_gate = new();
        private static HashSet<string> s_mutedNotifications = new(StringComparer.Ordinal);
        private static Dictionary<string, double> s_storageOverrides = new(StringComparer.Ordinal);
        private static string s_mutedNotificationData = string.Empty;

        internal static bool LinePlacement;
        internal static string LinePlacementShortcut = "LeftAlt";
        internal static int LinePlacementLength;
        internal static bool PinnedSort;
        internal static string PinnedSortMode = "quantity";
        internal static string PinnedSortDirection = "descending";
        internal static double PinnedHysteresisPercent;
        internal static bool PinnedCompact;
        internal static bool PinnedBarColors;
        internal static int PinnedColumns;
        internal static bool PinnedAutoColumns;
        internal static int PinnedRowsPerColumn;
        internal static bool PinnedLowOnly;
        internal static int PinnedLowThreshold;
        internal static int PinnedLowLimit;
        internal static bool QuickRemoveOnDemolish;
        internal static bool ClassicRecipeDisplay;
        internal static string PlanningBuildingColor = "vanilla";
        internal static double VehicleSoundRange;
        internal static double MachineSoundRange;
        internal static double TrainSoundVolume;
        internal static double TrainSoundRange;

        internal static string DefaultsUnit = "vanilla";
        internal static string DefaultsLoose = "vanilla";
        internal static string DefaultsFluid = "vanilla";
        internal static string DefaultsWarehouse = "vanilla";
        internal static string DefaultsMineDump = string.Empty;
        internal static string DefaultsMineWarn = string.Empty;

        internal static bool ResourceOverlay;
        internal static bool ResourceOverlayDepth;
        internal static bool ResourceOverlayTowerAreas;
        internal static bool ResourceOverlayTowerLabels;
        internal static int ResourceOverlayLabelScale;
        internal static int ResourceOverlayLabelAlpha;
        internal static double ResourceOverlayLabelHeight;
        internal static string ResourceTowerLineColor = "by_tower";
        internal static double ResourceTowerLineWidth;
        internal static double ResourceTowerZoomDamping;
        internal static double ResourceTowerZoomStart;
        internal static double ResourceTowerAreaHeight;
        internal static string ResourceTowerColors = string.Empty;
        internal static bool InfiniteGroundwater;
        internal static bool AllowSteam;
        internal static bool AllowExhaust;
        internal static bool WorldOperations;
        internal static bool AutoWorldDelivery;
        internal static bool ShipPreload;
        internal static string ShipPreloadData = string.Empty;
        internal static bool RecoverTrucks;
        internal static bool DumpToShipyard;
        internal static int RecoverPeriod;
        internal static bool StageMineTrucks;
        internal static int StageMineTrucksScan;
        internal static bool FreeCamera;
        internal static bool UnlimitedZoom;
        internal static bool GroundClipping;
        internal static bool HudLayout;
        internal static bool HudDragLocked;
        internal static int HudScale;
        internal static string HudHidden = string.Empty;
        internal static string HudPositions = string.Empty;
        internal static bool HudBackgrounds;
        internal static bool ShowHudOnFullscreenViews;
        internal static bool StorageOverrides;
        internal static double StorageMultiplier;
        internal static double StorageThroughputMultiplier;
        internal static string StorageOverrideData = string.Empty;
        internal static bool DesignationControls;
        internal static int DesignationLimit;
        internal static bool HideDesignations;
        internal static bool NotificationFilter;
        internal static bool FarmWarnings;
        internal static bool FarmFullToggleAlways;
        internal static bool BattleScoreOnMap;
        internal static bool ElectricityComputingTotals;
        internal static bool StackerDesignationOverlay;
        internal static bool TerrainGrid;
        internal static bool EfficiencyOverlay;
        internal static string EfficiencyOverlayMode = "percentage";
        internal static bool EfficiencyOverlayBuildings;
        internal static bool EfficiencyOverlayVehicles;
        internal static double EfficiencyOverlayUpdateSeconds;
        internal static double EfficiencyOverlayRenderDistance;
        internal static double EfficiencyOverlayLabelScale;
        internal static double KeepFullEmptyLabelScale;
        internal static string ParkingHqOffloadMode = "vanilla";
        internal static bool BridgeTrussEnabled;
        internal static bool BridgeCableEnabled;
        internal static string BridgeScaleMode = "off";
        internal static bool CenterDriving;
        internal static int TransportPillarSupportRadius;
        internal static int TransportPillarMaxHeight;
        internal static int TrainTrackPillarMaxHeight;
        internal static int TrainTrackPillarSupportDistance;
        internal static bool IgnorePillarRequirements;
        internal static bool FleetManager;
        internal static int FleetBatchLimit;
        internal static bool Overclocking;
        internal static bool OverclockTransportCapacityCompensation;
        internal static int OverclockTransportSpacingBonus;
        internal static int OverclockTransportStackBonus;
        internal static int OverclockMaxPercent;
        internal static int OverclockMinPercent;
        internal static int OverclockPowerCurve;
        internal static int OverclockWorkerCurve;
        internal static int OverclockComputingCurve;
        internal static int OverclockMaintenanceCurve;
        internal static int OverclockAutoIntervalSeconds;
        internal static int OverclockAutoPowerReserve;
        internal static int OverclockAutoWorkerReserve;
        internal static int OverclockAutoStepPercent;
        internal static int OverclockAutoDeadbandPercent;
        internal static int OverclockAutoMaxStepPercent;
        internal static int OverclockAutoLowFill;
        internal static int OverclockAutoNeutralFill;
        internal static int OverclockAutoHighFill;

        internal static void Load(ITajsSettings settings)
        {
            LinePlacement = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.LinePlacement);
            LinePlacementShortcut = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.LinePlacementShortcut);
            LinePlacementLength = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.LinePlacementLength);
            PinnedSort = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedSort);
            PinnedSortMode = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedSortMode);
            PinnedSortDirection = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedSortDirection);
            PinnedHysteresisPercent = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedHysteresis);
            PinnedCompact = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedCompact);
            PinnedBarColors = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedBarColors);
            PinnedColumns = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedColumns);
            PinnedAutoColumns = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedAutoColumns);
            PinnedRowsPerColumn = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedRowsPerColumn);
            PinnedLowOnly = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedLowOnly);
            PinnedLowThreshold = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedLowThreshold);
            PinnedLowLimit = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedLowLimit);
            QuickRemoveOnDemolish = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.QuickRemoveOnDemolish);
            ClassicRecipeDisplay = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ClassicRecipeDisplay);
            PlanningBuildingColor = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PlanningBuildingColor);
            VehicleSoundRange = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.VehicleSoundRange);
            MachineSoundRange = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.MachineSoundRange);
            TrainSoundVolume = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.TrainSoundVolume);
            TrainSoundRange = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.TrainSoundRange);

            DefaultsUnit = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DefaultsUnit);
            DefaultsLoose = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DefaultsLoose);
            DefaultsFluid = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DefaultsFluid);
            DefaultsWarehouse = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DefaultsWarehouse);
            DefaultsMineDump = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DefaultsMineDump);
            DefaultsMineWarn = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DefaultsMineWarn);

            ResourceOverlay = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceOverlay);
            ResourceOverlayDepth = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceOverlayDepth);
            ResourceOverlayTowerAreas = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceOverlayTowerAreas);
            ResourceOverlayTowerLabels = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceOverlayTowerLabels);
            ResourceOverlayLabelScale = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceOverlayLabelScale);
            ResourceOverlayLabelAlpha = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceOverlayLabelAlpha);
            ResourceOverlayLabelHeight = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceOverlayLabelHeight);
            ResourceTowerLineColor = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceTowerLineColor);
            ResourceTowerLineWidth = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceTowerLineWidth);
            ResourceTowerZoomDamping = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceTowerZoomDamping);
            ResourceTowerZoomStart = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceTowerZoomStart);
            ResourceTowerAreaHeight = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceTowerAreaHeight);
            ResourceTowerColors = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ResourceTowerColors);
            InfiniteGroundwater = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.InfiniteGroundwater);
            AllowSteam = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.AllowSteam);
            AllowExhaust = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.AllowExhaust);
            WorldOperations = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.WorldOperations);
            AutoWorldDelivery = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.AutoWorldDelivery);
            ShipPreload = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ShipPreload);
            ShipPreloadData = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ShipPreloadData);
            RecoverTrucks = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.RecoverTrucks);
            DumpToShipyard = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DumpToShipyard);
            RecoverPeriod = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.RecoverPeriod);
            StageMineTrucks = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StageMineTrucks);
            StageMineTrucksScan = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StageMineTrucksScan);
            FreeCamera = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FreeCamera);
            UnlimitedZoom = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.UnlimitedZoom);
            GroundClipping = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.GroundClipping);
            HudLayout = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudLayout);
            HudDragLocked = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudDragLocked);
            HudScale = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudScale);
            HudHidden = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudHidden);
            HudPositions = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudPositions);
            HudBackgrounds = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HudBackgrounds);
            ShowHudOnFullscreenViews = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ShowHudOnFullscreenViews);
            StorageOverrides = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageOverrides);
            StorageMultiplier = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageMultiplier);
            StorageThroughputMultiplier = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageThroughputMultiplier);
            StorageOverrideData = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageOverrideData);
            DesignationControls = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DesignationControls);
            DesignationLimit = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DesignationLimit);
            HideDesignations = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HideDesignations);
            NotificationFilter = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.NotificationFilter);
            FarmWarnings = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FarmWarnings);
            FarmFullToggleAlways = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FarmFullToggleAlways);
            BattleScoreOnMap = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.BattleScoreOnMap);
            ElectricityComputingTotals = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ElectricityComputingTotals);
            StackerDesignationOverlay = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StackerDesignationOverlay);
            TerrainGrid = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.TerrainGrid);
            EfficiencyOverlay = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.EfficiencyOverlay);
            EfficiencyOverlayMode = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.EfficiencyOverlayMode);
            EfficiencyOverlayBuildings = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.EfficiencyOverlayBuildings);
            EfficiencyOverlayVehicles = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.EfficiencyOverlayVehicles);
            EfficiencyOverlayUpdateSeconds = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.EfficiencyOverlayUpdateSeconds);
            EfficiencyOverlayRenderDistance = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.EfficiencyOverlayRenderDistance);
            EfficiencyOverlayLabelScale = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.EfficiencyOverlayLabelScale);
            KeepFullEmptyLabelScale = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.KeepFullEmptyLabelScale);
            ParkingHqOffloadMode = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ParkingHqOffloadMode);
            BridgeTrussEnabled = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.BridgeTrussEnabled);
            BridgeCableEnabled = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.BridgeCableEnabled);
            BridgeScaleMode = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.BridgeScaleMode);
            CenterDriving = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.CenterDriving);
            TransportPillarSupportRadius = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.TransportPillarSupportRadius);
            TransportPillarMaxHeight = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.TransportPillarMaxHeight);
            TrainTrackPillarMaxHeight = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.TrainTrackPillarMaxHeight);
            TrainTrackPillarSupportDistance = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.TrainTrackPillarSupportDistance);
            IgnorePillarRequirements = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.IgnorePillarRequirements);
            FleetManager = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FleetManager);
            FleetBatchLimit = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FleetBatchLimit);
            Overclocking = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.Overclocking);
            OverclockTransportCapacityCompensation = settings.Get<bool>(
                TajsTweaksSettingsCatalog.ModId,
                TajsTweaksSettingsCatalog.OverclockTransportCapacityCompensation);
            OverclockTransportSpacingBonus = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockTransportSpacingBonus);
            OverclockTransportStackBonus = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockTransportStackBonus);
            OverclockMaxPercent = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockMaxPercent);
            OverclockMinPercent = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockMinPercent);
            OverclockPowerCurve = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockPowerCurve);
            OverclockWorkerCurve = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockWorkerCurve);
            OverclockComputingCurve = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockComputingCurve);
            OverclockMaintenanceCurve = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockMaintenanceCurve);
            OverclockAutoIntervalSeconds = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoIntervalSeconds);
            OverclockAutoPowerReserve = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoPowerReserve);
            OverclockAutoWorkerReserve = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoWorkerReserve);
            OverclockAutoStepPercent = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoStepPercent);
            OverclockAutoDeadbandPercent = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoDeadbandPercent);
            OverclockAutoMaxStepPercent = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoMaxStepPercent);
            OverclockAutoLowFill = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoLowFill);
            OverclockAutoNeutralFill = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoNeutralFill);
            OverclockAutoHighFill = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.OverclockAutoHighFill);
            s_mutedNotificationData = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.MutedNotifications);
            RebuildParsedValues();
        }

        internal static void ApplyChange(SettingChangedEventArgs change)
        {
            if (change.NewValue is bool boolean)
            {
                SetBoolean(change.Descriptor.Key, boolean);
            }
            else if (change.NewValue is int integer)
            {
                SetInteger(change.Descriptor.Key, integer);
            }
            else if (change.NewValue is double number)
            {
                SetNumber(change.Descriptor.Key, number);
            }
            else if (change.NewValue is string text)
            {
                SetText(change.Descriptor.Key, text);
            }
        }

        internal static bool IsNotificationMuted(string id)
        {
            lock (s_gate)
            {
                if (!NotificationFilter)
                {
                    return false;
                }
                return s_mutedNotifications.Contains(id) ||
                       FarmWarnings && (id.IndexOf("CropCouldNotBeStored", StringComparison.Ordinal) >= 0 ||
                                        id.IndexOf("CropDiedNoMaintenance", StringComparison.Ordinal) >= 0 ||
                                        id.IndexOf("CropDiedNoWater", StringComparison.Ordinal) >= 0 ||
                                        id.IndexOf("CropDiedNoFertility", StringComparison.Ordinal) >= 0);
            }
        }

        internal static IReadOnlyDictionary<string, double> GetStorageOverrides()
        {
            lock (s_gate)
            {
                return new Dictionary<string, double>(s_storageOverrides, StringComparer.Ordinal);
            }
        }

        internal static IReadOnlyList<string> ParseIds(string? text) =>
            (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && x.Length <= 96)
            .Distinct(StringComparer.Ordinal)
            .Take(256)
            .ToArray();

        internal static IReadOnlyDictionary<string, double> ParseStorageOverrides(string? text)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (string part in (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(256))
            {
                string[] pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2 || pair[0].Trim().Length == 0 ||
                    !double.TryParse(pair[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                    double.IsNaN(value) || double.IsInfinity(value) || value < 0.1 || value > 10)
                {
                    continue;
                }
                result[pair[0].Trim()] = value;
            }
            return result;
        }

        internal static IReadOnlyDictionary<int, int> ParseTowerColors(string? text)
        {
            var result = new Dictionary<int, int>();
            foreach (string part in (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(256))
            {
                string[] pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2 || !int.TryParse(pair[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int towerId) ||
                    !int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int colorIndex) ||
                    towerId < 0 || colorIndex < 0 || colorIndex > 8)
                {
                    continue;
                }
                result[towerId] = colorIndex;
            }
            return result;
        }

        internal static string FormatTowerColors(IReadOnlyDictionary<int, int> colors)
        {
            return string.Join(
                ",",
                colors.OrderBy(x => x.Key).Take(256).Select(x =>
                    x.Key.ToString(CultureInfo.InvariantCulture) + "=" + x.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private static void SetBoolean(string key, bool value)
        {
            switch (key)
            {
                case TajsTweaksSettingsCatalog.LinePlacement: LinePlacement = value; break;
                case TajsTweaksSettingsCatalog.PinnedSort: PinnedSort = value; break;
                case TajsTweaksSettingsCatalog.PinnedCompact: PinnedCompact = value; break;
                case TajsTweaksSettingsCatalog.PinnedBarColors: PinnedBarColors = value; break;
                case TajsTweaksSettingsCatalog.PinnedAutoColumns: PinnedAutoColumns = value; break;
                case TajsTweaksSettingsCatalog.PinnedLowOnly: PinnedLowOnly = value; break;
                case TajsTweaksSettingsCatalog.QuickRemoveOnDemolish: QuickRemoveOnDemolish = value; break;
                case TajsTweaksSettingsCatalog.ClassicRecipeDisplay: ClassicRecipeDisplay = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlay: ResourceOverlay = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayDepth: ResourceOverlayDepth = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayTowerAreas: ResourceOverlayTowerAreas = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayTowerLabels: ResourceOverlayTowerLabels = value; break;
                case TajsTweaksSettingsCatalog.ElectricityComputingTotals: ElectricityComputingTotals = value; break;
                case TajsTweaksSettingsCatalog.StackerDesignationOverlay: StackerDesignationOverlay = value; break;
                case TajsTweaksSettingsCatalog.EfficiencyOverlay: EfficiencyOverlay = value; break;
                case TajsTweaksSettingsCatalog.EfficiencyOverlayBuildings: EfficiencyOverlayBuildings = value; break;
                case TajsTweaksSettingsCatalog.EfficiencyOverlayVehicles: EfficiencyOverlayVehicles = value; break;
                case TajsTweaksSettingsCatalog.BridgeTrussEnabled: BridgeTrussEnabled = value; break;
                case TajsTweaksSettingsCatalog.BridgeCableEnabled: BridgeCableEnabled = value; break;
                case TajsTweaksSettingsCatalog.CenterDriving: CenterDriving = value; break;
                case TajsTweaksSettingsCatalog.IgnorePillarRequirements: IgnorePillarRequirements = value; break;
                case TajsTweaksSettingsCatalog.InfiniteGroundwater: InfiniteGroundwater = value; break;
                case TajsTweaksSettingsCatalog.AllowSteam: AllowSteam = value; break;
                case TajsTweaksSettingsCatalog.AllowExhaust: AllowExhaust = value; break;
                case TajsTweaksSettingsCatalog.WorldOperations: WorldOperations = value; break;
                case TajsTweaksSettingsCatalog.AutoWorldDelivery: AutoWorldDelivery = value; break;
                case TajsTweaksSettingsCatalog.ShipPreload: ShipPreload = value; break;
                case TajsTweaksSettingsCatalog.RecoverTrucks: RecoverTrucks = value; break;
                case TajsTweaksSettingsCatalog.DumpToShipyard: DumpToShipyard = value; break;
                case TajsTweaksSettingsCatalog.StageMineTrucks: StageMineTrucks = value; break;
                case TajsTweaksSettingsCatalog.FreeCamera: FreeCamera = value; break;
                case TajsTweaksSettingsCatalog.UnlimitedZoom: UnlimitedZoom = value; break;
                case TajsTweaksSettingsCatalog.GroundClipping: GroundClipping = value; break;
                case TajsTweaksSettingsCatalog.HudLayout: HudLayout = value; break;
                case TajsTweaksSettingsCatalog.HudDragLocked: HudDragLocked = value; break;
                case TajsTweaksSettingsCatalog.HudBackgrounds: HudBackgrounds = value; break;
                case TajsTweaksSettingsCatalog.ShowHudOnFullscreenViews: ShowHudOnFullscreenViews = value; break;
                case TajsTweaksSettingsCatalog.StorageOverrides: StorageOverrides = value; break;
                case TajsTweaksSettingsCatalog.DesignationControls: DesignationControls = value; break;
                case TajsTweaksSettingsCatalog.HideDesignations: HideDesignations = value; break;
                case TajsTweaksSettingsCatalog.NotificationFilter: NotificationFilter = value; break;
                case TajsTweaksSettingsCatalog.FarmWarnings: FarmWarnings = value; break;
                case TajsTweaksSettingsCatalog.FarmFullToggleAlways: FarmFullToggleAlways = value; break;
                case TajsTweaksSettingsCatalog.BattleScoreOnMap: BattleScoreOnMap = value; break;
                case TajsTweaksSettingsCatalog.FleetManager: FleetManager = value; break;
                case TajsTweaksSettingsCatalog.TerrainGrid: TerrainGrid = value; break;
                case TajsTweaksSettingsCatalog.Overclocking: Overclocking = value; break;
                case TajsTweaksSettingsCatalog.OverclockTransportCapacityCompensation: OverclockTransportCapacityCompensation = value; break;
            }
        }

        private static void SetInteger(string key, int value)
        {
            switch (key)
            {
                case TajsTweaksSettingsCatalog.LinePlacementLength: LinePlacementLength = value; break;
                case TajsTweaksSettingsCatalog.PinnedColumns: PinnedColumns = value; break;
                case TajsTweaksSettingsCatalog.PinnedRowsPerColumn: PinnedRowsPerColumn = value; break;
                case TajsTweaksSettingsCatalog.PinnedLowThreshold: PinnedLowThreshold = value; break;
                case TajsTweaksSettingsCatalog.PinnedLowLimit: PinnedLowLimit = value; break;
                case TajsTweaksSettingsCatalog.RecoverPeriod: RecoverPeriod = value; break;
                case TajsTweaksSettingsCatalog.StageMineTrucksScan: StageMineTrucksScan = value; break;
                case TajsTweaksSettingsCatalog.HudScale: HudScale = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayLabelScale: ResourceOverlayLabelScale = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayLabelAlpha: ResourceOverlayLabelAlpha = value; break;
                case TajsTweaksSettingsCatalog.DesignationLimit: DesignationLimit = value; break;
                case TajsTweaksSettingsCatalog.FleetBatchLimit: FleetBatchLimit = value; break;
                case TajsTweaksSettingsCatalog.TransportPillarSupportRadius: TransportPillarSupportRadius = value; break;
                case TajsTweaksSettingsCatalog.TransportPillarMaxHeight: TransportPillarMaxHeight = value; break;
                case TajsTweaksSettingsCatalog.TrainTrackPillarMaxHeight: TrainTrackPillarMaxHeight = value; break;
                case TajsTweaksSettingsCatalog.TrainTrackPillarSupportDistance: TrainTrackPillarSupportDistance = value; break;
                case TajsTweaksSettingsCatalog.OverclockMaxPercent: OverclockMaxPercent = value; break;
                case TajsTweaksSettingsCatalog.OverclockTransportSpacingBonus: OverclockTransportSpacingBonus = value; break;
                case TajsTweaksSettingsCatalog.OverclockTransportStackBonus: OverclockTransportStackBonus = value; break;
                case TajsTweaksSettingsCatalog.OverclockMinPercent: OverclockMinPercent = value; break;
                case TajsTweaksSettingsCatalog.OverclockPowerCurve: OverclockPowerCurve = value; break;
                case TajsTweaksSettingsCatalog.OverclockWorkerCurve: OverclockWorkerCurve = value; break;
                case TajsTweaksSettingsCatalog.OverclockComputingCurve: OverclockComputingCurve = value; break;
                case TajsTweaksSettingsCatalog.OverclockMaintenanceCurve: OverclockMaintenanceCurve = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoIntervalSeconds: OverclockAutoIntervalSeconds = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoPowerReserve: OverclockAutoPowerReserve = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoWorkerReserve: OverclockAutoWorkerReserve = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoStepPercent: OverclockAutoStepPercent = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoDeadbandPercent: OverclockAutoDeadbandPercent = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoMaxStepPercent: OverclockAutoMaxStepPercent = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoLowFill: OverclockAutoLowFill = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoNeutralFill: OverclockAutoNeutralFill = value; break;
                case TajsTweaksSettingsCatalog.OverclockAutoHighFill: OverclockAutoHighFill = value; break;
            }
        }

        private static void SetNumber(string key, double value)
        {
            if (key == TajsTweaksSettingsCatalog.PinnedHysteresis)
            {
                PinnedHysteresisPercent = value;
            }
            if (key == TajsTweaksSettingsCatalog.StorageMultiplier)
            {
                StorageMultiplier = value;
            }
            if (key == TajsTweaksSettingsCatalog.StorageThroughputMultiplier)
            {
                StorageThroughputMultiplier = value;
            }
            if (key == TajsTweaksSettingsCatalog.ResourceOverlayLabelHeight)
            {
                ResourceOverlayLabelHeight = value;
            }
            if (key == TajsTweaksSettingsCatalog.ResourceTowerLineWidth)
            {
                ResourceTowerLineWidth = value;
            }
            if (key == TajsTweaksSettingsCatalog.ResourceTowerZoomDamping)
            {
                ResourceTowerZoomDamping = value;
            }
            if (key == TajsTweaksSettingsCatalog.ResourceTowerZoomStart)
            {
                ResourceTowerZoomStart = value;
            }
            if (key == TajsTweaksSettingsCatalog.ResourceTowerAreaHeight)
            {
                ResourceTowerAreaHeight = value;
            }
            if (key == TajsTweaksSettingsCatalog.KeepFullEmptyLabelScale)
            {
                KeepFullEmptyLabelScale = value;
            }
            if (key == TajsTweaksSettingsCatalog.VehicleSoundRange)
            {
                VehicleSoundRange = value;
            }
            if (key == TajsTweaksSettingsCatalog.MachineSoundRange)
            {
                MachineSoundRange = value;
            }
            if (key == TajsTweaksSettingsCatalog.TrainSoundVolume)
            {
                TrainSoundVolume = value;
            }
            if (key == TajsTweaksSettingsCatalog.TrainSoundRange)
            {
                TrainSoundRange = value;
            }
            if (key == TajsTweaksSettingsCatalog.EfficiencyOverlayUpdateSeconds)
            {
                EfficiencyOverlayUpdateSeconds = value;
            }
            if (key == TajsTweaksSettingsCatalog.EfficiencyOverlayRenderDistance)
            {
                EfficiencyOverlayRenderDistance = value;
            }
            if (key == TajsTweaksSettingsCatalog.EfficiencyOverlayLabelScale)
            {
                EfficiencyOverlayLabelScale = value;
            }
        }

        private static void SetText(string key, string value)
        {
            switch (key)
            {
                case TajsTweaksSettingsCatalog.LinePlacementShortcut: LinePlacementShortcut = value; break;
                case TajsTweaksSettingsCatalog.PinnedSortDirection: PinnedSortDirection = value; break;
                case TajsTweaksSettingsCatalog.PinnedSortMode: PinnedSortMode = value; break;
                case TajsTweaksSettingsCatalog.PlanningBuildingColor: PlanningBuildingColor = value; break;
                case TajsTweaksSettingsCatalog.ResourceTowerLineColor: ResourceTowerLineColor = value; break;
                case TajsTweaksSettingsCatalog.ResourceTowerColors: ResourceTowerColors = value; break;
                case TajsTweaksSettingsCatalog.DefaultsUnit: DefaultsUnit = value; break;
                case TajsTweaksSettingsCatalog.DefaultsLoose: DefaultsLoose = value; break;
                case TajsTweaksSettingsCatalog.DefaultsFluid: DefaultsFluid = value; break;
                case TajsTweaksSettingsCatalog.DefaultsWarehouse: DefaultsWarehouse = value; break;
                case TajsTweaksSettingsCatalog.DefaultsMineDump: DefaultsMineDump = value; break;
                case TajsTweaksSettingsCatalog.DefaultsMineWarn: DefaultsMineWarn = value; break;
                case TajsTweaksSettingsCatalog.ShipPreloadData: ShipPreloadData = value; break;
                case TajsTweaksSettingsCatalog.HudHidden: HudHidden = value; break;
                case TajsTweaksSettingsCatalog.HudPositions: HudPositions = value; break;
                case TajsTweaksSettingsCatalog.StorageOverrideData:
                    StorageOverrideData = value;
                    RebuildParsedValues();
                    break;
                case TajsTweaksSettingsCatalog.ParkingHqOffloadMode: ParkingHqOffloadMode = value; break;
                case TajsTweaksSettingsCatalog.BridgeScaleMode: BridgeScaleMode = value; break;
                case TajsTweaksSettingsCatalog.EfficiencyOverlayMode: EfficiencyOverlayMode = value; break;
                case TajsTweaksSettingsCatalog.MutedNotifications:
                    s_mutedNotificationData = value;
                    RebuildParsedValues();
                    break;
            }
        }

        private static void RebuildParsedValues()
        {
            lock (s_gate)
            {
                s_mutedNotifications = new HashSet<string>(ParseIds(s_mutedNotificationData), StringComparer.Ordinal);
                s_storageOverrides = ParseStorageOverrides(StorageOverrideData).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            }
        }
    }
}
