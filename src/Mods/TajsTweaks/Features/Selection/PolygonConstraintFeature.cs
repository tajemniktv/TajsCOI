// Taj's COI Mods | PolygonConstraintFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mafi;
using UnityEngine;

namespace TajsCOI.Tweaks.Features.Selection
{
    /// <summary>
    ///     Patches the shared 0.8.7b polygon editor at its input boundary. Native
    ///     validity, cancel, undo and commit logic therefore continue to receive the
    ///     constrained cursor as if it had been entered by the user.
    /// </summary>
    internal static class PolygonConstraintFeature
    {
        private sealed class DragState
        {
            internal readonly PolygonVector2 Origin;

            internal DragState(PolygonVector2 origin)
            {
                Origin = origin;
            }
        }

        private static readonly ConditionalWeakTable<object, DragState> s_dragStates = new();
        private static readonly object s_modifierGate = new();
        private static Func<bool> s_axisModifierHeld = DefaultAxisModifierHeld;
        private static Func<bool> s_gridModifierHeld = DefaultGridModifierHeld;
        private static bool s_installed;

        /// <summary>
        ///     Allows the common shortcut registry (#141) to supply the actual modifier
        ///     state without coupling this patch to a particular input implementation.
        /// </summary>
        internal static void ConfigureModifiers(Func<bool>? axisModifierHeld, Func<bool>? gridModifierHeld)
        {
            lock (s_modifierGate)
            {
                s_axisModifierHeld = axisModifierHeld ?? DefaultAxisModifierHeld;
                s_gridModifierHeld = gridModifierHeld ?? DefaultGridModifierHeld;
            }
        }

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }

            var stateType = Type.GetType("Mafi.Unity.Ui.Controllers.PolygonEditState, Mafi.Unity", false);
            MethodInfo? inputUpdate = stateType is null ? null : AccessTools.Method(stateType, "InputUpdate");
            if (!IsExpectedInputMethod(inputUpdate))
            {
                // Polygon editing remains fully native if the private seam changes.
                return;
            }

            harmony.Patch(
                inputUpdate!,
                prefix: new HarmonyMethod(typeof(PolygonConstraintFeature), nameof(InputUpdatePrefix)));
            s_installed = true;
        }

        private static bool IsExpectedInputMethod(MethodInfo? method)
        {
            if (method is null || method.IsStatic || method.ReturnType != typeof(bool))
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 7 &&
                   parameters[0].ParameterType == typeof(Vector2f) &&
                   parameters[1].ParameterType == typeof(bool) &&
                   parameters[2].ParameterType == typeof(bool) &&
                   parameters[3].ParameterType == typeof(bool) &&
                   parameters[4].ParameterType == typeof(bool) &&
                   parameters[5].ParameterType == typeof(bool) &&
                   parameters[6].ParameterType == typeof(float);
        }

        private static void InputUpdatePrefix(
            object __instance,
            ref Vector2f cursor,
            bool hasCursor,
            bool primaryDown,
            bool primaryOn)
        {
            if (!hasCursor)
            {
                if (!primaryOn)
                {
                    s_dragStates.Remove(__instance);
                }
                return;
            }

            if (primaryDown && IsIdle(__instance))
            {
                // Capture only the initial native click. It is intentionally never
                // replaced by a constrained cursor on later frames.
                s_dragStates.Remove(__instance);
                s_dragStates.Add(__instance, new DragState(ToVector(cursor)));
                return;
            }

            if (!s_dragStates.TryGetValue(__instance, out DragState? drag))
            {
                return;
            }

            bool axisHeld;
            bool gridHeld;
            lock (s_modifierGate)
            {
                axisHeld = s_axisModifierHeld();
                gridHeld = s_gridModifierHeld();
            }
            if (axisHeld || gridHeld)
            {
                PolygonVector2 constrained = PolygonConstraintMath.Apply(
                    drag.Origin,
                    ToVector(cursor),
                    axisHeld,
                    gridHeld);
                cursor = ToMafi(constrained);
            }

            // Apply the final release cursor above, then discard scene-local drag
            // state. ConditionalWeakTable also prevents stale editor instances from
            // being retained if a controller is destroyed unexpectedly.
            if (!primaryOn)
            {
                s_dragStates.Remove(__instance);
            }
        }

        private static bool IsIdle(object instance)
        {
            try
            {
                PropertyInfo? mode = AccessTools.Property(instance.GetType(), "Mode");
                return string.Equals(Convert.ToString(mode?.GetValue(instance)), "Idle", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static PolygonVector2 ToVector(Vector2f value) => new(value.X.ToFloat(), value.Y.ToFloat());

        private static Vector2f ToMafi(PolygonVector2 value) =>
            new(Fix32.FromDouble(value.X), Fix32.FromDouble(value.Y));

        private static bool DefaultAxisModifierHeld() =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private static bool DefaultGridModifierHeld() =>
            Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }
}
