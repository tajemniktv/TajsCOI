// Taj's COI Mods | TweaksSimulationSpeedDisplayFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using Mafi.Core.Environment;
using Mafi.Core.Simulation;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UiLabel = Mafi.Unity.UiToolkit.Library.Label;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Adds a compact, read-only multiplier to the native calendar speed row. The game speed
    ///     controller remains authoritative; this feature only observes its public state.
    /// </summary>
    internal static class TweaksSimulationSpeedDisplayFeature
    {
        private const string DisplayName = "TajsSimulationSpeedDisplay";

        private sealed class DisplayState
        {
            internal UiLabel Display = null!;
            internal GameSpeedController SpeedController = null!;
        }

        private static readonly ConditionalWeakTable<CalendarControlsHud, DisplayState> s_states = new();
        private static bool s_installed;

        internal static void Install(Harmony harmony)
        {
            ConstructorInfo constructor = typeof(CalendarControlsHud).GetConstructor(
                                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                                binder: null,
                                                new[]
                                                {
                                                    typeof(GameSpeedController),
                                                    typeof(IWeatherManager),
                                                    typeof(ICalendar),
                                                    typeof(IUnityInputMgr),
                                                },
                                                modifiers: null)
                                            ?? throw new MissingMethodException(
                                                typeof(CalendarControlsHud).FullName,
                                                ".ctor(GameSpeedController, IWeatherManager, ICalendar, IUnityInputMgr)");

            harmony.Patch(
                constructor,
                postfix: new HarmonyMethod(
                    typeof(TweaksSimulationSpeedDisplayFeature),
                    nameof(CalendarControlsConstructedPostfix)));
            s_installed = true;
        }

        internal static void Apply(DependencyResolver resolver)
        {
            if (!s_installed ||
                !resolver.TryResolve(out CalendarControlsHud hud) ||
                !resolver.TryResolve(out GameSpeedController speedController))
            {
                return;
            }

            try
            {
                EnsureDisplay(hud, speedController);
            }
            catch
            {
                // HUD presentation is optional. A changed UI tree must leave the native speed
                // controls available rather than surfacing an exception from the render tick.
            }
        }

        private static void CalendarControlsConstructedPostfix(
            CalendarControlsHud __instance,
            GameSpeedController speedController)
        {
            try
            {
                EnsureDisplay(__instance, speedController);
            }
            catch
            {
                // Keep this Harmony postfix fail-open: the vanilla calendar controls remain valid
                // even if a future UI layout no longer exposes the expected panel body.
            }
        }

        private static void EnsureDisplay(CalendarControlsHud hud, GameSpeedController speedController)
        {
            if (s_states.TryGetValue(hud, out DisplayState? existing))
            {
                Update(existing);
                return;
            }

            UiComponent speedRow = FindSpeedButtonsRow(hud)
                                   ?? throw new MissingMemberException(
                                       typeof(CalendarControlsHud).FullName,
                                       "native speed-control row");

            var display = new UiLabel(SimulationSpeedDisplayText.Format(speedController.SimSpeedMult).AsLoc())
                .Name(DisplayName)
                .InfoIconPosition(UiLabel.InfoIconPos.None)
                .FontBold()
                .FontSize(20)
                .TextCenterMiddle()
                .Height(new Px(26f))
                .AlignSelfCenter()
                .MinWidth(new Px(34f))
                .PaddingLeftRight(new Px(2f))
                .PaddingTopBottom(new Px(0f))
                .MarginTopBottom(new Px(0f))
                .Tooltip("Current simulation speed".AsLoc());
            display.RootElement.style.flexShrink = 0f;
            speedRow.Add(display);

            var state = new DisplayState
            {
                Display = display,
                SpeedController = speedController,
            };
            s_states.Add(hud, state);
            Update(state);
            display.Schedule.Execute(() => Update(state)).Every(250L);
        }

        private static void Update(DisplayState state)
        {
            state.Display.Value(SimulationSpeedDisplayText.Format(state.SpeedController.SimSpeedMult).AsLoc());
        }

        private static UiComponent? FindSpeedButtonsRow(UiComponent root)
        {
            UiComponent? best = null;
            int bestCount = 0;
            FindSpeedButtonsRow(root, ref best, ref bestCount, 0, 6);
            return bestCount >= 3 ? best : null;
        }

        private static void FindSpeedButtonsRow(
            UiComponent node,
            ref UiComponent? best,
            ref int bestCount,
            int depth,
            int maxDepth)
        {
            if (depth > maxDepth)
            {
                return;
            }

            int buttonCount = 0;
            foreach (UiComponent child in node.AllChildren)
            {
                if (IsButtonIcon(child))
                {
                    buttonCount++;
                }
            }
            if (buttonCount > bestCount)
            {
                best = node;
                bestCount = buttonCount;
            }

            foreach (UiComponent child in node.AllChildren)
            {
                FindSpeedButtonsRow(child, ref best, ref bestCount, depth + 1, maxDepth);
            }
        }

        private static bool IsButtonIcon(UiComponent component) =>
            component.GetType().Name.StartsWith("ButtonIcon", StringComparison.Ordinal);
    }
}
