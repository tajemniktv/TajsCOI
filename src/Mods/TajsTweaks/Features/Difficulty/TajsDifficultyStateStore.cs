// Taj's COI Mods | TajsDifficultyStateStore.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Core.Game;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    /// <summary>
    ///     Stores only the original difficulty values for one save slot. The file deliberately
    ///     contains scalar values and no game objects, delegates, resolver instances, or scene
    ///     references, so an invalid or stale file can never become part of the vanilla save blob.
    /// </summary>
    internal sealed class TajsDifficultyStateStore
    {
        private const string Header = "TajsTweaksDifficultyV1";
        private readonly Dictionary<string, string> m_originalValues = new(StringComparer.Ordinal);
        private string? m_filePath;

        internal IReadOnlyDictionary<string, string> OriginalValues => m_originalValues;

        internal void LoadOrCapture(string saveName, GameDifficultyConfig current, IReadOnlyDictionary<string, System.Reflection.PropertyInfo> properties)
        {
            m_originalValues.Clear();
            string safeName = Sanitize(saveName);
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsTweaks",
                "Difficulty",
                safeName.Length == 0 ? "current" : safeName);
            m_filePath = Path.Combine(directory, "state.txt");

            if (File.Exists(m_filePath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(m_filePath);
                    if (lines.Length > 0 && string.Equals(lines[0], Header, StringComparison.Ordinal))
                    {
                        foreach (string line in lines.Skip(1))
                        {
                            int separator = line.IndexOf('=');
                            if (separator <= 0 || separator == line.Length - 1)
                            {
                                continue;
                            }

                            string memberName = line.Substring(0, separator).Trim();
                            string encoded = line.Substring(separator + 1).Trim();
                            if (properties.ContainsKey(memberName) && encoded.Length > 0)
                            {
                                m_originalValues[memberName] = encoded;
                            }
                        }
                    }
                }
                catch
                {
                    m_originalValues.Clear();
                }
            }

            // A first load captures the values that actually came from the save. This is distinct
            // from the preset's vanilla defaults and remains stable after later Tajs changes. If a
            // later game version adds a field, fill only that missing field from the current save.
            // Existing entries are never overwritten.
            bool added = false;
            foreach (KeyValuePair<string, System.Reflection.PropertyInfo> pair in properties)
            {
                if (m_originalValues.ContainsKey(pair.Key))
                {
                    continue;
                }

                try
                {
                    if (TryEncode(pair.Value.GetValue(current), out string encoded))
                    {
                        m_originalValues[pair.Key] = encoded;
                        added = true;
                    }
                }
                catch
                {
                    // A single incompatible field is not allowed to block the remaining
                    // difficulty values from being captured.
                }
            }
            if (added || m_originalValues.Count == 0)
            {
                Save();
            }
        }

        internal bool TryGetOriginal(string memberName, System.Reflection.PropertyInfo property, out object? value)
        {
            value = null;
            return m_originalValues.TryGetValue(memberName, out string? encoded) &&
                   TryDecode(encoded, property.PropertyType, out value);
        }

        internal void Save()
        {
            if (m_filePath is null)
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(m_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporary = m_filePath + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllLines(
                    temporary,
                    new[] { Header }.Concat(m_originalValues.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value)),
                    new System.Text.UTF8Encoding(false));
                if (File.Exists(m_filePath))
                {
                    File.Replace(temporary, m_filePath, m_filePath + ".bak", true);
                }
                else
                {
                    File.Move(temporary, m_filePath);
                }
            }
            catch
            {
                // Optional metadata must never interfere with saving or loading the game.
            }
        }

        internal static bool TryEncode(object? value, out string encoded)
        {
            encoded = string.Empty;
            if (value is Percent percent)
            {
                encoded = percent == Percent.MaxValue
                    ? "unlimited"
                    : percent.ToIntPercentRounded().ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (value is Enum enumValue)
            {
                encoded = "enum:" + Convert.ToInt32(enumValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        internal static bool TryDecode(string encoded, Type targetType, out object? value)
        {
            value = null;
            if (targetType == typeof(Percent))
            {
                if (string.Equals(encoded, "unlimited", StringComparison.OrdinalIgnoreCase))
                {
                    value = Percent.MaxValue;
                    return true;
                }

                if (int.TryParse(encoded, NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
                {
                    value = percent.Percent();
                    return true;
                }
                return false;
            }

            if (targetType.IsEnum && encoded.StartsWith("enum:", StringComparison.Ordinal))
            {
                if (int.TryParse(encoded.Substring("enum:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int enumValue))
                {
                    value = Enum.ToObject(targetType, enumValue);
                    return true;
                }
            }

            return false;
        }

        private static string Sanitize(string? value)
        {
            string input = value?.Trim() ?? string.Empty;
            if (input.Length == 0)
            {
                return string.Empty;
            }

            char[] chars = input
                .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_')
                .ToArray();
            return new string(chars).Trim('.', '_');
        }
    }
}
