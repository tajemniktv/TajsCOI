// Taj's COI Mods | HeightLayerFilterHost.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi;
using Mafi.Core.Entities;
using Mafi.Core.GameLoop;
using Mafi.Core.Prototypes;
using Mafi.Unity.Entities;
using Mafi.Unity.Ports.Io;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    ///     Owns the gameplay-scene lifetime of the height-layer index. Native entity events update
    ///     the index incrementally; all entity and renderer references remain scene-scoped.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class HeightLayerFilterHost : IDisposable
    {
        private readonly DependencyResolver m_resolver;
        private HeightLayerFilterFeature? m_feature;
        private bool m_initialized;
        private bool m_disposed;

        public HeightLayerFilterHost(DependencyResolver resolver, IGameLoopEvents gameLoop)
        {
            m_resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            (gameLoop ?? throw new ArgumentNullException(nameof(gameLoop))).RegisterInitState(this, Initialize);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);
        }

        internal HeightLayerFilterScene? Scene => m_feature?.Scene;

        private void Initialize()
        {
            if (m_initialized || m_disposed)
            {
                return;
            }
            m_initialized = true;
            if (!m_resolver.TryResolve(out IEntitiesManager? entities) || entities is null)
            {
                return;
            }

            MbBasedEntitiesRenderer? renderer = m_resolver.TryResolve(out MbBasedEntitiesRenderer? resolvedRenderer)
                ? resolvedRenderer
                : null;
            IoPortsRenderer? portsRenderer = m_resolver.TryResolve(out IoPortsRenderer? resolvedPortsRenderer)
                ? resolvedPortsRenderer
                : null;
            m_feature = new HeightLayerFilterFeature(
                entities,
                binding: renderer is null
                    ? null
                    : entity => HeightLayerNativeBinding.TryCreate(entity, renderer, portsRenderer));
        }

        [Mafi.Core.Console.ConsoleCommand(
            documentation: "Sets the active construction height layer; entities crossing the cutoff remain visible.",
            customCommandName: "tajs_height_layer_set")]
        public string Set(string height)
        {
            if (m_feature is null || !int.TryParse(height, out int parsed))
            {
                return "Usage: tajs_height_layer_set <height>";
            }
            m_feature.SetActiveLayer(parsed);
            return m_feature.Status();
        }

        [Mafi.Core.Console.ConsoleCommand(
            documentation: "Raises the active construction height layer by one tile.",
            customCommandName: "tajs_height_layer_up")]
        public string Up()
        {
            if (m_feature is null)
            {
                return "Height layer filter is unavailable in this scene.";
            }
            int next = (m_feature.Scene.ActiveLayer ?? 0) + 1;
            m_feature.SetActiveLayer(next);
            return m_feature.Status();
        }

        [Mafi.Core.Console.ConsoleCommand(
            documentation: "Lowers the active construction height layer by one tile.",
            customCommandName: "tajs_height_layer_down")]
        public string Down()
        {
            if (m_feature is null)
            {
                return "Height layer filter is unavailable in this scene.";
            }
            int next = (m_feature.Scene.ActiveLayer ?? 0) - 1;
            m_feature.SetActiveLayer(next);
            return m_feature.Status();
        }

        [Mafi.Core.Console.ConsoleCommand(
            documentation: "Restores all construction layers and selection hit testing.",
            customCommandName: "tajs_height_layer_show_all")]
        public string ShowAll()
        {
            if (m_feature is null)
            {
                return "Height layer filter is unavailable in this scene.";
            }
            m_feature.ShowAll();
            return m_feature.Status();
        }

        [Mafi.Core.Console.ConsoleCommand(
            documentation: "Shows the active construction layer HUD indicator.",
            customCommandName: "tajs_height_layer_status")]
        public string Status() => m_feature?.Status() ?? "Height layer filter is unavailable in this scene.";

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_feature?.Dispose();
            m_feature = null;
        }

        private void OnTerminate() => Dispose();
    }
}
