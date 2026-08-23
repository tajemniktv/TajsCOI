// Taj's COI Mods | ITajsLogger.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Common.Logging
{
    public interface ITajsLogger
    {
        void Info(string message);

        void Warning(string message);

        void WarningOnce(string message);

        void Error(string message);

        void ErrorOnce(string message);

        void Exception(Exception exception, string? message = null);
    }
}
