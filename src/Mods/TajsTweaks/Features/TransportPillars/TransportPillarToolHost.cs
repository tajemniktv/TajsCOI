// Taj's COI Mods | TransportPillarToolHost.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Factory.Transports;
using Mafi.Core.GameLoop;
using Mafi.Core.Input;
using TajsCOI.Tweaks.Features.Selection;

namespace TajsCOI.Tweaks.Features.TransportPillars
{
    /// <summary>
    ///     Scene lifecycle and command surface for the pillar planner. The native pillar toolbar
    ///     remains responsible for its single-tile cursor controller; this host owns the bounded
    ///     area preview/commit state and guarantees it dies with the gameplay scene.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class TransportPillarToolHost
    {
        private readonly DependencyResolver m_resolver;
        private TransportPillarSelectionTool? m_tool;
        private bool m_initializationAttempted;

        public TransportPillarToolHost(DependencyResolver resolver, IGameLoopEvents gameLoop)
        {
            m_resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            (gameLoop ?? throw new ArgumentNullException(nameof(gameLoop))).RegisterInitState(this, Initialize);
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);
        }

        private void Initialize()
        {
            if (m_initializationAttempted)
            {
                return;
            }
            m_initializationAttempted = true;
            if (m_resolver.TryResolve(out TransportsManager manager) &&
                m_resolver.TryResolve(out IInputScheduler scheduler))
            {
                m_tool = new TransportPillarSelectionTool(manager, scheduler);
            }
        }

        [ConsoleCommand(
            documentation: "Activates the scene-owned transport pillar planner in add or remove mode.",
            customCommandName: "tajs_transport_pillar_tool")]
        public string Activate(string? mode = null)
        {
            if (m_tool is null)
            {
                return "Transport pillar planner is unavailable in this scene.";
            }

            string normalized = (mode ?? "add").Trim().ToLowerInvariant();
            if (normalized != "add" && normalized != "remove")
            {
                return "Usage: tajs_transport_pillar_tool <add|remove>";
            }

            m_tool.Activate(normalized == "remove" ? TransportPillarToolMode.Remove : TransportPillarToolMode.Add);
            return "Transport pillar planner active (" + normalized + "). Native validity checks remain authoritative.";
        }

        [ConsoleCommand(
            documentation: "Previews bounded transport pillar operations for an inclusive tile rectangle.",
            customCommandName: "tajs_transport_pillar_tool_preview")]
        public string Preview(string minX, string minY, string maxX, string maxY)
        {
            if (m_tool is null || !m_tool.IsActive)
            {
                return "Transport pillar planner is idle; activate add or remove mode first.";
            }
            if (!int.TryParse(minX, out int parsedMinX) || !int.TryParse(minY, out int parsedMinY) ||
                !int.TryParse(maxX, out int parsedMaxX) || !int.TryParse(maxY, out int parsedMaxY))
            {
                return "Usage: tajs_transport_pillar_tool_preview <min-x> <min-y> <max-x> <max-y>";
            }

            var bounds = new SceneAreaBounds(parsedMinX, parsedMinY, parsedMaxX, parsedMaxY);
            if (!bounds.IsWithin(
                    TransportPillarSelectionTool.MaxAreaWidth,
                    TransportPillarSelectionTool.MaxAreaHeight,
                    TransportPillarSelectionTool.MaxAreaCells))
            {
                return "Area rejected: bounds must be ordered and no larger than 64x64 tiles.";
            }

            IReadOnlyList<TransportPillarPlan> plans = m_tool.BuildAreaPreview(bounds);
            int valid = 0;
            foreach (TransportPillarPlan plan in plans)
            {
                if (plan.IsValid)
                {
                    valid++;
                }
            }
            return "Previewed " + plans.Count + " candidate(s); valid=" + valid + ", capped at " +
                   TransportPillarSelectionTool.MaxOperationsPerBatch + ".";
        }

        [ConsoleCommand(
            documentation: "Commits the current pillar planner preview; remove mode requires CONFIRM.",
            customCommandName: "tajs_transport_pillar_tool_commit")]
        public string Commit(string? confirmation = null)
        {
            if (m_tool is null)
            {
                return "Transport pillar planner is unavailable in this scene.";
            }

            bool confirm = string.Equals(confirmation, "CONFIRM", StringComparison.Ordinal);
            TransportPillarBatchResult result = m_tool.ExecuteArea(confirm);
            if (result.ConfirmationRequired)
            {
                return "No pillars were removed. Repeat with CONFIRM.";
            }
            return "Queued " + result.Queued + " pillar operation(s); skipped=" + result.Skipped + ".";
        }

        [ConsoleCommand(
            documentation: "Cancels the active transport pillar planner preview and releases scene state.",
            customCommandName: "tajs_transport_pillar_tool_cancel")]
        public string Cancel()
        {
            if (m_tool is null)
            {
                return "Transport pillar planner is unavailable in this scene.";
            }
            m_tool.Deactivate();
            return "Transport pillar planner cancelled.";
        }

        private void OnTerminate()
        {
            m_tool?.Dispose();
            m_tool = null;
        }
    }
}
