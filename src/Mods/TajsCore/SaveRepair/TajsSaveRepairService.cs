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
using Mafi.Core.Mods;
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
        private const string StaleTajsConfigTarget = "stale_tajs_config";

        private const string InfiniteGroundwaterTypeName =
            "InfiniteGroundwater.InfiniteGroundwaterReplenisher, InfiniteGroundwater";

        private const string ShipAutoExploreTypeName =
            "ShipAutoExplore.ShipAutoExploreController, ShipAutoExplore";

        private readonly DependencyResolver m_resolver;
        private readonly ISaveManager m_saveManager;
        private readonly IFileSystemHelper m_fileSystemHelper;
        private readonly ProtosDb m_protos;
        private readonly ITajsLogger m_log;
        private readonly SaveRepairHandlerRegistry m_handlers;

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
            m_handlers = BuildHandlerRegistry();
        }

        private SaveRepairHandlerRegistry BuildHandlerRegistry() => new(new[]
        {
            new SaveRepairHandler(
                InfiniteGroundwaterTarget,
                "TajsCore.SaveRepair.LegacySaveCallbackMigration",
                "registered legacy saveable callback",
                "0.8.7b: IEvent + callback save data + DependencyResolver collections",
                () => CreateLegacyFinding(new LegacyTarget(
                    InfiniteGroundwaterTarget,
                    "InfiniteGroundwater",
                    InfiniteGroundwaterTypeName,
                    "onNewDay",
                    LegacyEventKind.CalendarNewDay)),
                () => ExecuteLegacyRepair(new LegacyTarget(
                    InfiniteGroundwaterTarget,
                    "InfiniteGroundwater",
                    InfiniteGroundwaterTypeName,
                    "onNewDay",
                    LegacyEventKind.CalendarNewDay)),
                () => CreateLegacyFinding(new LegacyTarget(
                    InfiniteGroundwaterTarget,
                    "InfiniteGroundwater",
                    InfiniteGroundwaterTypeName,
                    "onNewDay",
                    LegacyEventKind.CalendarNewDay))),
            new SaveRepairHandler(
                ShipAutoExploreTarget,
                "TajsCore.SaveRepair.LegacySaveCallbackMigration",
                "registered legacy saveable callback",
                "0.8.7b: IEvent + callback save data + DependencyResolver collections",
                () => CreateLegacyFinding(new LegacyTarget(
                    ShipAutoExploreTarget,
                    "ShipAutoExplore",
                    ShipAutoExploreTypeName,
                    "onSimUpdate",
                    LegacyEventKind.SimUpdate)),
                () => ExecuteLegacyRepair(new LegacyTarget(
                    ShipAutoExploreTarget,
                    "ShipAutoExplore",
                    ShipAutoExploreTypeName,
                    "onSimUpdate",
                    LegacyEventKind.SimUpdate)),
                () => CreateLegacyFinding(new LegacyTarget(
                    ShipAutoExploreTarget,
                    "ShipAutoExplore",
                    ShipAutoExploreTypeName,
                    "onSimUpdate",
                    LegacyEventKind.SimUpdate))),
            new SaveRepairHandler(
                WorldMapQuickTradesTarget,
                "TajsCore.SaveRepair.WorldMapQuickTrade",
                "impossible village quick-trade reputation",
                "0.8.7b: WorldMapVillageProto.QuickTrades + QuickTradePairProto.MinReputationRequired",
                InspectWorldMapQuickTrades,
                RepairWorldMapQuickTradesMutation,
                InspectWorldMapQuickTrades),
            new SaveRepairHandler(
                StaleTajsConfigTarget,
                "TajsCore.SaveRepair.StaleTajsConfig",
                "known stale Tajs ModJsonConfig",
                "0.8.7b: ModJsonConfig.ModId + Parameters",
                InspectStaleTajsConfig,
                RepairStaleTajsConfig,
                InspectStaleTajsConfig),
        });

        [ConsoleCommand(
            documentation: "Reports supported save-repair findings without mutating the loaded game.",
            customCommandName: "tajs_save_sanitize_report")]
        public string Report()
        {
            var builder = new StringBuilder(2048);
            builder.AppendLine("TajsCore save sanitizer report (dry-run; no changes made)");
            builder.AppendLine("Audited handlers:");
            foreach (SaveRepairHandler handler in m_handlers.Handlers)
            {
                SaveRepairFinding finding;
                try
                {
                    finding = handler.Detect();
                }
                catch (Exception exception)
                {
                    finding = new SaveRepairFinding(
                        handler.Id,
                        SaveRepairStatus.Unavailable,
                        0,
                        "detector failed closed (" + exception.GetType().Name + ")");
                }

                builder.AppendLine(
                    "  " + handler.Id + ": " + finding.Status +
                    "; owner=" + handler.Owner +
                    "; target=" + handler.TargetKind +
                    "; items=" + finding.ItemCount +
                    (finding.Detail.Length == 0 ? string.Empty : "; " + finding.Detail));
            }
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
                return "Usage: tajs_save_sanitize_repair <target> CONFIRM <new-save-name>";
            }

            string requestedTarget = targetId!;
            string requestedOutputSaveName = outputSaveName!;
            string normalizedTarget = NormalizeTarget(requestedTarget);
            if (!m_handlers.TryGet(normalizedTarget, out SaveRepairHandler? handler) || handler is null)
            {
                return "Unknown save-repair target. Run tajs_save_sanitize_report for supported targets.";
            }

            SaveRepairFinding before;
            try
            {
                before = handler.Detect();
            }
            catch (Exception exception)
            {
                return "Save-repair detector failed closed (" + exception.GetType().Name + "); no changes made.";
            }

            if (before.Status != SaveRepairStatus.NeedsRepair)
            {
                return FormatFinding(handler, before) + "; no repaired copy was queued.";
            }

            if (!TryPrepareNewSave(requestedOutputSaveName, out string outputPath, out string manifestPath, out string saveFailure))
            {
                return saveFailure;
            }

            SaveRepairMutation mutation;
            try
            {
                mutation = handler.Repair();
            }
            catch (Exception exception)
            {
                return "Save repair failed closed (" + exception.GetType().Name + "); no repaired copy was queued. Do not save this instance.";
            }

            if (!mutation.Succeeded)
            {
                return mutation.Failure + " Do not save this instance.";
            }

            SaveRepairFinding after;
            try
            {
                // Verification deliberately calls the handler's detector, not a second ad-hoc
                // predicate. Dry-run, mutation preflight, and postcondition therefore share the
                // exact same classifier.
                after = handler.Verify();
            }
            catch (Exception exception)
            {
                return "The repair verification failed closed (" + exception.GetType().Name + "); no repaired copy was queued. Do not save this instance.";
            }

            if (after.Status != SaveRepairStatus.Clean)
            {
                return "The repair did not reach a clean postcondition; no repaired copy was queued. Do not save this instance.";
            }

            if (!SaveRepairManifest.TryWriteNew(
                    manifestPath,
                    m_saveManager.GameName,
                    requestedOutputSaveName,
                    before,
                    after,
                    mutation.ChangedCount,
                    out string manifestFailure))
            {
                return "The repair reached a clean in-memory postcondition, but its manifest could not be stored (" +
                       manifestFailure + "); no repaired copy was queued. Do not save this instance.";
            }

            return QueueRepairedCopy(
                handler.Id,
                requestedOutputSaveName,
                outputPath,
                mutation.ChangedCount);
        }

        private SaveRepairFinding CreateLegacyFinding(LegacyTarget target)
        {
            LegacyInspection inspection = InspectLegacy(target);
            return new SaveRepairFinding(
                target.Id,
                ToSaveRepairStatus(inspection.Status),
                inspection.CallbackCount + inspection.ResolverCount,
                "callbacks=" + inspection.CallbackCount +
                "; resolver-owners=" + inspection.ResolverCount +
                "; scanned-events=" + inspection.EventCount +
                (inspection.Detail.Length == 0 ? string.Empty : "; " + inspection.Detail));
        }

        private SaveRepairMutation ExecuteLegacyRepair(LegacyTarget target)
        {
            if (!TryRepairLegacy(target, out string failure))
            {
                return SaveRepairMutation.Failed(failure);
            }

            return SaveRepairMutation.SucceededWith(1);
        }

        private static SaveRepairStatus ToSaveRepairStatus(RepairStatus status) => status switch
        {
            RepairStatus.NotLoaded => SaveRepairStatus.NotLoaded,
            RepairStatus.Clean => SaveRepairStatus.Clean,
            RepairStatus.NeedsRepair => SaveRepairStatus.NeedsRepair,
            RepairStatus.Unsupported => SaveRepairStatus.Unsupported,
            RepairStatus.Unavailable => SaveRepairStatus.Unavailable,
            _ => SaveRepairStatus.Unavailable,
        };

        private static string FormatFinding(SaveRepairHandler handler, SaveRepairFinding finding) =>
            handler.Id + ": " + finding.Status + "; items=" + finding.ItemCount +
            (finding.Detail.Length == 0 ? string.Empty : "; " + finding.Detail);

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
            if (!m_handlers.TryGet(targetId, out SaveRepairHandler? handler) || handler is null)
            {
                return "The requested legacy migration is not supported by this Core build.";
            }

            SaveRepairFinding inspection;
            try
            {
                inspection = handler.Detect();
            }
            catch (Exception exception)
            {
                return "Legacy migration detector failed closed (" + exception.GetType().Name + "); no migration was performed.";
            }

            if (inspection.Status == SaveRepairStatus.NotLoaded)
            {
                return FormatFinding(handler, inspection) + "; no migration was performed.";
            }
            if (inspection.Status == SaveRepairStatus.Clean)
            {
                return FormatFinding(handler, inspection) + ". Save a new copy, then disable the standalone mod.";
            }
            if (inspection.Status != SaveRepairStatus.NeedsRepair)
            {
                return FormatFinding(handler, inspection) + "; no migration was performed. Do not save this instance.";
            }

            SaveRepairMutation mutation;
            try
            {
                mutation = handler.Repair();
            }
            catch (Exception exception)
            {
                return "Legacy migration failed closed (" + exception.GetType().Name + "); no migration was performed. Do not save this instance.";
            }
            if (!mutation.Succeeded)
            {
                return mutation.Failure + " Do not save this instance.";
            }

            SaveRepairFinding after;
            try
            {
                after = handler.Verify();
            }
            catch (Exception exception)
            {
                return "Legacy migration verification failed closed (" + exception.GetType().Name + "); do not save this instance.";
            }
            if (after.Status != SaveRepairStatus.Clean)
            {
                return "Legacy migration did not reach a clean postcondition; do not save this instance.";
            }

            return "Legacy " + handler.Id.Replace('_', ' ') + " detached. Save a new copy now, then disable the standalone mod.";
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

                if (!LegacySaveCallbackMigration.RemoveResolverObjects(
                        m_resolver,
                        value => value.GetType() == legacyType ||
                                 callbackOwners.Any(owner => ReferenceEquals(owner, value)),
                        out _,
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

        private void AppendPrototypeInventory(StringBuilder builder)
        {
            builder.AppendLine("Prototype inventory:");
            builder.AppendLine("  phantom-prototypes=" + m_protos.Phantoms.Count + " (report-only)");
            builder.AppendLine("  missing-reference scan=not enabled; unknown references remain untouched");
        }

        private SaveRepairFinding InspectWorldMapQuickTrades()
        {
            if (!TryCollectImpossibleQuickTrades(
                    out List<WorldMapQuickTradeCandidate> invalid,
                    out int villageCount,
                    out string failure))
            {
                return new SaveRepairFinding(
                    WorldMapQuickTradesTarget,
                    SaveRepairStatus.Unavailable,
                    0,
                    failure);
            }

            string detail = "villages-scanned=" + villageCount;
            if (invalid.Count != 0)
            {
                WorldMapQuickTradeCandidate first = invalid[0];
                detail += "; first=" + first.Village.Id + "/" + first.Trade.Id +
                          "; min-reputation=" + first.Trade.MinReputationRequired +
                          "; max-reputation=" + first.Village.MaxReputation +
                          "; confidence=type-specific";
            }

            return new SaveRepairFinding(
                WorldMapQuickTradesTarget,
                invalid.Count == 0 ? SaveRepairStatus.Clean : SaveRepairStatus.NeedsRepair,
                invalid.Count,
                detail);
        }

        private SaveRepairMutation RepairWorldMapQuickTradesMutation()
        {
            if (!TryCollectImpossibleQuickTrades(
                    out List<WorldMapQuickTradeCandidate> invalid,
                    out _,
                    out string failure))
            {
                return SaveRepairMutation.Failed("World-map quick-trade validation failed (" + failure + "); no repaired copy was queued.");
            }

            if (invalid.Count == 0)
            {
                return SaveRepairMutation.Failed("World-map quick trades are clean; no repaired copy was queued.");
            }

            FieldInfo? minReputationField = typeof(QuickTradePairProto).GetField(
                nameof(QuickTradePairProto.MinReputationRequired),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (minReputationField is null || minReputationField.FieldType != typeof(int) || !minReputationField.IsInitOnly)
            {
                return SaveRepairMutation.Failed(
                    "World-map quick-trade repair refused because the audited immutable field shape changed; no repaired copy was queued.");
            }

            try
            {
                foreach (WorldMapQuickTradeCandidate item in invalid)
                {
                    minReputationField.SetValue(item.Trade, item.Village.MaxReputation);
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "World-map quick-trade repair failed; no safe save was produced.");
                return SaveRepairMutation.Failed(
                    "World-map quick-trade repair failed (" + exception.GetType().Name + "); no repaired copy was queued.");
            }

            m_log.Info("Clamped " + invalid.Count + " impossible world-map quick-trade reputation requirement(s).");
            return SaveRepairMutation.SucceededWith(invalid.Count);
        }

        private SaveRepairFinding InspectStaleTajsConfig()
        {
            if (!TryFindStaleTajsConfigs(out List<object> configs, out bool hasUnknownShape, out string failure))
            {
                return new SaveRepairFinding(
                    StaleTajsConfigTarget,
                    SaveRepairStatus.Unavailable,
                    0,
                    failure);
            }

            if (hasUnknownShape)
            {
                return new SaveRepairFinding(
                    StaleTajsConfigTarget,
                    SaveRepairStatus.Unsupported,
                    configs.Count,
                    "a TajsTweaks ModJsonConfig contains an unknown parameter; no config values will be changed");
            }

            return new SaveRepairFinding(
                StaleTajsConfigTarget,
                configs.Count == 0 ? SaveRepairStatus.Clean : SaveRepairStatus.NeedsRepair,
                configs.Count,
                configs.Count == 0
                    ? "no allow-listed legacy ModJsonConfig values found"
                    : "allow-listed legacy ModJsonConfig values found; unknown config values remain untouched");
        }

        private SaveRepairMutation RepairStaleTajsConfig()
        {
            if (!TryFindStaleTajsConfigs(out List<object> configs, out bool hasUnknownShape, out string failure))
            {
                return SaveRepairMutation.Failed("Stale Tajs config inspection failed closed (" + failure + "); no changes made.");
            }
            if (hasUnknownShape)
            {
                return SaveRepairMutation.Failed("Stale Tajs config contains an unknown parameter; no changes made.");
            }
            if (configs.Count == 0)
            {
                return SaveRepairMutation.Failed("No known stale Tajs config was found; no changes made.");
            }

            if (!LegacySaveCallbackMigration.RemoveResolverObjects(
                    m_resolver,
                    IsKnownStaleTajsConfig,
                    out int removedCount,
                    out failure))
            {
                return SaveRepairMutation.Failed("Stale Tajs config cleanup was refused (" + failure + "); no safe save was produced.");
            }

            if (!TryFindStaleTajsConfigs(out List<object> remaining, out bool hasUnknownRemaining, out failure))
            {
                return SaveRepairMutation.Failed("Stale Tajs config verification failed closed (" + failure + "); no repaired copy was queued.");
            }
            if (hasUnknownRemaining || remaining.Count != 0)
            {
                return SaveRepairMutation.Failed("Known stale Tajs config remained in the resolver; no repaired copy was queued.");
            }

            m_log.Info("Removed " + configs.Count + " known stale Tajs ModJsonConfig instance(s).");
            return SaveRepairMutation.SucceededWith(Math.Max(1, Math.Min(configs.Count, removedCount)));
        }

        private bool TryFindStaleTajsConfigs(out List<object> configs, out bool hasUnknownShape, out string failure)
        {
            hasUnknownShape = false;
            if (!LegacySaveCallbackMigration.TryFindResolverObjects(
                    m_resolver,
                    IsTajsTweaksConfig,
                    out List<object> allConfigs,
                    out failure))
            {
                configs = new List<object>();
                return false;
            }

            configs = new List<object>();
            foreach (object config in allConfigs)
            {
                if (IsKnownStaleTajsConfig(config))
                {
                    configs.Add(config);
                }
                else if (config is ModJsonConfig modConfig && modConfig.GetSavedValues().Count != 0)
                {
                    hasUnknownShape = true;
                }
            }

            return true;
        }

        private static bool IsTajsTweaksConfig(object value) =>
            value is ModJsonConfig config &&
            string.Equals(config.ModId, "TajsTweaks", StringComparison.Ordinal);

        private static bool IsKnownStaleTajsConfig(object value)
        {
            if (value is not ModJsonConfig config ||
                !string.Equals(config.ModId, "TajsTweaks", StringComparison.Ordinal))
            {
                return false;
            }

            // TajsTweaks used to serialize this one setting through ModJsonConfig. During load
            // without a current config.json, 0.8.7b keeps those values in GetSavedValues() while
            // Parameters is empty. Inspect the saved values, not only the schema definitions.
            // Remove only this exact legacy shape; unknown/future values fail closed.
            var values = config.GetSavedValues();
            if (values.Count != 1)
            {
                return false;
            }

            foreach (KeyValuePair<string, object> entry in values)
            {
                // The historical config.json declared this as an integer. A matching key with a
                // different value type is an unknown shape and must remain untouched.
                if (!string.Equals(entry.Key, "unlocked_speed_max", StringComparison.Ordinal) ||
                    !(entry.Value is int))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryCollectImpossibleQuickTrades(
            out List<WorldMapQuickTradeCandidate> invalid,
            out int villageCount,
            out string failure)
        {
            invalid = new List<WorldMapQuickTradeCandidate>();
            villageCount = 0;
            failure = string.Empty;
            try
            {
                foreach (WorldMapVillageProto? village in m_protos.All<WorldMapVillageProto>())
                {
                    if (village is null || village.QuickTrades.IsNotValid)
                    {
                        failure = "a village or its quick-trade collection had an unsupported shape";
                        return false;
                    }

                    villageCount++;
                    foreach (QuickTradePairProto? trade in village.QuickTrades)
                    {
                        if (trade is null)
                        {
                            failure = "a quick-trade collection contained an unknown entry";
                            return false;
                        }

                        if (trade.MinReputationRequired > village.MaxReputation)
                        {
                            invalid.Add(new WorldMapQuickTradeCandidate(village, trade));
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name;
                return false;
            }
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

        private bool TryPrepareNewSave(
            string outputSaveName,
            out string outputPath,
            out string manifestPath,
            out string failure)
        {
            outputPath = string.Empty;
            manifestPath = string.Empty;
            failure = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(outputSaveName) ||
                    outputSaveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    failure = "The requested output save name is invalid.";
                    return false;
                }

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

                manifestPath = outputPath + ".tajs-repair.txt";
                if (File.Exists(manifestPath))
                {
                    failure = "Refusing to overwrite an existing repair manifest. Choose a new save name.";
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

        private static MethodInfo? FindCallbackMethod(Type legacyType, string callbackName)
        {
            MethodInfo? callback = legacyType.GetMethod(
                callbackName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            return callback is not null && !callback.IsStatic && callback.ReturnType == typeof(void)
                ? callback
                : null;
        }

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
            "tajs_config" => StaleTajsConfigTarget,
            "stale-tajs-config" => StaleTajsConfigTarget,
            "stale_config" => StaleTajsConfigTarget,
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

        private sealed class WorldMapQuickTradeCandidate
        {
            public WorldMapQuickTradeCandidate(WorldMapVillageProto village, QuickTradePairProto trade)
            {
                Village = village;
                Trade = trade;
            }

            public WorldMapVillageProto Village { get; }
            public QuickTradePairProto Trade { get; }
        }
    }
}
