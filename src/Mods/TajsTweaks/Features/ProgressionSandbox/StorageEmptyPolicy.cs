// Taj's COI Mods | StorageEmptyPolicy.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Reflection;
using Mafi.Core.Buildings.Storages;
using CoiStorage = Mafi.Core.Buildings.Storages.Storage;

namespace TajsCOI.Tweaks.Features.ProgressionSandbox
{
    internal static class StorageEmptyPolicy
    {
        /// <summary>
        /// Emptying a storage is destructive. Both the opt-in setting and an explicit
        /// confirmation token are required; no callback or delegate is persisted in a save.
        /// </summary>
        internal static bool IsAuthorized(bool settingEnabled, bool confirmed) => settingEnabled && confirmed;

        internal static bool IsNativeStorageType(string? typeName) =>
            !string.IsNullOrWhiteSpace(typeName) && typeName!.IndexOf("Storage", System.StringComparison.OrdinalIgnoreCase) >= 0;

        internal static bool IsNativeClearAvailable() =>
            typeof(CoiStorage).GetMethod("Cheat_ForceClear", BindingFlags.Instance | BindingFlags.NonPublic) is not null;
    }
}
