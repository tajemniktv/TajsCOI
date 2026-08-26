// Taj's COI Mods | ITajsSettings.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;

namespace TajsCOI.Common.Settings
{
    public interface ITajsSettings
    {
        public event EventHandler<SettingChangedEventArgs> Changed;

        public void Register(SettingDescriptor descriptor);

        public T Get<T>(string modId, string key);

        public SettingSetResult TrySet(string modId, string key, object? value);

        public IReadOnlyList<SettingSnapshot> GetSnapshot();
    }
}
