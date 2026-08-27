// Taj's COI Mods | HeightLayerFilter.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Ports;
using Mafi.Core.Ports.Io;
using Mafi.Unity.Entities;
using Mafi.Unity.Ports.Io;
using UnityEngine;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    /// Scene-local renderer binding. All three presentation surfaces are changed
    /// together so hidden entities cannot remain selectable through a port or a
    /// stale physics hit target. The optional readers preserve pre-existing native
    /// visibility when ShowAll or scene teardown restores the binding.
    /// </summary>
    internal sealed class HeightLayerRenderBinding
    {
        private readonly Action<bool>? m_setRendererVisible;
        private readonly Action<bool>? m_setPortsVisible;
        private readonly Action<bool>? m_setHitTestingEnabled;
        private readonly bool m_rendererWasVisible;
        private readonly bool m_portsWereVisible;
        private readonly bool m_hitTestingWasEnabled;

        internal HeightLayerRenderBinding(
            Action<bool>? setRendererVisible = null,
            Action<bool>? setPortsVisible = null,
            Action<bool>? setHitTestingEnabled = null,
            Func<bool>? readRendererVisible = null,
            Func<bool>? readPortsVisible = null,
            Func<bool>? readHitTestingEnabled = null)
        {
            m_setRendererVisible = setRendererVisible;
            m_setPortsVisible = setPortsVisible;
            m_setHitTestingEnabled = setHitTestingEnabled;
            m_rendererWasVisible = readRendererVisible?.Invoke() ?? true;
            m_portsWereVisible = readPortsVisible?.Invoke() ?? true;
            m_hitTestingWasEnabled = readHitTestingEnabled?.Invoke() ?? true;
        }

        internal void Apply(bool visible)
        {
            m_setRendererVisible?.Invoke(visible && m_rendererWasVisible);
            m_setPortsVisible?.Invoke(visible && m_portsWereVisible);
            m_setHitTestingEnabled?.Invoke(visible && m_hitTestingWasEnabled);
        }

        internal void Restore()
        {
            m_setRendererVisible?.Invoke(m_rendererWasVisible);
            m_setPortsVisible?.Invoke(m_portsWereVisible);
            m_setHitTestingEnabled?.Invoke(m_hitTestingWasEnabled);
        }
    }

    /// <summary>
    /// Immutable scene-index row. It intentionally stores only the id and
    /// presentation metadata, never a simulation entity reference.
    /// </summary>
    internal sealed class HeightLayerEntityRecord
    {
        internal int EntityId { get; }
        internal int MinHeight { get; }
        internal int MaxHeight { get; }
        internal string Category { get; }
        internal HeightLayerRenderBinding? RendererBinding { get; }

        internal HeightLayerEntityRecord(
            int entityId,
            int minHeight,
            int maxHeight,
            string? category,
            HeightLayerRenderBinding? rendererBinding)
        {
            if (maxHeight < minHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHeight), "Maximum height must not be below minimum height.");
            }

            EntityId = entityId;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
            Category = string.IsNullOrWhiteSpace(category) ? "unknown" : category!;
            RendererBinding = rendererBinding;
        }

        internal bool Intersects(int cutoff) => MinHeight <= cutoff && cutoff <= MaxHeight;
    }

    internal readonly struct HeightLayerVisibilityChange
    {
        internal int EntityId { get; }
        internal bool Visible { get; }

        internal HeightLayerVisibilityChange(int entityId, bool visible)
        {
            EntityId = entityId;
            Visible = visible;
        }
    }

    /// <summary>
    /// The reusable category/render policy adapter. Other presentation filters can
    /// register the same binding and call Apply without knowing how a renderer or
    /// instanced ports are represented by the game.
    /// </summary>
    internal sealed class HeightLayerPresentationAdapter : IDisposable
    {
        private readonly Dictionary<int, HeightLayerRenderBinding> m_bindings = new();

        internal void Bind(int entityId, HeightLayerRenderBinding binding)
        {
            if (m_bindings.TryGetValue(entityId, out HeightLayerRenderBinding? previous))
            {
                previous.Restore();
            }
            m_bindings[entityId] = binding;
        }

        internal void Unbind(int entityId)
        {
            if (m_bindings.TryGetValue(entityId, out HeightLayerRenderBinding? binding))
            {
                m_bindings.Remove(entityId);
                binding.Restore();
            }
        }

        internal bool Contains(int entityId) => m_bindings.ContainsKey(entityId);

        internal void Apply(int entityId, bool visible)
        {
            if (m_bindings.TryGetValue(entityId, out HeightLayerRenderBinding? binding))
            {
                binding.Apply(visible);
            }
        }

        internal void RestoreAll()
        {
            foreach (HeightLayerRenderBinding binding in m_bindings.Values)
            {
                binding.Restore();
            }
        }

        public void Dispose()
        {
            RestoreAll();
            m_bindings.Clear();
        }
    }

    /// <summary>
    /// EntityId -> vertical range index for the active gameplay scene.
    /// </summary>
    internal sealed class HeightLayerSceneIndex : IDisposable
    {
        private readonly Dictionary<int, HeightLayerEntityRecord> m_records = new();
        private readonly SortedDictionary<int, HashSet<int>> m_idsByMinHeight = new();
        private readonly SortedDictionary<int, HashSet<int>> m_idsByMaxHeight = new();
        private readonly HeightLayerPresentationAdapter m_presentation;
        private readonly Dictionary<int, Dictionary<object, bool>> m_externalVisibility = new();
        private int? m_cutoff;
        private bool m_disposed;

        internal HeightLayerSceneIndex(HeightLayerPresentationAdapter? presentation = null)
        {
            m_presentation = presentation ?? new HeightLayerPresentationAdapter();
        }

        internal int? Cutoff => m_cutoff;
        internal IReadOnlyDictionary<int, HeightLayerEntityRecord> Records => m_records;
        internal HeightLayerPresentationAdapter Presentation => m_presentation;

        internal HeightLayerEntityRecord Register(
            int entityId,
            int minHeight,
            int maxHeight,
            string? category = null,
            HeightLayerRenderBinding? rendererBinding = null)
        {
            ThrowIfDisposed();
            // Renderer refreshes replace the binding but retain external presentation policies
            // (for example category visibility) for this entity ID.
            RemoveInternal(entityId, clearExternalVisibility: false);
            var record = new HeightLayerEntityRecord(entityId, minHeight, maxHeight, category, rendererBinding);
            m_records.Add(entityId, record);
            AddToIndex(m_idsByMinHeight, record.MinHeight, entityId);
            AddToIndex(m_idsByMaxHeight, record.MaxHeight, entityId);
            if (rendererBinding is not null)
            {
                m_presentation.Bind(entityId, rendererBinding);
                m_presentation.Apply(entityId, IsVisible(record));
            }
            return record;
        }

        internal HeightLayerEntityRecord Register(
            IStaticEntity entity,
            string? category = null,
            HeightLayerRenderBinding? rendererBinding = null)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            HeightLayerEntityRecord range = HeightLayerEntityRange.Describe(entity, category, rendererBinding);
            return Register(range.EntityId, range.MinHeight, range.MaxHeight, range.Category, range.RendererBinding);
        }

        internal bool Remove(int entityId)
        {
            return RemoveInternal(entityId, clearExternalVisibility: true);
        }

        private bool RemoveInternal(int entityId, bool clearExternalVisibility)
        {
            if (!m_records.TryGetValue(entityId, out HeightLayerEntityRecord? record))
            {
                if (clearExternalVisibility)
                {
                    m_externalVisibility.Remove(entityId);
                }
                return false;
            }

            m_records.Remove(entityId);
            RemoveFromIndex(m_idsByMinHeight, record.MinHeight, entityId);
            RemoveFromIndex(m_idsByMaxHeight, record.MaxHeight, entityId);
            m_presentation.Unbind(entityId);
            if (clearExternalVisibility)
            {
                m_externalVisibility.Remove(entityId);
            }
            return true;
        }

        /// <summary>
        /// Adds or replaces a presentation-only visibility policy owned by another scene feature.
        /// Policies compose with the height cutoff and are retained across renderer refreshes.
        /// </summary>
        internal bool SetExternalVisibility(object owner, int entityId, bool visible)
        {
            if (owner is null) throw new ArgumentNullException(nameof(owner));
            ThrowIfDisposed();
            if (!m_externalVisibility.TryGetValue(entityId, out Dictionary<object, bool>? policies))
            {
                policies = new Dictionary<object, bool>();
                m_externalVisibility.Add(entityId, policies);
            }

            bool oldVisible = IsVisible(entityId);
            policies[owner] = visible;
            bool newVisible = IsVisible(entityId);
            if (oldVisible != newVisible && m_records.ContainsKey(entityId))
            {
                m_presentation.Apply(entityId, newVisible);
            }
            return m_records.ContainsKey(entityId);
        }

        internal void ClearExternalVisibility(object owner, int entityId)
        {
            if (owner is null) throw new ArgumentNullException(nameof(owner));
            if (!m_externalVisibility.TryGetValue(entityId, out Dictionary<object, bool>? policies))
            {
                return;
            }

            bool oldVisible = IsVisible(entityId);
            policies.Remove(owner);
            if (policies.Count == 0)
            {
                m_externalVisibility.Remove(entityId);
            }
            bool newVisible = IsVisible(entityId);
            if (oldVisible != newVisible && m_records.ContainsKey(entityId))
            {
                m_presentation.Apply(entityId, newVisible);
            }
        }

        internal void Rebuild(
            IEnumerable<IStaticEntity> entities,
            Func<IStaticEntity, string?>? category = null,
            Func<IStaticEntity, HeightLayerRenderBinding?>? rendererBinding = null)
        {
            ThrowIfDisposed();
            if (entities is null)
            {
                throw new ArgumentNullException(nameof(entities));
            }

            Clear();
            foreach (IStaticEntity entity in entities)
            {
                Register(entity, category?.Invoke(entity), rendererBinding?.Invoke(entity));
            }
        }

        internal IReadOnlyList<HeightLayerVisibilityChange> SetCutoff(int? cutoff)
        {
            ThrowIfDisposed();
            if (m_cutoff == cutoff)
            {
                return Array.Empty<HeightLayerVisibilityChange>();
            }

            int? previousCutoff = m_cutoff;
            m_cutoff = cutoff;
            var affected = new HashSet<int>();
            // A transition to or from Show All changes the presentation state of every
            // indexed entity, so the bounded range indexes cannot omit the previously
            // visible (or newly restored) records.  For cutoff-to-cutoff changes, only
            // entities intersecting either cutoff can change visibility.
            if (!previousCutoff.HasValue || !cutoff.HasValue)
            {
                affected.UnionWith(m_records.Keys);
            }
            else
            {
                affected.UnionWith(CollectVisibleCandidates(previousCutoff.Value));
                affected.UnionWith(CollectVisibleCandidates(cutoff.Value));
            }

            var changes = new List<HeightLayerVisibilityChange>();
            foreach (int entityId in affected)
            {
                if (!m_records.TryGetValue(entityId, out HeightLayerEntityRecord? record))
                {
                    continue;
                }

                bool oldVisible = IsVisible(record, previousCutoff);
                bool newVisible = IsVisible(record, cutoff);
                if (oldVisible == newVisible)
                {
                    continue;
                }

                m_presentation.Apply(entityId, newVisible);
                changes.Add(new HeightLayerVisibilityChange(entityId, newVisible));
            }
            return changes;
        }

        internal IReadOnlyList<HeightLayerVisibilityChange> ShowAll() => SetCutoff(null);

        internal bool IsVisible(int entityId)
        {
            return !m_records.TryGetValue(entityId, out HeightLayerEntityRecord? record) || IsVisible(record);
        }

        internal bool CanInteract(int entityId) => IsVisible(entityId);

        internal IReadOnlyList<HeightLayerEntityRecord> QueryVisible(string? category = null)
        {
            IEnumerable<HeightLayerEntityRecord> records = m_records.Values.Where(record => IsVisible(record));
            if (!string.IsNullOrWhiteSpace(category))
            {
                records = records.Where(record => string.Equals(record.Category, category, StringComparison.Ordinal));
            }
            return records.OrderBy(record => record.EntityId).ToArray();
        }

        internal void Clear()
        {
            foreach (int entityId in m_records.Keys.ToArray())
            {
                Remove(entityId);
            }
            m_idsByMinHeight.Clear();
            m_idsByMaxHeight.Clear();
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            Clear();
            m_presentation.Dispose();
            m_disposed = true;
        }

        private bool IsVisible(HeightLayerEntityRecord record) => IsVisible(record, m_cutoff);

        private bool IsVisible(HeightLayerEntityRecord record, int? cutoff) =>
            (!cutoff.HasValue || record.Intersects(cutoff.Value)) && IsExternallyVisible(record.EntityId);

        private bool IsExternallyVisible(int entityId) =>
            !m_externalVisibility.TryGetValue(entityId, out Dictionary<object, bool>? policies) ||
            policies.Values.All(visible => visible);

        private HashSet<int> CollectVisibleCandidates(int cutoff)
        {
            var byMin = new HashSet<int>();
            foreach (KeyValuePair<int, HashSet<int>> entry in m_idsByMinHeight)
            {
                if (entry.Key > cutoff)
                {
                    break;
                }
                byMin.UnionWith(entry.Value);
            }

            var byMax = new HashSet<int>();
            foreach (KeyValuePair<int, HashSet<int>> entry in m_idsByMaxHeight)
            {
                if (entry.Key >= cutoff)
                {
                    byMax.UnionWith(entry.Value);
                }
            }

            byMin.IntersectWith(byMax);
            return byMin;
        }

        private static void AddToIndex(SortedDictionary<int, HashSet<int>> index, int key, int entityId)
        {
            if (!index.TryGetValue(key, out HashSet<int>? ids))
            {
                ids = new HashSet<int>();
                index.Add(key, ids);
            }
            ids.Add(entityId);
        }

        private static void RemoveFromIndex(SortedDictionary<int, HashSet<int>> index, int key, int entityId)
        {
            if (!index.TryGetValue(key, out HashSet<int>? ids))
            {
                return;
            }
            ids.Remove(entityId);
            if (ids.Count == 0)
            {
                index.Remove(key);
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(HeightLayerSceneIndex));
            }
        }
    }

    /// <summary>
    /// Scene-facing owner that exposes the HUD state and placement policy without
    /// making the global runtime state or terrain renderer aware of this filter.
    /// </summary>
    internal sealed class HeightLayerFilterScene : IDisposable
    {
        internal HeightLayerSceneIndex Index { get; }
        internal int? ActiveLayer => Index.Cutoff;
        internal bool IsFiltering => ActiveLayer.HasValue;
        internal string HudIndicator =>
            ActiveLayer.HasValue ? "Height layer " + ActiveLayer.Value : "All height layers";

        internal HeightLayerFilterScene(HeightLayerSceneIndex? index = null)
        {
            Index = index ?? new HeightLayerSceneIndex();
        }

        internal IReadOnlyList<HeightLayerVisibilityChange> SetActiveLayer(int height) => Index.SetCutoff(height);

        internal IReadOnlyList<HeightLayerVisibilityChange> ShowAll() => Index.ShowAll();

        internal bool CanPlaceAgainst(int entityId) => Index.CanInteract(entityId);

        public void Dispose() => Index.Dispose();
    }

    internal static class HeightLayerEntityRange
    {
        internal static HeightLayerEntityRecord Describe(
            IStaticEntity entity,
            string? category = null,
            HeightLayerRenderBinding? rendererBinding = null)
        {
            int baseHeight = entity.CenterTile.Z;
            int minHeight = baseHeight;
            int maxHeight = baseHeight;
            foreach (var tile in entity.OccupiedTiles)
            {
                minHeight = Math.Min(minHeight, baseHeight + tile.RelativeFrom);
                // VerticalSizeRaw is a count and MaxHeight is inclusive.
                maxHeight = Math.Max(maxHeight, baseHeight + tile.RelativeFrom + tile.VerticalSizeRaw - 1);
            }

            string resolvedCategory = category ?? entity.Prototype.GetType().Name;
            return new HeightLayerEntityRecord(entity.Id.Value, minHeight, maxHeight, resolvedCategory, rendererBinding);
        }
    }

    /// <summary>
    /// Binds the scene index to the native static-entity lifecycle. The index is
    /// rebuilt from the current scene and then kept current for adds, removals,
    /// upgrades, and renderer refreshes. Simulation state remains owned by the
    /// native entity manager; this class only updates presentation callbacks.
    /// </summary>
    internal sealed class HeightLayerFilterFeature : IDisposable
    {
        internal const string ComponentId = "HeightLayerFilter";
        private readonly IEntitiesManager m_entities;
        private readonly Func<IStaticEntity, string?> m_category;
        private readonly Func<IStaticEntity, HeightLayerRenderBinding?> m_binding;
        private readonly HeightLayerFilterScene m_scene;
        private bool m_disposed;

        internal HeightLayerFilterFeature(
            IEntitiesManager entities,
            HeightLayerFilterScene? scene = null,
            Func<IStaticEntity, string?>? category = null,
            Func<IStaticEntity, HeightLayerRenderBinding?>? binding = null)
        {
            m_entities = entities ?? throw new ArgumentNullException(nameof(entities));
            m_scene = scene ?? new HeightLayerFilterScene();
            m_category = category ?? (entity => entity.Prototype.GetType().Name);
            m_binding = binding ?? (_ => null);

            m_scene.Index.Rebuild(m_entities.GetAllEntitiesOfType<IStaticEntity>(), m_category, m_binding);
            m_entities.StaticEntityAdded.AddNonSaveable(this, OnStaticEntityAdded);
            m_entities.StaticEntityRemoved.AddNonSaveable(this, OnStaticEntityRemoved);
            m_entities.OnUpgradeToBePerformed.AddNonSaveable(this, OnUpgradeStarting);
            m_entities.OnUpgradeJustPerformed.AddNonSaveable(this, OnUpgradeFinished);
            m_entities.OnEntityVisualChanged.AddNonSaveable(this, OnEntityVisualChanged);
        }

        internal HeightLayerFilterScene Scene => m_scene;

        internal IReadOnlyList<HeightLayerVisibilityChange> SetActiveLayer(int height) =>
            m_scene.SetActiveLayer(height);

        internal IReadOnlyList<HeightLayerVisibilityChange> ShowAll() => m_scene.ShowAll();

        internal bool CanInteract(int entityId) => m_scene.CanPlaceAgainst(entityId);

        internal string Status() => m_scene.HudIndicator;

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_entities.StaticEntityAdded.RemoveNonSaveable(this, OnStaticEntityAdded);
            m_entities.StaticEntityRemoved.RemoveNonSaveable(this, OnStaticEntityRemoved);
            m_entities.OnUpgradeToBePerformed.RemoveNonSaveable(this, OnUpgradeStarting);
            m_entities.OnUpgradeJustPerformed.RemoveNonSaveable(this, OnUpgradeFinished);
            m_entities.OnEntityVisualChanged.RemoveNonSaveable(this, OnEntityVisualChanged);
            m_scene.Dispose();
            m_disposed = true;
        }

        private void OnStaticEntityAdded(IStaticEntity entity)
        {
            if (!m_disposed && entity is not null)
            {
                m_scene.Index.Register(entity, m_category(entity), m_binding(entity));
            }
        }

        private void OnStaticEntityRemoved(IStaticEntity entity)
        {
            if (!m_disposed && entity is not null)
            {
                m_scene.Index.Remove(entity.Id.Value);
            }
        }

        private void OnUpgradeStarting(IUpgradableEntity entity)
        {
            if (!m_disposed && entity is IStaticEntity staticEntity)
            {
                m_scene.Index.Remove(staticEntity.Id.Value);
            }
        }

        private void OnUpgradeFinished(IUpgradableEntity entity, IEntityProto _)
        {
            if (!m_disposed && entity is IStaticEntity staticEntity)
            {
                m_scene.Index.Register(staticEntity, m_category(staticEntity), m_binding(staticEntity));
            }
        }

        private void OnEntityVisualChanged(IEntity entity)
        {
            if (!m_disposed && entity is IStaticEntity staticEntity &&
                m_scene.Index.Records.ContainsKey(staticEntity.Id.Value))
            {
                // Rendering objects can be rebuilt after the simulation entity is
                // unchanged. Re-registering refreshes only the presentation binding
                // and reapplies the current cutoff.
                m_scene.Index.Register(staticEntity, m_category(staticEntity), m_binding(staticEntity));
            }
        }
    }

    /// <summary>
    /// Native MB renderer adapter. Port visibility is deliberately an injected
    /// callback because IoPortsRenderer uses instanced chunks rather than child
    /// GameObjects; #144 can provide its category-aware port binding through the
    /// same HeightLayerRenderBinding contract.
    /// </summary>
    internal static class HeightLayerNativeBinding
    {
        private static readonly MethodInfo? s_getOrCreatePortsChunk =
            typeof(IoPortsRenderer).GetMethod(
                "getOrCreatePortsChunk",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static HeightLayerRenderBinding? TryCreate(
            IStaticEntity entity,
            MbBasedEntitiesRenderer renderer,
            IoPortsRenderer? portsRenderer = null)
        {
            if (entity is null || renderer is null ||
                !renderer.RenderedEntities.TryGetValue(entity, out EntityMb? mb) || mb is null)
            {
                return null;
            }

            GameObject gameObject = mb.gameObject;
            Action<bool>? setPortsVisible = null;
            if (portsRenderer is not null && entity is IEntityWithPorts withPorts)
            {
                var originalPortVisibility = new Dictionary<IoPort, bool>();
                foreach (IoPort port in withPorts.Ports)
                {
                    originalPortVisibility[port] = port.RendererId != 0;
                }
                setPortsVisible = visible =>
                {
                    foreach (KeyValuePair<IoPort, bool> port in originalPortVisibility)
                    {
                        if (visible && port.Value)
                        {
                            TrySetPortVisible(portsRenderer, port.Key, true);
                        }
                        else
                        {
                            TrySetPortVisible(portsRenderer, port.Key, false);
                        }
                    }
                };
            }
            return new HeightLayerRenderBinding(
                visible => gameObject.SetActive(visible),
                setPortsVisible,
                // Disabling the MB root also removes its physics hit target. A
                // separate callback remains available for renderers whose hit
                // testing is not represented by the root GameObject.
                _ => { },
                () => gameObject.activeSelf,
                () => true,
                () => gameObject.activeSelf);
        }

        private static void TrySetPortVisible(IoPortsRenderer renderer, IoPort port, bool visible)
        {
            try
            {
                if (!visible)
                {
                    // Port highlight colliders are separate pooled GameObjects;
                    // pause them before removing the instanced visual so hidden
                    // ports cannot still win cursor picking.
                    renderer.PauseHighlightsFor(port, keepCollider: false);
                }
                // PortsChunkStandard owns the authoritative ShowPort/HidePort helpers;
                // IoPortsRenderer intentionally exposes only visual-ID operations. The
                // exact 0.8.7b private seam is resolved once per call and fails open if
                // a later game build changes it.
                object? chunk = s_getOrCreatePortsChunk?.Invoke(renderer, new object[] { port });
                if (chunk is null)
                {
                    return;
                }

                MethodInfo? method = AccessTools.Method(
                    chunk.GetType(),
                    visible ? "ShowPort" : "HidePort",
                    new[] { typeof(IoPort) });
                method?.Invoke(chunk, new object[] { port });
                if (visible)
                {
                    renderer.RestoreHighlightsFor(port);
                }
            }
            catch
            {
                // Presentation filtering must never interrupt native port rendering.
            }
        }
    }
}
