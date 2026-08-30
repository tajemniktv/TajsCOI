// Taj's COI Mods | WorldVisibilityFilter.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Transports;
using Mafi.Unity.Entities;
using Mafi.Unity.Ports.Io;
using Mafi.Unity.Terrain;

namespace TajsCOI.Tweaks.Features.Presentation
{
    internal interface IWorldVisibilityPresentationAdapter : IDisposable
    {
        public void Attach(int entityId, IStaticEntity entity);
        public void Detach(int entityId);
        public void Apply(int entityId, bool visible);
        public bool CanSelect(int entityId, bool visible);
        public void SetCategoryVisible(bool visible);
    }

    /// <summary>
    ///     Native GameObject/port adapter for ordinary MB-backed entities. It changes only
    ///     presentation and hit testing; the simulation entity remains active throughout.
    /// </summary>
    internal class WorldVisibilityNativePresentationAdapter : IWorldVisibilityPresentationAdapter
    {
        private readonly Func<IStaticEntity, HeightLayerRenderBinding?> m_bindingFactory;
        private readonly HeightLayerSceneIndex? m_sharedIndex;
        private readonly object m_sharedOwner = new();
        private readonly Dictionary<int, HeightLayerRenderBinding> m_bindings = new();
        private readonly HashSet<int> m_sharedIds = new();

        internal WorldVisibilityNativePresentationAdapter(
            Func<IStaticEntity, HeightLayerRenderBinding?> bindingFactory,
            HeightLayerSceneIndex? sharedIndex = null)
        {
            m_bindingFactory = bindingFactory ?? throw new ArgumentNullException(nameof(bindingFactory));
            m_sharedIndex = sharedIndex;
        }

        public virtual void Attach(int entityId, IStaticEntity entity)
        {
            Detach(entityId);
            if (m_sharedIndex is not null)
            {
                m_sharedIds.Add(entityId);
                return;
            }
            HeightLayerRenderBinding? binding = m_bindingFactory(entity);
            if (binding is not null)
            {
                m_bindings[entityId] = binding;
            }
        }

        public virtual void Detach(int entityId)
        {
            if (m_sharedIndex is not null)
            {
                m_sharedIndex.ClearExternalVisibility(m_sharedOwner, entityId);
                m_sharedIds.Remove(entityId);
                return;
            }
            if (m_bindings.TryGetValue(entityId, out HeightLayerRenderBinding? binding))
            {
                m_bindings.Remove(entityId);
                binding.Restore();
            }
        }

        public virtual void Apply(int entityId, bool visible)
        {
            if (m_sharedIndex is not null)
            {
                if (visible)
                {
                    // Showing a category removes this owner's override entirely so Show All
                    // restores the shared #134 policy rather than leaving a dormant entry.
                    m_sharedIndex.ClearExternalVisibility(m_sharedOwner, entityId);
                }
                else
                {
                    m_sharedIndex.SetExternalVisibility(m_sharedOwner, entityId, visible);
                }
                return;
            }
            if (m_bindings.TryGetValue(entityId, out HeightLayerRenderBinding? binding))
            {
                binding.Apply(visible);
            }
        }

        public virtual bool CanSelect(int entityId, bool visible) => visible;

        public virtual void SetCategoryVisible(bool visible)
        {
            // Entity visibility is applied by WorldVisibilitySceneIndex only to IDs in this
            // category. Keeping this method on the adapter allows dedicated instanced adapters
            // to own their native category-level state without a second registry.
        }

        public virtual void Dispose()
        {
            if (m_sharedIndex is not null)
            {
                foreach (int entityId in m_sharedIds.ToArray())
                {
                    m_sharedIndex.ClearExternalVisibility(m_sharedOwner, entityId);
                }
                m_sharedIds.Clear();
                return;
            }
            foreach (HeightLayerRenderBinding binding in m_bindings.Values)
            {
                binding.Restore();
            }
            m_bindings.Clear();
        }
    }

