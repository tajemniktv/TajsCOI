// Taj's COI Mods | UnlockedSpeedService.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

#region

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Mafi;
using Mafi.Core.Console;
using Mafi.Core.GameLoop;
using Mafi.Core.Input;
using Mafi.Core.Simulation;
using Mafi.Logging;
using Mafi.Unity.InputControl;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Common.Settings;

#endregion

namespace TajsCOI.Tweaks.Features.UnlockedSpeed
{
    /// <summary>
    ///     Extends the normal game-speed controller with a bounded, configurable sequence. The
    ///     controller and input scheduler remain authoritative; the private SimLoopEvents seam
    ///     is used only after an explicitly requested high-speed command has been processed.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    public sealed class UnlockedSpeedService
    {
        private const string HarmonyId = "TajsCOI.Tweaks.UnlockedSpeed";
        private const int VanillaMaximum = 20;

        private static WeakReference<UnlockedSpeedService>? s_current;

        private readonly SimLoopEvents m_simLoop;
        private readonly GameSpeedController m_speedController;
        private readonly IInputScheduler m_inputScheduler;
        private readonly ShortcutsManager m_shortcuts;
        private readonly LazyResolve<IGameIdProvider> m_gameRunner;
        private readonly ITajsSettings m_settings;
        private readonly ITajsLogger m_log;

        private Harmony? m_harmony;
        private int m_maxSpeed;
        private int m_pendingHighSpeed;
        private bool m_highSpeedMode;
        private bool m_resumeOnSelect;
        private string m_sequenceMode = UnlockedSpeedSetting.VanillaSequenceMode;
        private string m_customSequence = string.Empty;
        private int[] m_sequence = Array.Empty<int>();
        private bool m_installed;
        private SimAdaptiveSpeedMode m_originalAdaptiveModeForTransition;
        private bool m_gameRunnerProbeAttempted;
        private object? m_gameRunnerInstance;
        private PropertyInfo? m_latestOvertimeProperty;
        private PropertyInfo? m_overtimeDurationProperty;

        public UnlockedSpeedService(
            SimLoopEvents simLoop,
            GameSpeedController speedController,
            IInputScheduler inputScheduler,
            ShortcutsManager shortcuts,
            IGameLoopEvents gameLoop,
            LazyResolve<IGameIdProvider> gameRunner,
            ITajsRuntime runtime,
            ITajsSettings settings)
        {
            m_simLoop = simLoop;
            m_speedController = speedController;
            m_inputScheduler = inputScheduler;
            m_shortcuts = shortcuts;
            m_gameRunner = gameRunner;
            m_settings = settings;
            m_log = runtime.GetLogger("TajsTweaks", "UnlockedSpeed");

            foreach (SettingDescriptor descriptor in UnlockedSpeedSetting.All)
            {
                settings.Register(descriptor);
            }
            m_maxSpeed = settings.Get<int>(UnlockedSpeedSetting.ModId, UnlockedSpeedSetting.Key);
            m_sequenceMode = settings.Get<string>(UnlockedSpeedSetting.ModId, UnlockedSpeedSetting.SequenceModeKey);
            m_customSequence = settings.Get<string>(UnlockedSpeedSetting.ModId, UnlockedSpeedSetting.CustomSequenceKey);
            m_resumeOnSelect = settings.Get<bool>(UnlockedSpeedSetting.ModId, UnlockedSpeedSetting.ResumeOnSelectKey);
            RebuildSequence();
            settings.Changed += OnSettingChanged;
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);

            if (SimLoopAccess.CanSetRequestedSpeed)
            {
                TryInstallPatches();
            }

            s_current = new WeakReference<UnlockedSpeedService>(this);
            if (!m_installed)
            {
                const string reason = "The normal speed controller or the private high-speed command seam could not be resolved.";
                m_log.Error(reason + " " + SimLoopAccess.BindingStatus);
                runtime.ReportCompatibility(
                    new CompatibilityReport(
                        "TajsTweaks",
                        "UnlockedSpeed",
                        CompatibilityState.Disabled,
                        "GameSpeedController command path and SimLoopEvents high-speed seam",
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
                        "GameSpeedController, GameSpeedChangeCmd and SimLoopEvents high-speed seam",
                        "Shortcut/controller patches registered",
                        "High speeds use the normal scheduled command path and are bounded by the validated setting."));
            }
        }

