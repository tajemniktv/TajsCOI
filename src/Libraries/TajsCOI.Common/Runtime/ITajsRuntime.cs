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
        public ITajsLogger GetLogger(string modId, string componentId);

        public void ReportCompatibility(CompatibilityReport report);

        public IReadOnlyList<CompatibilityReport> GetCompatibilitySnapshot();
    }
}
