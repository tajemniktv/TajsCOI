// Taj's COI Mods | IPerformanceFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance
{
    internal interface IPerformanceFeature
    {
        public string Id { get; }

        public string ConfigKey { get; }

        public bool IsProcessPatchInstalled();

        public void Install(ITajsRuntime runtime, ITajsLogger log);
    }
}
