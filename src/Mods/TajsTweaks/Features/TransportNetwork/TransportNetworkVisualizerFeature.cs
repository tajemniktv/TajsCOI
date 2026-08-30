// Taj's COI Mods | TransportNetworkVisualizerFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.Entities;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ports.Io;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiStatic.Toolbar;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Logging;
using TajsCOI.Tweaks.Features.Selection;
using UnityEngine;
using EntityId = Mafi.Core.EntityId;

namespace TajsCOI.Tweaks.Features.TransportNetwork
{
    internal static class TransportNetworkVisualizerFeature
    {
        internal const string ComponentId = "TransportNetworkVisualizer";

        internal static TransportNetworkVisualizerController Install(
            DependencyResolver resolver,
            ITajsLogger log)
        {
            if (resolver is null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }
            if (log is null)
            {
                throw new ArgumentNullException(nameof(log));
            }
            if (!resolver.TryResolve(out ToolbarHud? toolbar) || toolbar is null ||
                !resolver.TryResolve(out IUnityInputMgr? inputManager) || inputManager is null ||
                !resolver.TryResolve(out CursorPickingManager? picker) || picker is null ||
                !resolver.TryResolve(out EntitiesRenderingManager? rendering) || rendering is null)
            {
                throw new InvalidOperationException(
                    "ToolbarHud, IUnityInputMgr, CursorPickingManager, or EntitiesRenderingManager unavailable.");
            }

            resolver.TryResolve(out IoPortsRenderer? portsRenderer);
            return new TransportNetworkVisualizerController(
                toolbar,
                inputManager,
                picker,
                rendering,
                portsRenderer,
                log,
                TransportNetworkConnectorAdapters.Snapshot());
        }
    }

    /// <summary>
    ///     Scene-owned transport network tool. A click resolves one rendered seed, then follows
    ///     only connected ports using a visited ID set. Highlight state is always released before
    ///     replacing a selection or tearing down the gameplay scene.
    /// </summary>
    internal sealed class TransportNetworkVisualizerController : IToolbarItemController, IUnityInputController, ISceneSelectionOwner, IDisposable
    {
        internal const int MaximumTraceEntities = TransportNetworkTraversal.DefaultMaximumEntities;

        private static readonly ColorRgba s_segmentColor = new(50, 180, 255, 180);
        private static readonly ColorRgba s_connectorColor = new(255, 190, 45, 210);
        private static readonly ColorRgba s_endpointColor = new(120, 235, 130, 190);

        private readonly Button m_toolbarButton;
        private readonly IUnityInputMgr m_inputManager;
        private readonly CursorPickingManager m_picker;
        private readonly EntitiesRenderingManager m_rendering;
        private readonly IoPortsRenderer? m_portsRenderer;
        private readonly ITajsLogger m_log;
        private readonly List<ITransportNetworkConnectorAdapter> m_adapters;
        private readonly Dictionary<int, IRenderedEntity> m_entitiesById = new();
        private readonly Dictionary<int, ColorRgba> m_entityHighlightColors = new();
        private readonly PooledHighlightUtility<int, HighlightLease> m_entityHighlightPool;
        private readonly HashSet<EntityId> m_activeTraceIds = new();
        private HighlightId m_portHighlight;
        private bool m_hasPortHighlight;
        private bool m_coordinatorActive;
        private bool m_disposed;

