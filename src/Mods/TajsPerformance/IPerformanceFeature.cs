// Taj's COI Mods | IPerformanceFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Performance
{
    internal interface IPerformanceFeature
    {
        string Id { get; }

        string ConfigKey { get; }

        bool IsProcessPatchInstalled();

        void Install(ITajsRuntime runtime, ITajsLogger log);
    }
}
