// Taj's COI Mods | SettingSnapshot.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Common.Settings
{
    public sealed class SettingSnapshot
    {
        public SettingSnapshot(SettingDescriptor descriptor, object value)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public SettingDescriptor Descriptor { get; }
        public object Value { get; }
    }
}
