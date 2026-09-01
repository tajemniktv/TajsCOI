// Taj's COI Mods | TajsResearchQueueWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Threading;
using Mafi;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Research;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Tweaks.Features.Research;
using Column = Mafi.Unity.UiToolkit.Library.Column;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using Button = Mafi.Unity.UiToolkit.Library.Button;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Small native-UI adapter over ResearchManager's save-aware queue. It owns no research
    ///     state; every mutation is scheduled as the game's normal input command.
    /// </summary>
    internal sealed class TajsResearchQueueWindow : Window
    {
        private readonly ResearchQueueController m_queue;
        private readonly IInputScheduler m_scheduler;
        private readonly Column m_content;
        private readonly Label m_status;
        private readonly object m_commandGate = new();
        private IInputCommand? m_pendingCommand;
        private string m_pendingDescription = string.Empty;
        private int m_reconciliationRequested;

        internal TajsResearchQueueWindow(
            ResearchManager manager,
            IInputScheduler scheduler,
            ProtosDb protosDb,
            UiRoot uiRoot)
            : base("Research queue".AsLoc())
        {
            m_queue = new ResearchQueueController(manager, scheduler, protosDb);
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            WindowSize(new Px(640f), new Px(560f));
            Panel panel = new Panel().BodyGap(new Px(5f));
            var header = new Row(new Px(5f));
            header.Add(new Label("Native research queue".AsLoc()).FontBold());
            header.Add(new ButtonText(Button.General, "Refresh".AsLoc(), ManualRefresh));
            panel.Add(header);
            m_status = new Label(string.Empty.AsLoc());
            panel.Add(m_status);
            m_content = new Column(3.pt()).AlignItemsStretch();
            var scroll = new ScrollColumn();
            scroll.Add(m_content);
            panel.Add(scroll);
            Body.Add(panel);
            m_scheduler.OnCommandProcessed.AddNonSaveable(this, OnCommandProcessed);
            OnCloseStart += OnCloseStartInternal;
            Refresh();
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            Open(uiRoot);
        }

        /// <summary>
        ///     Called by the host's render callback after the native input event has fired. The
        ///     event itself only records a flag because it is raised on the simulation thread.
        /// </summary>
        internal void RefreshPending()
        {
            bool eventRequested = Interlocked.Exchange(ref m_reconciliationRequested, 0) != 0;

            IInputCommand? command;
            string description;
            lock (m_commandGate)
            {
                command = m_pendingCommand;
                if (command is null)
                {
                    return;
                }
                // OnCommandProcessed is the normal wake-up. InputScheduler also marks a
                // command processed when its processor throws, but deliberately skips that
                // event, so inspect only this pending command as a bounded render-thread
                // fallback. IsProcessed covers both successful and failed terminal states.
                if (!ResearchQueuePolicy.IsPendingCommandTerminal(command.IsProcessed))
                {
                    if (eventRequested)
                    {
                        Interlocked.Exchange(ref m_reconciliationRequested, 1);
                    }
                    return;
                }
                description = m_pendingDescription;
                m_pendingCommand = null;
                m_pendingDescription = string.Empty;
            }

            RefreshContent();
            if (!command.ResultSet)
            {
                SetStatus(description + "; native command failed; native state reconciled.");
            }
            else if (command.HasError)
            {
                SetStatus("Not changed: " + command.ErrorMessage);
            }
            else
            {
                SetStatus(description + "; native state reconciled.");
            }
        }

        private void Refresh()
        {
            RefreshContent();
            SetStatus(BuildSummary());
        }

        private void ManualRefresh()
        {
            lock (m_commandGate)
            {
                if (m_pendingCommand is not null)
                {
                    RefreshContent();
                    SetStatus("Refresh complete; native command is still pending. " + BuildSummary());
                    return;
                }
            }
            Refresh();
        }

        private void RefreshContent()
        {
            m_content.Clear();
            foreach (ResearchQueueEntry entry in ResearchQueuePolicy.OrderForDisplay(m_queue.Snapshot()))
            {
                var row = new Row(new Px(4f));
                string state = entry.InProgress
                    ? "in progress " + entry.ProgressPercent + "%"
                    : entry.InQueue
                        ? "queued #" + (entry.QueueIndex + 1)
                        : entry.Researched
                            ? "researched"
                            : entry.Locked
                                ? "locked"
                                : entry.Available ? "available" : "unavailable";
                row.Add(new Label((entry.PrototypeId + "  [" + state + "]").AsLoc()).Width(new Px(360f)));
                if (entry.InProgress)
                {
                    row.Add(new ButtonText(Button.General, "Cancel".AsLoc(), ApplyStop));
                }
                else if (entry.CanQueue)
                {
                    row.Add(new ButtonText(Button.General, "Queue".AsLoc(), () => Apply(entry.PrototypeId, true)));
                    if (entry.InQueue)
                    {
                        row.Add(new ButtonText(Button.General, "Remove".AsLoc(), () => Apply(entry.PrototypeId, false)));
                    }
                }
                m_content.Add(row);
            }
        }

        private string BuildSummary()
        {
            string current = m_queue.CurrentId;
            string currentText = string.IsNullOrEmpty(current)
                ? "none"
                : current + " (" + m_queue.CurrentProgressPercent + "%)";
            string queued = string.Join(", ", m_queue.QueueIds);
            return "Current: " + currentText + " | queued: " + (string.IsNullOrEmpty(queued) ? "none" : queued);
        }

        private void Apply(string id, bool enqueue)
        {
            lock (m_commandGate)
            {
                if (m_pendingCommand is not null)
                {
                    SetStatus("Please wait for the pending native research command to complete.");
                    return;
                }

                bool success = enqueue
                    ? m_queue.TryQueue(id, out string reason, out IInputCommand? command)
                    : m_queue.TryDequeue(id, out reason, out command);
                if (!success || command is null)
                {
                    SetStatus("Not changed: " + reason);
                    return;
                }
                m_pendingCommand = command;
                m_pendingDescription = (enqueue ? "Queued " : "Removed ") + id;
                SetStatus(m_pendingDescription + "; waiting for native command completion.");
            }
        }

        private void ApplyStop()
        {
            lock (m_commandGate)
            {
                if (m_pendingCommand is not null)
                {
                    SetStatus("Please wait for the pending native research command to complete.");
                    return;
                }
                m_pendingCommand = m_queue.ScheduleStop();
                m_pendingDescription = "Cancelled current research";
                SetStatus(m_pendingDescription + "; waiting for native command completion.");
            }
        }

        private void OnCommandProcessed(IInputCommand command)
        {
            lock (m_commandGate)
            {
                if (!ReferenceEquals(command, m_pendingCommand))
                {
                    return;
                }
                // Publish the wake-up while the pending-command identity is
                // stable; a render tick cannot otherwise observe a stale
                // event after the command has already been replaced.
                Interlocked.Exchange(ref m_reconciliationRequested, 1);
            }
        }

        private void OnCloseStartInternal(Window _)
        {
            m_scheduler.OnCommandProcessed.RemoveNonSaveable(this, OnCommandProcessed);
            OnCloseStart -= OnCloseStartInternal;
        }

        private void SetStatus(string value) => m_status.Value(value.AsLoc());
    }
}
