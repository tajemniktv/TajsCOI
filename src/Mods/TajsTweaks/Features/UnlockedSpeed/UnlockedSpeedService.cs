// Taj's COI Mods | UnlockedSpeedService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System.Threading;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.Simulation;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;

#endregion

namespace TajsCOI.Tweaks.Features.UnlockedSpeed
{
    /// <summary>
    ///     Bypasses the vanilla 20x requested-speed validation.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class UnlockedSpeedService
    {
        private readonly SimLoopEvents m_simLoop;
        private readonly ITajsLogger m_log;
        private int m_maxSpeed;

        public UnlockedSpeedService(SimLoopEvents simLoop, ITajsRuntime runtime, ITajsSettings settings)
        {
            m_simLoop = simLoop;
            m_log = runtime.GetLogger("TajsTweaks", "UnlockedSpeed");
            settings.Register(UnlockedSpeedSetting.Descriptor);
            m_maxSpeed = settings.Get<int>(UnlockedSpeedSetting.ModId, UnlockedSpeedSetting.Key);
            settings.Changed += OnSettingChanged;

            if (!SimLoopAccess.CanSetRequestedSpeed)
            {
                const string reason = "The private game contract changed or could not be resolved.";
                m_log.Error(reason + " " + SimLoopAccess.BindingStatus);
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        "TajsTweaks",
                        "UnlockedSpeed",
                        CompatibilityState.Disabled,
                        "SimLoopEvents requested-speed and adaptive-mode setters",
                        SimLoopAccess.BindingStatus,
                        reason));
            }
            else
            {
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        "TajsTweaks",
                        "UnlockedSpeed",
                        CompatibilityState.Compatible,
                        "SimLoopEvents requested-speed and adaptive-mode setters",
                        SimLoopAccess.BindingStatus,
                        "All required private bindings resolved."));
            }
        }

        private int MaxSpeed => Volatile.Read(ref m_maxSpeed);

        private void OnSettingChanged(object sender, SettingChangedEventArgs change)
        {
            if (string.Equals(change.Descriptor.StableId, UnlockedSpeedSetting.Descriptor.StableId, System.StringComparison.Ordinal) &&
                change.NewValue is int maxSpeed)
            {
                Volatile.Write(ref m_maxSpeed, maxSpeed);
                m_log.Info($"Unlocked simulation speed maximum changed to {maxSpeed}x.");
            }
        }

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

            if (!SimLoopAccess.TrySetRequestedSpeedUncapped(m_simLoop, speed, out string error))
            {
                m_log.ErrorOnce("Failed to set requested simulation speed: " + error);
                return $"Failed to set requested simulation speed: {error}";
            }

            return $"Requested simulation speed set to {speed}x (adaptive mode: Uncapped).";
        }

        [ConsoleCommand(
            documentation: "Shows the current requested simulation speed multiplier.",
            customCommandName: "get_game_speed_unlocked")]
        public string GetGameSpeedUnlocked() => $"Requested simulation speed: {m_simLoop.SimSpeedMult}x (configured max: {MaxSpeed}x).";
    }
}
