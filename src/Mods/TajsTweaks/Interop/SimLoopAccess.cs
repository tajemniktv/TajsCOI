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
    // S3011 is intentionally suppressed only for this compatibility seam. The vanilla API
    // hard-limits SetSimSpeed to 20x, so the unlocked-speed feature must reach fixed private
    // SimLoopEvents members. No user-supplied type/member names are reflected here.
#pragma warning disable S3011
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
#pragma warning restore S3011

    internal static bool CanSetRequestedSpeed =>
        AdaptiveModeSetter is not null && (SimSpeedSetter is not null || SimSpeedBackingField is not null);

    internal static bool TrySetRequestedSpeedUncapped(SimLoopEvents simLoop, int speed, out string error)
    {
        if (AdaptiveModeSetter is null)
        {
            error = "AdaptiveSimSpeedMode setter was not found.";
            return false;
        }

        Action<SimLoopEvents, int> setRequestedSpeed;
        if (SimSpeedSetter is { } speedSetter)
            setRequestedSpeed = (loop, value) => speedSetter.Invoke(loop, [value]);
        else if (SimSpeedBackingField is { } speedBackingField)
            setRequestedSpeed = (loop, value) => speedBackingField.SetValue(loop, value);
        else
        {
            error = "SimSpeedMult setter/backing field was not found.";
            return false;
        }

        try
        {
            AdaptiveModeSetter.Invoke(simLoop, [SimAdaptiveSpeedMode.Uncapped]);
            setRequestedSpeed(simLoop, speed);

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
