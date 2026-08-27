// Taj's COI Mods | TajsEntityMetadataService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mafi;
using Mafi.Core;
using Mafi.Core.Game;
using Mafi.Core.GameLoop;
using Mafi.Core.SaveGame;
using Mafi.Core.Console;
using TajsCOI.Common.Metadata;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Diagnostics;
using System.Globalization;

namespace TajsCOI.Core.Metadata
{
    [GlobalDependency(RegistrationMode.AsEverything)]
    internal sealed class TajsEntityMetadataService : IEntityMetadataService, IDisposable
    {
        internal const string ComponentId = "EntityMetadata";
        private readonly object m_gate = new();
        private readonly ISaveManager m_saveManager;
        private readonly ITajsLogger m_log;
        private readonly EntityMetadataStateStore m_store;
        private bool m_disposed;

        public TajsEntityMetadataService(
            DependencyResolver resolver,
            ISaveManager saveManager,
            IGameLoopEvents gameLoop,
            ITajsRuntime runtime)
        {
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
            string? identity = TryGetLoadedIdentity(resolver, saveManager.GameName);
            m_store.Load(identity);
            m_saveManager.OnSaveDone += OnSaveDone;
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);
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

                var record = new EntityMetadataRecord(identity, alias, note, normalizedGroup);
                if (!record.HasDisplayMetadata)
                {
                    m_store.RemoveEntity(identity);
                }
                else
                {
                    m_store.SetEntity(record);
                }
                return PersistLocked(out error);
            }
        }

        public bool TryClearEntityMetadata(EntityMetadataIdentity identity)
        {
            lock (m_gate)
            {
                bool changed = m_store.RemoveEntity(identity);
                return !changed || PersistIfBound();
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
                    group = new EntityMetadataGroup(Guid.NewGuid().ToString("N"),
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
                if (!m_store.Groups.TryGetValue(groupId.Trim(), out EntityMetadataGroup? existing))
                {
                    error = "The requested metadata group does not exist.";
                    return false;
                }
                if (existing.Locked && (name is not null || color is not null || locked != existing.Locked))
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
                    return PersistLocked(out error);
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
                m_store.RemoveGroup(normalized);
                foreach (EntityMetadataRecord record in m_store.Entities.Values
                             .Where(record => string.Equals(record.GroupId, normalized, StringComparison.Ordinal)).ToArray())
                {
                    m_store.SetEntity(record.With(record.Alias, record.Note, null));
                }
                return PersistIfBound();
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
            if (!int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId) || parsedId < 0 ||
                string.IsNullOrWhiteSpace(prototypeFingerprint))
            {
                return "Usage: tajs_metadata_set <entity-id> <prototype-fingerprint> <alias> <note> [group-id]";
            }
            try
            {
                var identity = new EntityMetadataIdentity(parsedId, prototypeFingerprint);
                return TrySetEntityMetadata(identity, alias, note, groupId, out string error)
                    ? "Entity metadata saved for " + identity + "."
                    : "Entity metadata was not saved: " + error;
            }
            catch (ArgumentException exception)
            {
                return "Entity metadata was not saved: " + exception.Message;
            }
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_saveManager.OnSaveDone -= OnSaveDone;
            lock (m_gate)
            {
                m_store.Save();
                m_store.Unbind();
            }
        }

        private void OnTerminate() => Dispose();

        private void OnSaveDone(SaveResult result)
        {
            if (result.FilePath.ValueOrNull is not string path)
            {
                return;
            }

            string? identity = CreateSaveIdentity(path, m_saveManager.GameName);
            if (identity is null)
            {
                m_log.Warning("Entity metadata sidecar was not rebound because the saved file identity was unavailable.");
                return;
            }

            lock (m_gate)
            {
                if (!m_store.Rebind(identity) || !m_store.Save())
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

        private static string? TryGetLoadedIdentity(DependencyResolver resolver, string gameName)
        {
            try
            {
                return resolver.TryResolve(out GameNameConfig? config) && config is not null && config.LoadedFile is SaveFileInfo file
                    ? CreateSaveIdentity(file, gameName)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? CreateSaveIdentity(string path, string gameName)
        {
            try
            {
                FileInfo file = new(path);
                return file.Exists
                    ? CreateSaveIdentity(
                        new SaveFileInfo(
                            Path.GetFileNameWithoutExtension(file.Name),
                            gameName,
                            file.LastWriteTimeUtc,
                            file.Length,
                            file.Extension),
                        gameName)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string CreateSaveIdentity(SaveFileInfo file, string gameName)
        {
            string canonical = string.Join(
                "\n",
                file.GameName ?? gameName,
                file.NameNoExtension ?? string.Empty,
                file.Extension ?? string.Empty,
                file.WriteTimestamp.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                file.SizeBytes.ToString(CultureInfo.InvariantCulture));
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes)
                {
                    builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }
    }
}