        private int MaxSpeed => Volatile.Read(ref m_maxSpeed);

        private void TryInstallPatches()
        {
            try
            {
                MethodInfo inputUpdate = AccessTools.Method(typeof(GameSpeedController), nameof(GameSpeedController.InputUpdate))
                                         ?? throw new MissingMethodException(typeof(GameSpeedController).FullName, nameof(GameSpeedController.InputUpdate));
                MethodInfo setSimSpeed = AccessTools.Method(typeof(SimLoopEvents), nameof(SimLoopEvents.SetSimSpeed), new[] { typeof(int) })
                                         ?? throw new MissingMethodException(typeof(SimLoopEvents).FullName, nameof(SimLoopEvents.SetSimSpeed));
                MethodInfo setSpeed = AccessTools.Method(typeof(GameSpeedController), nameof(GameSpeedController.SetSpeed), new[] { typeof(int) })
                                      ?? throw new MissingMethodException(typeof(GameSpeedController).FullName, nameof(GameSpeedController.SetSpeed));

                m_harmony = new Harmony(HarmonyId);
                m_harmony.Patch(
                    inputUpdate,
                    prefix: new HarmonyMethod(typeof(UnlockedSpeedService), nameof(InputUpdatePrefix)));
                m_harmony.Patch(
                    setSpeed,
                    prefix: new HarmonyMethod(typeof(UnlockedSpeedService), nameof(SetSpeedPrefix)));
                m_harmony.Patch(
                    setSimSpeed,
                    postfix: new HarmonyMethod(typeof(UnlockedSpeedService), nameof(SetSimSpeedPostfix)));
                m_installed = true;
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Unlocked speed patch installation failed; vanilla speed controls remain active.");
                try
                {
                    m_harmony?.UnpatchAll(HarmonyId);
                }
                catch (Exception rollbackException)
                {
                    m_log.Exception(rollbackException, "Unlocked speed patch rollback failed.");
                }
                m_harmony = null;
            }
        }

        private void OnSettingChanged(object? sender, SettingChangedEventArgs change)
        {
            if (string.Equals(change.Descriptor.StableId, UnlockedSpeedSetting.Descriptor.StableId, StringComparison.Ordinal) &&
                change.NewValue is int maxSpeed)
            {
                Volatile.Write(ref m_maxSpeed, maxSpeed);
                RebuildSequence();
                m_log.Info($"Unlocked simulation speed maximum changed to {maxSpeed}x.");
                if (m_simLoop.SimSpeedMult > maxSpeed)
                {
                    m_log.Warning($"Current requested speed remains at {m_simLoop.SimSpeedMult}x until a valid speed is selected.");
                }
                return;
            }

            if (string.Equals(change.Descriptor.StableId, UnlockedSpeedSetting.SequenceModeDescriptor.StableId, StringComparison.Ordinal) &&
                change.NewValue is string mode)
            {
                m_sequenceMode = mode;
                RebuildSequence();
                return;
            }

            if (string.Equals(change.Descriptor.StableId, UnlockedSpeedSetting.CustomSequenceDescriptor.StableId, StringComparison.Ordinal) &&
                change.NewValue is string customSequence)
            {
                m_customSequence = customSequence;
                RebuildSequence();
                return;
            }

            if (string.Equals(change.Descriptor.StableId, UnlockedSpeedSetting.ResumeOnSelectDescriptor.StableId, StringComparison.Ordinal) &&
                change.NewValue is bool resumeOnSelect)
            {
                m_resumeOnSelect = resumeOnSelect;
            }
        }

        private void RebuildSequence() =>
            m_sequence = SpeedSequence.Build(MaxSpeed, m_sequenceMode, m_customSequence).ToArray();

        private static bool InputUpdatePrefix(GameSpeedController __instance, ref bool __result)
        {
            if (s_current is null || !s_current.TryGetTarget(out UnlockedSpeedService? service) ||
                !service.TryHandleInput(__instance))
            {
                return true;
            }

            __result = true;
            return false;
        }

