// Taj's COI Mods | SettingChangedEventArgs.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Common.Settings
{
    public sealed class SettingChangedEventArgs : EventArgs
    {
        public SettingChangedEventArgs(SettingDescriptor descriptor, object oldValue, object newValue)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            OldValue = oldValue ?? throw new ArgumentNullException(nameof(oldValue));
            NewValue = newValue ?? throw new ArgumentNullException(nameof(newValue));
        }

        public SettingDescriptor Descriptor { get; }
        public object OldValue { get; }
        public object NewValue { get; }
    }
}
