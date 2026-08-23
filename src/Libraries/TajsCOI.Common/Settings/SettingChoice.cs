// Taj's COI Mods | SettingChoice.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Common.Settings
{
    public sealed class SettingChoice
    {
        public SettingChoice(string value, string displayName)
        {
            Value = Require(value, nameof(value));
            DisplayName = Require(displayName, nameof(displayName));
        }

        public string Value { get; }
        public string DisplayName { get; }

        private static string Require(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Setting choice text cannot be empty.", parameter)
                : value.Trim();
    }
}
