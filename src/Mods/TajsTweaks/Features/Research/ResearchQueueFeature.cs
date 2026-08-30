// Taj's COI Mods | ResearchQueueFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Research;

namespace TajsCOI.Tweaks.Features.Research
{
    /// <summary>
    ///     Adapter over CoI 0.8.7b's native queue. The controller stores no research progress and
    ///     schedules only serializable input commands; native prerequisite and persistence rules win.
    /// </summary>
    internal sealed class ResearchQueueController
    {
        private readonly ResearchManager m_manager;
        private readonly IInputScheduler m_scheduler;
        private readonly ProtosDb m_protosDb;

        internal ResearchQueueController(ResearchManager manager, IInputScheduler scheduler, ProtosDb protosDb)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            m_protosDb = protosDb ?? throw new ArgumentNullException(nameof(protosDb));
        }

        internal IReadOnlyList<string> QueueIds => m_manager.ResearchQueue
            .Select(node => node.Proto.Id.Value)
            .ToArray();

        internal string CurrentId => m_manager.CurrentResearch.HasValue
            ? m_manager.CurrentResearch.Value.Proto.Id.Value
            : string.Empty;

        internal IReadOnlyList<ResearchQueueEntry> Snapshot()
        {
            return m_manager.AllNodes
                .Select(node => new ResearchQueueEntry(
                    node.Proto.Id.Value,
                    node.Proto.IsAvailable,
                    node.State == ResearchNodeState.Researched,
                    node.IsLockedByCondition))
                .ToArray();
        }

        internal bool TryQueue(string prototypeId, out string reason)
        {
            if (!TryGetNode(prototypeId, out ResearchNodeProto? proto, out reason))
            {
                return false;
            }
            m_scheduler.ScheduleInputCmd(new ResearchQueueDequeueCmd(proto!.Id, isEnqueue: true));
            reason = string.Empty;
            return true;
        }

        internal bool TryDequeue(string prototypeId, out string reason)
        {
            if (!TryGetNode(prototypeId, out ResearchNodeProto? proto, out reason))
            {
                return false;
            }
            m_scheduler.ScheduleInputCmd(new ResearchQueueDequeueCmd(proto!.Id, isEnqueue: false));
            reason = string.Empty;
            return true;
        }

        internal bool TryStart(string prototypeId, out string reason)
        {
            if (!TryGetNode(prototypeId, out ResearchNodeProto? proto, out reason))
            {
                return false;
            }
            m_scheduler.ScheduleInputCmd(new ResearchStartCmd(proto!.Id));
            reason = string.Empty;
            return true;
        }

        private bool TryGetNode(string prototypeId, out ResearchNodeProto? proto, out string reason)
        {
            proto = null;
            string id = prototypeId?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                reason = "research prototype ID is empty";
                return false;
            }
            if (!m_protosDb.TryGetProto(new ResearchNodeProto.ID(id), out proto))
            {
                reason = "research prototype was not found";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }
}
