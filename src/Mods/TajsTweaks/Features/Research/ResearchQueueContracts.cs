// Taj's COI Mods | ResearchQueueContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Tweaks.Features.Research
{
    internal readonly struct ResearchQueueEntry
    {
        internal ResearchQueueEntry(
            string prototypeId,
            bool available,
            bool researched,
            bool locked,
            bool inProgress = false,
            int queueIndex = -1,
            int progressPercent = 0)
        {
            PrototypeId = prototypeId?.Trim() ?? string.Empty;
            Available = available;
            Researched = researched;
            Locked = locked;
            InProgress = inProgress;
            QueueIndex = queueIndex;
            ProgressPercent = Math.Max(0, Math.Min(100, progressPercent));
        }

        internal string PrototypeId { get; }
        internal bool Available { get; }
        internal bool Researched { get; }
        internal bool Locked { get; }
        internal bool InProgress { get; }
        internal int QueueIndex { get; }
        internal int ProgressPercent { get; }
        internal bool InQueue => QueueIndex >= 0;
        internal bool CanQueue => Available && !Researched && !Locked && PrototypeId.Length > 0;
    }

    internal static class ResearchQueuePolicy
    {
        /// <summary>
        ///     InputScheduler marks both successfully processed and failed commands as processed.
        ///     The window may therefore clear only after this terminal marker is visible.
        /// </summary>
        internal static bool IsPendingCommandTerminal(bool isProcessed) => isProcessed;

        internal static IReadOnlyList<ResearchQueueEntry> Validate(IEnumerable<ResearchQueueEntry> entries)
        {
            return (entries ?? Array.Empty<ResearchQueueEntry>())
                .Where(entry => entry.CanQueue)
                .GroupBy(entry => entry.PrototypeId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }

        internal static bool CanReorder(IReadOnlyList<string> queue, string prototypeId, int targetIndex)
        {
            if (queue is null || string.IsNullOrWhiteSpace(prototypeId) || targetIndex < 0 || targetIndex >= queue.Count)
            {
                return false;
            }
            int source = -1;
            for (int index = 0; index < queue.Count; index++)
            {
                if (string.Equals(queue[index], prototypeId, StringComparison.Ordinal))
                {
                    source = index;
                    break;
                }
            }
            return source >= 0;
        }

        internal static IReadOnlyList<string> Reorder(IReadOnlyList<string> queue, string prototypeId, int targetIndex)
        {
            if (!CanReorder(queue, prototypeId, targetIndex))
            {
                return queue?.ToArray() ?? Array.Empty<string>();
            }
            List<string> result = queue.ToList();
            result.Remove(prototypeId);
            result.Insert(Math.Min(targetIndex, result.Count), prototypeId);
            return result;
        }

        internal static IReadOnlyList<ResearchQueueEntry> OrderForDisplay(IEnumerable<ResearchQueueEntry> entries)
        {
            return (entries ?? Array.Empty<ResearchQueueEntry>())
                .OrderBy(entry => entry.InProgress ? 0 : entry.InQueue ? 1 : 2)
                .ThenBy(entry => entry.InQueue ? entry.QueueIndex : int.MaxValue)
                .ThenBy(entry => entry.PrototypeId, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
