// Taj's COI Mods | CompatibilityReport.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Common.Compatibility
{
    public sealed class CompatibilityReport
    {
        public CompatibilityReport(
            string modId,
            string componentId,
            CompatibilityState state,
            string? expected,
            string? observed,
            string? reason)
        {
            ModId = RequireId(modId, nameof(modId));
            ComponentId = RequireId(componentId, nameof(componentId));
            State = state;
            Expected = expected ?? string.Empty;
            Observed = observed ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public string ModId { get; }

        public string ComponentId { get; }

        public CompatibilityState State { get; }

        public string Expected { get; }

        public string Observed { get; }

        public string Reason { get; }

        private static string RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Compatibility identifiers cannot be empty.", parameterName);
            }

            return value;
        }
    }
}