        internal TransportNetworkVisualizerController(
            ToolbarHud toolbar,
            IUnityInputMgr inputManager,
            CursorPickingManager picker,
            EntitiesRenderingManager rendering,
            IoPortsRenderer? portsRenderer,
            ITajsLogger log,
            IEnumerable<ITransportNetworkConnectorAdapter>? optionalAdapters = null)
        {
            m_inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
            m_picker = picker ?? throw new ArgumentNullException(nameof(picker));
            m_rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
            m_portsRenderer = portsRenderer;
            m_log = log ?? throw new ArgumentNullException(nameof(log));
            m_adapters = new List<ITransportNetworkConnectorAdapter> { new NativeTransportNetworkConnectorAdapter() };
            if (optionalAdapters is not null)
            {
                foreach (ITransportNetworkConnectorAdapter adapter in optionalAdapters)
                {
                    if (adapter is not null && !m_adapters.Contains(adapter))
                    {
                        m_adapters.Add(adapter);
                    }
                }
            }

            m_toolbarButton = (toolbar ?? throw new ArgumentNullException(nameof(toolbar))).AddToolButton(
                Localize("Transport network"),
                this,
                "Assets/Unity/UserInterface/Toolbar/Transports.svg",
                945f);
            m_toolbarButton.Selected(false);
            m_entityHighlightPool = new PooledHighlightUtility<int, HighlightLease>(
                () => new HighlightLease(),
                (lease, id) =>
                {
                    if (!m_entitiesById.TryGetValue(id, out IRenderedEntity? entity) || entity is null || entity.IsDestroyed ||
                        !m_entityHighlightColors.TryGetValue(id, out ColorRgba color))
                    {
                        throw new InvalidOperationException("The transport network highlight entity is unavailable.");
                    }

                    lease.Handle = m_rendering.AddHighlight(entity, color);
                    if (lease.Handle == 0)
                    {
                        throw new InvalidOperationException("The renderer returned an empty highlight handle.");
                    }
                },
                lease =>
                {
                    if (lease.Handle != 0)
                    {
                        m_rendering.RemoveHighlight(lease.Handle);
                        lease.Handle = 0;
                    }
                });
        }

        internal TransportNetworkTrace? CurrentTrace { get; private set; }

        internal string SelectionStatus { get; private set; } = string.Empty;

        internal bool IsActive => !m_disposed && m_inputManager.ActiveControllers.AsEnumerable().Contains(this);

        public bool IsVisible => !m_disposed;

        public bool DeactivateShortcutsIfNotVisible => true;

        public ControllerConfig Config => ControllerConfig.ToolBlockingCamera;

        public event Action<IToolbarItemController>? VisibilityChanged;

        public void Activate() => TryActivate();

        internal bool TryActivate()
        {
            if (m_disposed || !SceneSelectionCoordinator.TryActivate(this))
            {
                return false;
            }

            m_coordinatorActive = true;
            ClearSelection();
            m_inputManager.ActivateNewController(this);
            m_toolbarButton.Selected(true);
            VisibilityChanged?.Invoke(this);
            return true;
        }

        public void Deactivate()
        {
            if (m_disposed)
            {
                return;
            }

            ClearSelection();
            ReleaseCoordinator();
            m_toolbarButton.Selected(false);
            VisibilityChanged?.Invoke(this);
        }

        public bool InputUpdate()
        {
            if (m_disposed)
            {
                return false;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                SceneSelectionCoordinator.CancelActive();
                ClearSelection();
                m_inputManager.DeactivateController(this);
                ReleaseCoordinator();
                return true;
            }

            if (!Input.GetMouseButtonDown(0))
            {
                return false;
            }

            if (!m_picker.TryPickEntity<IRenderedEntity>(out IRenderedEntity? entity) || entity is null || entity.IsDestroyed)
            {
                ClearSelection();
                return true;
            }

            Select(entity);
            return true;
        }

        internal void Select(IRenderedEntity seed)
        {
            if (m_disposed || seed is null || seed.IsDestroyed)
            {
                ClearSelection();
                return;
            }

            ClearSelection();
            try
            {
                foreach (ITransportNetworkConnectorAdapter adapter in TransportNetworkConnectorAdapters.Snapshot())
                {
                    if (!m_adapters.Contains(adapter))
                    {
                        m_adapters.Add(adapter);
                    }
                }
                CurrentTrace = TransportNetworkTraversal.Trace(seed, Describe, MaximumTraceEntities);
                SelectionStatus = CurrentTrace.IsTruncated
                    ? "Transport network contains at least " + CurrentTrace.Count +
                      " entities; display is bounded at " + MaximumTraceEntities + "."
                    : "Transport network contains " + CurrentTrace.Count + " entities.";
                foreach (TransportNetworkTraceEntry entry in CurrentTrace.Entries)
                {
                    if (m_entitiesById.ContainsKey(entry.EntityId.Value))
                    {
                        continue;
                    }
                    // Describe records entities as the traversal reaches them. This second pass
                    // only retains scene-local rendering references; the trace itself is IDs only.
                    if (entry.EntityId == seed.Id)
                    {
                        m_entitiesById[entry.EntityId.Value] = seed;
                    }
                }
                ApplyEntityHighlights();
                ApplyPortHighlights();
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Transport network selection failed open.");
                ClearSelection();
            }
        }

