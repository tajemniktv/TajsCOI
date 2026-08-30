// Taj's COI Mods | TransportPillarSelectionTool.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Input;
using TajsCOI.Tweaks.Features.Selection;

namespace TajsCOI.Tweaks.Features.TransportPillars
{
    internal enum TransportPillarToolMode
    {
        Add,
        Remove,
    }

    /// <summary>
    ///     Value-only description of a pillar operation. It is intentionally scene-owned: a plan
    ///     is stale as soon as the scene changes and must be revalidated before a command is queued.
    /// </summary>
    internal sealed class TransportPillarPlan
    {
        internal TransportPillarPlan(
            TransportPillarToolMode mode,
            int? transportId,
            int supportIndex,
            int? pillarId,
            Tile2i position,
            HeightTilesI topHeight,
            bool valid,
            string reason)
        {
            Mode = mode;
            TransportId = transportId;
            SupportIndex = supportIndex;
            PillarId = pillarId;
            Position = position;
            TopHeight = topHeight;
            IsValid = valid;
            Reason = reason ?? string.Empty;
        }

        internal TransportPillarToolMode Mode { get; }
        internal int? TransportId { get; }
        internal int SupportIndex { get; }
        internal int? PillarId { get; }
        internal Tile2i Position { get; }
        internal HeightTilesI TopHeight { get; }
        internal bool IsValid { get; }
        internal string Reason { get; }
    }

    internal sealed class TransportPillarBatchResult
    {
        internal TransportPillarBatchResult(int queued, int skipped, bool confirmationRequired = false)
        {
            Queued = queued;
            Skipped = skipped;
            ConfirmationRequired = confirmationRequired;
        }

        internal int Queued { get; }
        internal int Skipped { get; }
        internal bool ConfirmationRequired { get; }
    }

    /// <summary>
    ///     Scene-owned add/remove planner for transport pillars. Native TransportsManager remains
    ///     the authority for geometry, structural redundancy, occupancy, and command execution.
    ///     The optional preview callback can render a native preview, but this class never mutates
    ///     renderer-only phantom pillars.
    /// </summary>
    internal sealed class TransportPillarSelectionTool : ISceneSelectionOwner, IDisposable
    {
        internal const int MaxAreaWidth = 64;
        internal const int MaxAreaHeight = 64;
        internal const int MaxAreaCells = 4096;
        internal const int MaxOperationsPerBatch = 512;

        private readonly TransportsManager m_manager;
        private readonly IInputScheduler m_scheduler;
        private readonly Action<TransportPillarPlan?>? m_previewChanged;
        private readonly List<TransportPillarPlan> m_areaPlans = new();
        private TransportPillarPlan? m_hoverPlan;
        private bool m_coordinatorActive;
        private bool m_disposed;

        internal TransportPillarSelectionTool(
            TransportsManager manager,
            IInputScheduler scheduler,
            Action<TransportPillarPlan?>? previewChanged = null)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            m_previewChanged = previewChanged;
            Mode = TransportPillarToolMode.Add;
        }

        internal TransportPillarToolMode Mode { get; private set; }
        internal bool IsActive { get; private set; }
        internal TransportPillarPlan? HoverPlan => m_hoverPlan;
        internal IReadOnlyList<TransportPillarPlan> AreaPlans => m_areaPlans;
        internal bool LastAreaQueryTruncated { get; private set; }

        internal bool Activate(TransportPillarToolMode mode)
        {
            if (m_disposed)
            {
                return false;
            }
            if (!SceneSelectionCoordinator.TryActivate(this))
            {
                return false;
            }
            m_coordinatorActive = true;
            Mode = mode;
            IsActive = true;
            ClearPreview();
            return true;
        }

        internal void SetMode(TransportPillarToolMode mode)
        {
            Mode = mode;
            m_hoverPlan = null;
            m_areaPlans.Clear();
            m_previewChanged?.Invoke(null!);
        }

        internal void Deactivate()
        {
            IsActive = false;
            if (m_coordinatorActive)
            {
                SceneSelectionCoordinator.Deactivate(this);
                m_coordinatorActive = false;
            }
            ClearPreview();
        }

        public void CancelSelection() => Deactivate();

