// Taj's COI Mods | ProductFlowIndex.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TajsCOI.Core.Flow
{
    public enum ProductFlowEntityKind
    {
        Producer,
        Consumer,
        Storage,
        Transport,
        Vehicle,
        ConstructionRequirement,
    }

    public sealed class ProductFlowEntitySnapshot
    {
        public ProductFlowEntitySnapshot(string entityId, string prototypeId, ProductFlowEntityKind kind,
            IEnumerable<ProductFlowQuantity>? products = null, double capacity = 0, string? displayName = null)
        {
            EntityId = Require(entityId, nameof(entityId));
            PrototypeId = Require(prototypeId, nameof(prototypeId));
            Kind = kind;
            if (double.IsNaN(capacity) || double.IsInfinity(capacity) || capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            DisplayName = displayName is null || string.IsNullOrWhiteSpace(displayName) ? PrototypeId : displayName.Trim();
            Products = new ReadOnlyCollection<ProductFlowQuantity>((products ?? Enumerable.Empty<ProductFlowQuantity>()).Where(x => x.Quantity != 0).ToArray());
        }
        public string EntityId { get; }
        public string PrototypeId { get; }
        public ProductFlowEntityKind Kind { get; }
        public string DisplayName { get; }
        public double Capacity { get; }
        public IReadOnlyList<ProductFlowQuantity> Products { get; }
        private static string Require(string value, string parameter) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Entity identifiers cannot be empty.", parameter) : value.Trim();
    }

    public readonly struct ProductFlowQuantity
    {
        public ProductFlowQuantity(string productId, double quantity)
        {
            ProductId = string.IsNullOrWhiteSpace(productId) ? throw new ArgumentException("Product identifiers cannot be empty.", nameof(productId)) : productId.Trim();
            if (double.IsNaN(quantity) || double.IsInfinity(quantity) || quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity = quantity;
        }
        public string ProductId { get; }
        public double Quantity { get; }
    }

    public sealed class ProductFlowQueryResult
    {
        internal ProductFlowQueryResult(string productId, IReadOnlyDictionary<ProductFlowEntityKind, IReadOnlyList<ProductFlowEntitySnapshot>> entities,
            IReadOnlyDictionary<ProductFlowEntityKind, double> totals)
        {
            ProductId = productId;
            EntitiesByKind = entities;
            TotalsByKind = totals;
        }
        public string ProductId { get; }
        public IReadOnlyDictionary<ProductFlowEntityKind, IReadOnlyList<ProductFlowEntitySnapshot>> EntitiesByKind { get; }
        public IReadOnlyDictionary<ProductFlowEntityKind, double> TotalsByKind { get; }
        public int Count => EntitiesByKind.Values.Sum(x => x.Count);
    }

    /// <summary>
    /// Scene-owned stable product index. It stores value snapshots only, never renderer or entity
    /// references, and performs at most one explicit bootstrap scan per scene activation.
    /// </summary>
    public sealed class ProductFlowIndex : IDisposable
    {
        private readonly Dictionary<string, ProductFlowEntitySnapshot> m_entities = new(StringComparer.Ordinal);
        private bool m_bootstrapped;
        private bool m_disposed;

        public bool IsBootstrapped => m_bootstrapped;
        public int Count => m_entities.Count;
        public long Revision { get; private set; }

        public int BootstrapOnce(Func<IEnumerable<ProductFlowEntitySnapshot>> scan)
        {
            ThrowIfDisposed();
            if (scan is null) throw new ArgumentNullException(nameof(scan));
            if (m_bootstrapped) return 0;
            IEnumerable<ProductFlowEntitySnapshot>? result = scan();
            ProductFlowEntitySnapshot[] snapshots = result is null ? Array.Empty<ProductFlowEntitySnapshot>() : result.ToArray();
            var next = new Dictionary<string, ProductFlowEntitySnapshot>(StringComparer.Ordinal);
            foreach (ProductFlowEntitySnapshot entity in snapshots)
            {
                if (entity is null) throw new InvalidOperationException("Bootstrap returned a null entity.");
                next[entity.EntityId] = entity;
            }
            m_entities.Clear();
            foreach (KeyValuePair<string, ProductFlowEntitySnapshot> pair in next) m_entities.Add(pair.Key, pair.Value);
            m_bootstrapped = true;
            Revision++;
            return m_entities.Count;
        }

        public void Upsert(ProductFlowEntitySnapshot entity)
        {
            ThrowIfDisposed();
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            m_entities[entity.EntityId] = entity;
            Revision++;
        }

        public bool Remove(string entityId)
        {
            ThrowIfDisposed();
            bool removed = !string.IsNullOrWhiteSpace(entityId) && m_entities.Remove(entityId.Trim());
            if (removed) Revision++;
            return removed;
        }

        public void OnEntityAdded(ProductFlowEntitySnapshot entity) => Upsert(entity);
        public void OnEntityChanged(ProductFlowEntitySnapshot entity) => Upsert(entity);
        public bool OnEntityRemoved(string entityId) => Remove(entityId);

        public ProductFlowQueryResult Query(string productId)
        {
            ThrowIfDisposed();
            string id = string.IsNullOrWhiteSpace(productId) ? throw new ArgumentException("Product is required.", nameof(productId)) : productId.Trim();
            var byKind = new Dictionary<ProductFlowEntityKind, IReadOnlyList<ProductFlowEntitySnapshot>>();
            var totals = new Dictionary<ProductFlowEntityKind, double>();
            foreach (ProductFlowEntityKind kind in Enum.GetValues(typeof(ProductFlowEntityKind)))
            {
                ProductFlowEntitySnapshot[] matches = m_entities.Values
                    .Where(e => e.Kind == kind && e.Products.Any(p => string.Equals(p.ProductId, id, StringComparison.Ordinal)))
                    .OrderBy(e => e.EntityId, StringComparer.Ordinal).ToArray();
                byKind[kind] = new ReadOnlyCollection<ProductFlowEntitySnapshot>(matches);
                totals[kind] = matches.Sum(e => e.Products.Where(p => p.ProductId == id).Sum(p => p.Quantity));
            }
            return new ProductFlowQueryResult(id,
                new ReadOnlyDictionary<ProductFlowEntityKind, IReadOnlyList<ProductFlowEntitySnapshot>>(byKind),
                new ReadOnlyDictionary<ProductFlowEntityKind, double>(totals));
        }

        public void Clear()
        {
            m_entities.Clear();
            m_bootstrapped = false;
            Revision++;
        }

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            m_entities.Clear();
            m_bootstrapped = false;
            Revision++;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(ProductFlowIndex));
        }
    }

    public interface IProductFlowHighlightService
    {
        IDisposable Highlight(string entityId, ProductFlowEntityKind kind);
    }

    public interface IProductResourceVisualizationActivator
    {
        IDisposable Activate(string productId);
    }

    /// <summary>Temporary UI session that releases highlights and resource visualization on clear.</summary>
    public sealed class ProductFlowExplorerSession : IDisposable
    {
        private readonly List<IDisposable> m_handles = new();
        private bool m_disposed;

        public ProductFlowExplorerSession(ProductFlowIndex index) => Index = index ?? throw new ArgumentNullException(nameof(index));
        public ProductFlowIndex Index { get; }
        public ProductFlowQueryResult? Current { get; private set; }

        public ProductFlowQueryResult Select(string productId, IProductFlowHighlightService? highlights = null,
            IProductResourceVisualizationActivator? resourceVisualization = null)
        {
            if (m_disposed) throw new ObjectDisposedException(nameof(ProductFlowExplorerSession));
            ClearHandles();
            try
            {
                Current = Index.Query(productId);
                if (highlights is not null)
                    foreach (KeyValuePair<ProductFlowEntityKind, IReadOnlyList<ProductFlowEntitySnapshot>> group in Current.EntitiesByKind)
                        foreach (ProductFlowEntitySnapshot entity in group.Value) AddHandle(highlights.Highlight(entity.EntityId, group.Key));
                if (resourceVisualization is not null) AddHandle(resourceVisualization.Activate(Current.ProductId));
                return Current;
            }
            catch
            {
                Current = null;
                ClearHandles();
                throw;
            }
        }

        public void Clear()
        {
            Current = null;
            ClearHandles();
        }

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            Clear();
        }

        private void AddHandle(IDisposable? handle) { if (handle is not null) m_handles.Add(handle); }
        private void ClearHandles()
        {
            for (int i = m_handles.Count - 1; i >= 0; i--) m_handles[i].Dispose();
            m_handles.Clear();
        }
    }
}
