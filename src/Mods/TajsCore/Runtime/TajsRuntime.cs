// Taj's COI Mods | TajsRuntime.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Core.Runtime
{
    [GlobalDependency(RegistrationMode.AsEverything)]
    internal sealed class TajsRuntime : ITajsRuntime
    {
        private readonly ConcurrentDictionary<ComponentKey, CompatibilityReport> m_compatibility = new();
        private readonly ConcurrentDictionary<ComponentKey, ITajsLogger> m_loggers = new();

        public ITajsLogger GetLogger(string modId, string componentId)
        {
            ComponentKey key = CreateKey(modId, componentId);
            return m_loggers.GetOrAdd(key, item => new TajsLogger(item.ModId, item.ComponentId));
        }

        public void ReportCompatibility(CompatibilityReport report)
        {
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var key = new ComponentKey(report.ModId, report.ComponentId);
            m_compatibility.AddOrUpdate(key, report, (_, _) => report);
        }

        public IReadOnlyList<CompatibilityReport> GetCompatibilitySnapshot() =>
            m_compatibility.Values
                .OrderBy(report => report.ModId, StringComparer.Ordinal)
                .ThenBy(report => report.ComponentId, StringComparer.Ordinal)
                .ToArray();

        private static ComponentKey CreateKey(string modId, string componentId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Runtime mod ID cannot be empty.", nameof(modId));
            }

            if (string.IsNullOrWhiteSpace(componentId))
            {
                throw new ArgumentException("Runtime component ID cannot be empty.", nameof(componentId));
            }

            return new ComponentKey(modId, componentId);
        }
    }
}
