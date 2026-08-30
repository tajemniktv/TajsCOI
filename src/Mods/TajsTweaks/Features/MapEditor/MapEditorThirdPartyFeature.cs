// Taj's COI Mods | MapEditorThirdPartyFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Unity;
using Mafi.Unity.MainMenu;

namespace TajsCOI.Tweaks.Features.MapEditor
{
    /// <summary>
    ///     Keeps third-party assemblies in the native map-editor load list. Only manifest data is
    ///     retained in the context; the game's TryLoadMods remains the compatibility authority.
    /// </summary>
    internal static class MapEditorThirdPartyFeature
    {
        private const string HarmonyId = "TajsCOI.Tweaks.MapEditorThirdPartyMods";
        private static readonly ModdedMapEditorContext s_context = new();
        private static FieldInfo? s_mainField;
        private static bool s_installed;

        /// <summary>
        ///     Installs from the data-only mod constructor so the first main-menu map-editor
        ///     click is covered. This is an ordinary Main/MainMenuScreen transition hook and has
        ///     no dependency on the optional bootstrap assembly.
        /// </summary>
        internal static void InstallProcess()
        {
            if (!MapEditorStartupSettings.TryReadPersistedBoolean(
                    TajsTweaksSettingsCatalog.ModId + "." + TajsTweaksSettingsCatalog.MapEditorThirdPartyMods,
                    out bool enabled) || !enabled)
            {
                return;
            }

            Install(new Harmony(HarmonyId));
        }

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            if (!MapEditorNativeContract.TryResolve(
                    out MethodInfo? target,
                    out MethodInfo? transition,
                    out MethodInfo? tryLoadMods,
                    out FieldInfo? mainField) ||
                target is null || transition is null || tryLoadMods is null || mainField is null)
            {
                throw new MissingMethodException("0.8.7b map-editor transition contract was not resolved");
            }

            s_mainField = mainField;
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(MapEditorThirdPartyFeature), nameof(Prefix)));
                harmony.Patch(transition, postfix: new HarmonyMethod(typeof(MapEditorThirdPartyFeature), nameof(ClearContext)));
                harmony.Patch(tryLoadMods, prefix: new HarmonyMethod(typeof(MapEditorThirdPartyFeature), nameof(PrefixTryLoadMods)));
            }
            catch
            {
                // Do not leave a half-installed process patch if a version-specific transition
                // seam changes. The caller's normal compatibility path can retry fail-open.
                harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(transition, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(tryLoadMods, HarmonyPatchType.All, harmony.Id);
                throw;
            }
            s_installed = true;
        }

        internal static void Reset()
        {
            s_context.Clear();
        }

        private static bool Prefix(MainMenuScreen __instance)
        {
            try
            {
                if (s_mainField?.GetValue(__instance) is not IMain main)
                {
                    return true;
                }

                LoadedModData[] loadedThirdParty = main.LoadedMods
                    .Where(mod => mod?.Manifest is not null && !mod.Manifest.IsCoreMod && !mod.Manifest.IsDlcMod)
                    .ToArray();
                if (loadedThirdParty.Length == 0)
                {
                    // Preserve vanilla behavior when there is no active third-party content to
                    // carry into the editor.
                    s_context.Clear();
                    return true;
                }

                ImmutableArray<AvailableModData> availableThirdParty = main.AvailableThirdPartyMods;
                MapEditorModManifest[] availableManifests = BuildManifests(availableThirdParty).ToArray();
                s_context.Begin(BuildManifests(loadedThirdParty));
                IReadOnlyList<MapEditorModManifest> compatible = s_context.Resolve(
                    manifest => MapEditorModSelection.IsCompatible(manifest, availableManifests));
                foreach (MapEditorModDecision decision in s_context.Decisions.Where(decision => !decision.Compatible))
                {
                    Log.Warning(
                        "Map editor skipped third-party mod '" + decision.Manifest.Id + "': " + decision.Reason + ".");
                }
                if (compatible.Count == 0)
                {
                    s_context.Clear();
                    return true;
                }

                HashSet<(string Id, string Version)> compatibleManifests = new(
                    compatible.Select(mod => (mod.Id, mod.Version)));
                ImmutableArray<AvailableModData> selectedThirdParty = availableThirdParty
                    .Where(mod => mod?.Manifest is not null &&
                                  compatibleManifests.Contains((mod.Manifest.Id, mod.Manifest.Version.ToString())))
                    .ToImmutableArray();
                ImmutableArray<AvailableModData> selected = main.AvailableMods
                    .SelectCoreAndDlcMods()
                    .Concat(selectedThirdParty);
                if (!main.TryLoadMods(selected, includeMissingCoreMods: true, out ImmutableArray<LoadedModData> loadedMods, out _))
                {
                    s_context.Clear();
                    return true;
                }
                Log.Warning(
                    "Map editor carrying compatible third-party mods: " +
                    string.Join(", ", compatible.Select(mod => mod.Id + "@" + mod.Version)) + ".");
                main.StartMapEditor(Mafi.Collections.ImmutableCollections.ImmutableArray<IConfig>.Empty, loadedMods);
                return false;
            }
            catch
            {
                s_context.Clear();
                return true;
            }
        }

        private static void ClearContext()
        {
            s_context.Clear();
        }

        private static void PrefixTryLoadMods(
            Mafi.Unity.Main __instance,
            ref ImmutableArray<AvailableModData> modsToLoad)
        {
            try
            {
                if (!s_context.IsActive || modsToLoad.Any(mod => mod?.Manifest is not null &&
                                                                  !mod.Manifest.IsCoreMod &&
                                                                  !mod.Manifest.IsDlcMod))
                {
                    return;
                }

                HashSet<(string Id, string Version)> compatibleManifests = new(
                    s_context.Decisions
                        .Where(decision => decision.Compatible)
                        .Select(decision => (decision.Manifest.Id, decision.Manifest.Version)));
                if (compatibleManifests.Count == 0)
                {
                    return;
                }

                ImmutableArray<AvailableModData> selectedThirdParty = __instance.AvailableThirdPartyMods
                    .Where(mod => mod?.Manifest is not null &&
                                  compatibleManifests.Contains((mod.Manifest.Id, mod.Manifest.Version.ToString())))
                    .ToImmutableArray();
                if (selectedThirdParty.IsEmpty)
                {
                    return;
                }

                modsToLoad = modsToLoad.Concat(selectedThirdParty);
            }
            catch
            {
                // The callback must never interfere with native mod loading when the optional
                // compatibility seam changes or throws.
                s_context.Clear();
            }
        }

        private static IEnumerable<MapEditorModManifest> BuildManifests(Mafi.Collections.ImmutableCollections.ImmutableArray<AvailableModData> available)
        {
            foreach (AvailableModData mod in available)
            {
                if (mod?.Manifest is not null)
                {
                    yield return new MapEditorModManifest(mod.Manifest.Id, mod.Manifest.Version.ToString());
                }
            }
        }

        private static IEnumerable<MapEditorModManifest> BuildManifests(IEnumerable<LoadedModData> loaded)
        {
            foreach (LoadedModData mod in loaded ?? Array.Empty<LoadedModData>())
            {
                if (mod?.Manifest is not null && !mod.Manifest.IsCoreMod && !mod.Manifest.IsDlcMod)
                {
                    yield return new MapEditorModManifest(mod.Manifest.Id, mod.Manifest.Version.ToString());
                }
            }
        }
    }
}
