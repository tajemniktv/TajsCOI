// Taj's COI Mods | ProcessHarmonyPatchOwnership.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace TajsCOI.Performance
{
    /// <summary>
    ///     Reads process-lifetime Harmony ownership without retaining any gameplay-scene object.
    ///     Performance features use this before installing a patch from a recreated resolver.
    /// </summary>
    internal static class ProcessHarmonyPatchOwnership
    {
        internal static bool HasExpected(IEnumerable<Patch>? patches, string owner, MethodInfo patchMethod) =>
            patches?.Any(x => string.Equals(x.owner, owner, StringComparison.Ordinal) && x.PatchMethod == patchMethod) == true;

        internal static bool HasOwner(IEnumerable<Patch>? patches, string owner) =>
            patches?.Any(x => string.Equals(x.owner, owner, StringComparison.Ordinal)) == true;

        internal static string Describe(Patches? patches)
        {
            if (patches is null)
            {
                return "none";
            }

            var entries = new List<string>();
            Append(entries, "prefix", patches.Prefixes);
            Append(entries, "postfix", patches.Postfixes);
            Append(entries, "transpiler", patches.Transpilers);
            Append(entries, "finalizer", patches.Finalizers);
            return entries.Count == 0 ? "none" : string.Join(", ", entries);
        }

        private static void Append(ICollection<string> entries, string kind, IEnumerable<Patch>? patches)
        {
            if (patches is null)
            {
                return;
            }

            foreach (IGrouping<string, Patch> group in patches.GroupBy(x => x.owner, StringComparer.Ordinal))
            {
                entries.Add($"{kind}:{group.Key} x{group.Count()}");
            }
        }
    }
}
