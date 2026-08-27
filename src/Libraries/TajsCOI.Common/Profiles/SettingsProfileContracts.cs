// Taj's COI Mods | SettingsProfileContracts.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Common.Profiles
{
    public sealed class SettingsProfile
    {
        public SettingsProfile(
            int schema,
            string suiteVersion,
            string name,
            IReadOnlyList<string> categories,
            IReadOnlyList<string> modules,
            IReadOnlyDictionary<string, object> values)
        {
            if (schema <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schema));
            }
            Schema = schema;
            SuiteVersion = Require(suiteVersion, nameof(suiteVersion));
            Name = Require(name, nameof(name));
            Categories = Copy(categories);
            Modules = Copy(modules);
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            foreach (KeyValuePair<string, object> item in values)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    throw new ArgumentException("Profile setting IDs cannot be empty.", nameof(values));
                }
                if (!IsPrimitive(item.Value))
                {
                    throw new ArgumentException(
                        "Profile values must be JSON primitives.",
                        nameof(values));
                }
            }

            Values = values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }

        public int Schema { get; }
        public string SuiteVersion { get; }
        public string Name { get; }
        public IReadOnlyList<string> Categories { get; }
        public IReadOnlyList<string> Modules { get; }
        public IReadOnlyDictionary<string, object> Values { get; }

        public SettingsProfile WithName(string name) =>
            new(Schema, SuiteVersion, name, Categories, Modules, Values);

        public SettingsProfile With(string name) => WithName(name);

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> values) =>
            new List<string>(values ?? throw new ArgumentNullException(nameof(values))).AsReadOnly();

        private static string Require(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Profile text cannot be empty.", parameter)
                : value.Trim();

        private static bool IsPrimitive(object? value) =>
            value is null || value is string || value is bool || value is byte || value is sbyte ||
            value is short || value is ushort || value is int || value is uint || value is long ||
            value is ulong || value is float || value is double || value is decimal;
    }

    public enum SettingsProfilePreviewState
    {
        Current,
        Proposed,
        Unavailable,
        Invalid,
        Unchanged,
    }

    public sealed class SettingsProfilePreviewEntry
    {
        public SettingsProfilePreviewEntry(
            string stableId,
            SettingsProfilePreviewState state,
            object? currentValue,
            object? proposedValue,
            string message)
        {
            StableId = stableId ?? throw new ArgumentNullException(nameof(stableId));
            State = state;
            CurrentValue = currentValue;
            ProposedValue = proposedValue;
            Message = message ?? string.Empty;
        }

        public string StableId { get; }
        public SettingsProfilePreviewState State { get; }
        public object? CurrentValue { get; }
        public object? ProposedValue { get; }
        public string Message { get; }
    }

    public sealed class SettingsProfilePreview
    {
        public SettingsProfilePreview(
            SettingsProfile profile,
            IReadOnlyList<SettingsProfilePreviewEntry> entries,
            IReadOnlyList<string> skippedIds)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            SkippedIds = skippedIds ?? throw new ArgumentNullException(nameof(skippedIds));
        }

        public SettingsProfile Profile { get; }
        public IReadOnlyList<SettingsProfilePreviewEntry> Entries { get; }
        public IReadOnlyList<string> SkippedIds { get; }
        public bool CanApply =>
            Entries.All(entry => entry.State != SettingsProfilePreviewState.Invalid);
    }

    public sealed class SettingsProfileApplyResult
    {
        public SettingsProfileApplyResult(int appliedCount, IReadOnlyList<string> skippedIds, IReadOnlyList<string> errors)
        {
            AppliedCount = appliedCount;
            SkippedIds = skippedIds ?? throw new ArgumentNullException(nameof(skippedIds));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public int AppliedCount { get; }
        public IReadOnlyList<string> SkippedIds { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Success => Errors.Count == 0;
    }

    public interface ISettingsProfileService
    {
        IReadOnlyList<SettingsProfile> List();

        bool TryGet(string name, out SettingsProfile? profile);

        SettingsProfilePreview Preview(SettingsProfile profile);

        SettingsProfileApplyResult Apply(SettingsProfile profile);

        bool TrySave(SettingsProfile profile, out string error);

        bool TryDelete(string name, out string error);

        bool TryDuplicate(string sourceName, string destinationName, out SettingsProfile? profile, out string error);

        bool TryRename(string sourceName, string destinationName, out SettingsProfile? profile, out string error);

        bool TryImport(string path, string? nameOverride, out SettingsProfile? profile, out string error);

        bool TryExport(string name, string path, out string error);
    }
}
