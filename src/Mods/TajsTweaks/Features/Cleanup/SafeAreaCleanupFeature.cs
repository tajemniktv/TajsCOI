// Taj's COI Mods | SafeAreaCleanupFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Input;
using Mafi.Core.Products;
using TajsCOI.Tweaks.Features.Selection;

namespace TajsCOI.Tweaks.Features.Cleanup
{
    /// <summary>
    ///     Scene-owned area cleanup picker. Selection is shared with other rectangle tools; this
    ///     feature retains only IDs and immutable preview metadata until a confirmed commit.
    /// </summary>
    internal sealed class SafeAreaCleanupFeature
    {
        private readonly IEntitiesManager m_entities;
        private readonly IInputScheduler m_scheduler;
        private readonly IProductsManager m_products;
        private readonly EntityRectangleSelectionTool m_selection;
        private readonly List<SafeAreaSelectionEntry> m_pending = new();
        private SafeAreaCleanupMode? m_mode;
        private bool m_truncated;

        internal SafeAreaCleanupFeature(
            IEntitiesManager entities,
            IInputScheduler scheduler,
            IProductsManager products)
        {
            m_entities = entities ?? throw new ArgumentNullException(nameof(entities));
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            m_products = products ?? throw new ArgumentNullException(nameof(products));
            m_selection = new EntityRectangleSelectionTool(
                EnumerateCandidates,
                CanSelect,
                OnSelectionCompleted);
        }

        internal bool IsActive => m_selection.IsActive;
        internal SafeAreaCleanupMode? Mode => m_mode;
        internal IReadOnlyList<SafeAreaSelectionEntry> PendingSelection => m_pending;

        internal string Activate(SafeAreaCleanupMode mode)
        {
            m_pending.Clear();
            m_truncated = false;
            m_mode = mode;
            return m_selection.Activate(
                mode == SafeAreaCleanupMode.DisconnectedTransport
                    ? "Drag a rectangle over disconnected transports; release to preview cleanup. Escape or right-click cancels."
                    : "Drag a rectangle over product buffers; release to preview exact product quantities. Escape or right-click cancels.");
        }

        /// <summary>
        ///     Builds a deterministic tile-rectangle preview for console/UI callers that already
        ///     own a native terrain area. It uses the same candidate/filter path as the shared
        ///     screen rectangle picker and therefore cannot create a second selection authority.
        /// </summary>
        internal string BuildAreaPreview(SceneAreaBounds bounds)
        {
            if (!bounds.IsWithin(64, 64, 4096))
            {
                return "Area rejected: bounds must be ordered and no larger than 64x64 tiles.";
            }
            if (!m_mode.HasValue)
            {
                return "Safe area cleanup is idle; select transport or products first.";
            }

            m_selection.Deactivate();
            IEnumerable<IStaticEntity> matches = EnumerateCandidates()
                .Where(entity => bounds.Contains(entity.CenterTile.X, entity.CenterTile.Y) && CanSelect(entity));
            OnSelectionCompleted(matches.ToArray());
            return Status();
        }

        internal void UpdateInput() => m_selection.UpdateInput();

        internal void Deactivate()
        {
            m_selection.Deactivate();
            m_pending.Clear();
            m_mode = null;
            m_truncated = false;
        }

        internal string Status()
        {
            if (!m_mode.HasValue)
            {
                return "Safe area cleanup is idle.";
            }

            int productCount = m_pending.Sum(entry => entry.Products.Count);
            string suffix = m_truncated ? "; selection capped at " + SafeAreaCleanupLimits.MaxSelectedEntities : string.Empty;
            return "Safe area cleanup " + m_mode.Value + ": selected=" + m_pending.Count +
                   ", product previews=" + productCount + suffix +
                   ". Commit requires CONFIRM; quick mode also requires policy=ALLOW-QUICK.";
        }

        internal string Commit(bool quickRemove, string? confirmation, string? policy)
        {
            if (!m_mode.HasValue || m_pending.Count == 0)
            {
                return "Safe area cleanup has no pending selection.";
            }
            if (!SafeAreaCleanupPolicy.TryValidateCommit(quickRemove, confirmation, policy, out string error))
            {
                return error;
            }

            int queued;
            int stale;
            try
            {
                (queued, stale) = m_mode.Value == SafeAreaCleanupMode.DisconnectedTransport
                    ? QueueDisconnectedTransportRemoval(quickRemove)
                    : QueueProductCleanup(quickRemove);
            }
            catch (Exception exception)
            {
                Deactivate();
                return "Safe area cleanup stopped fail-open; any operations already queued remain unchanged: " + exception.Message;
            }

            Deactivate();
            return "Safe area cleanup queued " + queued + " operation(s); skipped stale entries=" + stale +
                   ". Selection and pending orders are scene-runtime only.";
        }

