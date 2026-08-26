// Taj's COI Mods | TweaksFullscreenHudFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Tracks only the native fullscreen windows that can cover the gameplay HUD. The layout
    ///     owner applies the visibility policy to already-discovered HUD roots, avoiding a new
    ///     whole-tree scan whenever a modal window opens or closes.
    /// </summary>
    internal static class TweaksFullscreenHudFeature
    {
        internal static void Install(Harmony harmony)
        {
            Type? window = AccessTools.TypeByName("Mafi.Unity.UiToolkit.Library.Window, Mafi.Unity");
            if (window is null)
            {
                throw new TypeLoadException("Mafi.Unity.UiToolkit.Library.Window");
            }
            MethodInfo[] opens = window.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == "Open" && method.GetParameters().Length >= 1).ToArray();
            MethodInfo? close = AccessTools.Method(window, "Close");
            MethodInfo? closeNoFade = AccessTools.Method(window, "CloseNoFade");
            if (opens.Length == 0 || close is null || closeNoFade is null)
            {
                throw new MissingMethodException(window.FullName, "Open/Close");
            }
            foreach (MethodInfo open in opens)
            {
                harmony.Patch(open, postfix: new HarmonyMethod(typeof(TweaksFullscreenHudFeature), nameof(OpenPostfix)));
            }
            harmony.Patch(close, postfix: new HarmonyMethod(typeof(TweaksFullscreenHudFeature), nameof(ClosePostfix)));
            harmony.Patch(closeNoFade, postfix: new HarmonyMethod(typeof(TweaksFullscreenHudFeature), nameof(ClosePostfix)));

            Type? uiRoot = AccessTools.TypeByName("Mafi.Unity.UiToolkit.UiRoot, Mafi.Unity");
            MethodInfo? setVisibility = uiRoot is null ? null : AccessTools.Method(uiRoot, "SetUiVisibility", new[] { typeof(bool) });
            if (setVisibility is not null)
            {
                harmony.Patch(setVisibility, postfix: new HarmonyMethod(typeof(TweaksFullscreenHudFeature), nameof(UiVisibilityPostfix)));
            }
        }

        private static void OpenPostfix(object __instance) => TweaksHudLayoutFeature.OnFullscreenWindowChanged(__instance, true);

        private static void ClosePostfix(object __instance) => TweaksHudLayoutFeature.OnFullscreenWindowChanged(__instance, false);

        private static void UiVisibilityPostfix(bool isVisible) => TweaksHudLayoutFeature.OnUiVisibilityChanged(isVisible);
    }
}