        private static void SetSimSpeedPostfix(SimLoopEvents __instance, int speedMult)
        {
            if (s_current is not null && s_current.TryGetTarget(out UnlockedSpeedService? service))
            {
                service.OnVanillaSpeedCommandApplied(__instance, speedMult);
            }
        }

        private static void SetSpeedPrefix(GameSpeedController __instance, int speedMult)
        {
            if (s_current is not null && s_current.TryGetTarget(out UnlockedSpeedService? service))
            {
                service.OnSpeedRequested(__instance, speedMult);
            }
        }

        private void OnSpeedRequested(GameSpeedController controller, int speed)
        {
            if (!m_installed || !ReferenceEquals(controller, m_speedController) || speed <= VanillaMaximum || speed > MaxSpeed)
            {
                if (m_installed && ReferenceEquals(controller, m_speedController) && speed > 0 && speed <= VanillaMaximum)
                {
                    Volatile.Write(ref m_pendingHighSpeed, 0);
                    RestoreAdaptiveModeIfNeeded();
                }
                return;
            }

            if (!m_highSpeedMode)
            {
                m_originalAdaptiveModeForTransition = m_simLoop.AdaptiveSimSpeedMode;
                if (!SimLoopAccess.TrySetAdaptiveSpeedMode(m_simLoop, SimAdaptiveSpeedMode.Uncapped, out string modeError))
                {
                    m_log.ErrorOnce("Failed to enable uncapped adaptive speed: " + modeError);
                    return;
                }
                m_highSpeedMode = true;
            }
            Volatile.Write(ref m_pendingHighSpeed, speed);
        }

        private bool TryHandleInput(GameSpeedController controller)
        {
            if (!m_installed || !ReferenceEquals(controller, m_speedController) || UnityInputManager.IsInputFieldFocused())
            {
                return false;
            }

            if (m_shortcuts.IsDown(m_shortcuts.PauseGame) || m_shortcuts.IsDown(m_shortcuts.SetGameSpeedTo0))
            {
                TogglePause();
                return true;
            }
            if (m_shortcuts.IsDown(m_shortcuts.SetGameSpeedTo1))
            {
                QueueSpeed(1);
                return true;
            }
            if (m_shortcuts.IsDown(m_shortcuts.SetGameSpeedTo2))
            {
                QueueSpeed(2);
                return true;
            }
            if (m_shortcuts.IsDown(m_shortcuts.SetGameSpeedTo3))
            {
                QueueSpeed(3);
                return true;
            }
            if (m_shortcuts.IsDown(m_shortcuts.SetGameSpeedTo4) && controller.HasSuperSpeedOption)
            {
                QueueSpeed(GetLastSuperSpeed());
                return true;
            }
            if (m_shortcuts.IsDown(m_shortcuts.IncreaseGameSpeed))
            {
                int next = m_sequence.FirstOrDefault(value => value > m_simLoop.SimSpeedMult);
                if (m_simLoop.IsSimPaused)
                {
                    next = m_sequence.Length == 0 ? 0 : m_sequence[0];
                }
                if (next > 0)
                {
                    QueueSpeed(next);
                }
                return true;
            }
            if (m_shortcuts.IsDown(m_shortcuts.DecreaseGameSpeed))
            {
                int previous = 0;
                if (!m_simLoop.IsSimPaused)
                {
                    foreach (int value in m_sequence)
                    {
                        if (value >= m_simLoop.SimSpeedMult)
                        {
                            break;
                        }
                        previous = value;
                    }
                }
                if (previous > 0)
                {
                    QueueSpeed(previous);
                }
                return true;
            }
            return false;
        }

        private int GetLastSuperSpeed()
        {
            int speed = m_speedController.LastSeenSuperSpeed;
            if (speed < 4 || speed > MaxSpeed)
            {
                speed = Math.Min(12, MaxSpeed);
            }
            return Math.Max(4, speed);
        }

        private void TogglePause() =>
            m_inputScheduler.ScheduleInputCmd(new SetSimPauseStateCmd(!m_simLoop.IsSimPaused));

