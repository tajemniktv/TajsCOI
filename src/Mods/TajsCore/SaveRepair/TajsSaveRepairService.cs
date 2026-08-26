// Taj's COI Mods | TajsSaveRepairService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mafi;
using Mafi.Core;
using Mafi.Core.Console;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using Mafi.Core.World.Entities;
using Mafi.Core.World.QuickTrade;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Core.SaveRepair
{
    /// <summary>
    ///     Opt-in, type-specific repair utilities for loaded saves. This service deliberately
    ///     reports unknown data but only mutates structures with an audited repair contract.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class TajsSaveRepairService
    {
        private const string InfiniteGroundwaterTarget = "infinite_groundwater";
        private const string ShipAutoExploreTarget = "ship_auto_explore";
        private const string WorldMapQuickTradesTarget = "world_map_quick_trades";

        private const string InfiniteGroundwaterTypeName =
            "InfiniteGroundwater.InfiniteGroundwaterReplenisher, InfiniteGroundwater";

        private const string ShipAutoExploreTypeName =
            "ShipAutoExplore.ShipAutoExploreController, ShipAutoExplore";

        private readonly DependencyResolver m_resolver;
        private readonly ISaveManager m_saveManager;
        private readonly IFileSystemHelper m_fileSystemHelper;
        private readonly ProtosDb m_protos;
        private readonly ITajsLogger m_log;

        public TajsSaveRepairService(
            DependencyResolver resolver,
            ISaveManager saveManager,
            IFileSystemHelper fileSystemHelper,
            ProtosDb protos,
            ITajsRuntime runtime)
        {
            m_resolver = resolver;
            m_saveManager = saveManager;
            m_fileSystemHelper = fileSystemHelper;
            m_protos = protos;
            m_log = runtime.GetLogger("TajsCore", "SaveRepair");
        }

        [ConsoleCommand(
            documentation: "Reports supported save-repair findings without mutating the loaded game.",
            customCommandName: "tajs_save_sanitize_report")]
        public string Report()
        {
            var builder = new StringBuilder(2048);
            builder.AppendLine("TajsCore save sanitizer report (dry-run; no changes made)");
            builder.AppendLine("Supported repairs:");
            AppendLegacyReport(builder, InfiniteGroundwaterTarget, InfiniteGroundwaterTypeName, "onNewDay", LegacyEventKind.CalendarNewDay);
            AppendLegacyReport(builder, ShipAutoExploreTarget, ShipAutoExploreTypeName, "onSimUpdate", LegacyEventKind.SimUpdate);
            AppendWorldMapQuickTradeReport(builder);
            AppendPrototypeInventory(builder);
            builder.AppendLine("Unsupported or uncertain save data is intentionally left untouched.");
            return builder.ToString().TrimEnd();
        }

        [ConsoleCommand(
            documentation: "Repairs one supported save finding and queues a repaired copy. Requires target, CONFIRM, and a new save name.",
            customCommandName: "tajs_save_sanitize_repair")]
        public string Repair(string? targetId, string? confirmation, string? outputSaveName)
        {
            if (string.IsNullOrWhiteSpace(targetId) ||
                !string.Equals(confirmation, "CONFIRM", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(outputSaveName))
            {
                return "Usage: tajs_save_sanitize_repair <infinite_groundwater|ship_auto_explore|world_map_quick_trades> CONFIRM <new-save-name>";
            }

            string requestedTarget = targetId!;
            string requestedOutputSaveName = outputSaveName!;
            if (!TryPrepareNewSave(requestedOutputSaveName, out string outputPath, out string saveFailure))
            {
                return saveFailure;
            }

            string normalizedTarget = NormalizeTarget(requestedTarget);
            if (normalizedTarget == WorldMapQuickTradesTarget)
            {
                return RepairWorldMapQuickTrades(requestedOutputSaveName, outputPath);
            }

            if (!TryGetLegacyTarget(normalizedTarget, out LegacyTarget target))
            {
                return "Unknown save-repair target. Run tajs_save_sanitize_report for supported targets.";
            }

            LegacyInspection inspection = InspectLegacy(target);
            if (inspection.Status != RepairStatus.NeedsRepair)
            {
                return FormatLegacyInspection(target, inspection) + "; no repaired copy was queued.";
            }

            if (!TryRepairLegacy(target, out string repairFailure))
            {
                return repairFailure + " Do not save this instance.";
            }

            LegacyInspection after = InspectLegacy(target);
            if (after.Status != RepairStatus.Clean)
            {
                return "The repair did not reach a clean postcondition; no repaired copy was queued. Do not save this instance.";
            }

            return QueueRepairedCopy(target.Id, requestedOutputSaveName, outputPath);
        }

        /// <summary>
        ///     Compatibility command for the migration workflow shipped before the Core service
        ///     existed. It detaches only; the caller may choose the save slot through the normal
        ///     game UI. The new command above is preferred because it enforces a new output slot.
        /// </summary>
        [ConsoleCommand(
            documentation: "Detaches the audited legacy InfiniteGroundwater callback. Save a new copy only after a successful result.",
            customCommandName: "tajs_infinite_groundwater_migrate")]
        public string MigrateLegacyInfiniteGroundwaterSave() => MigrateLegacy(InfiniteGroundwaterTarget);

        [ConsoleCommand(
            documentation: "Detaches the audited legacy ShipAutoExplore callback. Save a new copy only after a successful result.",
            customCommandName: "tajs_ship_auto_explore_migrate")]
        public string MigrateLegacyShipAutoExploreSave() => MigrateLegacy(ShipAutoExploreTarget);

        private string MigrateLegacy(string targetId)
        {
            if (!TryGetLegacyTarget(targetId, out LegacyTarget target))
            {
                return "The requested legacy migration is not supported by this Core build.";
            }

            LegacyInspection inspection = InspectLegacy(target);
            if (inspection.Status == RepairStatus.NotLoaded)
            {
                return FormatLegacyInspection(target, inspection) + "; no migration was performed.";
            }
            if (inspection.Status == RepairStatus.Clean)
            {
                return FormatLegacyInspection(target, inspection) + ". Save a new copy, then disable the standalone mod.";
            }
            if (inspection.Status != RepairStatus.NeedsRepair)
            {
                return FormatLegacyInspection(target, inspection) + "; no migration was performed. Do not save this instance.";
            }

            if (!TryRepairLegacy(target, out string failure))
            {
                return failure + " Do not save this instance.";
            }

            return "Legacy " + target.DisplayName + " detached. Save a new copy now, then disable the standalone mod.";
        }

        private bool TryRepairLegacy(LegacyTarget target, out string failure)
        {
            failure = string.Empty;
            var legacyType = Type.GetType(target.TypeName, false);
            if (legacyType is null)
            {
                failure = "Legacy " + target.DisplayName + " is not loaded; no migration was performed.";
                return false;
            }

            MethodInfo? callbackMethod = FindCallbackMethod(legacyType, target.CallbackName);
            if (callbackMethod is null || callbackMethod.GetParameters().Length != 0)
            {
                failure = "Legacy " + target.DisplayName + " has an unsupported callback shape; no migration was performed.";
                return false;
            }

            if (!TryResolveLegacyEvent(target.EventKind, out IEvent source, out string eventFailure))
            {
                failure = eventFailure;
                return false;
            }

            try
            {
                if (!LegacySaveCallbackMigration.TryDetachCallbacksFromResolvedEvents(
                        m_resolver,
                        source,
                        legacyType,
                        callbackMethod,
                        out bool callbackRegistered,
                        out List<object> callbackOwners,
                        out string detachFailure))
                {
                    failure = "Legacy " + target.DisplayName + " callback detachment could not be confirmed (" + detachFailure + ").";
                    return false;
                }

                if (!LegacySaveCallbackMigration.RemoveResolverEntries(
                        m_resolver,
                        legacyType,
                        callbackOwners,
                        out string cleanupFailure))
                {
                    failure = "The legacy " + target.DisplayName + " event was detached, but resolver cleanup failed (" + cleanupFailure + ").";
                    return false;
                }

                m_log.Info(
                    "Detached legacy " + target.DisplayName + " callback and resolver ownership; callback registered=" + callbackRegistered + ".");
                return true;
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Legacy " + target.DisplayName + " migration failed; no safe save was produced.");
                failure = "Legacy " + target.DisplayName + " migration failed; see the log for details.";
                return false;
            }
        }

        private LegacyInspection InspectLegacy(LegacyTarget target)
        {
            var legacyType = Type.GetType(target.TypeName, false);
            if (legacyType is null)
            {
                return new LegacyInspection(RepairStatus.NotLoaded, 0, 0, 0, "legacy assembly is not loaded");
            }

            MethodInfo? callbackMethod = FindCallbackMethod(legacyType, target.CallbackName);
            if (callbackMethod is null || callbackMethod.GetParameters().Length != 0)
            {
                return new LegacyInspection(RepairStatus.Unsupported, 0, 0, 0, "callback shape is not the audited 0.8.7b shape");
            }

            if (!TryResolveLegacyEvent(target.EventKind, out IEvent source, out string eventFailure))
            {
                return new LegacyInspection(RepairStatus.Unavailable, 0, 0, 0, eventFailure);
            }

            try
            {
                if (!LegacySaveCallbackMigration.TryInspectCallbacksFromResolvedEvents(
                        m_resolver,
                        source,
                        legacyType,
                        callbackMethod.Name,
                        out int callbackCount,
                        out int eventCount,
                        out string inspectFailure))
                {
                    return new LegacyInspection(RepairStatus.Unsupported, callbackCount, 0, eventCount, inspectFailure);
                }

                int resolverCount = CountResolvedObjectsOfType(legacyType);
                RepairStatus status = callbackCount == 0 && resolverCount == 0
                    ? RepairStatus.Clean
                    : RepairStatus.NeedsRepair;
                return new LegacyInspection(status, callbackCount, resolverCount, eventCount, string.Empty);
            }
            catch (Exception exception)
            {
                return new LegacyInspection(RepairStatus.Unavailable, 0, 0, 0, exception.GetType().Name);
            }
        }

        private void AppendLegacyReport(
            StringBuilder builder,
            string id,
            string typeName,
            string callbackName,
            LegacyEventKind eventKind)
        {
            LegacyTarget target = new(id, id.Replace('_', ' '), typeName, callbackName, eventKind);
            builder.AppendLine("  " + FormatLegacyInspection(target, InspectLegacy(target)));
        }

        private static string FormatLegacyInspection(LegacyTarget target, LegacyInspection inspection)
        {
            string detail = inspection.Detail.Length == 0 ? string.Empty : "; " + inspection.Detail;
            return target.Id + ": " + inspection.Status +
                   "; callbacks=" + inspection.CallbackCount +
                   "; resolver-owners=" + inspection.ResolverCount +
                   "; scanned-events=" + inspection.EventCount + detail;
        }

        private void AppendPrototypeInventory(StringBuilder builder)
        {
            builder.AppendLine("Prototype inventory:");
            builder.AppendLine("  phantom-prototypes=" + m_protos.Phantoms.Count + " (report-only)");
            builder.AppendLine("  missing-reference scan=not enabled; unknown references remain untouched");
        }

        private void AppendWorldMapQuickTradeReport(StringBuilder builder)
        {
            try
            {
                int villageCount = 0;
                int invalidCount = 0;
                foreach (WorldMapVillageProto village in m_protos.All<WorldMapVillageProto>())
                {
                    villageCount++;
                    foreach (QuickTradePairProto trade in village.QuickTrades)
                    {
                        if (trade.MinReputationRequired > village.MaxReputation)
                        {
                            invalidCount++;
                            builder.AppendLine(
                                "  " + WorldMapQuickTradesTarget + ": needs repair; village=" + village.Id +
                                "; trade=" + trade.Id + "; min-reputation=" + trade.MinReputationRequired +
                                "; max-reputation=" + village.MaxReputation +
                                "; confidence=type-specific");
                        }
                    }
                }

                if (invalidCount == 0)
                {
                    builder.AppendLine("  " + WorldMapQuickTradesTarget + ": clean; villages-scanned=" + villageCount);
                }
            }
            catch (Exception exception)
            {
                builder.AppendLine("  " + WorldMapQuickTradesTarget + ": unavailable; " + exception.GetType().Name);
            }
        }

        private string RepairWorldMapQuickTrades(string outputSaveName, string outputPath)
        {
            var invalid = new List<(WorldMapVillageProto village, QuickTradePairProto trade)>();
            try
            {
                foreach (WorldMapVillageProto village in m_protos.All<WorldMapVillageProto>())
                {
                    foreach (QuickTradePairProto trade in village.QuickTrades)
                    {
                        if (trade.MinReputationRequired > village.MaxReputation)
                        {
                            invalid.Add((village, trade));
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                return "World-map quick-trade validation failed (" + exception.GetType().Name + "); no repaired copy was queued.";
            }

            if (invalid.Count == 0)
            {
                return "World-map quick trades are clean; no repaired copy was queued.";
            }

            FieldInfo? minReputationField = typeof(QuickTradePairProto).GetField(
                nameof(QuickTradePairProto.MinReputationRequired),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (minReputationField is null || !minReputationField.IsInitOnly)
            {
                return "World-map quick-trade repair refused because the audited immutable field shape changed; no repaired copy was queued.";
            }

            try
            {
                foreach ((WorldMapVillageProto village, QuickTradePairProto trade) item in invalid)
                {
                    minReputationField.SetValue(item.trade, item.village.MaxReputation);
                }

                foreach ((WorldMapVillageProto village, QuickTradePairProto trade) item in invalid)
                {
                    if (item.trade.MinReputationRequired > item.village.MaxReputation)
                    {
                        return "World-map quick-trade repair did not reach its postcondition; no repaired copy was queued. Do not save this instance.";
                    }
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "World-map quick-trade repair failed; no safe save was produced.");
                return "World-map quick-trade repair failed (" + exception.GetType().Name + "); do not save this instance.";
            }

            m_log.Info("Clamped " + invalid.Count + " impossible world-map quick-trade reputation requirement(s).");
            return QueueRepairedCopy(WorldMapQuickTradesTarget, outputSaveName, outputPath, invalid.Count);
        }

        private string QueueRepairedCopy(string targetId, string outputSaveName, string outputPath, int changedCount = 1)
        {
            try
            {
                m_saveManager.RequestGameSave(outputSaveName);
                return "Save repair complete for " + targetId + "; changed=" + changedCount +
                       "; repaired copy queued as '" + outputSaveName + "' at '" + outputPath + "'.";
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Save repair succeeded in memory, but the repaired copy could not be queued.");
                return "Save repair changed the loaded game, but queuing the repaired copy failed (" + exception.GetType().Name +
                       "). Do not exit without saving a new copy.";
            }
        }

        private bool TryPrepareNewSave(string outputSaveName, out string outputPath, out string failure)
        {
            outputPath = string.Empty;
            failure = string.Empty;
            try
            {
                outputPath = m_fileSystemHelper.GetSaveFilePath(outputSaveName, m_saveManager.GameName);
                if (File.Exists(outputPath))
                {
                    failure = "Refusing to overwrite existing save '" + outputPath + "'. Choose a new save name.";
                    return false;
                }

                if (m_saveManager is SaveManager saveManager && saveManager.LastSaveFilePath.HasValue &&
                    string.Equals(
                        Path.GetFullPath(outputPath),
                        Path.GetFullPath(saveManager.LastSaveFilePath.Value),
                        StringComparison.OrdinalIgnoreCase))
                {
                    failure = "Refusing to overwrite the currently loaded save. Choose a new save name.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = "The requested output save name is invalid (" + exception.GetType().Name + ").";
                return false;
            }
        }

        private bool TryResolveLegacyEvent(LegacyEventKind eventKind, out IEvent source, out string failure)
        {
            source = null!;
            failure = string.Empty;
            switch (eventKind)
            {
                case LegacyEventKind.CalendarNewDay:
                    if (m_resolver.TryResolve(out ICalendar calendar))
                    {
                        source = calendar.NewDay;
                        return true;
                    }
                    failure = "The active game calendar is unavailable; no migration was performed.";
                    return false;
                case LegacyEventKind.SimUpdate:
                    if (m_resolver.TryResolve(out ISimLoopEvents simLoopEvents))
                    {
                        source = simLoopEvents.Update;
                        return true;
                    }
                    failure = "The active simulation loop is unavailable; no migration was performed.";
                    return false;
                default:
                    failure = "The target event is not supported by this Core build.";
                    return false;
            }
        }

        private static MethodInfo? FindCallbackMethod(Type legacyType, string callbackName) => legacyType.GetMethod(
            callbackName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private int CountResolvedObjectsOfType(Type type)
        {
            var values = new List<object>();
            foreach (object value in m_resolver.GetAllResolvedObjects())
            {
                AddUniqueExactType(values, value, type);
            }
            foreach (object value in m_resolver.AllResolvedInstances)
            {
                AddUniqueExactType(values, value, type);
            }
            return values.Count;
        }

        private static void AddUniqueExactType(List<object> values, object value, Type type)
        {
            if (value.GetType() != type || values.Any(existing => ReferenceEquals(existing, value)))
            {
                return;
            }
            values.Add(value);
        }

        private static string NormalizeTarget(string targetId) => targetId.Trim().ToLowerInvariant() switch
        {
            "infinitegroundwater" => InfiniteGroundwaterTarget,
            "infinite-groundwater" => InfiniteGroundwaterTarget,
            "shipautoexplore" => ShipAutoExploreTarget,
            "ship-auto-explore" => ShipAutoExploreTarget,
            "quick_trades" => WorldMapQuickTradesTarget,
            "world-map-quick-trades" => WorldMapQuickTradesTarget,
            _ => targetId.Trim().ToLowerInvariant(),
        };

        private static bool TryGetLegacyTarget(string id, out LegacyTarget target)
        {
            switch (id)
            {
                case InfiniteGroundwaterTarget:
                    target = new LegacyTarget(id, "InfiniteGroundwater", InfiniteGroundwaterTypeName, "onNewDay", LegacyEventKind.CalendarNewDay);
                    return true;
                case ShipAutoExploreTarget:
                    target = new LegacyTarget(id, "ShipAutoExplore", ShipAutoExploreTypeName, "onSimUpdate", LegacyEventKind.SimUpdate);
                    return true;
                default:
                    target = default;
                    return false;
            }
        }

        private enum LegacyEventKind
        {
            CalendarNewDay,
            SimUpdate,
        }

        private enum RepairStatus
        {
            NotLoaded,
            Clean,
            NeedsRepair,
            Unsupported,
            Unavailable,
        }

        private readonly struct LegacyTarget
        {
            public LegacyTarget(string id, string displayName, string typeName, string callbackName, LegacyEventKind eventKind)
            {
                Id = id;
                DisplayName = displayName;
                TypeName = typeName;
                CallbackName = callbackName;
                EventKind = eventKind;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public string TypeName { get; }
            public string CallbackName { get; }
            public LegacyEventKind EventKind { get; }
        }

        private readonly struct LegacyInspection
        {
            public LegacyInspection(RepairStatus status, int callbackCount, int resolverCount, int eventCount, string detail)
            {
                Status = status;
                CallbackCount = callbackCount;
                ResolverCount = resolverCount;
                EventCount = eventCount;
                Detail = detail;
            }

            public RepairStatus Status { get; }
            public int CallbackCount { get; }
            public int ResolverCount { get; }
            public int EventCount { get; }
            public string Detail { get; }
        }
    }
}
