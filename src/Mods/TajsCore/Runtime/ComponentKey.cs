// Taj's COI Mods | ComponentKey.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;

namespace TajsCOI.Core.Runtime
{
    internal readonly struct ComponentKey : IEquatable<ComponentKey>
    {
        internal ComponentKey(string modId, string componentId)
        {
            ModId = modId;
            ComponentId = componentId;
        }

        internal string ModId { get; }

        internal string ComponentId { get; }

        public bool Equals(ComponentKey other) =>
            string.Equals(ModId, other.ModId, StringComparison.Ordinal) &&
            string.Equals(ComponentId, other.ComponentId, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ComponentKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return StringComparer.Ordinal.GetHashCode(ModId) * 397 ^
                       StringComparer.Ordinal.GetHashCode(ComponentId);
            }
        }
    }
}
