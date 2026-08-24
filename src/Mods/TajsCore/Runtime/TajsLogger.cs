// Taj's COI Mods | TajsLogger.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi;
using TajsCOI.Common.Logging;

namespace TajsCOI.Core.Runtime
{
    internal sealed class TajsLogger : ITajsLogger
    {
        private readonly string m_prefix;

        internal TajsLogger(string modId, string componentId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Log mod ID cannot be empty.", nameof(modId));
            }

            if (string.IsNullOrWhiteSpace(componentId))
            {
                throw new ArgumentException("Log component ID cannot be empty.", nameof(componentId));
            }

            m_prefix = $"[TajsCOI][{modId}][{componentId}] ";
        }

        public void Info(string message) => Log.Info(Prefix(message));

        public void Warning(string message) => Log.Warning(Prefix(message));

        public void WarningOnce(string message) => Log.WarningOnce(Prefix(message));

        public void Error(string message) => Log.Error(Prefix(message));

        public void ErrorOnce(string message) => Log.ErrorOnce(Prefix(message));

        public void Exception(Exception exception, string? message = null)
        {
            if (exception is null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            Log.Exception(exception, string.IsNullOrEmpty(message) ? m_prefix.TrimEnd() : Prefix(message));
        }

        private string Prefix(string? message) => m_prefix + (message ?? string.Empty);
    }
}
