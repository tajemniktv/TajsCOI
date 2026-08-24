// Taj's COI Mods | SettingSetResult.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace TajsCOI.Common.Settings
{
    public sealed class SettingSetResult
    {
        private SettingSetResult(bool success, object? value, string error, SettingApplyMode applyMode)
        {
            Success = success;
            Value = value;
            Error = error;
            ApplyMode = applyMode;
        }

        public bool Success { get; }
        public object? Value { get; }
        public string Error { get; }
        public SettingApplyMode ApplyMode { get; }

        public static SettingSetResult Accepted(object value, SettingApplyMode applyMode) =>
            new(true, value, string.Empty, applyMode);

        public static SettingSetResult Rejected(string error) =>
            new(false, null, error, SettingApplyMode.Immediate);
    }
}
