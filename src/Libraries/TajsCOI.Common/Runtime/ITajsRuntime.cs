// Taj's COI Mods | ITajsRuntime.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;

namespace TajsCOI.Common.Runtime
{
    public interface ITajsRuntime
    {
        ITajsLogger GetLogger(string modId, string componentId);

        void ReportCompatibility(CompatibilityReport report);

        IReadOnlyList<CompatibilityReport> GetCompatibilitySnapshot();
    }
}
