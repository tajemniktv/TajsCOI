// Taj's Game | SimLoopAccess.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Reflection;
using Mafi.Core.Simulation;
using static System.Reflection.BindingFlags;

#endregion

namespace TajsTweaks.Interop;

/// <summary>
///     Contains the fragile/private SimLoopEvents access used by runtime features.
///     If MaFi changes these internals, this should be the only place that needs fixing.
/// </summary>
internal static class SimLoopAccess
{
    private static readonly BindingFlags InstanceFlags = Instance | Public | NonPublic;

    private static readonly PropertyInfo? SimSpeedProperty =
        typeof(SimLoopEvents).GetProperty("SimSpeedMult", InstanceFlags);

    private static readonly MethodInfo? SimSpeedSetter =
        SimSpeedProperty?.GetSetMethod(true);

    private static readonly FieldInfo? SimSpeedBackingField =
        typeof(SimLoopEvents).GetField("<SimSpeedMult>k__BackingField", InstanceFlags);

    private static readonly PropertyInfo? AdaptiveModeProperty =
        typeof(SimLoopEvents).GetProperty("AdaptiveSimSpeedMode", InstanceFlags);

    private static readonly MethodInfo? AdaptiveModeSetter =
        AdaptiveModeProperty?.GetSetMethod(true);

    internal static bool CanSetRequestedSpeed => SimSpeedSetter is not null || SimSpeedBackingField is not null;

    internal static bool TrySetRequestedSpeedUncapped(SimLoopEvents simLoop, int speed, out string error)
    {
        try
        {
            AdaptiveModeSetter?.Invoke(simLoop, [SimAdaptiveSpeedMode.Uncapped]);

            if (SimSpeedSetter is not null)
                SimSpeedSetter.Invoke(simLoop, [speed]);
            else if (SimSpeedBackingField is not null)
                SimSpeedBackingField.SetValue(simLoop, speed);
            else
            {
                error = "SimSpeedMult setter/backing field was not found.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }
}