        internal TransportPillarPlan Probe(Transport transport, int supportIndex)
        {
            if (transport is null || transport.IsDestroyed)
            {
                return Invalid(null, supportIndex, default, "Transport is no longer available.");
            }

            ImmutableArray<TransportSupportInfo> supports = transport.Trajectory.TilesSupportInfo;
            if (supportIndex < 0 || supportIndex >= supports.Length)
            {
                return Invalid(transport, supportIndex, default, "No transport support exists at the cursor.");
            }

            TransportSupportInfo support = supports[supportIndex];
            if (support.PillarAttachmentType == TransportPillarAttachmentType.NoAttachment)
            {
                return Invalid(transport, supportIndex, support.Position.Xy, "This trajectory point cannot accept a pillar.");
            }

            Tile2i position = support.Position.Xy;
            HeightTilesI topHeight = support.Position.Height;
            if (m_manager.HasPillarAt(position, topHeight, out TransportPillar existing))
            {
                bool removable = m_manager.IsPillarRedundant(position);
                return Mode == TransportPillarToolMode.Remove
                    ? new TransportPillarPlan(
                        Mode,
                        transport.Id.Value,
                        supportIndex,
                        existing.Id.Value,
                        position,
                        topHeight,
                        removable,
                        removable ? string.Empty : "Pillar is structurally required.")
                    : Invalid(transport, supportIndex, position, "A pillar already supports this trajectory.");
            }

            if (Mode == TransportPillarToolMode.Remove)
            {
                return Invalid(transport, supportIndex, position, "No pillar exists at this trajectory point.");
            }

            bool canBuild = m_manager.CanBuildPillarAt(position, topHeight, out _, out _);
            bool canExtend = m_manager.CanExtendPillarAt(position, topHeight, out _, out _);
            return new TransportPillarPlan(
                Mode,
                transport.Id.Value,
                supportIndex,
                null,
                position,
                topHeight,
                canBuild || canExtend,
                canBuild || canExtend ? string.Empty : "Native pillar occupancy or height checks rejected this location.");
        }

        internal TransportPillarPlan UpdateHover(Transport? transport, int supportIndex)
        {
            m_hoverPlan = transport is null
                ? Invalid(null, supportIndex, default, "No transport intersects the cursor.")
                : Probe(transport, supportIndex);
            m_previewChanged?.Invoke(m_hoverPlan);
            return m_hoverPlan;
        }

        internal IReadOnlyList<TransportPillarPlan> BuildAreaPreview(SceneAreaBounds bounds)
        {
            m_areaPlans.Clear();
            LastAreaQueryTruncated = false;
            if (!bounds.IsWithin(MaxAreaWidth, MaxAreaHeight, MaxAreaCells))
            {
                return m_areaPlans;
            }

            var occupiedPositions = new HashSet<Tile2i>();
            if (Mode == TransportPillarToolMode.Add)
            {
                IReadOnlyList<Transport> transports = SceneSelectionWorldQuery.SelectItems(
                    m_manager.Transports,
                    bounds,
                    transport => transport is not null && !transport.IsDestroyed && IntersectsBounds(transport, bounds),
                    transport => transport.Id.Value,
                    MaxOperationsPerBatch,
                    out bool queryTruncated);
                LastAreaQueryTruncated = queryTruncated;
                foreach (Transport transport in transports)
                {
                    if (transport is null || transport.IsDestroyed)
                    {
                        continue;
                    }
                    ImmutableArray<TransportSupportInfo> supports = transport.Trajectory.TilesSupportInfo;
                    for (int index = 0; index < supports.Length && m_areaPlans.Count < MaxOperationsPerBatch; index++)
                    {
                        TransportSupportInfo support = supports[index];
                        if (support.PillarAttachmentType == TransportPillarAttachmentType.NoAttachment ||
                            !bounds.Contains(support.Position.Xy.X, support.Position.Xy.Y) ||
                            !occupiedPositions.Add(support.Position.Xy))
                        {
                            continue;
                        }

                        m_areaPlans.Add(Probe(transport, index));
                    }
                    if (m_areaPlans.Count >= MaxOperationsPerBatch)
                    {
                        break;
                    }
                }
            }
            else
            {
                IReadOnlyList<KeyValuePair<Tile2i, TransportPillar>> pillars = SceneSelectionWorldQuery.SelectItems(
                    m_manager.Pillars,
                    bounds,
                    item => item.Value is not null && !item.Value.IsDestroyed && bounds.Contains(item.Key.X, item.Key.Y),
                    item => item.Value.Id.Value,
                    MaxOperationsPerBatch,
                    out bool queryTruncated);
                LastAreaQueryTruncated = queryTruncated;
                foreach (KeyValuePair<Tile2i, TransportPillar> item in pillars)
                {
                    if (m_areaPlans.Count >= MaxOperationsPerBatch)
                    {
                        break;
                    }
                    if (item.Value is null || item.Value.IsDestroyed)
                    {
                        continue;
                    }
                    if (!bounds.Contains(item.Key.X, item.Key.Y))
                    {
                        continue;
                    }

                    bool redundant = m_manager.IsPillarRedundant(item.Key);
                    m_areaPlans.Add(
                        new TransportPillarPlan(
                            Mode,
                            null,
                            -1,
                            item.Value.Id.Value,
                            item.Key,
                            item.Value.TopTileHeight,
                            redundant,
                            redundant ? string.Empty : "Pillar is structurally required."));
                }
            }

            foreach (TransportPillarPlan plan in m_areaPlans)
            {
                m_previewChanged?.Invoke(plan);
            }
            return m_areaPlans;
        }

