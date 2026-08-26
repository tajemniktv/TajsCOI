// Taj's COI Mods | OverclockingStateStore.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    internal sealed class OverclockEntityPolicy
    {
        internal bool HasManualOverride;
        internal int ManualPercent = 100;
        internal bool HasAutoOverride;
        internal bool Auto;
        internal bool HasBoundsOverride;
        internal int MinPercent = 100;
        internal int MaxPercent = 300;
    }

    internal sealed class OverclockGroup
    {
        internal int Id;
        internal string Name = string.Empty;
        internal bool Locked;
        internal int ColorIndex;
        internal int HighlightAlpha = 85;
        internal int ManualDefault;
        internal bool Auto;
        internal int MinPercent = 100;
        internal int MaxPercent = 300;
        internal readonly HashSet<int> Members = new();
    }

    /// <summary>
    ///     Save-scoped policy metadata. It deliberately lives outside the vanilla save serializer;
    ///     native Machine.m_speedFactorBase remains the persisted machine speed value, while this
    ///     file holds transport speed plus Auto/group intent and therefore cannot alter vanilla
    ///     object/class ID allocation.
    /// </summary>
    internal sealed class OverclockingStateStore
    {
        private const string Header = "TajsTweaksOverclockingV1";
        private readonly Dictionary<int, OverclockEntityPolicy> m_entities = new();
        private readonly List<OverclockGroup> m_groups = new();
        private string? m_filePath;
        private int m_nextGroupId = 1;
        private int m_selectedGroupId = -1;

        internal IReadOnlyDictionary<int, OverclockEntityPolicy> Entities => m_entities;
        internal IReadOnlyList<OverclockGroup> Groups => m_groups;
        internal int SelectedGroupId => m_selectedGroupId;

        internal void LoadForSave(string saveName)
        {
            m_entities.Clear();
            m_groups.Clear();
            m_nextGroupId = 1;
            m_selectedGroupId = -1;
            string safeName = Sanitize(saveName);
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Captain of Industry",
                "TajsTweaks",
                "Overclocking",
                safeName.Length == 0 ? "current" : safeName);
            m_filePath = Path.Combine(directory, "state.txt");

            if (!File.Exists(m_filePath))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(m_filePath))
                {
                    ParseLine(line);
                }
            }
            catch
            {
                // A malformed optional policy file must leave the game at native rates.
                m_entities.Clear();
                m_groups.Clear();
                m_nextGroupId = 1;
                m_selectedGroupId = -1;
            }
        }

        internal OverclockEntityPolicy GetOrCreateEntity(int entityId)
        {
            if (!m_entities.TryGetValue(entityId, out OverclockEntityPolicy? policy))
            {
                policy = new OverclockEntityPolicy();
                m_entities[entityId] = policy;
            }

            return policy;
        }

        internal bool TryGetEntity(int entityId, out OverclockEntityPolicy? policy) => m_entities.TryGetValue(entityId, out policy);

        internal void RemoveEntity(int entityId) => m_entities.Remove(entityId);

        internal OverclockGroup? GetGroup(int groupId) => m_groups.FirstOrDefault(group => group.Id == groupId);

        internal OverclockGroup? GetGroupForEntity(int entityId) =>
            m_groups.FirstOrDefault(group => group.Members.Contains(entityId));

        internal OverclockGroup CreateGroup(string? requestedName)
        {
            string name = string.IsNullOrWhiteSpace(requestedName) ? "Group " + (m_groups.Count + 1) : CleanName(requestedName);
            var group = new OverclockGroup { Id = m_nextGroupId++, Name = name };
            m_groups.Add(group);
            m_selectedGroupId = group.Id;
            Save();
            return group;
        }

        internal bool DeleteGroup(int groupId)
        {
            int removed = m_groups.RemoveAll(group => group.Id == groupId);
            if (removed == 0)
            {
                return false;
            }

            if (m_selectedGroupId == groupId)
            {
                m_selectedGroupId = m_groups.Count == 0 ? -1 : m_groups[0].Id;
            }

            Save();
            return true;
        }

        internal bool RenameGroup(int groupId, string name)
        {
            OverclockGroup? group = GetGroup(groupId);
            if (group is null || group.Locked || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            group.Name = CleanName(name);
            Save();
            return true;
        }

        internal bool AddMember(int groupId, int entityId)
        {
            OverclockGroup? group = GetGroup(groupId);
            if (group is null || group.Locked)
            {
                return false;
            }

            foreach (OverclockGroup other in m_groups)
            {
                if (other.Id != groupId)
                {
                    other.Members.Remove(entityId);
                }
            }

            bool changed = group.Members.Add(entityId);
            if (changed)
            {
                Save();
            }

            return changed;
        }

        internal bool RemoveMember(int groupId, int entityId)
        {
            OverclockGroup? group = GetGroup(groupId);
            if (group is null || group.Locked || !group.Members.Remove(entityId))
            {
                return false;
            }

            Save();
            return true;
        }

        internal bool SetSelected(int groupId)
        {
            if (groupId != -1 && GetGroup(groupId) is null)
            {
                return false;
            }

            m_selectedGroupId = groupId;
            Save();
            return true;
        }

        internal void Prune(Func<int, bool> exists)
        {
            bool changed = false;
            foreach (int id in m_entities.Keys.Where(id => !exists(id)).ToArray())
            {
                m_entities.Remove(id);
                changed = true;
            }

            foreach (OverclockGroup group in m_groups)
            {
                int removed = group.Members.RemoveWhere(id => !exists(id));
                changed |= removed > 0;
            }

            if (changed)
            {
                Save();
            }
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

                var lines = new List<string> { Header, "S\t" + m_selectedGroupId + "\t" + m_nextGroupId };
                foreach (KeyValuePair<int, OverclockEntityPolicy> pair in m_entities.OrderBy(pair => pair.Key))
                {
                    OverclockEntityPolicy policy = pair.Value;
                    lines.Add(
                        string.Join(
                            "\t",
                            "E",
                            pair.Key,
                            Bool(policy.HasManualOverride),
                            policy.ManualPercent,
                            Bool(policy.HasAutoOverride),
                            Bool(policy.Auto),
                            Bool(policy.HasBoundsOverride),
                            policy.MinPercent,
                            policy.MaxPercent));
                }

                foreach (OverclockGroup group in m_groups.OrderBy(group => group.Id))
                {
                    string members = string.Join(",", group.Members.OrderBy(id => id));
                    string name = Convert.ToBase64String(Encoding.UTF8.GetBytes(CleanName(group.Name)));
                    lines.Add(
                        string.Join(
                            "\t",
                            "G",
                            group.Id,
                            Bool(group.Locked),
                            group.ColorIndex,
                            group.HighlightAlpha,
                            group.ManualDefault,
                            Bool(group.Auto),
                            group.MinPercent,
                            group.MaxPercent,
                            name,
                            members));
                }

                string temporaryPath = m_filePath + ".tmp";
                File.WriteAllLines(temporaryPath, lines);
                if (File.Exists(m_filePath))
                {
                    File.Replace(temporaryPath, m_filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, m_filePath);
                }
            }
            catch
            {
                // Policy persistence is best-effort and never blocks gameplay.
            }
        }

        private void ParseLine(string line)
        {
            string[] fields = line.Split('\t');
            if (fields.Length == 0 || fields[0] == Header)
            {
                return;
            }

            if (fields[0] == "S" && fields.Length >= 3 && int.TryParse(fields[1], out int selected) &&
                int.TryParse(fields[2], out int next))
            {
                m_selectedGroupId = selected;
                m_nextGroupId = Math.Max(1, next);
                return;
            }

            if (fields[0] == "E" && fields.Length >= 9 && int.TryParse(fields[1], out int entityId))
            {
                var policy = new OverclockEntityPolicy
                {
                    HasManualOverride = ParseBool(fields[2]),
                    ManualPercent = ParseInt(fields[3], 100),
                    HasAutoOverride = ParseBool(fields[4]),
                    Auto = ParseBool(fields[5]),
                    HasBoundsOverride = ParseBool(fields[6]),
                    MinPercent = ParseInt(fields[7], 100),
                    MaxPercent = ParseInt(fields[8], 300),
                };
                m_entities[entityId] = policy;
                return;
            }

            if (fields[0] == "G" && fields.Length >= 11 && int.TryParse(fields[1], out int groupId))
            {
                string name;
                try
                {
                    name = Encoding.UTF8.GetString(Convert.FromBase64String(fields[9]));
                }
                catch
                {
                    name = "Group " + groupId;
                }

                var group = new OverclockGroup
                {
                    Id = groupId,
                    Locked = ParseBool(fields[2]),
                    ColorIndex = ParseInt(fields[3], 0),
                    HighlightAlpha = Math.Max(4, Math.Min(100, ParseInt(fields[4], 85))),
                    ManualDefault = ParseInt(fields[5], 0),
                    Auto = ParseBool(fields[6]),
                    MinPercent = ParseInt(fields[7], 100),
                    MaxPercent = ParseInt(fields[8], 300),
                    Name = CleanName(name),
                };
                foreach (string member in fields[10].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(member, out int id) && id >= 0)
                    {
                        group.Members.Add(id);
                    }
                }

                m_groups.RemoveAll(existing => existing.Id == groupId);
                m_groups.Add(group);
                m_nextGroupId = Math.Max(m_nextGroupId, groupId + 1);
            }
        }

        private static string Bool(bool value) => value ? "1" : "0";
        private static bool ParseBool(string value) => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        private static int ParseInt(string value, int fallback) => int.TryParse(value, out int result) ? result : fallback;

        private static string CleanName(string? value) => (value ?? string.Empty).Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();

        private static string Sanitize(string value)
        {
            string result = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalid, '_');
            }

            return result.Trim();
        }
    }
}
