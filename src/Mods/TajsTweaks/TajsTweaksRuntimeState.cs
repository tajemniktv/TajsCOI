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
        internal static bool PinnedLowOnly;
        internal static int PinnedLowThreshold;
        internal static int PinnedLowLimit;

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
        internal static bool WorldOperations;
        internal static bool AutoWorldDelivery;
        internal static bool ShipPreload;
        internal static string ShipPreloadData = string.Empty;
        internal static bool RecoverTrucks;
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
        internal static bool StorageOverrides;
        internal static double StorageMultiplier;
        internal static double StorageThroughputMultiplier;
        internal static string StorageOverrideData = string.Empty;
        internal static bool DesignationControls;
        internal static int DesignationLimit;
        internal static bool HideDesignations;
        internal static bool NotificationFilter;
        internal static bool FarmWarnings;
        internal static bool FleetManager;
        internal static int FleetBatchLimit;

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
            PinnedLowOnly = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedLowOnly);
            PinnedLowThreshold = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedLowThreshold);
            PinnedLowLimit = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.PinnedLowLimit);

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
            WorldOperations = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.WorldOperations);
            AutoWorldDelivery = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.AutoWorldDelivery);
            ShipPreload = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ShipPreload);
            ShipPreloadData = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.ShipPreloadData);
            RecoverTrucks = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.RecoverTrucks);
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
            StorageOverrides = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageOverrides);
            StorageMultiplier = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageMultiplier);
            StorageThroughputMultiplier = settings.Get<double>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageThroughputMultiplier);
            StorageOverrideData = settings.Get<string>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.StorageOverrideData);
            DesignationControls = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DesignationControls);
            DesignationLimit = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.DesignationLimit);
            HideDesignations = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.HideDesignations);
            NotificationFilter = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.NotificationFilter);
            FarmWarnings = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FarmWarnings);
            FleetManager = settings.Get<bool>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FleetManager);
            FleetBatchLimit = settings.Get<int>(TajsTweaksSettingsCatalog.ModId, TajsTweaksSettingsCatalog.FleetBatchLimit);
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

        private static void SetBoolean(string key, bool value)
        {
            switch (key)
            {
                case TajsTweaksSettingsCatalog.LinePlacement: LinePlacement = value; break;
                case TajsTweaksSettingsCatalog.PinnedSort: PinnedSort = value; break;
                case TajsTweaksSettingsCatalog.PinnedCompact: PinnedCompact = value; break;
                case TajsTweaksSettingsCatalog.PinnedBarColors: PinnedBarColors = value; break;
                case TajsTweaksSettingsCatalog.PinnedLowOnly: PinnedLowOnly = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlay: ResourceOverlay = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayDepth: ResourceOverlayDepth = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayTowerAreas: ResourceOverlayTowerAreas = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayTowerLabels: ResourceOverlayTowerLabels = value; break;
                case TajsTweaksSettingsCatalog.WorldOperations: WorldOperations = value; break;
                case TajsTweaksSettingsCatalog.AutoWorldDelivery: AutoWorldDelivery = value; break;
                case TajsTweaksSettingsCatalog.ShipPreload: ShipPreload = value; break;
                case TajsTweaksSettingsCatalog.RecoverTrucks: RecoverTrucks = value; break;
                case TajsTweaksSettingsCatalog.StageMineTrucks: StageMineTrucks = value; break;
                case TajsTweaksSettingsCatalog.FreeCamera: FreeCamera = value; break;
                case TajsTweaksSettingsCatalog.UnlimitedZoom: UnlimitedZoom = value; break;
                case TajsTweaksSettingsCatalog.GroundClipping: GroundClipping = value; break;
                case TajsTweaksSettingsCatalog.HudLayout: HudLayout = value; break;
                case TajsTweaksSettingsCatalog.HudDragLocked: HudDragLocked = value; break;
                case TajsTweaksSettingsCatalog.StorageOverrides: StorageOverrides = value; break;
                case TajsTweaksSettingsCatalog.DesignationControls: DesignationControls = value; break;
                case TajsTweaksSettingsCatalog.HideDesignations: HideDesignations = value; break;
                case TajsTweaksSettingsCatalog.NotificationFilter: NotificationFilter = value; break;
                case TajsTweaksSettingsCatalog.FarmWarnings: FarmWarnings = value; break;
                case TajsTweaksSettingsCatalog.FleetManager: FleetManager = value; break;
            }
        }

        private static void SetInteger(string key, int value)
        {
            switch (key)
            {
                case TajsTweaksSettingsCatalog.LinePlacementLength: LinePlacementLength = value; break;
                case TajsTweaksSettingsCatalog.PinnedColumns: PinnedColumns = value; break;
                case TajsTweaksSettingsCatalog.PinnedLowThreshold: PinnedLowThreshold = value; break;
                case TajsTweaksSettingsCatalog.PinnedLowLimit: PinnedLowLimit = value; break;
                case TajsTweaksSettingsCatalog.RecoverPeriod: RecoverPeriod = value; break;
                case TajsTweaksSettingsCatalog.StageMineTrucksScan: StageMineTrucksScan = value; break;
                case TajsTweaksSettingsCatalog.HudScale: HudScale = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayLabelScale: ResourceOverlayLabelScale = value; break;
                case TajsTweaksSettingsCatalog.ResourceOverlayLabelAlpha: ResourceOverlayLabelAlpha = value; break;
                case TajsTweaksSettingsCatalog.DesignationLimit: DesignationLimit = value; break;
                case TajsTweaksSettingsCatalog.FleetBatchLimit: FleetBatchLimit = value; break;
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
        }

        private static void SetText(string key, string value)
        {
            switch (key)
            {
                case TajsTweaksSettingsCatalog.LinePlacementShortcut: LinePlacementShortcut = value; break;
                case TajsTweaksSettingsCatalog.PinnedSortDirection: PinnedSortDirection = value; break;
                case TajsTweaksSettingsCatalog.PinnedSortMode: PinnedSortMode = value; break;
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
