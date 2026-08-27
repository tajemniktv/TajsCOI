// Taj's COI Mods | TajsStorageAdvancedConfiguration.cs
// Copyright (C) 2026 - 2026 Grzegorz Nowak (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private static FieldInfo? s_enforcingVehiclesField;

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

        internal static bool TryApplyCapacity(CoiStorage target, int capacity, out string reason, bool remember = true)
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
                    if (remember)
                    {
                        TajsStorageAdvancedState.SetCapacityOverride(target.Id.Value, capacity);
                    }
                    return true;
                }

                // StorageBase keeps this operation protected. Resolve the exact 0.8.7b seam
                // once; unlike the cheat-only helper this path also updates product statistics.
                GetForceCapacityMethod()!.Invoke(target, new object[] { new Quantity(capacity) });
                if (remember)
                {
                    TajsStorageAdvancedState.SetCapacityOverride(target.Id.Value, capacity);
                }
                return true;
            }
            catch (Exception exception)
            {
                reason = "capacity setter failed: " + exception.GetType().Name;
                return false;
            }
        }

        internal static bool TryClearCapacityOverride(CoiStorage target, out string reason)
        {
            if (!CanClearCapacityOverride(target, out reason))
            {
                return false;
            }

            try
            {
                if (target.StoredProduct.HasValue)
                {
                    GetForceCapacityMethod()!.Invoke(target, new object[] { target.Prototype.Capacity });
                }
                TajsStorageAdvancedState.ClearCapacityOverride(target.Id.Value);
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                reason = "capacity reset failed: " + exception.GetType().Name;
                return false;
            }
        }

        internal static bool CanClearCapacityOverride(CoiStorage target, out string reason)
        {
            if (target is null || target.IsDestroyed)
            {
                reason = "storage is missing or destroyed";
                return false;
            }

            if (target.CurrentQuantity.Value > target.Prototype.Capacity.Value)
            {
                reason = "destination contains more product than its native capacity";
                return false;
            }

            if (target.StoredProduct.HasValue && GetForceCapacityMethod() is null)
            {
                reason = "the game capacity setter is unavailable";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static MethodInfo? GetForceCapacityMethod() =>
            s_forceCapacityMethod ??= AccessTools.Method(
                typeof(StorageBase),
                "ForceNewCapacityTo",
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

        internal static bool IsEnforcingCustomVehicles(CoiStorage storage)
        {
            s_enforcingVehiclesField ??= AccessTools.Field(typeof(CoiStorage), "m_isEnforcingCustomVehicles");
            return s_enforcingVehiclesField?.GetValue(storage) is bool value && value;
        }

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
            // EntitiesCloneConfigHelper also carries generic entity state. Storage transfers
            // must not accidentally pause/unpause, rename, reprioritize, rewire, or change
            // power policy simply because the native helper includes those fields.
            data.IsPaused = destination.IsPaused;
            data.IsElectricitySurplusGenerator = destination.IsElectricitySurplusGenerator;
            data.IsElectricitySurplusConsumer = destination.IsElectricitySurplusConsumer;
            data.ElectricityGenerationPriority = destination.ElectricityGenerationPriority;
            data.CustomTitle = destination.CustomTitle;
            data.AssignedInputs = destination.AssignedInputs;
            data.AssignedOutputs = destination.AssignedOutputs;
            data.IsConstructionPaused = destination.IsConstructionPaused;
            data.ConstructionPriorityOverride = destination.ConstructionPriorityOverride;
            data.LinkedEntities = destination.LinkedEntities;

            if ((fields & StorageTransferFields.LogisticsThresholds) == 0)
            {
                data.GeneralPriority = destination.GeneralPriority;
            }

            if ((fields & StorageTransferFields.ImportExportEnablement) == 0)
            {
                data.AllowNonAssignedOutput = destination.AllowNonAssignedOutput;
            }

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

        /// <summary>
        ///     Returns only the extension-owned values that must travel alongside the native
        ///     EntityConfigData copy. Native product/logistics fields remain owned by the game.
        /// </summary>
        internal static IReadOnlyDictionary<string, object> ReadBlueprintValues(object runtimeEntity)
        {
            if (!(runtimeEntity is CoiStorage storage) || storage.IsDestroyed)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }

            var values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [AllowAllProductsKey] = TajsStorageAdvancedState.IsAllowAll(storage.Id.Value),
            };
            int? capacity = TajsStorageAdvancedState.GetCapacityOverride(storage.Id.Value);
            if (capacity.HasValue)
            {
                values[CapacityOverrideKey] = capacity.Value;
            }

            return values;
        }

        internal static bool ApplyBlueprintValues(
            object runtimeEntity,
            IReadOnlyDictionary<string, object> values)
        {
            if (!(runtimeEntity is CoiStorage storage) || storage.IsDestroyed || values is null)
            {
                return false;
            }

            bool? allowAll = null;
            if (values.TryGetValue(AllowAllProductsKey, out object? allowAllRaw))
            {
                if (!(allowAllRaw is bool parsedAllowAll))
                {
                    return false;
                }
                allowAll = parsedAllowAll;
            }

            int? capacity = null;
            if (values.TryGetValue(CapacityOverrideKey, out object? capacityRaw))
            {
                try
                {
                    capacity = Convert.ToInt32(capacityRaw, CultureInfo.InvariantCulture);
                }
                catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException)
                {
                    return false;
                }
            }

            if (allowAll.HasValue)
            {
                if (allowAll.Value)
                {
                    TajsStorageAdvancedState.SetAllowAll(storage.Id.Value);
                }
                else
                {
                    TajsStorageAdvancedState.ClearAllowAll(storage.Id.Value);
                }
            }

            if (capacity.HasValue && !TryApplyCapacity(storage, capacity.Value, out _))
            {
                return false;
            }

            return true;
        }
    }

    internal static class TajsStorageAdvancedState
    {
        private const string StateHeader = "TajsTweaksStorageAdvancedV1";
        private static readonly object s_gate = new();
        private static readonly HashSet<int> s_allowAllProducts = new();
        private static readonly Dictionary<int, int> s_capacityOverrides = new();
        private static int s_lastApplied;
        private static int s_lastSkipped;
        private static string s_lastReport = string.Empty;
        private static string? s_stateFilePath;

        internal static void LoadForSave(string? saveName)
        {
            string safeName = SanitizeSaveName(saveName);
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsTweaks",
                "StorageAdvanced",
                safeName.Length == 0 ? "current" : safeName);
            string path = Path.Combine(directory, "state.txt");

            lock (s_gate)
            {
                s_allowAllProducts.Clear();
                s_capacityOverrides.Clear();
                s_lastApplied = 0;
                s_lastSkipped = 0;
                s_lastReport = string.Empty;
                s_stateFilePath = path;
            }

            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 || lines[0] != StateHeader)
                {
                    return;
                }

                for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    string[] parts = line.Split('\t');
                    if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entityId) || entityId < 0)
                    {
                        continue;
                    }

                    lock (s_gate)
                    {
                        if (parts[0] == "allow")
                        {
                            s_allowAllProducts.Add(entityId);
                        }
                        else if (parts[0] == "capacity" &&
                                 parts.Length >= 3 &&
                                 int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int capacity) &&
                                 capacity > 0)
                        {
                            s_capacityOverrides[entityId] = capacity;
                        }
                    }
                }
            }
            catch
            {
                // Optional metadata is fail-open. Native storage state remains authoritative.
                lock (s_gate)
                {
                    s_allowAllProducts.Clear();
                    s_capacityOverrides.Clear();
                }
            }
        }

        internal static void UnbindSave()
        {
            lock (s_gate)
            {
                s_stateFilePath = null;
                s_allowAllProducts.Clear();
                s_capacityOverrides.Clear();
                s_lastApplied = 0;
                s_lastSkipped = 0;
                s_lastReport = string.Empty;
            }
        }

        internal static bool IsAllowAll(int entityId)
        {
            lock (s_gate)
            {
                return s_allowAllProducts.Contains(entityId);
            }
        }

        internal static void SetAllowAll(int entityId)
        {
            bool changed;
            lock (s_gate)
            {
                changed = s_allowAllProducts.Add(entityId);
            }
            if (changed)
            {
                Persist();
            }
        }

        internal static void ClearAllowAll(int entityId)
        {
            bool changed;
            lock (s_gate)
            {
                changed = s_allowAllProducts.Remove(entityId);
            }
            if (changed)
            {
                Persist();
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
            bool changed;
            lock (s_gate)
            {
                changed = !s_capacityOverrides.TryGetValue(entityId, out int previous) || previous != capacity;
                s_capacityOverrides[entityId] = capacity;
            }
            if (changed)
            {
                Persist();
            }
        }

        internal static void ClearCapacityOverride(int entityId)
        {
            bool changed;
            lock (s_gate)
            {
                changed = s_capacityOverrides.Remove(entityId);
            }
            if (changed)
            {
                Persist();
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

        private static void Persist()
        {
            string? path;
            string[] lines;
            lock (s_gate)
            {
                path = s_stateFilePath;
                var entries = new List<string>(1 + s_allowAllProducts.Count + s_capacityOverrides.Count) { StateHeader };
                entries.AddRange(
                    s_allowAllProducts.Select(id =>
                        "allow\t" + id.ToString(CultureInfo.InvariantCulture)));
                entries.AddRange(
                    s_capacityOverrides.Select(pair =>
                        "capacity\t" + pair.Key.ToString(CultureInfo.InvariantCulture) + "\t" +
                        pair.Value.ToString(CultureInfo.InvariantCulture)));
                lines = entries.ToArray();
            }

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllLines(path, lines);
            }
            catch
            {
                // An unwritable optional sidecar must not affect native gameplay state.
            }
        }

        private static string SanitizeSaveName(string? saveName)
        {
            string value = (saveName ?? string.Empty).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Length > 96 ? value.Substring(0, 96) : value;
        }
    }
}