        internal TransportPillarBatchResult ExecuteHover(bool confirmRemoval = false)
        {
            if (m_hoverPlan is null)
            {
                return new TransportPillarBatchResult(0, 1);
            }
            TransportPillarBatchResult result = Execute(new[] { m_hoverPlan }, confirmRemoval);
            m_hoverPlan = null;
            m_previewChanged?.Invoke(null!);
            return result;
        }

        internal TransportPillarBatchResult ExecuteArea(bool confirmRemoval = false)
        {
            TransportPillarBatchResult result = Execute(m_areaPlans, confirmRemoval);
            m_areaPlans.Clear();
            m_previewChanged?.Invoke(null!);
            return result;
        }

        internal void ClearPreview()
        {
            m_hoverPlan = null;
            m_areaPlans.Clear();
            m_previewChanged?.Invoke(null!);
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            IsActive = false;
            if (m_coordinatorActive)
            {
                SceneSelectionCoordinator.Deactivate(this);
                m_coordinatorActive = false;
            }
            ClearPreview();
        }

        private TransportPillarBatchResult Execute(IEnumerable<TransportPillarPlan> plans, bool confirmRemoval)
        {
            int queued = 0;
            int skipped = 0;
            bool confirmationRequired = false;
            foreach (TransportPillarPlan plan in plans)
            {
                if (queued >= MaxOperationsPerBatch)
                {
                    skipped++;
                    continue;
                }
                if (plan.Mode == TransportPillarToolMode.Remove)
                {
                    if (!confirmRemoval)
                    {
                        confirmationRequired = true;
                        skipped++;
                        continue;
                    }
                    if (!m_manager.HasPillarAt(plan.Position, plan.TopHeight, out TransportPillar? pillar) ||
                        pillar is null || pillar.IsDestroyed || !m_manager.IsPillarRedundant(plan.Position))
                    {
                        skipped++;
                        continue;
                    }
                    m_scheduler.ScheduleInputCmd(new RemoveTransportPillarCmd(pillar.Id));
                    queued++;
                    continue;
                }

                if (!TryGetTransport(plan.TransportId, out Transport? transport) ||
                    transport is null || transport.IsDestroyed ||
                    !TryRevalidateAdd(transport, plan.SupportIndex, plan.Position, plan.TopHeight))
                {
                    skipped++;
                    continue;
                }
                m_scheduler.ScheduleInputCmd(new AddTransportPillarCmd(transport.Id, plan.SupportIndex));
                queued++;
            }

            return new TransportPillarBatchResult(queued, skipped, confirmationRequired);
        }

        private bool TryRevalidateAdd(Transport transport, int supportIndex, Tile2i position, HeightTilesI topHeight)
        {
            ImmutableArray<TransportSupportInfo> supports = transport.Trajectory.TilesSupportInfo;
            if (supportIndex < 0 || supportIndex >= supports.Length || supports[supportIndex].Position.Xy != position ||
                supports[supportIndex].Position.Height != topHeight ||
                supports[supportIndex].PillarAttachmentType == TransportPillarAttachmentType.NoAttachment ||
                m_manager.HasPillarAt(position, topHeight, out _))
            {
                return false;
            }

            return m_manager.CanBuildOrExtendPillarAt(position, topHeight);
        }

        private TransportPillarPlan Invalid(Transport? transport, int supportIndex, Tile2i position, string reason) =>
            new(Mode, transport?.Id.Value, supportIndex, null, position, default, false, reason);

        private bool TryGetTransport(int? transportId, out Transport? transport)
        {
            if (!transportId.HasValue)
            {
                transport = null;
                return false;
            }

            foreach (Transport candidate in m_manager.Transports)
            {
                if (candidate is not null && candidate.Id.Value == transportId.Value)
                {
                    transport = candidate;
                    return true;
                }
            }

            transport = null;
            return false;
        }

        private static bool IntersectsBounds(Transport transport, SceneAreaBounds bounds)
        {
            foreach (TransportSupportInfo support in transport.Trajectory.TilesSupportInfo)
            {
                if (bounds.Contains(support.Position.Xy.X, support.Position.Xy.Y))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
