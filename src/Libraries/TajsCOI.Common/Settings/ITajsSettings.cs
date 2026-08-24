// Taj's COI Mods | ITajsSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace TajsCOI.Common.Settings
{
    public interface ITajsSettings
    {
        event EventHandler<SettingChangedEventArgs> Changed;

        void Register(SettingDescriptor descriptor);

        T Get<T>(string modId, string key);

        SettingSetResult TrySet(string modId, string key, object? value);

        IReadOnlyList<SettingSnapshot> GetSnapshot();
    }
}
