// Taj's COI Mods | TajsStorageAdvancedConfiguration.cs
// Copyright (C) 2026 - 2026 Grzegorz Nowak (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Buildings.Storages.NuclearWaste;
using Mafi.Core.Entities;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles;
using CoiStorage = Mafi.Core.Buildings.Storages.Storage;

namespace TajsCOI.Tweaks.Features.Storage
{
    [Flags]
    internal enum StorageTransferFields
    {
        None = 0,
        ProductAssignment = 1 << 0,
        LogisticsThresholds = 1 << 1,
        ImportExportEnablement = 1 << 2,
        TruckPolicy = 1 << 3,
        Alerts = 1 << 4,
        KeepFullEmpty = 1 << 5,
        CapacityOverride = 1 << 6,
        All = ProductAssignment | LogisticsThresholds | ImportExportEnablement | TruckPolicy | Alerts | KeepFullEmpty | CapacityOverride,
    }

    internal readonly struct StorageTransferCandidate
    {
        internal StorageTransferCandidate(int entityId, bool compatible, string reason)
        {
            EntityId = entityId;
            Compatible = compatible;
            Reason = reason ?? string.Empty;
        }

        internal int EntityId { get; }
        internal bool Compatible { get; }
        internal string Reason { get; }
    }

    /// <summary>
    /// Keeps storage transfer policy independent of UI. Native EntityConfigData remains the
    /// authority for the actual values; this class only validates scope and removes fields that
    /// the player did not select.
    /// </summary>
    internal static class TajsStorageAdvancedConfiguration
    {
        internal const string AllowAllProductsKey = "TajsStorage.AllowAllProducts";
        internal const string CapacityOverrideKey = "TajsStorage.InstanceCapacity";
        private static MethodInfo? s_forceCapacityMethod;

        internal static bool IsRestricted(CoiStorage storage) =>
            storage is NuclearWasteStorage ||
            storage?.Prototype is null ||
            !storage.Prototype.ProductType.HasValue;

