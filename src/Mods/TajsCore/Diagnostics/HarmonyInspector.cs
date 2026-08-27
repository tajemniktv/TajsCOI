// Taj's COI Mods | HarmonyInspector.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TajsCOI.Common.Diagnostics;

namespace TajsCOI.Core.Diagnostics
{
    /// <summary>
    ///     Captures Harmony's current read-only patch metadata. This is deliberately stateless and
    ///     command/UI driven; it never runs from a frame callback and never changes patch order.
    /// </summary>
    internal static class HarmonyInspector
    {
        private const string TajsOwnerPrefix = "TajsCOI.";

        internal static HarmonyInspectionSnapshot Capture()
        {
            try
            {
                var targets = new List<HarmonyTargetSnapshot>();
                foreach (MethodBase original in Harmony.GetAllPatchedMethods()
                             .OrderBy(method => method.DeclaringType?.Assembly.GetName().Name ?? string.Empty, StringComparer.Ordinal)
                             .ThenBy(method => method.DeclaringType?.FullName ?? string.Empty, StringComparer.Ordinal)
                             .ThenBy(method => method.Name, StringComparer.Ordinal)
                             .ThenBy(FormatHarmonyMethod, StringComparer.Ordinal))
                {
                    Patches? patches = Harmony.GetPatchInfo(original);
                    if (patches is null)
                    {
                        continue;
                    }

                    var entries = new List<HarmonyPatchSnapshot>();
                    AddEntries(entries, HarmonyPatchKind.Prefix, patches.Prefixes);
                    AddEntries(entries, HarmonyPatchKind.Postfix, patches.Postfixes);
                    AddEntries(entries, HarmonyPatchKind.Transpiler, patches.Transpilers);
                    AddEntries(entries, HarmonyPatchKind.Finalizer, patches.Finalizers);
                    if (!entries.Any(entry => entry.IsTajsOwned))
                    {
                        continue;
                    }

                    entries.Sort(CompareEntries);
                    string signature = FormatHarmonyMethod(original);
                    string[] nonTajsOwners = entries
                        .Where(entry => !entry.IsTajsOwned)
                        .Select(entry => entry.OwnerId)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(owner => owner, StringComparer.Ordinal)
                        .ToArray();
                    (HarmonyCollisionRisk risk, string reason) = ClassifyRisk(entries);
                    targets.Add(
                        new HarmonyTargetSnapshot(
                            original.DeclaringType?.Assembly.GetName().Name ?? string.Empty,
                            original.DeclaringType?.FullName ?? "<global>",
                            original.Name,
                            signature,
                            entries,
                            nonTajsOwners,
                            risk,
                            reason));
                }

                return new HarmonyInspectionSnapshot(DateTime.UtcNow, targets);
            }
            catch (Exception exception)
            {
                return HarmonyInspectionSnapshot.Empty(
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static string FormatHarmonyMethod(MethodBase method) => RuntimeMethodFormatter.Format(method);

        private static void AddEntries(
            ICollection<HarmonyPatchSnapshot> destination,
            HarmonyPatchKind kind,
            IEnumerable<Patch> patches)
        {
            foreach (Patch patch in patches ?? Enumerable.Empty<Patch>())
            {
                string owner = (patch.owner ?? string.Empty).Trim();
                MethodInfo? patchMethod = patch.PatchMethod;
                string method = patchMethod is null ? "<unknown>" : FormatHarmonyMethod(patchMethod);
                // Harmony can retain a patch record after its declaring assembly has
                // disappeared (for example while a mod is reloading). Keep the owner in
                // the snapshot even when the method cannot be resolved so shared-target
                // diagnostics do not silently lose a foreign owner.
                if (owner.Length == 0)
                {
                    continue;
                }

                destination.Add(
                    new HarmonyPatchSnapshot(
                        kind,
                        owner,
                        method,
                        patch.priority,
                        patch.before,
                        patch.after,
                        owner.StartsWith(TajsOwnerPrefix, StringComparison.Ordinal),
                        patchMethod is not null && patchMethod.ReturnType == typeof(bool)));
            }
        }

        private static int CompareEntries(HarmonyPatchSnapshot left, HarmonyPatchSnapshot right)
        {
            int result = left.Kind.CompareTo(right.Kind);
            if (result != 0)
            {
                return result;
            }
            result = string.Compare(left.OwnerId, right.OwnerId, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }
            result = string.Compare(left.PatchMethod, right.PatchMethod, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }
            result = right.Priority.CompareTo(left.Priority);
            if (result != 0)
            {
                return result;
            }
            result = string.Compare(string.Join(",", left.Before), string.Join(",", right.Before), StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }
            result = string.Compare(string.Join(",", left.After), string.Join(",", right.After), StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }
            return left.ReturnsBoolean.CompareTo(right.ReturnsBoolean);
        }

        internal static (HarmonyCollisionRisk Risk, string Reason) ClassifyRisk(IReadOnlyList<HarmonyPatchSnapshot> entries)
        {
            var reasons = new List<string>();
            if (entries
                .Where(entry => entry.IsTajsOwned)
                .GroupBy(entry => new { entry.Kind, entry.OwnerId, entry.PatchMethod })
                .Any(group => group.Count() > 1))
            {
                reasons.Add("duplicate Tajs registration for the same owner/kind/patch method");
            }

            HarmonyPatchSnapshot[] transpilers = entries
                .Where(entry => entry.Kind == HarmonyPatchKind.Transpiler)
                .ToArray();
            string[] transpilerOwners = transpilers
                .Select(entry => entry.OwnerId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            bool hasTajsTranspiler = transpilers.Any(entry => entry.IsTajsOwned);
            if (hasTajsTranspiler && transpilerOwners.Length > 1)
            {
                bool hasForeignTranspiler = transpilers.Any(entry => !entry.IsTajsOwned);
                reasons.Add(
                    hasForeignTranspiler
                        ? "Tajs and non-Tajs transpilers share the target"
                        : "multiple Tajs transpiler owners share the target");
            }

            HarmonyPatchSnapshot[] prefixes = entries
                .Where(entry => entry.Kind == HarmonyPatchKind.Prefix)
                .ToArray();
            if (prefixes.Length > 1 && prefixes.Any(IsBooleanPrefix))
            {
                reasons.Add("multiple prefixes include a bool-returning prefix that can suppress the original");
            }

            foreach (IGrouping<HarmonyPatchKind, HarmonyPatchSnapshot> kindGroup in entries.GroupBy(entry => entry.Kind))
            {
                string[] owners = kindGroup.Select(entry => entry.OwnerId).Distinct(StringComparer.Ordinal).ToArray();
                var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (HarmonyPatchSnapshot entry in kindGroup)
                {
                    foreach (string before in entry.Before)
                    {
                        // Harmony's before/after ordering is scoped to one patch category. A
                        // reference to an owner that is absent from this category is normally an
                        // optional integration and must not become a false collision warning.
                        if (owners.Contains(before, StringComparer.Ordinal))
                        {
                            AddOrderingEdge(edges, entry.OwnerId, before);
                        }
                    }
                    foreach (string after in entry.After)
                    {
                        if (owners.Contains(after, StringComparer.Ordinal))
                        {
                            AddOrderingEdge(edges, after, entry.OwnerId);
                        }
                    }
                }
                if (HasCycle(edges))
                {
                    reasons.Add("Harmony " + kindGroup.Key.ToString().ToLowerInvariant() + " before/after constraints contain a cycle");
                }
            }

            string reason = string.Join("; ", reasons.Distinct(StringComparer.Ordinal));
            if (reason.IndexOf("duplicate Tajs", StringComparison.Ordinal) >= 0 ||
                reason.IndexOf("transpilers share", StringComparison.Ordinal) >= 0 ||
                reason.IndexOf("multiple Tajs transpiler owners", StringComparison.Ordinal) >= 0)
            {
                return (HarmonyCollisionRisk.High, reason);
            }
            if (reason.Length > 0)
            {
                return (HarmonyCollisionRisk.Medium, reason);
            }

            bool hasTajs = entries.Any(entry => entry.IsTajsOwned);
            bool hasForeign = entries.Any(entry => !entry.IsTajsOwned);
            return hasTajs && hasForeign
                ? (HarmonyCollisionRisk.Informational, "Tajs and non-Tajs owners share the target; no heuristic conflict was found.")
                : (HarmonyCollisionRisk.None, string.Empty);
        }

        private static bool IsBooleanPrefix(HarmonyPatchSnapshot entry)
        {
            // The snapshot is intentionally value-only. A bool-returning prefix is represented by
            // its formatted method name, so this conservative heuristic is limited to metadata
            // that Harmony exposes without retaining MethodInfo objects in the public snapshot.
            return entry.ReturnsBoolean;
        }

        private static void AddOrderingEdge(Dictionary<string, HashSet<string>> edges, string from, string to)
        {
            if (!edges.TryGetValue(from, out HashSet<string>? destinations))
            {
                destinations = new HashSet<string>(StringComparer.Ordinal);
                edges.Add(from, destinations);
            }
            destinations.Add(to);
        }

        private static bool HasCycle(Dictionary<string, HashSet<string>> edges)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string node in edges.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (HasCycle(node, edges, visiting, visited))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasCycle(
            string node,
            Dictionary<string, HashSet<string>> edges,
            ISet<string> visiting,
            ISet<string> visited)
        {
            if (visiting.Contains(node))
            {
                return true;
            }
            if (!visited.Add(node))
            {
                return false;
            }

            visiting.Add(node);
            if (edges.TryGetValue(node, out HashSet<string>? destinations))
            {
                foreach (string destination in destinations.OrderBy(value => value, StringComparer.Ordinal))
                {
                    if (HasCycle(destination, edges, visiting, visited))
                    {
                        return true;
                    }
                }
            }
            visiting.Remove(node);
            return false;
        }
    }
}
