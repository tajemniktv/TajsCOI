// Taj's COI Mods | ResearchTreeLayoutFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Core.Research;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.Ui.Research;
using UnityEngine;

namespace TajsCOI.Tweaks.Features.Presentation
{
    /// <summary>
    ///     Applies an alternate input to ResearchWindow's native layout calculation. The game
    ///     keeps raw grid coordinates in m_positionsMap; this feature never writes that map and
    ///     recalculates from those raw values for every node/connector request.
    /// </summary>
    internal static class ResearchTreeLayoutFeature
    {
        private sealed class WindowState
        {
            internal bool HasGeometry;
            internal int ScreenWidth;
            internal int ScreenHeight;
            internal float TreeWidth;
            internal float TreeHeight;
            internal string Layout = string.Empty;
        }

        private static readonly object s_gate = new();
        private static readonly List<WeakReference<ResearchWindow>> s_windows = new();
        private static readonly ConditionalWeakTable<ResearchWindow, WindowState> s_states = new();
        private static FieldInfo? s_positionsMapField;
        private static MethodInfo? s_positionsMapGetter;
        private static FieldInfo? s_treeViewField;
        private static MethodInfo? s_buildTreeMethod;

        internal static void Install(Harmony harmony)
        {
            Type type = typeof(ResearchWindow);
            MethodInfo positionMethod = AccessTools.Method(type, "getNodePos", new[] { typeof(ResearchNode) })
                                         ?? throw new MissingMethodException(type.FullName, "getNodePos");
            s_positionsMapField = AccessTools.Field(type, "m_positionsMap")
                                  ?? throw new MissingFieldException(type.FullName, "m_positionsMap");
            s_positionsMapGetter = AccessTools.Method(s_positionsMapField.FieldType, "get_Item", new[] { typeof(ResearchNode) })
                                   ?? throw new MissingMethodException(s_positionsMapField.FieldType.FullName, "get_Item");
            s_treeViewField = AccessTools.Field(type, "m_treeView")
                              ?? throw new MissingFieldException(type.FullName, "m_treeView");
            s_buildTreeMethod = AccessTools.Method(type, "buildTree", Type.EmptyTypes)
                                ?? throw new MissingMethodException(type.FullName, "buildTree");
            ConstructorInfo constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(x => x.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[]
                {
                    typeof(ResearchManager),
                    typeof(IInputScheduler),
                    typeof(NewInstanceOf<ResearchDetailUi>),
                    typeof(ShortcutsManager),
                }))
                ?? throw new MissingMethodException(type.FullName, ".ctor");
            MethodInfo renderUpdate = AccessTools.Method(type, "RenderUpdate", new[] { typeof(GameTime) })
                                      ?? throw new MissingMethodException(type.FullName, "RenderUpdate");

            harmony.Patch(
                positionMethod,
                postfix: new HarmonyMethod(typeof(ResearchTreeLayoutFeature), nameof(GetNodePosPostfix)));
            harmony.Patch(
                constructor,
                postfix: new HarmonyMethod(typeof(ResearchTreeLayoutFeature), nameof(WindowCreatedPostfix)));
            harmony.Patch(
                renderUpdate,
                postfix: new HarmonyMethod(typeof(ResearchTreeLayoutFeature), nameof(RenderUpdatePostfix)));
        }

        internal static void RefreshAll()
        {
            WeakReference<ResearchWindow>[] windows;
            lock (s_gate)
            {
                s_windows.RemoveAll(x => !x.TryGetTarget(out _));
                windows = s_windows.ToArray();
            }

            foreach (WeakReference<ResearchWindow> reference in windows)
            {
                if (reference.TryGetTarget(out ResearchWindow? window))
                {
                    RefreshWindow(window);
                }
            }
        }

        internal static void Reset()
        {
            lock (s_gate)
            {
                s_windows.Clear();
            }
        }

        private static void WindowCreatedPostfix(ResearchWindow __instance)
        {
            lock (s_gate)
            {
                s_windows.RemoveAll(x => !x.TryGetTarget(out _));
                if (!s_windows.Any(x => x.TryGetTarget(out ResearchWindow? value) && ReferenceEquals(value, __instance)))
                {
                    s_windows.Add(new WeakReference<ResearchWindow>(__instance));
                }
            }
        }

        private static void RenderUpdatePostfix(ResearchWindow __instance)
        {
            try
            {
                WindowState state;
                if (!s_states.TryGetValue(__instance, out WindowState? existingState))
                {
                    state = new WindowState();
                    s_states.Add(__instance, state);
                }
                else
                {
                    state = existingState!;
                }
                if (!TryReadGeometry(__instance, out int screenWidth, out int screenHeight, out float treeWidth, out float treeHeight))
                {
                    return;
                }

                bool changed = state.HasGeometry &&
                               (state.ScreenWidth != screenWidth || state.ScreenHeight != screenHeight ||
                                Math.Abs(state.TreeWidth - treeWidth) > 0.5f || Math.Abs(state.TreeHeight - treeHeight) > 0.5f ||
                                !string.Equals(state.Layout, TajsTweaksRuntimeState.ResearchTreeLayout, StringComparison.Ordinal));
                state.HasGeometry = true;
                state.ScreenWidth = screenWidth;
                state.ScreenHeight = screenHeight;
                state.TreeWidth = treeWidth;
                state.TreeHeight = treeHeight;
                state.Layout = TajsTweaksRuntimeState.ResearchTreeLayout ?? string.Empty;
                if (changed)
                {
                    RefreshWindow(__instance);
                }
            }
            catch
            {
                // A changed UI seam must leave the native tree untouched.
            }
        }

        private static void GetNodePosPostfix(object __instance, object[] __args, ref Vector2i __result)
        {
            if (!string.Equals(TajsTweaksRuntimeState.ResearchTreeLayout, "compact", StringComparison.Ordinal) ||
                __args.Length == 0 || __args[0] is not ResearchNode node ||
                s_positionsMapField is null || s_positionsMapGetter is null)
            {
                return;
            }

            try
            {
                object? map = s_positionsMapField.GetValue(__instance);
                if (map is null || s_positionsMapGetter.Invoke(map, new object[] { node }) is not Vector2i rawPosition)
                {
                    return;
                }

                (int x, int y) = ResearchTreeSpacingPolicy.Resolve(TajsTweaksRuntimeState.ResearchTreeLayout)
                    .Apply(rawPosition.X, rawPosition.Y);
                __result = new Vector2i(x, y);
            }
            catch
            {
                // Keep the vanilla result if the private dictionary shape changes.
            }
        }

        private static void RefreshWindow(ResearchWindow window)
        {
            try
            {
                s_buildTreeMethod?.Invoke(window, null);
                if (s_states.TryGetValue(window, out WindowState? state))
                {
                    state.Layout = TajsTweaksRuntimeState.ResearchTreeLayout ?? string.Empty;
                }
            }
            catch
            {
                // Rebuilding is presentation-only; a changed seam must not break research UI.
            }
        }

        private static bool TryReadGeometry(
            ResearchWindow window,
            out int screenWidth,
            out int screenHeight,
            out float treeWidth,
            out float treeHeight)
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            treeWidth = 0;
            treeHeight = 0;
            if (s_treeViewField?.GetValue(window) is not PanAndZoom treeView)
            {
                return false;
            }

            treeWidth = treeView.ResolvedWidth;
            treeHeight = treeView.ResolvedHeight;
            return treeWidth > 0 || treeHeight > 0;
        }
    }
}
