// Taj's COI Mods | ITransportFlowLimitReader.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsCOI.Common.Tuning
{
    /// <summary>
    ///     Read-only view of per-transport flow policies for diagnostics. The implementation is
    ///     owned by TajsTweaks; consumers such as TajsProfiler depend only on this Common
    ///     contract and never on the gameplay mod assembly.
    /// </summary>
    public interface ITransportFlowLimitReader
    {
        /// <summary>
        ///     Gets the configured limit in product units per simulation second. A missing entry
        ///     (or a non-positive value) means unlimited/native flow.
        /// </summary>
        bool TryGetConfiguredLimit(int entityId, out double unitsPerSimulationSecond);
    }
}
