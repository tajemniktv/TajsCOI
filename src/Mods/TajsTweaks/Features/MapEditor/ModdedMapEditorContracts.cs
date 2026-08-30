// Taj's COI Mods | ModdedMapEditorContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Tweaks.Features.MapEditor
{
    internal readonly struct MapEditorModManifest
    {
        internal MapEditorModManifest(string id, string version)
        {
            Id = id?.Trim() ?? string.Empty;
            Version = version?.Trim() ?? string.Empty;
        }

        internal string Id { get; }
        internal string Version { get; }
        internal bool IsValid => !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(Version);
    }

    internal readonly struct MapEditorModDecision
    {
        internal MapEditorModDecision(MapEditorModManifest manifest, bool compatible, string reason)
        {
            Manifest = manifest;
            Compatible = compatible;
            Reason = reason?.Trim() ?? string.Empty;
        }

        internal MapEditorModManifest Manifest { get; }
        internal bool Compatible { get; }
        internal string Reason { get; }
    }

    internal static class MapEditorModSelection
    {
        internal static bool IsCompatible(
            MapEditorModManifest requested,
            IEnumerable<MapEditorModManifest> available)
        {
            if (!requested.IsValid)
            {
                return false;
            }

            return (available ?? Array.Empty<MapEditorModManifest>()).Any(candidate =>
                candidate.IsValid &&
                string.Equals(candidate.Id, requested.Id, StringComparison.Ordinal) &&
                string.Equals(candidate.Version, requested.Version, StringComparison.Ordinal));
        }
    }

    /// <summary>Temporary manifest-only context; resolver/mod instances are deliberately excluded.</summary>
    internal sealed class ModdedMapEditorContext
    {
        private readonly List<MapEditorModManifest> m_manifests = new();
        private readonly List<MapEditorModDecision> m_decisions = new();
        internal bool IsActive { get; private set; }
        internal IReadOnlyList<MapEditorModManifest> Manifests => m_manifests;
        internal IReadOnlyList<MapEditorModDecision> Decisions => m_decisions;

        internal void Begin(IEnumerable<MapEditorModManifest> manifests)
        {
            Clear();
            m_manifests.AddRange(
                (manifests ?? Array.Empty<MapEditorModManifest>()).Where(manifest => manifest.IsValid).GroupBy(manifest => manifest.Id, StringComparer.Ordinal)
                .Select(group => group.First()));
            IsActive = true;
        }

        internal IReadOnlyList<MapEditorModManifest> Resolve(Func<MapEditorModManifest, bool> canResolve)
        {
            m_decisions.Clear();
            List<MapEditorModManifest> compatible = new();
            foreach (MapEditorModManifest manifest in m_manifests)
            {
                bool accepted = canResolve?.Invoke(manifest) == true;
                m_decisions.Add(new MapEditorModDecision(manifest, accepted, accepted ? string.Empty : "manifest could not be resolved in editor mode"));
                if (accepted)
                {
                    compatible.Add(manifest);
                }
            }
            return compatible;
        }

        internal void Clear()
        {
            m_manifests.Clear();
            m_decisions.Clear();
            IsActive = false;
        }
    }
}