        private bool QueueSpeed(int speed, bool? resumeOverride = null)
        {
            if (!m_installed || speed < 1 || speed > MaxSpeed)
            {
                return false;
            }

            bool wasPaused = m_simLoop.IsSimPaused;
            bool highSpeed = speed > VanillaMaximum;
            if (highSpeed)
            {
                if (!m_highSpeedMode)
                {
                    m_originalAdaptiveModeForTransition = m_simLoop.AdaptiveSimSpeedMode;
                }
                if (!SimLoopAccess.TrySetAdaptiveSpeedMode(m_simLoop, SimAdaptiveSpeedMode.Uncapped, out string modeError))
                {
                    m_log.ErrorOnce("Failed to enable uncapped adaptive speed: " + modeError);
                    return false;
                }
                m_highSpeedMode = true;
                Volatile.Write(ref m_pendingHighSpeed, speed);
            }
            else
            {
                Volatile.Write(ref m_pendingHighSpeed, 0);
                RestoreAdaptiveModeIfNeeded();
            }

            try
            {
                // SetSpeed is the normal controller entry point. It schedules the same pause and
                // GameSpeedChangeCmd objects as vanilla and updates the controller's super-speed
                // bookkeeping for the native calendar controls.
                m_speedController.SetSpeed(speed);
                bool resume = resumeOverride ?? m_resumeOnSelect;
                if (wasPaused && !resume)
                {
                    // SetSpeed resumes before changing speed. A following normal pause command
                    // preserves the optional "stay paused" policy without mutating the sim loop.
                    m_inputScheduler.ScheduleInputCmd(new SetSimPauseStateCmd(isPaused: true));
                }
                return true;
            }
            catch (Exception exception)
            {
                Volatile.Write(ref m_pendingHighSpeed, 0);
                m_log.Exception(exception, "Unlocked speed could not schedule the normal game-speed command.");
                return false;
            }
        }

        private void RestoreAdaptiveModeIfNeeded()
        {
            if (!m_highSpeedMode)
            {
                return;
            }

            if (!SimLoopAccess.TrySetAdaptiveSpeedMode(m_simLoop, m_originalAdaptiveModeForTransition, out string restoreError))
            {
                m_log.Warning("Could not restore the pre-unlocked-speed adaptive mode: " + restoreError);
            }
            m_highSpeedMode = false;
        }

        private void OnVanillaSpeedCommandApplied(SimLoopEvents simLoop, int speedMult)
        {
            int pending = Volatile.Read(ref m_pendingHighSpeed);
            if (!ReferenceEquals(simLoop, m_simLoop) || pending == 0 || speedMult != pending || speedMult <= VanillaMaximum)
            {
                return;
            }

            if (!SimLoopAccess.TrySetRequestedSpeedUncapped(simLoop, speedMult, out string error))
            {
                m_log.ErrorOnce("Failed to apply queued unlocked simulation speed: " + error);
                return;
            }
            Volatile.Write(ref m_pendingHighSpeed, 0);
        }

        [ConsoleCommand(
            documentation: "Sets requested simulation speed through the normal game-speed command path without the vanilla 20x limit.",
            customCommandName: "set_game_speed_unlocked")]
        public string SetGameSpeedUnlocked(int speed)
        {
            int maxSpeed = MaxSpeed;
            if (speed < 1 || speed > maxSpeed)
            {
                return $"Invalid speed. Valid range is 1-{maxSpeed}.";
            }
            if (!QueueSpeed(speed))
            {
                return "Unlocked speed is unavailable on this game build.";
            }
            return
                $"Game speed command queued for {speed}x (adaptive mode: {(speed > VanillaMaximum ? "Uncapped" : m_simLoop.AdaptiveSimSpeedMode.ToString())}).";
        }

        [ConsoleCommand(
            documentation: "Shows requested speed, the configured stepping sequence, and the latest simulation-step budget/actual values.",
            customCommandName: "get_game_speed_unlocked")]
        public string GetGameSpeedUnlocked()
        {
            string state = m_simLoop.IsSimPaused ? "paused" : "running";
            string budget = m_simLoop.IsSimPaused
                ? "paused"
                : $"steps/update {m_simLoop.SimStepsPerUpdate}/{m_simLoop.BudgetedSimSteps}";
            string saturation = !m_simLoop.IsSimPaused &&
                                m_simLoop.BudgetedSimSteps > 0 &&
                                m_simLoop.SimStepsPerUpdate < m_simLoop.BudgetedSimSteps
                ? "budget not fully reached in the latest update"
                : "no shortfall reported by the latest update";
            string overtime = ReadOvertimeStatus();
            return
                $"Requested simulation speed: {m_simLoop.SimSpeedMult}x ({state}); configured max: {MaxSpeed}x; adaptive mode: {m_simLoop.AdaptiveSimSpeedMode}; {budget}; {saturation}.\n" +
                $"Speed sequence ({m_sequenceMode}): {string.Join(",", m_sequence)}; resume on selection: {m_resumeOnSelect}; {overtime}.";
        }

