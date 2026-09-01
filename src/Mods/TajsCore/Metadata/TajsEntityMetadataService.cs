// Taj's COI Mods | TajsEntityMetadataService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Console;
using Mafi.Core.Entities;
using Mafi.Core.Game;
using Mafi.Core.GameLoop;
using Mafi.Core.SaveGame;
using TajsCOI.Common.Diagnostics;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Metadata;
using TajsCOI.Common.Persistence;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Core.Metadata
{
    [GlobalDependency(RegistrationMode.AsEverything)]
    internal sealed class TajsEntityMetadataService : IEntityMetadataService, IDisposable
    {
        internal const string ComponentId = "EntityMetadata";
        private readonly object m_gate = new();
        private readonly List<PendingRemoval> m_pendingRemovals = new();
        private readonly ISaveManager m_saveManager;
        private readonly ITajsLogger m_log;
        private readonly EntityMetadataStateStore m_store;
        private readonly bool m_lifecycleAttached;
        private bool m_disposed;

        // Test-only construction keeps mutation/rollback coverage independent from the native
        // gameplay resolver. Runtime instances always use the lifecycle constructor below.
        internal TajsEntityMetadataService(EntityMetadataStateStore store)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_saveManager = null!;
            m_log = null!;
            m_lifecycleAttached = false;
        }

        public TajsEntityMetadataService(
            DependencyResolver resolver,
            IEntitiesManager entitiesManager,
            ISaveManager saveManager,
            IGameLoopEvents gameLoop,
            ITajsRuntime runtime)
        {
            if (entitiesManager is null)
            {
                throw new ArgumentNullException(nameof(entitiesManager));
            }
            m_saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            m_log = runtime.GetLogger("TajsCore", ComponentId);
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsCOI",
                "EntityMetadata");
            m_store = new EntityMetadataStateStore(root);
            TajsSaveIdentity? identity = TryGetLoadedIdentity(resolver, saveManager.GameName);
            m_store.LoadIdentity(identity);
            m_saveManager.OnSaveDone += OnSaveDone;
            entitiesManager.EntityRemovedFull.AddNonSaveable(this, OnEntityRemoved);
            gameLoop.SyncUpdate.AddNonSaveable(this, OnSyncUpdate);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);
            m_lifecycleAttached = true;
            runtime.RegisterComponent(
                new RuntimeComponentDescriptor(
                    "TajsCore",
                    ComponentId,
                    RuntimeComponentLifetime.GameplayScene,
                    "ISaveManager.OnSaveDone and optional per-save metadata sidecar",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()));
        }

        public IReadOnlyList<EntityMetadataRecord> GetEntityMetadataSnapshot()
        {
            lock (m_gate)
            {
                return m_store.Entities.Values
                    .OrderBy(record => record.Identity.EntityId)
                    .ThenBy(record => record.Identity.PrototypeFingerprint, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        public IReadOnlyList<EntityMetadataGroup> GetGroupSnapshot()
        {
            lock (m_gate)
            {
                return m_store.Groups.Values
                    .OrderBy(group => group.Order)
                    .ThenBy(group => group.GroupId, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        public bool TryGetEntityMetadata(EntityMetadataIdentity identity, out EntityMetadataRecord? metadata)
        {
            lock (m_gate)
            {
                return m_store.Entities.TryGetValue(identity, out metadata);
            }
        }

        public bool TryGetGroup(string groupId, out EntityMetadataGroup? group)
        {
            group = null;
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }
            lock (m_gate)
            {
                return m_store.Groups.TryGetValue(groupId.Trim(), out group);
            }
        }

        public IReadOnlyList<EntityMetadataRecord> ResolveLiveMetadata(IEnumerable<EntityMetadataIdentity> liveEntities)
        {
            if (liveEntities is null)
            {
                throw new ArgumentNullException(nameof(liveEntities));
            }
            lock (m_gate)
            {
                return liveEntities
                    .Distinct()
                    .Where(identity => m_store.Entities.ContainsKey(identity))
                    .Select(identity => m_store.Entities[identity])
                    .ToArray();
            }
        }

        public bool TrySetEntityMetadata(
            EntityMetadataIdentity identity,
            string? alias,
            string? note,
            string? groupId,
            out string error)
        {
            error = string.Empty;
            string? normalizedGroup = string.IsNullOrWhiteSpace(groupId) ? null : groupId!.Trim();
            lock (m_gate)
            {
                if (normalizedGroup is not null && !m_store.Groups.ContainsKey(normalizedGroup))
                {
                    error = "The requested metadata group does not exist.";
                    return false;
                }

                bool hadPrevious = m_store.Entities.TryGetValue(identity, out EntityMetadataRecord? previous);
                var record = new EntityMetadataRecord(identity, alias, note, normalizedGroup);
                if (!record.HasDisplayMetadata)
                {
                    m_store.RemoveEntity(identity);
                }
                else
                {
                    m_store.SetEntity(record);
                }

                if (PersistLocked(out error))
                {
                    return true;
                }

                if (hadPrevious)
                {
                    m_store.SetEntity(previous!);
                }
                else
                {
                    m_store.RemoveEntity(identity);
                }
                return false;
            }
        }

        public bool TryClearEntityMetadata(EntityMetadataIdentity identity)
        {
            lock (m_gate)
            {
                if (!m_store.Entities.TryGetValue(identity, out EntityMetadataRecord? previous))
                {
                    return true;
                }

                m_store.RemoveEntity(identity);
                if (PersistIfBound())
                {
                    return true;
                }

                m_store.SetEntity(previous);
                return false;
            }
        }

        public bool TryCreateGroup(string? name, string? color, out EntityMetadataGroup? group, out string error)
        {
            group = null;
            error = string.Empty;
            lock (m_gate)
            {
                try
                {
                    int order = m_store.Groups.Values.Select(item => item.Order).DefaultIfEmpty(-1).Max() + 1;
                    group = new EntityMetadataGroup(
                        Guid.NewGuid().ToString("N"),
                        string.IsNullOrWhiteSpace(name) ? "Group " + (order + 1) : name!.Trim(),
                        order,
                        string.IsNullOrWhiteSpace(color) ? "#66C2A5" : color!.Trim(),
                        false);
                    m_store.SetGroup(group);
                    if (!PersistLocked(out error))
                    {
                        m_store.RemoveGroup(group.GroupId);
                        group = null;
                        return false;
                    }
                    return true;
                }
                catch (ArgumentException exception)
                {
                    error = exception.Message;
                    return false;
                }
            }
        }

        public bool TryUpdateGroup(string groupId, string? name, int order, string? color, bool locked, out string error)
        {
            error = string.Empty;
            lock (m_gate)
            {
                string normalizedGroupId = groupId?.Trim() ?? string.Empty;
                if (!m_store.Groups.TryGetValue(normalizedGroupId, out EntityMetadataGroup? existing))
                {
                    error = "The requested metadata group does not exist.";
                    return false;
                }
                if (existing.Locked && (name is not null || color is not null || order != existing.Order))
                {
                    error = "The metadata group is locked.";
                    return false;
                }
                try
                {
                    EntityMetadataGroup updated = existing.With(
                        string.IsNullOrWhiteSpace(name) ? existing.Name : name!.Trim(),
                        order,
                        string.IsNullOrWhiteSpace(color) ? existing.Color : color!.Trim(),
                        locked);
                    m_store.SetGroup(updated);
                    if (PersistLocked(out error))
                    {
                        return true;
                    }

                    m_store.SetGroup(existing);
                    return false;
                }
                catch (ArgumentException exception)
                {
                    error = exception.Message;
                    return false;
                }
            }
        }

        public bool TryDeleteGroup(string groupId)
        {
            lock (m_gate)
            {
                string normalized = groupId?.Trim() ?? string.Empty;
                if (!m_store.Groups.TryGetValue(normalized, out EntityMetadataGroup? group) || group.Locked)
                {
                    return false;
                }
                EntityMetadataRecord[] members = m_store.Entities.Values
                    .Where(record => string.Equals(record.GroupId, normalized, StringComparison.Ordinal))
                    .ToArray();
                m_store.RemoveGroup(normalized);
                foreach (EntityMetadataRecord record in members)
                {
                    m_store.SetEntity(record.With(record.Alias, record.Note, null));
                }

                if (PersistIfBound())
                {
                    return true;
                }

                m_store.SetGroup(group);
                foreach (EntityMetadataRecord record in members)
                {
                    m_store.SetEntity(record);
                }
                return false;
            }
        }

        public int PruneConfirmed(IEnumerable<EntityMetadataIdentity> confirmedDestroyed)
        {
            if (confirmedDestroyed is null)
            {
                throw new ArgumentNullException(nameof(confirmedDestroyed));
            }
            lock (m_gate)
            {
                int removed = 0;
                foreach (EntityMetadataIdentity identity in confirmedDestroyed.Distinct())
                {
                    if (m_store.RemoveEntity(identity))
                    {
                        removed++;
                    }
                }
                if (removed > 0 && !PersistIfBound())
                {
                    m_log.Warning("Entity metadata was pruned in memory but the optional sidecar could not be written.");
                }
                return removed;
            }
        }

        [ConsoleCommand(
            documentation: "Lists optional per-save entity aliases, notes, and groups.",
            customCommandName: "tajs_metadata_list")]
        public string ListMetadata(string? entityId = "")
        {
            int? requestedId = int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null;
            IReadOnlyList<EntityMetadataRecord> records = GetEntityMetadataSnapshot()
                .Where(record => !requestedId.HasValue || record.Identity.EntityId == requestedId.Value)
                .ToArray();
            IReadOnlyList<EntityMetadataGroup> groups = GetGroupSnapshot();
            string groupText = groups.Count == 0
                ? "none"
                : string.Join(", ", groups.Select(group => group.GroupId + "=" + group.Name + (group.Locked ? "[locked]" : string.Empty)));
            string recordText = records.Count == 0
                ? "none"
                : string.Join(
                    " | ",
                    records.Select(record => record.Identity.EntityId + "@" + record.Identity.PrototypeFingerprint +
                                             " alias=\"" + record.Alias + "\" note=\"" + record.Note + "\" group=" +
                                             (record.GroupId ?? "none")));
            return "Entity metadata: groups=" + groupText + "; records=" + recordText + ".";
        }

        [ConsoleCommand(
            documentation: "Sets an optional entity alias, note, and group using its numeric ID plus prototype fingerprint.",
            customCommandName: "tajs_metadata_set")]
        public string SetMetadata(string entityId, string prototypeFingerprint, string alias, string note, string? groupId = "")
        {
            if (!TryParseIdentity(entityId, prototypeFingerprint, out EntityMetadataIdentity identity))
            {
                return "Usage: tajs_metadata_set <entity-id> <prototype-fingerprint> <alias> <note> [group-id]";
            }
            try
            {
                return TrySetEntityMetadata(identity, alias, note, groupId, out string error)
                    ? "Entity metadata saved for " + identity + "."
                    : "Entity metadata was not saved: " + error;
            }
            catch (ArgumentException exception)
            {
                return "Entity metadata was not saved: " + exception.Message;
            }
        }

        [ConsoleCommand(
            documentation: "Clears an entity's alias, note, and group metadata without changing the entity.",
            customCommandName: "tajs_metadata_clear")]
        public string ClearMetadata(string entityId, string prototypeFingerprint)
        {
            if (!TryParseIdentity(entityId, prototypeFingerprint, out EntityMetadataIdentity identity))
            {
                return "Usage: tajs_metadata_clear <entity-id> <prototype-fingerprint>";
            }

            return TryClearEntityMetadata(identity)
                ? "Entity metadata cleared for " + identity + "."
                : "Entity metadata could not be cleared for " + identity + ".";
        }

        [ConsoleCommand(
            documentation: "Removes an entity from its group while retaining its alias and note.",
            customCommandName: "tajs_metadata_ungroup")]
        public string UngroupMetadata(string entityId, string prototypeFingerprint)
        {
            if (!TryParseIdentity(entityId, prototypeFingerprint, out EntityMetadataIdentity identity))
            {
                return "Usage: tajs_metadata_ungroup <entity-id> <prototype-fingerprint>";
            }

            if (!TryGetEntityMetadata(identity, out EntityMetadataRecord? metadata) || metadata is null)
            {
                return "No entity metadata exists for " + identity + ".";
            }

            return TrySetEntityMetadata(identity, metadata.Alias, metadata.Note, null, out string error)
                ? "Entity " + identity + " was removed from its metadata group."
                : "Entity metadata was not ungrouped: " + error;
        }

        [ConsoleCommand(
            documentation: "Creates a save-scoped entity metadata group.",
            customCommandName: "tajs_metadata_group_create")]
        public string CreateMetadataGroup(string? name = null, string? color = null)
        {
            return TryCreateGroup(name, color, out EntityMetadataGroup? group, out string error)
                ? "Created metadata group " + group!.GroupId + " ('" + group.Name + "')."
                : "Metadata group was not created: " + error;
        }

        [ConsoleCommand(
            documentation: "Updates a save-scoped entity metadata group.",
            customCommandName: "tajs_metadata_group_update")]
        public string UpdateMetadataGroup(string groupId, string order, string locked, string? name = null, string? color = null)
        {
            if (!int.TryParse(order, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedOrder) ||
                parsedOrder < 0 || !bool.TryParse(locked, out bool parsedLocked))
            {
                return "Usage: tajs_metadata_group_update <group-id> <order> <true|false> [name] [color]";
            }

            return TryUpdateGroup(groupId, name, parsedOrder, color, parsedLocked, out string error)
                ? "Metadata group " + groupId + " updated."
                : "Metadata group was not updated: " + error;
        }

        [ConsoleCommand(
            documentation: "Deletes an unlocked save-scoped entity metadata group and clears its membership.",
            customCommandName: "tajs_metadata_group_delete")]
        public string DeleteMetadataGroup(string groupId)
        {
            return TryDeleteGroup(groupId)
                ? "Metadata group " + groupId + " deleted."
                : "Metadata group is missing, locked, or could not be persisted.";
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            if (m_lifecycleAttached)
            {
                m_saveManager.OnSaveDone -= OnSaveDone;
            }
            lock (m_gate)
            {
                m_pendingRemovals.Clear();
                if (m_lifecycleAttached)
                {
                    m_store.Save();
                }
                m_store.Unbind();
            }
        }

        private void OnTerminate() => Dispose();

        private void OnEntityRemoved(IEntity entity, EntityRemoveReason _)
        {
            if (m_disposed || entity is null)
            {
                return;
            }

            if (!TryCreateEntityIdentity(entity, out EntityMetadataIdentity identity))
            {
                return;
            }

            bool pruneImmediately = false;
            lock (m_gate)
            {
                if (entity.IsDestroyed)
                {
                    pruneImmediately = true;
                }
                else
                {
                    // EntityRemovedFull is raised before the native destroy callback. Defer the
                    // decision until sync, and discard non-destroy removals after two observations.
                    // This prevents partial/reload queries from deleting metadata accidentally.
                    m_pendingRemovals.Add(new PendingRemoval(entity, identity));
                }
            }

            if (pruneImmediately)
            {
                PruneConfirmed(new[] { identity });
            }
        }

        private void OnSyncUpdate(GameTime _)
        {
            if (m_disposed)
            {
                return;
            }

            List<EntityMetadataIdentity>? confirmed = null;
            lock (m_gate)
            {
                for (int i = m_pendingRemovals.Count - 1; i >= 0; i--)
                {
                    PendingRemoval pending = m_pendingRemovals[i];
                    if (pending.Entity.IsDestroyed)
                    {
                        (confirmed ??= new List<EntityMetadataIdentity>()).Add(pending.Identity);
                        m_pendingRemovals.RemoveAt(i);
                    }
                    else if (++pending.Observations >= 2)
                    {
                        m_pendingRemovals.RemoveAt(i);
                    }
                }
            }

            if (confirmed is not null)
            {
                PruneConfirmed(confirmed);
            }
        }

        private static bool TryCreateEntityIdentity(IEntity entity, out EntityMetadataIdentity identity)
        {
            try
            {
                identity = new EntityMetadataIdentity(entity.Id.Value, "proto:" + entity.Prototype.Id.Value);
                return true;
            }
            catch
            {
                identity = default;
                return false;
            }
        }

        private sealed class PendingRemoval
        {
            internal PendingRemoval(IEntity entity, EntityMetadataIdentity identity)
            {
                Entity = entity;
                Identity = identity;
            }

            internal IEntity Entity { get; }
            internal EntityMetadataIdentity Identity { get; }
            internal int Observations { get; set; }
        }

        private void OnSaveDone(SaveResult result)
        {
            if (result.FilePath.ValueOrNull is not string path)
            {
                return;
            }

            if (TajsSaveIdentity.IsAutosavePath(path))
            {
                return;
            }

            TajsSaveIdentity? identity = TajsSaveIdentity.FromFile(path, m_saveManager.GameName);
            if (identity is null)
            {
                m_log.Warning("Entity metadata sidecar was not rebound because the saved file identity was unavailable.");
                return;
            }

            lock (m_gate)
            {
                if (!m_store.RebindIdentity(identity) || !m_store.Save())
                {
                    m_log.Warning("Entity metadata sidecar could not be written after the native save completed.");
                }
            }
        }

        private bool PersistLocked(out string error)
        {
            if (PersistIfBound())
            {
                error = string.Empty;
                return true;
            }
            error = "Entity metadata is active in memory, but its optional sidecar could not be written.";
            return false;
        }

        private bool PersistIfBound() => !m_store.IsBound || m_store.Save();

        private static bool TryParseIdentity(
            string entityId,
            string prototypeFingerprint,
            out EntityMetadataIdentity identity)
        {
            if (!int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId) || parsedId < 0 ||
                string.IsNullOrWhiteSpace(prototypeFingerprint))
            {
                identity = default;
                return false;
            }

            identity = new EntityMetadataIdentity(parsedId, prototypeFingerprint);
            return true;
        }

        private static TajsSaveIdentity? TryGetLoadedIdentity(DependencyResolver resolver, string gameName)
        {
            try
            {
                if (!resolver.TryResolve(out GameNameConfig? config) || config is null || config.LoadedFile is not SaveFileInfo file ||
                    !resolver.TryResolve(out IFileSystemHelper? fileSystem) || fileSystem is null)
                {
                    return null;
                }

                string path = fileSystem.GetSaveFilePath(file);
                return TajsSaveIdentity.FromFile(path, file.GameName ?? gameName, file.NameNoExtension);
            }
            catch
            {
                return null;
            }
        }

    }
}
