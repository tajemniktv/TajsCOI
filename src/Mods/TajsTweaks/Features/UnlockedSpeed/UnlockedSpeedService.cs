// Taj's COI Mods | UnlockedSpeedService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Simulation;
using TajsTweaks.Interop;

#endregion

namespace TajsTweaks.Features.UnlockedSpeed
{
    /// <summary>
    ///     Bypasses the vanilla 20x requested-speed validation.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class UnlockedSpeedService
    {
        private readonly SimLoopEvents _simLoop;

        public UnlockedSpeedService(SimLoopEvents simLoop)
        {
            _simLoop = simLoop;

            if (!SimLoopAccess.CanSetRequestedSpeed)
            {
                Log.Error(
                    "TajsTweaks/UnlockedSpeed: Required SimLoopEvents speed/adaptive-mode interop was not found. " +
                    "The game probably changed its simulation internals.");
            }
        }

        private static int MaxSpeed => UnlockedSpeedSettings.MaxSpeed;

        [ConsoleCommand(
            documentation: "Sets requested simulation speed without the vanilla 20x limit.",
            customCommandName: "set_game_speed_unlocked")]
        public string SetGameSpeedUnlocked(int speed)
        {
            int maxSpeed = MaxSpeed;
            if (speed < 1 || speed > maxSpeed)
            {
                return $"Invalid speed. Valid range is 1-{maxSpeed}.";
            }

            if (!SimLoopAccess.TrySetRequestedSpeedUncapped(_simLoop, speed, out string error))
            {
                return $"Failed to set requested simulation speed: {error}";
            }

            return $"Requested simulation speed set to {speed}x (adaptive mode: Uncapped).";
        }

        [ConsoleCommand(
            documentation: "Shows the current requested simulation speed multiplier.",
            customCommandName: "get_game_speed_unlocked")]
        public string GetGameSpeedUnlocked() => $"Requested simulation speed: {_simLoop.SimSpeedMult}x (configured max: {MaxSpeed}x).";
    }
}