        private string ReadOvertimeStatus()
        {
            try
            {
                if (!m_gameRunnerProbeAttempted)
                {
                    m_gameRunnerProbeAttempted = true;
                    m_gameRunnerInstance = m_gameRunner.Value;
                    if (m_gameRunnerInstance is not null)
                    {
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        Type runnerType = m_gameRunnerInstance.GetType();
                        m_latestOvertimeProperty = runnerType.GetProperty("LatestSimUpdateWasOvertime", flags);
                        m_overtimeDurationProperty = runnerType.GetProperty("LatestSimUpdateOvertimeDuration", flags);
                    }
                }

                if (m_gameRunnerInstance is null || m_latestOvertimeProperty is null)
                {
                    return "overtime telemetry unavailable";
                }

                bool overtime = m_latestOvertimeProperty.GetValue(m_gameRunnerInstance) is true;
                if (!overtime)
                {
                    return "latest simulation update within budget";
                }

                if (m_overtimeDurationProperty?.GetValue(m_gameRunnerInstance) is TimeSpan duration)
                {
                    return $"latest simulation update overtime ({duration.TotalMilliseconds:F1} ms)";
                }
                return "latest simulation update overtime";
            }
            catch (Exception exception)
            {
                return "overtime telemetry unavailable (" + exception.GetType().Name + ")";
            }
        }

        [ConsoleCommand(
            documentation: "Restores the vanilla 20x maximum, vanilla speed-step sequence, and paused-selection behavior.",
            customCommandName: "tajs_unlocked_speed_reset")]
        public string ResetToVanilla()
        {
            SettingSetResult max = m_settings.TrySet(UnlockedSpeedSetting.ModId, UnlockedSpeedSetting.Key, VanillaMaximum);
            SettingSetResult mode = m_settings.TrySet(
                UnlockedSpeedSetting.ModId,
                UnlockedSpeedSetting.SequenceModeKey,
                UnlockedSpeedSetting.VanillaSequenceMode);
            SettingSetResult sequence = m_settings.TrySet(
                UnlockedSpeedSetting.ModId,
                UnlockedSpeedSetting.CustomSequenceKey,
                "1,2,3,12");
            SettingSetResult resume = m_settings.TrySet(
                UnlockedSpeedSetting.ModId,
                UnlockedSpeedSetting.ResumeOnSelectKey,
                true);
            SettingSetResult[] results = { max, mode, sequence, resume };
            SettingSetResult? failure = results.FirstOrDefault(result => !result.Success);
            if (failure is { } failed)
            {
                return "Could not restore all vanilla speed settings: " + failed.Error;
            }

            if (m_highSpeedMode)
            {
                RestoreAdaptiveModeIfNeeded();
            }
            Volatile.Write(ref m_pendingHighSpeed, 0);
            if (m_simLoop.SimSpeedMult > VanillaMaximum)
            {
                QueueSpeed(12, resumeOverride: false);
            }
            return "Unlocked speed reset to vanilla settings (20x maximum, 1x/2x/3x/12x sequence).";
        }

        private void OnTerminate()
        {
            m_settings.Changed -= OnSettingChanged;
            Volatile.Write(ref m_pendingHighSpeed, 0);
            try
            {
                m_harmony?.UnpatchAll(HarmonyId);
            }
            catch (Exception exception)
            {
                m_log.Exception(exception, "Unlocked speed patch cleanup failed during gameplay teardown.");
            }
            m_harmony = null;
            m_installed = false;
            if (s_current is not null && s_current.TryGetTarget(out UnlockedSpeedService? current) &&
                ReferenceEquals(current, this))
            {
                s_current = null;
            }
        }
    }
}
