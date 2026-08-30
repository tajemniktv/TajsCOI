// Taj's COI Mods | LightingEffectiveState.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsCOI.Visuals.Features.Lighting
{
    /// <summary>
    ///     Read-only description of the policy inputs and result used by the single lighting
    ///     backend. It carries no Unity references and is therefore safe for diagnostics/tests.
    /// </summary>
    internal readonly struct LightingEffectiveState
    {
        internal LightingEffectiveState(
            LightingPolicy baseLightingPolicy,
            LightingPolicy? timeOfDayPresentation,
            LightingPolicy effectivePolicy,
            bool isInitialized)
        {
            BaseLightingPolicy = baseLightingPolicy;
            TimeOfDayPresentation = timeOfDayPresentation;
            EffectivePolicy = effectivePolicy;
            IsInitialized = isInitialized;
        }

        internal LightingPolicy BaseLightingPolicy { get; }
        internal LightingPolicy? TimeOfDayPresentation { get; }
        internal LightingPolicy EffectivePolicy { get; }
        internal bool IsInitialized { get; }
    }
}
