// Taj's COI Mods | TajsVanillaShortcutBridge.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using Mafi.Unity.InputControl;
using TajsCOI.Common.Shortcuts;

namespace TajsCOI.Tweaks.Features.Shortcuts
{
    /// <summary>
    ///     Reads the native 0.8.7b shortcut table once so Core can report real vanilla conflicts.
    ///     The bridge retains only value strings in the Core registry and never owns native input.
    /// </summary>
    internal static class TajsVanillaShortcutBridge
    {
        internal static bool TryCache(IShortcutRegistry registry, out int cached, out string error)
        {
            cached = 0;
            error = string.Empty;
            if (registry is null)
            {
                error = "Shortcut registry is unavailable.";
                return false;
            }

            try
            {
                var bindings = new List<KeyValuePair<string, ShortcutCombination>>();
                foreach (KeyValuePair<System.Reflection.PropertyInfo, KbAttribute> item in ShortcutsStorage.GetAllAttributes())
                {
                    if (item.Key.GetValue(ShortcutsStorage.Current) is not KeyBindings keyBindings)
                    {
                        continue;
                    }

                    AddBinding(bindings, item.Value.GroupId + ".primary", keyBindings.Primary);
                    AddBinding(bindings, item.Value.GroupId + ".secondary", keyBindings.Secondary);
                }

                registry.CacheVanillaBindings(bindings);
                cached = bindings.Count;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name;
                return false;
            }
        }

        internal static bool TryNormalizeNativeBinding(string? nativeBinding, out ShortcutCombination combination)
        {
            string normalized = (nativeBinding ?? string.Empty)
                .Replace("LeftControl", "CTRL")
                .Replace("RightControl", "CTRL")
                .Replace("LeftShift", "SHIFT")
                .Replace("RightShift", "SHIFT")
                .Replace("LeftAlt", "ALT")
                .Replace("RightAlt", "ALT")
                .Replace("LeftMeta", "META")
                .Replace("RightMeta", "META")
                .Replace("LeftWindows", "META")
                .Replace("RightWindows", "META")
                .Replace("LeftCommand", "META")
                .Replace("RightCommand", "META");
            return ShortcutCombination.TryParse(normalized, out combination);
        }

        private static void AddBinding(
            ICollection<KeyValuePair<string, ShortcutCombination>> destination,
            string id,
            KeyBinding binding)
        {
            if (!binding.IsEmpty && TryNormalizeNativeBinding(binding.ToString(), out ShortcutCombination combination) &&
                !combination.IsEmpty)
            {
                destination.Add(new KeyValuePair<string, ShortcutCombination>(id, combination));
            }
        }
    }
}