        internal static bool IsCompatible(CoiStorage source, CoiStorage target, out string reason)
        {
            if (source is null || target is null || source.IsDestroyed || target.IsDestroyed)
            {
                reason = "storage is missing or destroyed";
                return false;
            }

            if (source is NuclearWasteStorage || target is NuclearWasteStorage)
            {
                reason = "nuclear-waste storage is explicitly restricted";
                return false;
            }

            if (source.GetType() != target.GetType())
            {
                reason = "storage entity types differ";
                return false;
            }

            ProductType? sourceType = source.Prototype.ProductType;
            ProductType? targetType = target.Prototype.ProductType;
            if (sourceType.HasValue != targetType.HasValue ||
                (sourceType.HasValue && !sourceType.Value.Equals(targetType!.Value)))
            {
                reason = "supported product types differ";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static bool CanTransferProduct(CoiStorage source, CoiStorage target, out string reason)
        {
            if ((source.StoredProduct.HasValue && target.StoredProduct.HasValue) &&
                source.StoredProduct.Value != target.StoredProduct.Value &&
                target.CurrentQuantity.IsPositive)
            {
                reason = "destination contains a different product";
                return false;
            }

            if (source.StoredProduct.HasValue && !target.IsProductSupported(source.StoredProduct.Value))
            {
                reason = "destination does not support the assigned product";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static bool CanTransferCapacity(CoiStorage target, int capacity, out string reason)
        {
            if (target is null || target.IsDestroyed)
            {
                reason = "storage is missing or destroyed";
                return false;
            }

            if (capacity <= 0)
            {
                reason = "capacity must be positive";
                return false;
            }

            if (target.CurrentQuantity.Value > capacity)
            {
                reason = "destination contains more product than the requested capacity";
                return false;
            }

            if (GetForceCapacityMethod() is null)
            {
                reason = "the game capacity setter is unavailable";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static bool TryApplyCapacity(CoiStorage target, int capacity, out string reason)
        {
            if (!CanTransferCapacity(target, capacity, out reason))
            {
                return false;
            }

            try
            {
                // Unassigned storages have no native buffer yet. Retain the policy and apply
                // it from the TryAssignProduct postfix when the first compatible product arrives.
                if (!target.StoredProduct.HasValue)
                {
                    TajsStorageAdvancedState.SetCapacityOverride(target.Id.Value, capacity);
                    return true;
                }

                // StorageBase keeps this operation internal to the game. Resolve the exact
                // 0.8.7b seam once and fail open if a future game removes or changes it.
                GetForceCapacityMethod()!.Invoke(target, new object[] { new Quantity(capacity) });
                TajsStorageAdvancedState.SetCapacityOverride(target.Id.Value, capacity);
                return true;
            }
            catch (Exception exception)
            {
                reason = "capacity setter failed: " + exception.GetType().Name;
                return false;
            }
        }

        internal static void ClearCapacityOverride(CoiStorage target)
        {
            TajsStorageAdvancedState.ClearCapacityOverride(target.Id.Value);
            if (target.StoredProduct.HasValue)
            {
                try
                {
                    GetForceCapacityMethod()?.Invoke(target, new object[] { target.Prototype.Capacity });
                }
                catch
                {
                    // Reset is best effort; the native capacity remains authoritative.
                }
            }
        }

        private static MethodInfo? GetForceCapacityMethod() =>
            s_forceCapacityMethod ??= AccessTools.Method(
                typeof(StorageBase),
                "Cheat_ForceNewCapacityTo",
                new[] { typeof(Quantity) });

        internal static IEnumerable<ProductProto> CompatibleProducts(CoiStorage storage, ProtosDb protosDb)
        {
            if (IsRestricted(storage) || protosDb is null)
            {
                return Enumerable.Empty<ProductProto>();
            }

            ProductType productType = storage.Prototype.ProductType!.Value;
            return protosDb.All<ProductProto>()
                .Where(product => product is not null &&
                                  productType.Matches(product.Type) &&
                                  IsRealProduct(product))
                .OrderBy(product => product.Strings.Name.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static bool IsRealProduct(ProductProto product) =>
            product is not null &&
            !product.IsExcludedFromStats &&
            product != ProductProto.Phantom &&
            !product.Id.Value.StartsWith("ParkVehicle_", StringComparison.Ordinal);

        internal static void RemoveUnselectedFields(EntityConfigData data, StorageTransferFields fields)
        {
            if ((fields & StorageTransferFields.ProductAssignment) == 0)
            {
                data.SetStorageStoredProduct(Option<ProductProto>.None);
                data.SetBool(AllowAllProductsKey, null);
            }

            if ((fields & StorageTransferFields.LogisticsThresholds) == 0)
            {
                data.SetPercent("StorageImportUntilPercent", null);
                data.SetPercent("StorageExportFromPercent", null);
                data.SetPercent("StorageTransportFromPercent", null);
                data.SetPercent("StorageTransportUntilPercent", null);
                data.SetInt("StorageImportPriority", null);
                data.SetInt("StorageExportPriority", null);
            }

            if ((fields & StorageTransferFields.ImportExportEnablement) == 0)
            {
                data.IsLogisticsInputDisabled = null;
                data.IsLogisticsOutputDisabled = null;
            }

            if ((fields & StorageTransferFields.TruckPolicy) == 0)
            {
                data.SetBool("IsEnforcingCustomVehicles", null);
                data.SetProtoArray<VehicleGroupProto>("AllowedTruckGroups", null);
                data.AssignedVehicles = null;
            }

            if ((fields & StorageTransferFields.Alerts) == 0)
            {
                data.SetBool("AlertWhenAboveEnabled", null);
                data.SetPercent("AlertWhenAbove", null);
                data.SetBool("AlertWhenBelowEnabled", null);
                data.SetPercent("AlertWhenBelow", null);
            }

            if ((fields & StorageTransferFields.KeepFullEmpty) == 0)
            {
                data.SetInt("StorageCheatMode", null);
            }

            if ((fields & StorageTransferFields.CapacityOverride) == 0)
            {
                data.SetInt(CapacityOverrideKey, null);
            }
        }

        /// <summary>
        /// Native Storage.ApplyConfigInternal treats several missing booleans as false
        /// (notably alerts and custom-truck enforcement). Overlaying the destination's
        /// current config for unselected fields keeps field-level transfer semantics exact.
        /// </summary>
        internal static void PreserveUnselectedFields(
            EntityConfigData data,
            EntityConfigData destination,
            StorageTransferFields fields)
        {
            if ((fields & StorageTransferFields.ProductAssignment) == 0)
            {
                data.SetStorageStoredProduct(destination.GetStorageStoredProduct());
                data.SetBool(AllowAllProductsKey, destination.GetBool(AllowAllProductsKey));
            }

            if ((fields & StorageTransferFields.LogisticsThresholds) == 0)
            {
                data.SetPercent("StorageImportUntilPercent", destination.GetPercent("StorageImportUntilPercent"));
                data.SetPercent("StorageExportFromPercent", destination.GetPercent("StorageExportFromPercent"));
                data.SetPercent("StorageTransportFromPercent", destination.GetPercent("StorageTransportFromPercent"));
                data.SetPercent("StorageTransportUntilPercent", destination.GetPercent("StorageTransportUntilPercent"));
                data.SetInt("StorageImportPriority", destination.GetInt("StorageImportPriority"));
                data.SetInt("StorageExportPriority", destination.GetInt("StorageExportPriority"));
            }

            if ((fields & StorageTransferFields.ImportExportEnablement) == 0)
            {
                data.IsLogisticsInputDisabled = destination.IsLogisticsInputDisabled;
                data.IsLogisticsOutputDisabled = destination.IsLogisticsOutputDisabled;
            }

            if ((fields & StorageTransferFields.TruckPolicy) == 0)
            {
                data.SetBool("IsEnforcingCustomVehicles", destination.GetBool("IsEnforcingCustomVehicles"));
                data.SetProtoArray<VehicleGroupProto>(
                    "AllowedTruckGroups",
                    destination.GetProtoArray<VehicleGroupProto>("AllowedTruckGroups", unlockedOnly: false));
                data.AssignedVehicles = destination.AssignedVehicles;
            }

            if ((fields & StorageTransferFields.Alerts) == 0)
            {
                data.SetBool("AlertWhenAboveEnabled", destination.GetBool("AlertWhenAboveEnabled"));
                data.SetPercent("AlertWhenAbove", destination.GetPercent("AlertWhenAbove"));
                data.SetBool("AlertWhenBelowEnabled", destination.GetBool("AlertWhenBelowEnabled"));
                data.SetPercent("AlertWhenBelow", destination.GetPercent("AlertWhenBelow"));
            }

            if ((fields & StorageTransferFields.KeepFullEmpty) == 0)
            {
                data.SetInt("StorageCheatMode", destination.GetInt("StorageCheatMode"));
            }

            if ((fields & StorageTransferFields.CapacityOverride) == 0)
            {
                data.SetInt(CapacityOverrideKey, destination.GetInt(CapacityOverrideKey));
            }
        }

        internal static string DescribeFields(StorageTransferFields fields)
        {
            var names = new List<string>();
            if ((fields & StorageTransferFields.ProductAssignment) != 0) names.Add("product assignment");
            if ((fields & StorageTransferFields.LogisticsThresholds) != 0) names.Add("logistics thresholds/priorities");
            if ((fields & StorageTransferFields.ImportExportEnablement) != 0) names.Add("import/export enablement");
            if ((fields & StorageTransferFields.TruckPolicy) != 0) names.Add("truck policy");
            if ((fields & StorageTransferFields.Alerts) != 0) names.Add("alerts");
            if ((fields & StorageTransferFields.KeepFullEmpty) != 0) names.Add("keep-full/keep-empty");
            if ((fields & StorageTransferFields.CapacityOverride) != 0) names.Add("capacity override");
            return names.Count == 0 ? "nothing" : string.Join(", ", names);
        }
    }

    internal static class TajsStorageAdvancedState
    {
        private static readonly object s_gate = new();
        private static readonly HashSet<int> s_allowAllProducts = new();
        private static readonly Dictionary<int, int> s_capacityOverrides = new();
        private static int s_lastApplied;
        private static int s_lastSkipped;
        private static string s_lastReport = string.Empty;

        internal static bool IsAllowAll(int entityId)
        {
            lock (s_gate)
            {
                return s_allowAllProducts.Contains(entityId);
            }
        }

        internal static void SetAllowAll(int entityId)
        {
            lock (s_gate)
            {
                s_allowAllProducts.Add(entityId);
            }
        }

        internal static void ClearAllowAll(int entityId)
        {
            lock (s_gate)
            {
                s_allowAllProducts.Remove(entityId);
            }
        }

        internal static int? GetCapacityOverride(int entityId)
        {
            lock (s_gate)
            {
                return s_capacityOverrides.TryGetValue(entityId, out int value) ? value : null;
            }
        }

        internal static void SetCapacityOverride(int entityId, int capacity)
        {
            lock (s_gate)
            {
                s_capacityOverrides[entityId] = capacity;
            }
        }

        internal static void ClearCapacityOverride(int entityId)
        {
            lock (s_gate)
            {
                s_capacityOverrides.Remove(entityId);
            }
        }

        internal static void Clear()
        {
            lock (s_gate)
            {
                s_allowAllProducts.Clear();
                s_capacityOverrides.Clear();
                s_lastApplied = 0;
                s_lastSkipped = 0;
                s_lastReport = string.Empty;
            }
        }

        internal static void RecordTransfer(int applied, int skipped, IEnumerable<string> reasons)
        {
            lock (s_gate)
            {
                s_lastApplied = applied;
                s_lastSkipped = skipped;
                string detail = string.Join("; ", reasons.Where(x => !string.IsNullOrWhiteSpace(x)).Take(6));
                s_lastReport = "Storage configuration applied to " + applied + " storage(s); skipped " + skipped + "." +
                               (detail.Length == 0 ? string.Empty : " " + detail);
            }
        }

        internal static string LastTransferReport
        {
            get
            {
                lock (s_gate)
                {
                    return s_lastReport;
                }
            }
        }

        internal static (int Applied, int Skipped) LastTransferCounts
        {
            get
            {
                lock (s_gate)
                {
                    return (s_lastApplied, s_lastSkipped);
                }
            }
        }
    }
}
