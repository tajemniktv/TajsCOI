// Taj's COI Mods | ProfilerSettingsCatalog.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using TajsCOI.Common.Settings;
using TajsCOI.Profiler.Core;

namespace TajsCOI.Profiler
{
    internal static class ProfilerSettingsCatalog
    {
        internal const string ModId = "TajsProfiler";
        private const string ModDisplayName = "Taj's Profiler";
        private const SettingFlags Flags = SettingFlags.Advanced;

        internal const string FrameMillisecondsKey = "spike_frame_ms";
        internal const string WaitMillisecondsKey = "spike_wait_ms";
        internal const string SimulationMillisecondsKey = "spike_sim_ms";
        internal const string MajorPhaseMillisecondsKey = "spike_phase_ms";
        internal const string RelativeMultiplierKey = "spike_relative";
        internal const string CooldownSecondsKey = "spike_cooldown_s";
        internal const string MaximumCapturesKey = "spike_max_captures";
        internal const string PreWindowSecondsKey = "spike_pre_s";
        internal const string PostWindowSecondsKey = "spike_post_s";
        internal const string AutomaticDeepKey = "auto_deep_capture";
        internal const string CounterSamplingMillisecondsKey = "counter_sampling_ms";

        internal static readonly IReadOnlyList<SettingDescriptor> All = new[]
        {
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                FrameMillisecondsKey,
                "Frame spike threshold",
                "Absolute frame/update trigger in milliseconds.",
                50,
                1,
                1000,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                WaitMillisecondsKey,
                "Wait-for-sim threshold",
                "Absolute main-thread wait-for-simulation trigger in milliseconds.",
                20,
                1,
                1000,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                SimulationMillisecondsKey,
                "Simulation threshold",
                "Absolute simulation worker-cycle trigger in milliseconds.",
                50,
                1,
                1000,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                MajorPhaseMillisecondsKey,
                "Major phase threshold",
                "Absolute sync/render/simulation phase trigger in milliseconds.",
                25,
                1,
                1000,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                RelativeMultiplierKey,
                "Relative spike multiplier",
                "Trigger when a frame is this multiple of the rolling median.",
                3,
                1.1,
                20,
                0.1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                CooldownSecondsKey,
                "Automatic capture cooldown",
                "Minimum seconds between automatic broad captures.",
                30,
                1,
                600,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Integer(
                ModId,
                ModDisplayName,
                MaximumCapturesKey,
                "Maximum automatic captures",
                "Bound on retained automatic spike captures per session.",
                8,
                1,
                64,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                PreWindowSecondsKey,
                "Spike pre-window",
                "Seconds of broad history retained before a trigger.",
                3,
                0,
                30,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                PostWindowSecondsKey,
                "Spike post-window",
                "Seconds of broad history retained after a trigger.",
                4,
                1,
                30,
                1,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Boolean(
                ModId,
                ModDisplayName,
                AutomaticDeepKey,
                "Automatic deep capture",
                "Arms callback spans when an automatic broad spike trigger fires.",
                false,
                "Profiler",
                flags: Flags),
            SettingDescriptor.Float(
                ModId,
                ModDisplayName,
                CounterSamplingMillisecondsKey,
                "Runtime counter interval",
                "Minimum interval between Unity/managed memory counter reads.",
                250,
                50,
                2000,
                50,
                "Profiler",
                flags: Flags),
        };

        internal static void RegisterAll(ITajsSettings settings)
        {
            foreach (SettingDescriptor descriptor in All)
            {
                settings.Register(descriptor);
            }
        }

        internal static RuntimeSpikePolicy ReadPolicy(ITajsSettings settings)
        {
            return new RuntimeSpikePolicy(
                settings.Get<double>(ModId, FrameMillisecondsKey),
                settings.Get<double>(ModId, WaitMillisecondsKey),
                settings.Get<double>(ModId, SimulationMillisecondsKey),
                settings.Get<double>(ModId, MajorPhaseMillisecondsKey),
                settings.Get<double>(ModId, RelativeMultiplierKey),
                settings.Get<double>(ModId, CooldownSecondsKey),
                settings.Get<int>(ModId, MaximumCapturesKey),
                settings.Get<double>(ModId, PreWindowSecondsKey),
                settings.Get<double>(ModId, PostWindowSecondsKey),
                settings.Get<bool>(ModId, AutomaticDeepKey));
        }

        internal static double ReadCounterSamplingSeconds(ITajsSettings settings) =>
            Math.Max(0.05, settings.Get<double>(ModId, CounterSamplingMillisecondsKey) / 1000.0);
    }
}
