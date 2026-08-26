// Taj's COI Mods | RuntimeDiagnosticsModels.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TajsCOI.Common.Diagnostics
{
    public enum HarmonyPatchKind
    {
        Prefix,
        Postfix,
        Transpiler,
        Finalizer,
    }

    public enum HarmonyCollisionRisk
    {
        None,
        Informational,
        Medium,
        High,
    }

    public enum RuntimeCapabilityState
    {
        Available,
        Degraded,
        Unavailable,
    }

    public enum RuntimeComponentLifetime
    {
        Process,
        GameplayScene,
    }

    public enum RuntimeRegistrationStatus
    {
        Added,
        Updated,
        AlreadyRegistered,
        Rejected,
    }

    public sealed class HarmonyPatchSnapshot
    {
        public HarmonyPatchSnapshot(
            HarmonyPatchKind kind,
            string ownerId,
            string patchMethod,
            int priority,
            IEnumerable<string>? before,
            IEnumerable<string>? after,
            bool isTajsOwned,
            bool returnsBoolean = false)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException("Harmony patch owner cannot be empty.", nameof(ownerId));
            }
            if (string.IsNullOrWhiteSpace(patchMethod))
            {
                throw new ArgumentException("Harmony patch method cannot be empty.", nameof(patchMethod));
            }

            Kind = kind;
            OwnerId = ownerId;
            PatchMethod = patchMethod;
            Priority = priority;
            Before = CopyStrings(before);
            After = CopyStrings(after);
            IsTajsOwned = isTajsOwned;
            ReturnsBoolean = returnsBoolean;
        }

        public HarmonyPatchKind Kind { get; }
        public string OwnerId { get; }
        public string PatchMethod { get; }
        public int Priority { get; }
        public IReadOnlyList<string> Before { get; }
        public IReadOnlyList<string> After { get; }
        public bool IsTajsOwned { get; }
        public bool ReturnsBoolean { get; }

        private static IReadOnlyList<string> CopyStrings(IEnumerable<string>? values) =>
            Array.AsReadOnly(
                (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    public sealed class HarmonyTargetSnapshot
    {
        public HarmonyTargetSnapshot(
            string originalAssembly,
            string originalType,
            string originalMethod,
            string originalSignature,
            IEnumerable<HarmonyPatchSnapshot>? patches,
            IEnumerable<string>? nonTajsOwners,
            HarmonyCollisionRisk risk,
            string? riskReason)
        {
            if (string.IsNullOrWhiteSpace(originalSignature))
            {
                throw new ArgumentException("Harmony target signature cannot be empty.", nameof(originalSignature));
            }

            OriginalAssembly = originalAssembly ?? string.Empty;
            OriginalType = originalType ?? string.Empty;
            OriginalMethod = originalMethod ?? string.Empty;
            OriginalSignature = originalSignature;
            Patches = Array.AsReadOnly((patches ?? Enumerable.Empty<HarmonyPatchSnapshot>()).ToArray());
            NonTajsOwners = CopyStrings(nonTajsOwners);
            Risk = risk;
            RiskReason = riskReason ?? string.Empty;
        }

        public string OriginalAssembly { get; }
        public string OriginalType { get; }
        public string OriginalMethod { get; }
        public string OriginalSignature { get; }
        public IReadOnlyList<HarmonyPatchSnapshot> Patches { get; }
        public IReadOnlyList<string> NonTajsOwners { get; }
        public HarmonyCollisionRisk Risk { get; }
        public string RiskReason { get; }
        public bool IsSharedTarget => NonTajsOwners.Count > 0;
        public int TajsPatchCount => Patches.Count(patch => patch.IsTajsOwned);

        private static IReadOnlyList<string> CopyStrings(IEnumerable<string>? values) =>
            Array.AsReadOnly(
                (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    public sealed class HarmonyInspectionSnapshot
    {
        public HarmonyInspectionSnapshot(
            DateTime capturedUtc,
            IEnumerable<HarmonyTargetSnapshot>? targets,
            string? error = null)
        {
            CapturedUtc = capturedUtc;
            Targets = Array.AsReadOnly((targets ?? Enumerable.Empty<HarmonyTargetSnapshot>()).ToArray());
            Error = error ?? string.Empty;
            TajsPatchedTargetCount = Targets.Count;
            SharedTargetCount = Targets.Count(target => target.IsSharedTarget);
            AttentionCount = Targets.Count(target =>
                target.Risk == HarmonyCollisionRisk.Medium || target.Risk == HarmonyCollisionRisk.High);
            TajsPatchCount = Targets.Sum(target => target.TajsPatchCount);
        }

        public DateTime CapturedUtc { get; }
        public IReadOnlyList<HarmonyTargetSnapshot> Targets { get; }
        public int TajsPatchedTargetCount { get; }
        public int SharedTargetCount { get; }
        public int AttentionCount { get; }
        public int TajsPatchCount { get; }
        public string Error { get; }
        public bool IsAvailable => Error.Length == 0;

        public static HarmonyInspectionSnapshot Empty(string error) =>
            new(DateTime.UtcNow, Array.Empty<HarmonyTargetSnapshot>(), error);
    }

    public sealed class RuntimeCapabilityDescriptor
    {
        public RuntimeCapabilityDescriptor(
            string capabilityId,
            string modId,
            string componentId,
            RuntimeCapabilityState state,
            string? version,
            string? details,
            string? reason,
            RuntimeComponentLifetime lifetime)
        {
            CapabilityId = Require(capabilityId, nameof(capabilityId));
            ModId = Require(modId, nameof(modId));
            ComponentId = Require(componentId, nameof(componentId));
            State = state;
            Version = version ?? string.Empty;
            Details = details ?? string.Empty;
            Reason = reason ?? string.Empty;
            Lifetime = lifetime;
        }

        public string CapabilityId { get; }
        public string ModId { get; }
        public string ComponentId { get; }
        public RuntimeCapabilityState State { get; }
        public string Version { get; }
        public string Details { get; }
        public string Reason { get; }
        public RuntimeComponentLifetime Lifetime { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Runtime identifier cannot be empty.", parameterName);
            }
            return value.Trim();
        }
    }

    public sealed class RuntimeComponentDescriptor
    {
        public RuntimeComponentDescriptor(
            string modId,
            string componentId,
            RuntimeComponentLifetime lifetime,
            string? expectedSeam,
            IEnumerable<string>? harmonyOwnerIds,
            IEnumerable<string>? requiredCapabilityIds,
            IEnumerable<string>? optionalCapabilityIds)
        {
            ModId = Require(modId, nameof(modId));
            ComponentId = Require(componentId, nameof(componentId));
            Lifetime = lifetime;
            ExpectedSeam = expectedSeam ?? string.Empty;
            HarmonyOwnerIds = CopyStrings(harmonyOwnerIds);
            RequiredCapabilityIds = CopyStrings(requiredCapabilityIds);
            OptionalCapabilityIds = CopyStrings(optionalCapabilityIds);
        }

        public string ModId { get; }
        public string ComponentId { get; }
        public RuntimeComponentLifetime Lifetime { get; }
        public string ExpectedSeam { get; }
        public IReadOnlyList<string> HarmonyOwnerIds { get; }
        public IReadOnlyList<string> RequiredCapabilityIds { get; }
        public IReadOnlyList<string> OptionalCapabilityIds { get; }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Runtime identifier cannot be empty.", parameterName);
            }
            return value.Trim();
        }

        private static IReadOnlyList<string> CopyStrings(IEnumerable<string>? values) =>
            Array.AsReadOnly(
                (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    public sealed class LoadedModSnapshot
    {
        public LoadedModSnapshot(string id, string? version, string? displayName, bool loadSucceeded, string? loadError)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Loaded mod ID cannot be empty.", nameof(id));
            }

            Id = id.Trim();
            Version = version ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName!;
            LoadSucceeded = loadSucceeded;
            LoadError = loadError ?? string.Empty;
        }

        public string Id { get; }
        public string Version { get; }
        public string DisplayName { get; }
        public bool LoadSucceeded { get; }
        public string LoadError { get; }
    }

    public sealed class RuntimeRegistrationResult
    {
        public RuntimeRegistrationResult(RuntimeRegistrationStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public RuntimeRegistrationStatus Status { get; }
        public string Message { get; }
        public bool IsSuccess => Status != RuntimeRegistrationStatus.Rejected;
    }
}