        private void OnSelectionCompleted(IReadOnlyList<IStaticEntity> matches)
        {
            m_pending.Clear();
            m_truncated = matches.Count > SafeAreaCleanupLimits.MaxSelectedEntities;
            IEnumerable<IStaticEntity> bounded = matches
                .OrderBy(entity => entity.Id.Value)
                .Take(SafeAreaCleanupLimits.MaxSelectedEntities);
            foreach (IStaticEntity entity in bounded)
            {
                if (entity is null || entity.IsDestroyed)
                {
                    continue;
                }

                if (m_mode == SafeAreaCleanupMode.DisconnectedTransport && entity is Transport transport)
                {
                    m_pending.Add(new SafeAreaSelectionEntry(entity.Id.Value, entity.GetTitle()));
                }
                else if (m_mode == SafeAreaCleanupMode.Products)
                {
                    IReadOnlyList<SafeAreaProductPreview> preview = BuildProductPreview(entity);
                    if (preview.Count != 0)
                    {
                        m_pending.Add(new SafeAreaSelectionEntry(entity.Id.Value, entity.GetTitle(), preview));
                    }
                }
            }
        }

        private IEnumerable<IStaticEntity> EnumerateCandidates()
        {
            foreach (IStaticEntity entity in m_entities.GetAllEntitiesOfType<IStaticEntity>())
            {
                yield return entity;
            }
        }

        private bool CanSelect(IStaticEntity entity)
        {
            if (entity is null || entity.IsDestroyed)
            {
                return false;
            }
            return m_mode == SafeAreaCleanupMode.DisconnectedTransport
                ? entity is Transport transport && IsDisconnectedTransport(transport)
                : BuildProductPreview(entity).Count != 0;
        }

        private static bool IsDisconnectedTransport(Transport transport) =>
            !transport.IsDestroyed && !transport.IsFullyConnected;

        private (int queued, int stale) QueueDisconnectedTransportRemoval(bool quickRemove)
        {
            int queued = 0;
            int stale = 0;
            foreach (SafeAreaSelectionEntry entry in m_pending)
            {
                if (!m_entities.TryGetEntity<Transport>(new EntityId(entry.EntityId), out Transport? transport) ||
                    transport is null || !IsDisconnectedTransport(transport) ||
                    !m_entities.CanRemoveEntity(transport, EntityRemoveReason.Remove).IsSuccess)
                {
                    stale++;
                    continue;
                }

                // This is the same normal removal command used by the native area-removal tool.
                m_scheduler.ScheduleInputCmd(
                    new StartDeconstructionOfStaticEntityCmd(
                        transport.Id,
                        EntityRemoveReason.Remove,
                        isAreaRemove: true));
                if (quickRemove)
                {
                    m_scheduler.ScheduleInputCmd(new FinishBuildOfStaticEntityCmd(transport.Id, payWithUnity: true));
                }
                queued++;
            }
            return (queued, stale);
        }

        private (int queued, int stale) QueueProductCleanup(bool quickRemove)
        {
            int queued = 0;
            int stale = 0;
            foreach (SafeAreaSelectionEntry entry in m_pending)
            {
                if (!m_entities.TryGetEntity<IStaticEntity>(new EntityId(entry.EntityId), out IStaticEntity? entity) ||
                    entity is null || entity.IsDestroyed)
                {
                    stale++;
                    continue;
                }

                IReadOnlyList<SafeAreaProductPreview> current = BuildProductPreview(entity);
                foreach (SafeAreaProductPreview requested in entry.Products)
                {
                    SafeAreaProductPreview? available = current.FirstOrDefault(candidate => string.Equals(
                        candidate.ProductId,
                        requested.ProductId,
                        StringComparison.Ordinal));
                    int currentQuantity = available?.Quantity ?? 0;
                    int quantity = Math.Min(requested.Quantity, currentQuantity);
                    if (quantity <= 0 || !TryGetProduct(requested.ProductId, out ProductProto? product) || product is null ||
                        !m_products.CanBeCleared(product))
                    {
                        stale++;
                        continue;
                    }
                    if (queued >= SafeAreaCleanupLimits.MaxProductCommands)
                    {
                        return (queued, stale + 1);
                    }

                    // The quick flag is a policy/confirmation distinction. The command still
                    // carries the exact reviewed quantity and uses the authoritative accounting
                    // path, avoiding an accidental "clear everything" operation.
                    m_scheduler.ScheduleInputCmd(
                        new SafeAreaCleanupProductCmd(
                            entity.Id,
                            new ProductProto.ID(requested.ProductId),
                            new Quantity(quantity)));
                    queued++;
                }
            }
            return (queued, stale);
        }

        private bool TryGetProduct(string productId, out ProductProto? product)
        {
            foreach (ProductProto candidate in m_products.SlimIdManager.ManagedProtos)
            {
                if (string.Equals(candidate.Id.Value, productId, StringComparison.Ordinal))
                {
                    product = candidate;
                    return true;
                }
            }

            product = null;
            return false;
        }

        private static IReadOnlyList<SafeAreaProductPreview> BuildProductPreview(IStaticEntity entity)
        {
            var quantities = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (IProductBufferReadOnly buffer in SafeAreaCleanupCommandsProcessor.EnumerateReadOnlyBuffers(entity))
            {
                if (buffer is null || buffer.Quantity.IsNotPositive || buffer.Product is null ||
                    buffer.Product.IsPhantom)
                {
                    continue;
                }

                string productId = buffer.Product.Id.Value;
                if (quantities.TryGetValue(productId, out int existing))
                {
                    quantities[productId] = checked(existing + buffer.Quantity.Value);
                }
                else
                {
                    quantities.Add(productId, buffer.Quantity.Value);
                }
            }

            return quantities
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new SafeAreaProductPreview(pair.Key, pair.Value))
                .ToArray();
        }
    }
}