    /// <summary>
    ///     Dedicated adapter for layout/transport instanced renderers (lifts, sorters and
    ///     connectors). It contributes an external policy to the shared #134 presentation index;
    ///     it never disables a guessed GameObject because instanced entities do not own one.
    /// </summary>
    internal sealed class WorldVisibilityInstancedPresentationAdapter : IWorldVisibilityPresentationAdapter
    {
        private readonly HeightLayerSceneIndex? m_sharedIndex;
        private readonly object m_sharedOwner = new();
        private readonly HashSet<int> m_entityIds = new();

        internal WorldVisibilityInstancedPresentationAdapter(HeightLayerSceneIndex? sharedIndex)
        {
            m_sharedIndex = sharedIndex;
        }

        public void Attach(int entityId, IStaticEntity entity)
        {
            Detach(entityId);
            m_entityIds.Add(entityId);
        }

        public void Detach(int entityId)
        {
            m_sharedIndex?.ClearExternalVisibility(m_sharedOwner, entityId);
            m_entityIds.Remove(entityId);
        }

        public void Apply(int entityId, bool visible)
        {
            if (m_sharedIndex is null)
            {
                return;
            }
            if (visible)
            {
                m_sharedIndex.ClearExternalVisibility(m_sharedOwner, entityId);
            }
            else
            {
                m_sharedIndex.SetExternalVisibility(m_sharedOwner, entityId, visible);
            }
        }

        public bool CanSelect(int entityId, bool visible) => visible;

        public void SetCategoryVisible(bool visible)
        {
        }

        public void Dispose()
        {
            foreach (int entityId in m_entityIds.ToArray())
            {
                m_sharedIndex?.ClearExternalVisibility(m_sharedOwner, entityId);
            }
            m_entityIds.Clear();
        }
    }

    /// <summary>
    ///     Dedicated tree adapter. Trees are chunk-instanced and do not have one GameObject per
    ///     entity, so visibility is delegated to TreesRenderer's supported category operation.
    /// </summary>
    internal sealed class WorldVisibilityTreePresentationAdapter : IWorldVisibilityPresentationAdapter
    {
        private readonly Action<bool>? m_setVisible;
        private readonly bool m_originalVisible;

        internal WorldVisibilityTreePresentationAdapter(Action<bool>? setVisible, bool originalVisible = true)
        {
            m_setVisible = setVisible;
            m_originalVisible = originalVisible;
        }

        public void Attach(int entityId, IStaticEntity entity)
        {
        }

        public void Detach(int entityId)
        {
        }

        public void Apply(int entityId, bool visible)
        {
        }

        public bool CanSelect(int entityId, bool visible) => visible;

        public void SetCategoryVisible(bool visible) => m_setVisible?.Invoke(visible && m_originalVisible);

        public void Dispose() => SetCategoryVisible(m_originalVisible);
    }

    internal sealed class WorldVisibilityCategoryDescriptor
    {
        internal WorldVisibilityCategoryDescriptor(
            string id,
            string displayName,
            Func<IStaticEntity, bool> matches,
            IWorldVisibilityPresentationAdapter adapter)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Category ID is required.", nameof(id)) : id;
            DisplayName = displayName ?? string.Empty;
            Matches = matches ?? throw new ArgumentNullException(nameof(matches));
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        internal string Id { get; }
        internal string DisplayName { get; }
        internal Func<IStaticEntity, bool> Matches { get; }
        internal IWorldVisibilityPresentationAdapter Adapter { get; }
        internal bool Hidden { get; set; }
    }

    /// <summary>
    ///     Extensible category registry. Categories are policy-only and safe-visible by default;
    ///     callers may register compatible future entity classes without changing the UI model.
    /// </summary>
    internal sealed class WorldVisibilityCategoryRegistry : IDisposable
    {
        private readonly Dictionary<string, WorldVisibilityCategoryDescriptor> m_categories =
            new(StringComparer.OrdinalIgnoreCase);

        private bool m_disposed;

        internal IReadOnlyCollection<WorldVisibilityCategoryDescriptor> Categories => m_categories.Values;

