// Taj's COI Mods | ITajsRuntime.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Diagnostics;
using TajsCOI.Common.Logging;

namespace TajsCOI.Common.Runtime
{
    public interface ITajsRuntime
    {
        public ITajsLogger GetLogger(string modId, string componentId);

        public void ReportCompatibility(CompatibilityReport report);

        public IReadOnlyList<CompatibilityReport> GetCompatibilitySnapshot();

        public RuntimeRegistrationResult RegisterCapability(RuntimeCapabilityDescriptor capability);

        public RuntimeRegistrationResult RegisterComponent(RuntimeComponentDescriptor component);

        public bool IsCapabilityAvailable(string capabilityId);

        public IReadOnlyList<RuntimeCapabilityDescriptor> GetCapabilitySnapshot();

        public IReadOnlyList<RuntimeComponentDescriptor> GetComponentSnapshot();

        public IReadOnlyList<LoadedModSnapshot> GetLoadedModSnapshot();

        public HarmonyInspectionSnapshot GetHarmonyInspectionSnapshot();

        /// <summary>
        /// Drops registrations marked as gameplay-scene lifetime at the scene boundary.
        /// Process-lifetime metadata remains intact.
        /// </summary>
        public void ClearGameplaySceneRegistrations();
    }
}
