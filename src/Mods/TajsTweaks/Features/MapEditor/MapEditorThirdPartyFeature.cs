// Taj's COI Mods | MapEditorThirdPartyFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
        private static readonly ModdedMapEditorContext s_context = new();
        private static FieldInfo? s_mainField;
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            MethodInfo target = AccessTools.Method(typeof(MainMenuScreen), "onMapEditorClick")
                                ?? throw new MissingMethodException(typeof(MainMenuScreen).FullName, "onMapEditorClick");
            s_mainField = AccessTools.Field(typeof(MainMenuScreen), "m_main")
                          ?? throw new MissingFieldException(typeof(MainMenuScreen).FullName, "m_main");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(MapEditorThirdPartyFeature), nameof(Prefix)));
            s_installed = true;
        }

        internal static void Reset()
        {
            s_context.Clear();
            s_mainField = null;
            s_installed = false;
        }

        private static bool Prefix(MainMenuScreen __instance)
        {
            if (s_mainField?.GetValue(__instance) is not IMain main)
            {
                return true;
            }
            try
            {
                ImmutableArray<AvailableModData> available = main.AvailableMods;
                s_context.Begin(BuildManifests(available));
                IReadOnlyList<MapEditorModManifest> compatible = s_context.Resolve(_ => true);
                if (compatible.Count == 0 || !main.TryLoadMods(available, includeMissingCoreMods: true, out ImmutableArray<LoadedModData> loadedMods, out _))
                {
                    return true;
                }
                main.StartMapEditor(Mafi.Collections.ImmutableCollections.ImmutableArray<IConfig>.Empty, loadedMods);
                return false;
            }
            catch
            {
                return true;
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
    }
}
