#region

using System.Reflection;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Simulation;
using static System.Reflection.BindingFlags;

#endregion

namespace TajsTweaks.Features.UnlockedSpeed;

/// <summary>
///     bypasses the vanilla 20x requested-speed validation
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf)]
public sealed class UnlockedSpeedService
{
    private const int MaxSpeed = 100;

    private static readonly BindingFlags InstanceFlags =
        Instance | Public | NonPublic;

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

    private readonly SimLoopEvents _mSimLoop;

    public UnlockedSpeedService(SimLoopEvents simLoop)
    {
        _mSimLoop = simLoop;

        if (SimSpeedSetter is null && SimSpeedBackingField is null)
            Log.Error(
                "TajsTweaks/UnlockedSpeed: SimSpeedMult setter/backing field was not found. " +
                "The game probably changed its simulation internals.");
    }

    [ConsoleCommand(
        documentation: "Sets requested simulation speed without the vanilla 20x limit. Range: 1-100.",
        customCommandName: "set_game_speed_unlocked")]
    public string SetGameSpeedUnlocked(int speed)
    {
        if (speed < 1 || speed > MaxSpeed) return $"Invalid speed. Valid range is 1-{MaxSpeed}.";

        // Match the useful part of Speed++: do not let adaptive/predictive mode
        // intentionally throttle the requested multiplier.
        AdaptiveModeSetter?.Invoke(
            _mSimLoop,
            new object[] { SimAdaptiveSpeedMode.Uncapped });

        // SimLoopEvents.SetSimSpeed() performs vanilla's 1..20 validation.
        // Its private SimSpeedMult setter does not, so invoke that directly.
        if (SimSpeedSetter is not null)
            SimSpeedSetter.Invoke(_mSimLoop, new object[] { speed });
        else if (SimSpeedBackingField is not null)
            SimSpeedBackingField.SetValue(_mSimLoop, speed);
        else
            return "Failed: SimSpeedMult setter/backing field was not found.";

        return $"Requested simulation speed set to {speed}x (adaptive mode: Uncapped).";
    }

    [ConsoleCommand(
        documentation: "Shows the current requested simulation speed multiplier.",
        customCommandName: "get_game_speed_unlocked")]
    public string GetGameSpeedUnlocked()
    {
        return $"Requested simulation speed: {_mSimLoop.SimSpeedMult}x";
    }

    [ConsoleCommand(
        documentation: "Shows basic TajsTweaks status.",
        customCommandName: "tajs_tweaks_info")]
    public string GetInfo()
    {
        return $"TajsTweaks loaded. Requested speed: {_mSimLoop.SimSpeedMult}x.";
    }
}