        internal bool Register(WorldVisibilityCategoryDescriptor descriptor)
        {
            if (descriptor is null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
            if (m_disposed || m_categories.ContainsKey(descriptor.Id))
            {
                return false;
            }
            m_categories.Add(descriptor.Id, descriptor);
            return true;
        }

        internal bool TryGet(string id, out WorldVisibilityCategoryDescriptor? descriptor) =>
            m_categories.TryGetValue(id ?? string.Empty, out descriptor);

        internal WorldVisibilityCategoryDescriptor? Resolve(IStaticEntity entity)
        {
            foreach (WorldVisibilityCategoryDescriptor category in m_categories.Values)
            {
                try
                {
                    if (category.Matches(entity))
                    {
                        return category;
                    }
                }
                catch
                {
                    // A third-party category must not prevent the remaining categories from
                    // classifying an entity.
                }
            }
            return null;
        }

        internal IReadOnlyList<string> HiddenCategoryIds =>
            m_categories.Values.Where(category => category.Hidden).Select(category => category.Id).OrderBy(id => id).ToArray();

        internal bool SetHidden(string id, bool hidden)
        {
            if (!TryGet(id, out WorldVisibilityCategoryDescriptor? category) || category is null)
            {
                return false;
            }

            category.Hidden = hidden;
            return true;
        }

        internal void ApplyPersisted(string? text)
        {
            foreach (WorldVisibilityCategoryDescriptor category in m_categories.Values)
            {
                category.Hidden = false;
            }
            foreach (string id in ParseIds(text))
            {
                if (TryGet(id, out WorldVisibilityCategoryDescriptor? category) && category is not null)
                {
                    category.Hidden = true;
                }
            }
        }

        internal void ShowAll()
        {
            foreach (WorldVisibilityCategoryDescriptor category in m_categories.Values)
            {
                category.Hidden = false;
                category.Adapter.SetCategoryVisible(true);
            }
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            foreach (WorldVisibilityCategoryDescriptor category in m_categories.Values)
            {
                category.Adapter.Dispose();
            }
            m_categories.Clear();
            m_disposed = true;
        }

        internal static IEnumerable<string> ParseIds(string? text)
        {
            return (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim()).Where(id => id.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal sealed class WorldVisibilityEntityRecord
    {
        internal WorldVisibilityEntityRecord(int entityId, string categoryId)
        {
            EntityId = entityId;
            CategoryId = categoryId;
        }

        internal int EntityId { get; }
        internal string CategoryId { get; }
    }

    /// <summary>
    ///     Scene-owned entity index. Adds/removals and renderer refreshes are event-driven; filter
    ///     changes touch only the affected category's ID set and never scan the world per frame.
    /// </summary>
    internal sealed class WorldVisibilitySceneIndex : IDisposable
    {
        private readonly WorldVisibilityCategoryRegistry m_registry;

        // Category membership is the small overlay owned by #144. Native renderer bindings and
        // lifecycle/range ownership stay in the shared #134 HeightLayerSceneIndex when supplied.
        private readonly Dictionary<int, WorldVisibilityEntityRecord> m_records = new();
        private readonly Dictionary<string, HashSet<int>> m_idsByCategory = new(StringComparer.OrdinalIgnoreCase);
        private bool m_disposed;

        internal WorldVisibilitySceneIndex(WorldVisibilityCategoryRegistry registry)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        internal IReadOnlyDictionary<int, WorldVisibilityEntityRecord> Records => m_records;
        internal WorldVisibilityCategoryRegistry Categories => m_registry;

        internal bool Register(IStaticEntity entity)
        {
            if (m_disposed || entity is null)
            {
                return false;
            }
            WorldVisibilityCategoryDescriptor? category = m_registry.Resolve(entity);
            if (category is null)
            {
                return false;
            }
            Remove(entity.Id.Value);
            var record = new WorldVisibilityEntityRecord(entity.Id.Value, category.Id);
            m_records.Add(record.EntityId, record);
            if (!m_idsByCategory.TryGetValue(category.Id, out HashSet<int>? ids))
            {
                ids = new HashSet<int>();
                m_idsByCategory.Add(category.Id, ids);
            }
            ids.Add(record.EntityId);
            category.Adapter.Attach(record.EntityId, entity);
            category.Adapter.Apply(record.EntityId, !category.Hidden);
            category.Adapter.SetCategoryVisible(!category.Hidden);
            return true;
        }

        internal bool Remove(int entityId)
        {
            if (!m_records.TryGetValue(entityId, out WorldVisibilityEntityRecord? record))
            {
                return false;
            }
            m_records.Remove(entityId);
            if (m_idsByCategory.TryGetValue(record.CategoryId, out HashSet<int>? ids))
            {
                ids.Remove(entityId);
                if (ids.Count == 0)
                {
                    m_idsByCategory.Remove(record.CategoryId);
                }
            }
            if (m_registry.TryGet(record.CategoryId, out WorldVisibilityCategoryDescriptor? category) && category is not null)
            {
                category.Adapter.Detach(entityId);
            }
            return true;
        }

        internal bool SetCategoryHidden(string id, bool hidden)
        {
            if (!m_registry.TryGet(id, out WorldVisibilityCategoryDescriptor? category) || category is null)
            {
                return false;
            }
            category.Hidden = hidden;
            category.Adapter.SetCategoryVisible(!hidden);
            if (m_idsByCategory.TryGetValue(category.Id, out HashSet<int>? ids))
            {
                foreach (int entityId in ids.ToArray())
                {
                    category.Adapter.Apply(entityId, !hidden);
                }
            }
            return true;
        }

        internal void ShowAll()
        {
            foreach (WorldVisibilityCategoryDescriptor category in m_registry.Categories)
            {
                category.Hidden = false;
                category.Adapter.SetCategoryVisible(true);
                if (m_idsByCategory.TryGetValue(category.Id, out HashSet<int>? ids))
                {
                    foreach (int entityId in ids.ToArray())
                    {
                        category.Adapter.Apply(entityId, true);
                    }
                }
            }
        }

        internal bool IsVisible(int entityId)
        {
            return !m_records.TryGetValue(entityId, out WorldVisibilityEntityRecord? record) ||
                   !m_registry.TryGet(record.CategoryId, out WorldVisibilityCategoryDescriptor? category) ||
                   category is null || !category.Hidden;
        }

        internal bool CanSelect(int entityId)
        {
            if (!m_records.TryGetValue(entityId, out WorldVisibilityEntityRecord? record) ||
                !m_registry.TryGet(record.CategoryId, out WorldVisibilityCategoryDescriptor? category) || category is null)
            {
                return true;
            }
            return category.Adapter.CanSelect(entityId, !category.Hidden);
        }

        internal IReadOnlyList<WorldVisibilityEntityRecord> QueryVisible(string? categoryId = null)
        {
            IEnumerable<WorldVisibilityEntityRecord> result = m_records.Values.Where(record => IsVisible(record.EntityId));
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                result = result.Where(record => string.Equals(record.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase));
            }
            return result.OrderBy(record => record.EntityId).ToArray();
        }

        internal void Clear()
        {
            foreach (int entityId in m_records.Keys.ToArray())
            {
                Remove(entityId);
            }
            m_idsByCategory.Clear();
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            Clear();
            m_registry.Dispose();
            m_disposed = true;
        }
    }

    internal sealed class WorldVisibilityHudIndicator
    {
        internal bool IsVisible { get; private set; }
        internal string Text { get; private set; } = string.Empty;

        internal void Update(IEnumerable<string> hiddenCategoryIds)
        {
            string[] ids =
                hiddenCategoryIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id).ToArray() ??
                Array.Empty<string>();
            IsVisible = ids.Length != 0;
            Text = IsVisible ? "Visibility filters active: " + string.Join(", ", ids) : string.Empty;
        }
    }

    /// <summary>
    ///     Gameplay-scene owner for category policy, renderer adapters and the HUD indicator.
    /// </summary>
    internal sealed class WorldVisibilityFilterFeature : IDisposable
    {
        internal const string ComponentId = "WorldVisibilityFilters";
        internal const string OrdinaryBuildings = "ordinary-buildings";
        internal const string SettlementDecorations = "settlement-decorations";
        internal const string Offices = "offices";
        internal const string Trees = "trees";
        internal const string Lifts = "lifts";
        internal const string Sorters = "sorters";
        internal const string BalancersConnectors = "balancers-connectors";

        private readonly IEntitiesManager m_entities;
        private readonly WorldVisibilityCategoryRegistry m_registry;
        private readonly WorldVisibilitySceneIndex m_index;
        private readonly WorldVisibilityHudIndicator m_indicator = new();
        private bool m_disposed;

        internal WorldVisibilityFilterFeature(
            IEntitiesManager entities,
            MbBasedEntitiesRenderer? renderer = null,
            IoPortsRenderer? portsRenderer = null,
            TreesRenderer? treesRenderer = null,
            string? persistedHiddenCategories = null,
            WorldVisibilityCategoryRegistry? registry = null,
            HeightLayerSceneIndex? sharedHeightLayerIndex = null)
        {
            m_entities = entities ?? throw new ArgumentNullException(nameof(entities));
            m_registry = registry ?? CreateDefaultRegistry(renderer, portsRenderer, treesRenderer, sharedHeightLayerIndex);
            m_registry.ApplyPersisted(persistedHiddenCategories);
            m_index = new WorldVisibilitySceneIndex(m_registry);
            m_index.Clear();
            foreach (IStaticEntity entity in m_entities.GetAllEntitiesOfType<IStaticEntity>())
            {
                m_index.Register(entity);
            }
            foreach (string categoryId in m_registry.HiddenCategoryIds)
            {
                m_index.SetCategoryHidden(categoryId, true);
            }
            m_entities.StaticEntityAdded.AddNonSaveable(this, OnStaticEntityAdded);
            m_entities.StaticEntityRemoved.AddNonSaveable(this, OnStaticEntityRemoved);
            m_entities.OnUpgradeToBePerformed.AddNonSaveable(this, OnUpgradeStarting);
            m_entities.OnUpgradeJustPerformed.AddNonSaveable(this, OnUpgradeFinished);
            m_entities.OnEntityVisualChanged.AddNonSaveable(this, OnEntityVisualChanged);
            RefreshIndicator();
        }

        internal WorldVisibilitySceneIndex Index => m_index;
        internal WorldVisibilityHudIndicator Indicator => m_indicator;
        internal IReadOnlyCollection<WorldVisibilityCategoryDescriptor> Categories => m_registry.Categories;

        internal string SetCategoryHidden(string id, bool hidden)
        {
            if (!m_index.SetCategoryHidden(id, hidden))
            {
                return "Unknown world visibility category: " + id + ".";
            }
            RefreshIndicator();
            return (hidden ? "Hidden " : "Showing ") + id + ". " + Status();
        }

        internal string ShowAll()
        {
            m_index.ShowAll();
            RefreshIndicator();
            return "World visibility filters cleared. " + Status();
        }

        internal void ApplyPersistedPolicy(string? text)
        {
            m_index.ShowAll();
            foreach (string id in WorldVisibilityCategoryRegistry.ParseIds(text))
            {
                m_index.SetCategoryHidden(id, true);
            }
            RefreshIndicator();
        }

        internal string Status() => m_indicator.IsVisible ? m_indicator.Text : "World visibility filters inactive.";

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
            m_index.Dispose();
            m_disposed = true;
        }

        private void OnStaticEntityAdded(IStaticEntity entity)
        {
            if (!m_disposed)
            {
                m_index.Register(entity);
            }
        }

        private void OnStaticEntityRemoved(IStaticEntity entity)
        {
            if (!m_disposed && entity is not null)
            {
                m_index.Remove(entity.Id.Value);
            }
        }

        private void OnUpgradeStarting(IUpgradableEntity entity)
        {
            if (!m_disposed && entity is IStaticEntity staticEntity)
            {
                m_index.Remove(staticEntity.Id.Value);
            }
        }

        private void OnUpgradeFinished(IUpgradableEntity entity, IEntityProto _)
        {
            if (!m_disposed && entity is IStaticEntity staticEntity)
            {
                m_index.Register(staticEntity);
            }
        }

        private void OnEntityVisualChanged(IEntity entity)
        {
            if (!m_disposed && entity is IStaticEntity staticEntity && m_index.Records.ContainsKey(staticEntity.Id.Value))
            {
                m_index.Register(staticEntity);
            }
        }

        private void RefreshIndicator() => m_indicator.Update(m_registry.HiddenCategoryIds);

        private static WorldVisibilityCategoryRegistry CreateDefaultRegistry(
            MbBasedEntitiesRenderer? renderer,
            IoPortsRenderer? portsRenderer,
            TreesRenderer? treesRenderer,
            HeightLayerSceneIndex? sharedHeightLayerIndex)
        {
            var registry = new WorldVisibilityCategoryRegistry();
            Func<IStaticEntity, HeightLayerRenderBinding?> nativeBinding = entity =>
                renderer is null ? null : HeightLayerNativeBinding.TryCreate(entity, renderer, portsRenderer);
            registry.Register(
                new WorldVisibilityCategoryDescriptor(
                    OrdinaryBuildings,
                    "Ordinary buildings",
                    IsOrdinaryBuilding,
                    new WorldVisibilityNativePresentationAdapter(nativeBinding, sharedHeightLayerIndex)));
            registry.Register(
                new WorldVisibilityCategoryDescriptor(
                    SettlementDecorations,
                    "Settlement decorations",
                    IsSettlementDecoration,
                    new WorldVisibilityNativePresentationAdapter(nativeBinding, sharedHeightLayerIndex)));
            registry.Register(
                new WorldVisibilityCategoryDescriptor(
                    Offices,
                    "Offices",
                    MatchesName("Office"),
                    new WorldVisibilityNativePresentationAdapter(nativeBinding, sharedHeightLayerIndex)));
            registry.Register(
                new WorldVisibilityCategoryDescriptor(
                    Trees,
                    "Trees",
                    _ => false,
                    new WorldVisibilityTreePresentationAdapter(CreateTreeVisibilityAction(treesRenderer))));
            registry.Register(
                new WorldVisibilityCategoryDescriptor(
                    Lifts,
                    "Lifts",
                    MatchesName("Lift"),
                    new WorldVisibilityInstancedPresentationAdapter(sharedHeightLayerIndex)));
            registry.Register(
                new WorldVisibilityCategoryDescriptor(
                    Sorters,
                    "Sorters",
                    MatchesName("Sorter"),
                    new WorldVisibilityInstancedPresentationAdapter(sharedHeightLayerIndex)));
            registry.Register(
                new WorldVisibilityCategoryDescriptor(
                    BalancersConnectors,
                    "Balancers/connectors",
                    MatchesAny("Balancer", "Connector", "Zipper"),
                    new WorldVisibilityInstancedPresentationAdapter(sharedHeightLayerIndex)));
            return registry;
        }

        private static Action<bool>? CreateTreeVisibilityAction(TreesRenderer? renderer)
        {
            if (renderer is null)
            {
                return null;
            }

            MethodInfo? method = typeof(TreesRenderer).GetMethod(
                "SetTreeRenderingState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return method is null
                ? null
                : visible =>
                {
                    try
                    {
                        method.Invoke(renderer, new object[] { visible });
                    }
                    catch
                    {
                    }
                };
        }

        private static bool IsOrdinaryBuilding(IStaticEntity entity) =>
            entity is not Transport && !IsSettlementDecoration(entity) && !MatchesName("Office")(entity) &&
            !MatchesAny("Lift", "Sorter", "Balancer", "Connector", "Zipper")(entity);

        private static bool IsSettlementDecoration(IStaticEntity entity) =>
            MatchesAny("Decoration", "Housing", "Residential", "Settlement")(entity);

        private static Func<IStaticEntity, bool> MatchesName(string token) =>
            entity => NameOf(entity).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Func<IStaticEntity, bool> MatchesAny(params string[] tokens) =>
            entity => tokens.Any(token => NameOf(entity).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

        private static string NameOf(IStaticEntity entity) =>
            entity.GetType().Name + " " + entity.Prototype.GetType().Name + " " + entity.Prototype.Id.Value;
    }
}
