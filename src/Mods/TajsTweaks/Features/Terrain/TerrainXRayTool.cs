// Taj's COI Mods | TerrainXRayTool.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Terrain;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using Mafi.Unity.Terrain;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiStatic.Toolbar;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Settings;
using TajsCOI.Common.Shortcuts;
using UnityEngine;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Owns the scene-local terrain X-ray controller. The renderer's native X-ray seam is
    ///     used directly; terrain simulation arrays are never written by this feature.
    /// </summary>
    internal static class TweaksTerrainXRayFeature
    {
        private static WeakReference<TerrainXRayController>? s_controller;

        internal static void Install(
            DependencyResolver resolver,
            ITajsSettings settings,
            ITajsLogger log)
        {
            if (!resolver.TryResolve(out TerrainManager? terrain) || terrain is null ||
                !resolver.TryResolve(out TerrainRenderer? renderer) || renderer is null ||
                !resolver.TryResolve(out TerrainCursor? cursor) || cursor is null ||
                !resolver.TryResolve(out LinesFactory? lines) || lines is null ||
                !resolver.TryResolve(out ToolbarHud? toolbar) || toolbar is null ||
                !resolver.TryResolve(out IUnityInputMgr? inputManager) || inputManager is null)
            {
                throw new InvalidOperationException(
                    "TerrainManager, TerrainRenderer, TerrainCursor, LinesFactory, ToolbarHud, or IUnityInputMgr unavailable");
            }

            Dispose();
            var controller = new TerrainXRayController(
                terrain,
                renderer,
                cursor,
                lines,
                toolbar,
                inputManager,
                settings,
                log,
                resolver.TryResolve(out IShortcutInputService? shortcutInput) ? shortcutInput : null);
            s_controller = new WeakReference<TerrainXRayController>(controller);
            controller.SyncInputState();
        }

        internal static void ApplySettings()
        {
            if (TryGetController(out TerrainXRayController? controller) && controller is not null)
            {
                controller.ApplySettings();
            }
        }

        internal static void Dispose()
        {
            if (TryGetController(out TerrainXRayController? controller) && controller is not null)
            {
                controller.Dispose();
            }

            s_controller = null;
        }

        private static bool TryGetController(out TerrainXRayController? controller)
        {
            controller = null;
            if (s_controller is null || !s_controller.TryGetTarget(out controller) || controller is null)
            {
                s_controller = null;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    ///     Scene-owned X-ray mode. Radius and depth are deliberately transient tool state, while
    ///     the boolean setting controls whether the mode is restored when a scene is recreated.
    /// </summary>
    internal sealed class TerrainXRayController : IToolbarItemController
    {
        internal const int MinimumRadius = 10;
        internal const int MaximumRadius = 40;
        internal const int DefaultRadius = 20;
        internal const int DefaultDepth = -10;

        private readonly TerrainManager m_terrain;
        private readonly TerrainRenderer m_renderer;
        private readonly TerrainCursor m_cursor;
        private readonly IUnityInputMgr m_inputManager;
        private readonly ITajsSettings m_settings;
        private readonly ITajsLogger m_log;
        private readonly TerrainCircleRenderer m_topCircle;
        private readonly TerrainCircleRenderer m_bottomCircle;
        private readonly Button m_toolbarButton;
        private readonly IDisposable? m_shortcutRegistration;
        private readonly HashSet<Chunk2i> m_activeChunks = new();
        private readonly HashSet<Chunk2i> m_nextChunks = new();
        private readonly HashSet<Chunk2i> m_updateChunks = new();

        private Tile2i m_previousCenter;
        private int m_previousRadius;
        private int m_previousDepth;
        private bool m_hasAppliedState;
        private bool m_disposed;
        private bool m_inputLifecycleTransition;

        internal TerrainXRayController(
            TerrainManager terrain,
            TerrainRenderer renderer,
            TerrainCursor cursor,
            LinesFactory lines,
            ToolbarHud toolbar,
            IUnityInputMgr inputManager,
            ITajsSettings settings,
            ITajsLogger log,
            IShortcutInputService? shortcutInput)
        {
            m_terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            m_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            m_cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
            m_inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
            m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
            m_log = log ?? throw new ArgumentNullException(nameof(log));
            Radius = DefaultRadius;
            Depth = DefaultDepth;

            m_topCircle = new TerrainCircleRenderer(lines ?? throw new ArgumentNullException(nameof(lines)));
            m_topCircle.SetColor(new Color(0.8f, 0.8f, 0.8f, 0.3f));
            m_topCircle.SetWidth(0.5f);
            m_topCircle.Hide();
            m_bottomCircle = new TerrainCircleRenderer(lines);
            m_bottomCircle.SetColor(new Color(0.6f, 0.6f, 0.6f, 0.3f));
            m_bottomCircle.SetWidth(0.5f);
            m_bottomCircle.Hide();
            m_toolbarButton = toolbar.AddToolButton(
                Localize("Terrain X-ray"),
                this,
                "Assets/Unity/UserInterface/Toolbar/XRayView.svg",
                940f);
            m_shortcutRegistration = shortcutInput?.RegisterHandler(
                "TajsTweaks.TerrainXRay",
                () => m_inputManager.ToggleController(this));
        }

        internal Tile2i Center { get; private set; }

        internal int Radius { get; private set; }

        internal int Depth { get; private set; }

        public bool IsVisible => !m_disposed;

        public bool DeactivateShortcutsIfNotVisible => true;

        public ControllerConfig Config => ControllerConfig.ToolBlockingCamera;

        public event Action<IToolbarItemController>? VisibilityChanged;

        public void Activate()
        {
            if (m_disposed)
            {
                return;
            }

            m_inputLifecycleTransition = true;
            try
            {
                SetPreferredState(true);
                m_cursor.Activate();
                m_topCircle.Show();
                m_bottomCircle.Show();
            }
            finally
            {
                m_inputLifecycleTransition = false;
            }
        }

        public void Deactivate()
        {
            if (m_disposed)
            {
                return;
            }

            m_inputLifecycleTransition = true;
            try
            {
                SetPreferredState(false);
                RestoreChangedChunks();
                m_cursor.Deactivate();
                m_topCircle.Hide();
                m_bottomCircle.Hide();
            }
            finally
            {
                m_inputLifecycleTransition = false;
            }
        }

        public bool InputUpdate()
        {
            if (m_disposed)
            {
                return false;
            }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                m_inputManager.DeactivateController(this);
                return true;
            }

            if (TerrainXRayModifiers.DepthHeld())
            {
                int value = (-10f * Input.GetAxis("MouseScroll")).CeilToInt();
                if (TerrainXRayModifiers.RadiusHeld())
                {
                    Radius = Mathf.Clamp(Radius + value, MinimumRadius, MaximumRadius);
                    Depth = Mathf.Clamp(Depth, -Radius, 0);
                }
                else
                {
                    Depth = Mathf.Clamp(Depth + value, -Radius, 0);
                }
            }

            UpdateXRay();
            return false;
        }

        internal void ApplySettings()
        {
            if (m_disposed || m_inputLifecycleTransition)
            {
                return;
            }

            SyncInputState();
        }

        internal void SyncInputState()
        {
            if (m_disposed || m_inputLifecycleTransition)
            {
                return;
            }

            bool active = m_inputManager.ActiveControllers.AsEnumerable().Contains(this);
            if (TajsTweaksRuntimeState.TerrainXRay)
            {
                if (!active)
                {
                    m_inputManager.ActivateNewController(this);
                }
            }
            else if (active)
            {
                m_inputManager.DeactivateController(this);
            }
        }

        internal void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_inputLifecycleTransition = true;
            try
            {
                if (m_inputManager.ActiveControllers.AsEnumerable().Contains(this))
                {
                    m_inputManager.DeactivateController(this);
                }
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Terrain X-ray controller could not be removed from input state during scene termination.");
            }
            finally
            {
                m_inputLifecycleTransition = false;
            }

            // Deactivate() may not be called when the resolver tears down a scene. Always
            // restore the renderer before destroying scene-owned guide lines.
            RestoreChangedChunks();
            m_cursor.Deactivate();
            m_topCircle.Hide();
            m_bottomCircle.Hide();
            m_topCircle.Destroy();
            m_bottomCircle.Destroy();
            m_shortcutRegistration?.Dispose();
            TerrainXRayModifiers.Reset();
            m_toolbarButton.RemoveFromHierarchy();
            VisibilityChanged?.Invoke(this);
        }

        private void SetPreferredState(bool enabled)
        {
            if (m_disposed)
            {
                return;
            }

            SettingSetResult result = m_settings.TrySet(
                TajsTweaksSettingsCatalog.ModId,
                TajsTweaksSettingsCatalog.TerrainXRay,
                enabled);
            if (!result.Success)
            {
                m_log.Warning("Terrain X-ray preference could not be saved: " + result.Error);
            }
        }

        private void UpdateXRay()
        {
            if (!m_cursor.HasValue)
            {
                if (m_hasAppliedState)
                {
                    RestoreChangedChunks();
                }
                return;
            }

            Tile2i center = m_cursor.Tile2i;
            bool changed = !m_hasAppliedState || center != m_previousCenter ||
                           Radius != m_previousRadius || Depth != m_previousDepth;
            if (!changed)
            {
                return;
            }

            TerrainXRayChunkIndex.ComputeAffectedChunks(
                center,
                Radius,
                m_terrain.TerrainWidth,
                m_terrain.TerrainHeight,
                m_nextChunks);

            TerrainXRayChunkDiff diff = TerrainXRayChunkIndex.Diff(m_activeChunks, m_nextChunks, stateChanged: true);
            m_updateChunks.Clear();
            m_updateChunks.UnionWith(diff.UpdateChunks());
            m_renderer.SetXRayData(center, new RelTile1i(Radius), new ThicknessTilesI(Depth));
            foreach (Chunk2i chunk in m_updateChunks)
            {
                m_renderer.NotifyChunkUpdated(chunk);
            }

            m_activeChunks.Clear();
            m_activeChunks.UnionWith(m_nextChunks);
            Center = center;
            m_previousCenter = center;
            m_previousRadius = Radius;
            m_previousDepth = Depth;
            m_hasAppliedState = true;
            m_topCircle.SetCircle(center.CornerTile2f, new RelTile1i(Radius), m_terrain.GetHeight(center));
            m_bottomCircle.SetCircle(
                center.CornerTile2f,
                new RelTile1i((Radius * 3) / 4),
                m_terrain.GetHeight(center) + new ThicknessTilesI(Depth).ThicknessTilesF);
        }

        private void RestoreChangedChunks()
        {
            if (m_activeChunks.Count == 0 && !m_hasAppliedState)
            {
                return;
            }

            m_renderer.DisableXRay();
            m_updateChunks.Clear();
            m_updateChunks.UnionWith(m_activeChunks);
            foreach (Chunk2i chunk in m_updateChunks)
            {
                m_renderer.NotifyChunkUpdated(chunk);
            }
            m_activeChunks.Clear();
            m_hasAppliedState = false;
        }

        private static LocStrFormatted Localize(string text) =>
            LocalizationManager.CreateAlreadyLocalizedStr("TajsTweaksTerrainXRay_" + text, text).AsFormatted;
    }

    /// <summary>
    ///     Computes terrain (256x256) chunks intersecting the native circular X-ray footprint.
    ///     Kept pure so edge clamping and set-diff behavior can be regression-tested without a
    ///     live Unity scene.
    /// </summary>
    internal static class TerrainXRayChunkIndex
    {
        // ChunkBasedRenderingManager.GetChunkIndex uses tileCoord >> 8 in 0.8.7b.
        internal const int ChunkSize = 256;

        internal static HashSet<Chunk2i> ComputeAffectedChunks(
            Tile2i center,
            int radius,
            int terrainWidth,
            int terrainHeight)
        {
            var result = new HashSet<Chunk2i>();
            ComputeAffectedChunks(center, radius, terrainWidth, terrainHeight, result);
            return result;
        }

        internal static void ComputeAffectedChunks(
            Tile2i center,
            int radius,
            int terrainWidth,
            int terrainHeight,
            ISet<Chunk2i> destination)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (radius < 0 || terrainWidth <= 0 || terrainHeight <= 0)
            {
                return;
            }

            int minX = (int)Math.Max(0L, (long)center.X - radius);
            int maxX = (int)Math.Min(terrainWidth - 1L, (long)center.X + radius);
            int minY = (int)Math.Max(0L, (long)center.Y - radius);
            int maxY = (int)Math.Min(terrainHeight - 1L, (long)center.Y + radius);
            if (minX > maxX || minY > maxY)
            {
                return;
            }

            int minChunkX = minX / ChunkSize;
            int maxChunkX = maxX / ChunkSize;
            int minChunkY = minY / ChunkSize;
            int maxChunkY = maxY / ChunkSize;
            long radiusSquared = (long)radius * radius;
            for (int chunkY = minChunkY; chunkY <= maxChunkY; chunkY++)
            {
                int chunkMinY = chunkY * ChunkSize;
                int chunkMaxY = Math.Min(terrainHeight - 1, chunkMinY + ChunkSize - 1);
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    int chunkMinX = chunkX * ChunkSize;
                    int chunkMaxX = Math.Min(terrainWidth - 1, chunkMinX + ChunkSize - 1);
                    int nearestX = center.X < chunkMinX ? chunkMinX : center.X > chunkMaxX ? chunkMaxX : center.X;
                    int nearestY = center.Y < chunkMinY ? chunkMinY : center.Y > chunkMaxY ? chunkMaxY : center.Y;
                    long dx = (long)nearestX - center.X;
                    long dy = (long)nearestY - center.Y;
                    if (dx * dx + dy * dy <= radiusSquared)
                    {
                        destination.Add(new Chunk2i(chunkX, chunkY));
                    }
                }
            }
        }

        internal static TerrainXRayChunkDiff Diff(
            ISet<Chunk2i> previous,
            ISet<Chunk2i> next,
            bool stateChanged)
        {
            if (previous is null || next is null)
            {
                throw new ArgumentNullException(previous is null ? nameof(previous) : nameof(next));
            }

            var entered = new HashSet<Chunk2i>(next);
            entered.ExceptWith(previous);
            var exited = new HashSet<Chunk2i>(previous);
            exited.ExceptWith(next);
            var changed = new HashSet<Chunk2i>();
            if (stateChanged)
            {
                changed.UnionWith(previous);
                changed.IntersectWith(next);
            }
            return new TerrainXRayChunkDiff(entered, exited, changed);
        }
    }

    /// <summary>
    ///     Modifier providers are replaceable by the shared shortcut layer (#141). Unity key
    ///     polling remains the fail-open fallback for scenes where that layer is unavailable.
    /// </summary>
    internal static class TerrainXRayModifiers
    {
        private static readonly object s_gate = new();
        private static Func<bool> s_depthHeld = DefaultDepthHeld;
        private static Func<bool> s_radiusHeld = DefaultRadiusHeld;

        internal static bool DepthHeld()
        {
            lock (s_gate)
            {
                return s_depthHeld();
            }
        }

        internal static bool RadiusHeld()
        {
            lock (s_gate)
            {
                return s_radiusHeld();
            }
        }

        internal static void Configure(Func<bool>? depthHeld, Func<bool>? radiusHeld)
        {
            lock (s_gate)
            {
                s_depthHeld = depthHeld ?? DefaultDepthHeld;
                s_radiusHeld = radiusHeld ?? DefaultRadiusHeld;
            }
        }

        internal static void Reset() => Configure(null, null);

        private static bool DefaultDepthHeld() =>
            Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        private static bool DefaultRadiusHeld() =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    internal readonly struct TerrainXRayChunkDiff
    {
        internal TerrainXRayChunkDiff(
            IReadOnlyCollection<Chunk2i> entered,
            IReadOnlyCollection<Chunk2i> exited,
            IReadOnlyCollection<Chunk2i> changed)
        {
            Entered = entered;
            Exited = exited;
            Changed = changed;
        }

        internal IReadOnlyCollection<Chunk2i> Entered { get; }

        internal IReadOnlyCollection<Chunk2i> Exited { get; }

        internal IReadOnlyCollection<Chunk2i> Changed { get; }

        internal IEnumerable<Chunk2i> UpdateChunks()
        {
            return Entered.Concat(Exited).Concat(Changed).Distinct();
        }
    }
}
