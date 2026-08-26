// Taj's COI Mods | SimLoopAccess.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Reflection;
using Mafi.Core.Simulation;
using static System.Reflection.BindingFlags;

#endregion

namespace TajsCOI.Tweaks.Features.UnlockedSpeed
{
    /// <summary>
    ///     Contains the fragile/private SimLoopEvents access used by Unlocked Speed.
    ///     If MaFi changes these internals, this should be the only place that needs fixing.
    /// </summary>
    internal static class SimLoopAccess
    {
        internal static bool CanSetRequestedSpeed =>
            s_adaptiveModeSetter is not null && (s_simSpeedSetter is not null || s_simSpeedBackingField is not null);

        internal static string BindingStatus =>
            $"adaptive setter={DescribeSetter(s_adaptiveModeProperty, typeof(SimAdaptiveSpeedMode))}, " +
            $"requested-speed setter={DescribeSetter(s_simSpeedProperty, typeof(int))}, " +
            $"requested-speed backing field={DescribeField(s_rawSimSpeedBackingField, typeof(int))}";

        internal static bool TrySetAdaptiveSpeedMode(SimLoopEvents simLoop, SimAdaptiveSpeedMode mode, out string error)
        {
            if (s_adaptiveModeSetter is null)
            {
                error = "AdaptiveSimSpeedMode setter was not found.";
                return false;
            }

            try
            {
                s_adaptiveModeSetter.Invoke(simLoop, [mode]);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                return false;
            }
        }

        internal static bool TrySetRequestedSpeedUncapped(SimLoopEvents simLoop, int speed, out string error)
        {
            if (s_adaptiveModeSetter is null)
            {
                error = "AdaptiveSimSpeedMode setter was not found.";
                return false;
            }

            Action<SimLoopEvents, int> setRequestedSpeed;
            if (s_simSpeedSetter is { } speedSetter)
            {
                setRequestedSpeed = (loop, value) => speedSetter.Invoke(loop, [value]);
            }
            else if (s_simSpeedBackingField is { } speedBackingField)
            {
                setRequestedSpeed = (loop, value) => speedBackingField.SetValue(loop, value);
            }
            else
            {
                error = "SimSpeedMult setter/backing field was not found.";
                return false;
            }

            try
            {
                s_adaptiveModeSetter.Invoke(simLoop, [SimAdaptiveSpeedMode.Uncapped]);
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
        // S3011 is intentionally suppressed only for this compatibility seam. The vanilla API
        // hard-limits SetSimSpeed to 20x, so the unlocked-speed feature must reach fixed private
        // SimLoopEvents members. No user-supplied type/member names are reflected here.
#pragma warning disable S3011
        private static readonly BindingFlags s_instanceFlags = Instance | Public | NonPublic;

        private static readonly PropertyInfo? s_simSpeedProperty =
            typeof(SimLoopEvents).GetProperty("SimSpeedMult", s_instanceFlags);

        private static readonly MethodInfo? s_simSpeedSetter =
            GetValidatedSetter(s_simSpeedProperty, typeof(int));

        private static readonly FieldInfo? s_rawSimSpeedBackingField =
            typeof(SimLoopEvents).GetField("<SimSpeedMult>k__BackingField", s_instanceFlags);

        private static readonly FieldInfo? s_simSpeedBackingField =
            ValidateField(s_rawSimSpeedBackingField, typeof(int)) ? s_rawSimSpeedBackingField : null;

        private static readonly PropertyInfo? s_adaptiveModeProperty =
            typeof(SimLoopEvents).GetProperty("AdaptiveSimSpeedMode", s_instanceFlags);

        private static readonly MethodInfo? s_adaptiveModeSetter =
            GetValidatedSetter(s_adaptiveModeProperty, typeof(SimAdaptiveSpeedMode));
#pragma warning restore S3011

        private static MethodInfo? GetValidatedSetter(PropertyInfo? property, Type expectedType)
        {
            if (property is null ||
                property.PropertyType != expectedType ||
                property.GetIndexParameters().Length != 0)
            {
                return null;
            }

            MethodInfo? setter = property.GetSetMethod(true);
            ParameterInfo[] parameters = setter?.GetParameters() ?? Array.Empty<ParameterInfo>();
            return setter is { IsStatic: false } && setter.ReturnType == typeof(void) &&
                   parameters.Length == 1 && parameters[0].ParameterType == expectedType
                ? setter
                : null;
        }

        private static bool ValidateField(FieldInfo? field, Type expectedType) =>
            field is { IsStatic: false } && field.FieldType == expectedType;

        private static string DescribeSetter(PropertyInfo? property, Type expectedType)
        {
            if (property is null)
            {
                return "missing";
            }
            if (GetValidatedSetter(property, expectedType) is null)
            {
                return "invalid";
            }
            return "resolved";
        }

        private static string DescribeField(FieldInfo? field, Type expectedType)
        {
            if (field is null)
            {
                return "missing";
            }
            if (!ValidateField(field, expectedType))
            {
                return "invalid";
            }
            return "resolved";
        }
    }
}
