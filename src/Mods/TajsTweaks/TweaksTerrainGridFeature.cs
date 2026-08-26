// Taj's COI Mods | TweaksTerrainGridFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using Mafi;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using Mafi.Unity.Terrain;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiStatic.Toolbar;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.Utils;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Settings;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Adds a persistent toolbar controller backed by the game's native terrain-grid
    ///     activation facility. The controller is weakly discoverable so a scene-owned
    ///     TerrainRenderer is never retained by process-static state.
    /// </summary>
    internal static class TweaksTerrainGridFeature
    {
        private static WeakReference<TerrainGridController>? s_controller;

        internal static void Install(DependencyResolver resolver, ITajsSettings settings, ITajsLogger log)
        {
            if (!resolver.TryResolve(out ToolbarHud toolbar) ||
                !resolver.TryResolve(out TerrainRenderer terrainRenderer) ||
                !resolver.TryResolve(out IUnityInputMgr inputManager))
            {
                throw new InvalidOperationException("ToolbarHud, TerrainRenderer, or IUnityInputMgr unavailable");
            }

            Dispose();
            var controller = new TerrainGridController(toolbar, terrainRenderer, inputManager, settings, log);
            s_controller = new WeakReference<TerrainGridController>(controller);
            controller.SyncInputState();
        }

        internal static void ApplySettings()
        {
            if (TryGetController(out TerrainGridController? controller) && controller is not null)
            {
                controller.ApplySettings();
            }
        }

        internal static void Dispose()
        {
            if (TryGetController(out TerrainGridController? controller) && controller is not null)
            {
                controller.Dispose();
            }

            s_controller = null;
        }

        private static bool TryGetController(out TerrainGridController? controller)
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
    ///     A mode controller intentionally uses ControllerConfig.Mode: turning on another
    ///     tool must not turn off the independent terrain-grid visualization.
    /// </summary>
    internal sealed class TerrainGridController : IToolbarItemController, IUnityInputController
    {
        private readonly IActivator m_gridActivator;
        private readonly IUnityInputMgr m_inputManager;
        private readonly ITajsSettings m_settings;
        private readonly ITajsLogger m_log;
        private readonly Button m_toolbarButton;
        private bool m_disposed;
        private bool m_inputLifecycleTransition;

        public bool IsVisible => !m_disposed;

        public bool DeactivateShortcutsIfNotVisible => true;

        public ControllerConfig Config => ControllerConfig.Mode;

        public event Action<IToolbarItemController>? VisibilityChanged;

        internal TerrainGridController(
            ToolbarHud toolbar,
            TerrainRenderer terrainRenderer,
            IUnityInputMgr inputManager,
            ITajsSettings settings,
            ITajsLogger log)
        {
            m_gridActivator = terrainRenderer.CreateGridLinesActivator();
            m_inputManager = inputManager;
            m_settings = settings;
            m_log = log;
            m_toolbarButton = toolbar.AddToolButton(
                Localize("Terrain grid"),
                this,
                "Assets/Unity/UserInterface/Toolbar/TerrainGrid.svg",
                1f);
            ApplySettings(syncInputState: false);
        }

        public void Activate()
        {
            m_inputLifecycleTransition = true;
            try
            {
                SetPreferredState(true);
            }
            finally
            {
                m_inputLifecycleTransition = false;
            }
        }

        public void Deactivate()
        {
            m_inputLifecycleTransition = true;
            try
            {
                SetPreferredState(false);
            }
            finally
            {
                m_inputLifecycleTransition = false;
            }
        }

        public bool InputUpdate() => false;

        internal void ApplySettings()
        {
            ApplySettings(syncInputState: true);
        }

        private void ApplySettings(bool syncInputState)
        {
            if (m_disposed)
            {
                return;
            }

            // IActivator is the renderer's counted native handle. SetActive keeps repeated
            // scene/settings refreshes idempotent and does not interfere with other native
            // users of the same terrain-grid facility.
            m_gridActivator.SetActive(TajsTweaksRuntimeState.TerrainGrid);
            m_toolbarButton.Selected(m_gridActivator.IsActive);
            VisibilityChanged?.Invoke(this);
            if (syncInputState && !m_inputLifecycleTransition)
            {
                SyncInputState();
            }
        }

        internal void SyncInputState()
        {
            if (m_disposed || m_inputLifecycleTransition)
            {
                return;
            }

            bool isInputControllerActive = m_inputManager.ActiveControllers.AsEnumerable().Contains(this);
            if (TajsTweaksRuntimeState.TerrainGrid)
            {
                if (!isInputControllerActive)
                {
                    m_inputManager.ActivateNewController(this);
                }
            }
            else if (isInputControllerActive)
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
                m_log.Exception(exception, "Terrain grid controller could not be removed from input state during scene termination.");
            }
            finally
            {
                m_inputLifecycleTransition = false;
            }

            // The preference remains true so a recreated gameplay scene restores it, while
            // this scene-owned activation handle is always released before the renderer dies.
            m_gridActivator.DeactivateIfActive();
            m_toolbarButton.RemoveFromHierarchy();
        }

        private void SetPreferredState(bool enabled)
        {
            if (m_disposed)
            {
                return;
            }

            SettingSetResult result = m_settings.TrySet(
                TajsTweaksSettingsCatalog.ModId,
                TajsTweaksSettingsCatalog.TerrainGrid,
                enabled);
            if (!result.Success)
            {
                m_log.Warning("Terrain grid preference could not be saved: " + result.Error);
                return;
            }

            // TrySet notifies the host when the value changes. Refresh explicitly as well so
            // equal-value writes and test/fallback settings implementations stay synchronized.
            ApplySettings();
        }

        private static LocStrFormatted Localize(string text) =>
            LocalizationManager.CreateAlreadyLocalizedStr("TajsTweaksTerrainGrid_" + text, text).AsFormatted;
    }
}