        internal void ClearSelection()
        {
            ClearPortHighlight();
            m_entityHighlightPool.Clear();
            m_entitiesById.Clear();
            m_entityHighlightColors.Clear();
            m_activeTraceIds.Clear();
            CurrentTrace = null;
            SelectionStatus = string.Empty;
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            try
            {
                if (m_inputManager.ActiveControllers.AsEnumerable().Contains(this))
                {
                    m_inputManager.DeactivateController(this);
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Transport network tool could not be removed from input state.");
            }
            ClearSelection();
            ReleaseCoordinator();
            m_entityHighlightPool.Dispose();
            m_toolbarButton.RemoveFromHierarchy();
        }

        public void CancelSelection() => Deactivate();

        private void ReleaseCoordinator()
        {
            if (!m_coordinatorActive)
            {
                return;
            }

            SceneSelectionCoordinator.Deactivate(this);
            m_coordinatorActive = false;
        }

        private TransportNetworkNodeDescription? Describe(IRenderedEntity entity)
        {
            foreach (ITransportNetworkConnectorAdapter adapter in m_adapters)
            {
                try
                {
                    if (adapter.TryDescribe(entity, out TransportNetworkNodeDescription description) &&
                        description is not null && description.EntityId == entity.Id)
                    {
                        m_entitiesById[entity.Id.Value] = entity;
                        return description;
                    }
                }
                catch (Exception exception)
                {
                    m_log.Exception(exception, "Transport network adapter failed for entity " + entity.Id.Value + ".");
                }
            }

            return null;
        }

        private void ApplyEntityHighlights()
        {
            if (CurrentTrace is null)
            {
                return;
            }

            m_entityHighlightColors.Clear();
            foreach (TransportNetworkTraceEntry entry in CurrentTrace.Entries)
            {
                m_activeTraceIds.Add(entry.EntityId);
                if (m_entitiesById.ContainsKey(entry.EntityId.Value))
                {
                    m_entityHighlightColors[entry.EntityId.Value] = ColorFor(entry.Classification);
                }
            }
            try
            {
                m_entityHighlightPool.Set(m_entityHighlightColors.Keys);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Transport network entity highlighting failed open.");
                m_entityHighlightPool.Clear();
            }
        }

        private void ApplyPortHighlights()
        {
            if (m_portsRenderer is null || m_activeTraceIds.Count == 0)
            {
                return;
            }

            // IoPortsRenderer owns the pooled visuals and computes disconnected-port arrows from
            // IoPort.Type and IoPort.Direction. Connected ports remain colored but receive no
            // guessed arrow; optional adapters without native IoPorts cannot draw one at all.
            m_portHighlight = m_portsRenderer.HighlightPorts(
                port => port is not null && !port.IsDestroyed && m_activeTraceIds.Contains(port.OwnerEntity.Id),
                withoutColliders: true);
            m_hasPortHighlight = true;
        }

        private void ClearPortHighlight()
        {
            if (!m_hasPortHighlight || m_portsRenderer is null)
            {
                m_hasPortHighlight = false;
                return;
            }

            try
            {
                m_portsRenderer.ClearPortsHighlight(m_portHighlight);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Transport network port highlights could not be cleared.");
            }
            finally
            {
                m_hasPortHighlight = false;
            }
        }

        private static ColorRgba ColorFor(TransportNetworkEntityClassification classification) => classification switch
        {
            TransportNetworkEntityClassification.Segment => s_segmentColor,
            TransportNetworkEntityClassification.Connector => s_connectorColor,
            _ => s_endpointColor,
        };

        private static LocStrFormatted Localize(string text) =>
            LocalizationManager.CreateAlreadyLocalizedStr("TajsTweaksTransportNetwork_" + text, text).AsFormatted;

        private sealed class HighlightLease
        {
            internal ulong Handle;
        }
    }
}
