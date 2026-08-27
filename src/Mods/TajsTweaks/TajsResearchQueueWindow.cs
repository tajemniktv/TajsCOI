// Taj's COI Mods | TajsResearchQueueWindow.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Research;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Tweaks.Features.Research;
using UnityEngine;
using UnityEngine.UIElements;
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
        private readonly Column m_content;
        private readonly Label m_status;

        internal TajsResearchQueueWindow(
            ResearchManager manager,
            IInputScheduler scheduler,
            ProtosDb protosDb,
            UiRoot uiRoot)
            : base("Research queue".AsLoc())
        {
            m_queue = new ResearchQueueController(manager, scheduler, protosDb);
            WindowSize(new Px(640f), new Px(560f));
            Panel panel = new Panel().BodyGap(new Px(5f));
            Row header = new Row(new Px(5f));
            header.Add(new Label("Native research queue".AsLoc()).FontBold());
            header.Add(new ButtonText(Button.General, "Refresh".AsLoc(), Refresh));
            panel.Add(header);
            m_status = new Label(string.Empty.AsLoc());
            panel.Add(m_status);
            m_content = new Column(3.pt()).AlignItemsStretch();
            ScrollColumn scroll = new ScrollColumn();
            scroll.Add(m_content);
            panel.Add(scroll);
            Body.Add(panel);
            Refresh();
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            Open(uiRoot);
        }

        private void Refresh()
        {
            m_content.Clear();
            string current = m_queue.CurrentId;
            string queued = string.Join(", ", m_queue.QueueIds.ToArray());
            m_status.Value(("Current: " + (string.IsNullOrEmpty(current) ? "none" : current) +
                            " | queued: " + (string.IsNullOrEmpty(queued) ? "none" : queued)).AsLoc());
            foreach (ResearchQueueEntry entry in m_queue.Snapshot().OrderBy(x => x.PrototypeId, StringComparer.Ordinal))
            {
                Row row = new Row(new Px(4f));
                string state = entry.Researched ? "researched" : entry.Locked ? "locked" : entry.Available ? "available" : "unavailable";
                row.Add(new Label((entry.PrototypeId + "  [" + state + "]").AsLoc()).Width(new Px(360f)));
                if (entry.Available && !entry.Researched && !entry.Locked)
                {
                    row.Add(new ButtonText(Button.General, "Queue".AsLoc(), () => Apply(entry.PrototypeId, true)));
                    row.Add(new ButtonText(Button.General, "Remove".AsLoc(), () => Apply(entry.PrototypeId, false)));
                }
                m_content.Add(row);
            }
        }

        private void Apply(string id, bool enqueue)
        {
            bool success = enqueue ? m_queue.TryQueue(id, out string reason) : m_queue.TryDequeue(id, out reason);
            m_status.Value((success ? (enqueue ? "Queued " : "Removed ") + id : "Not changed: " + reason).AsLoc());
            Refresh();
        }
    }
}
