// Taj's COI Mods | ITajsLogger.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Common.Logging
{
    public interface ITajsLogger
    {
        public void Info(string message);

        public void Warning(string message);

        public void WarningOnce(string message);

        public void Error(string message);

        public void ErrorOnce(string message);

        public void Exception(Exception exception, string? message = null);
    }
}
