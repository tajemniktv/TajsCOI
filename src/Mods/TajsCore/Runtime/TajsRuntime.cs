// Taj's COI Mods | TajsRuntime.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Diagnostics;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Runtime;
using TajsCOI.Core.Diagnostics;

namespace TajsCOI.Core.Runtime
{
    [GlobalDependency(RegistrationMode.AsEverything)]
    internal sealed class TajsRuntime : ITajsRuntime
    {
        private readonly ConcurrentDictionary<ComponentKey, CompatibilityReport> m_compatibility = new();
        private readonly ConcurrentDictionary<ComponentKey, ITajsLogger> m_loggers = new();
        private readonly ConcurrentDictionary<string, RuntimeCapabilityDescriptor> m_capabilities = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<ComponentKey, RuntimeComponentDescriptor> m_components = new();

        public ITajsLogger GetLogger(string modId, string componentId)
        {
            ComponentKey key = CreateKey(modId, componentId);
            return m_loggers.GetOrAdd(key, item => new TajsLogger(item.ModId, item.ComponentId));
        }

        public void ReportCompatibility(CompatibilityReport report)
        {
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var key = new ComponentKey(report.ModId, report.ComponentId);
            m_compatibility.AddOrUpdate(key, report, (_, _) => report);
        }

        public IReadOnlyList<CompatibilityReport> GetCompatibilitySnapshot() =>
            m_compatibility.Values
                .OrderBy(report => report.ModId, StringComparer.Ordinal)
                .ThenBy(report => report.ComponentId, StringComparer.Ordinal)
                .ToArray();

        public RuntimeRegistrationResult RegisterCapability(RuntimeCapabilityDescriptor capability)
        {
            if (capability is null)
            {
                throw new ArgumentNullException(nameof(capability));
            }

            if (m_capabilities.TryAdd(capability.CapabilityId, capability))
            {
                return new RuntimeRegistrationResult(
                    RuntimeRegistrationStatus.Added,
                    "Capability registered: " + capability.CapabilityId);
            }

            RuntimeCapabilityDescriptor previous = m_capabilities[capability.CapabilityId];

            if (!SameCapabilityOwner(previous, capability))
            {
                return RejectRegistration(
                    "Capability '" + capability.CapabilityId + "' is already owned by " +
                    previous.ModId + "/" + previous.ComponentId + ".");
            }

            if (SameCapabilityValues(previous, capability))
            {
                return new RuntimeRegistrationResult(
                    RuntimeRegistrationStatus.AlreadyRegistered,
                    "Capability already registered: " + capability.CapabilityId);
            }

            m_capabilities[capability.CapabilityId] = capability;
            return new RuntimeRegistrationResult(
                RuntimeRegistrationStatus.Updated,
                "Capability updated: " + capability.CapabilityId);
        }

        public RuntimeRegistrationResult RegisterComponent(RuntimeComponentDescriptor component)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            ComponentKey key = CreateKey(component.ModId, component.ComponentId);
            if (m_components.TryAdd(key, component))
            {
                return new RuntimeRegistrationResult(
                    RuntimeRegistrationStatus.Added,
                    "Component registered: " + component.ModId + "/" + component.ComponentId);
            }

            RuntimeComponentDescriptor previous = m_components[key];

            if (!SameComponentValues(previous, component))
            {
                return RejectRegistration(
                    "Component '" + component.ModId + "/" + component.ComponentId + "' was registered with incompatible metadata.");
            }

            return new RuntimeRegistrationResult(
                RuntimeRegistrationStatus.AlreadyRegistered,
                "Component already registered: " + component.ModId + "/" + component.ComponentId);
        }

        public bool IsCapabilityAvailable(string capabilityId) =>
            !string.IsNullOrWhiteSpace(capabilityId) &&
            m_capabilities.TryGetValue(capabilityId.Trim(), out RuntimeCapabilityDescriptor? capability) &&
            capability.State == RuntimeCapabilityState.Available;

        public IReadOnlyList<RuntimeCapabilityDescriptor> GetCapabilitySnapshot() =>
            m_capabilities.Values
                .OrderBy(capability => capability.CapabilityId, StringComparer.Ordinal)
                .ToArray();

        public IReadOnlyList<RuntimeComponentDescriptor> GetComponentSnapshot() =>
            m_components.Values
                .OrderBy(component => component.ModId, StringComparer.Ordinal)
                .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
                .ToArray();

        public IReadOnlyList<LoadedModSnapshot> GetLoadedModSnapshot() =>
            Mafi.Core.Mods.ModsLoader.LoadedAndFailedMods
                .Where(mod => mod.Manifest.Id.StartsWith("Tajs", StringComparison.Ordinal))
                .Select(mod => new LoadedModSnapshot(
                    mod.Manifest.Id,
                    Convert.ToString(mod.Manifest.Version, System.Globalization.CultureInfo.InvariantCulture),
                    mod.Manifest.DisplayName,
                    !mod.LoadError.HasValue,
                    mod.LoadError.HasValue ? mod.LoadError.Value.ToString() : string.Empty))
                .OrderBy(mod => mod.Id, StringComparer.Ordinal)
                .ToArray();

        public HarmonyInspectionSnapshot GetHarmonyInspectionSnapshot() => HarmonyInspector.Capture();

        private RuntimeRegistrationResult RejectRegistration(string message)
        {
            ReportCompatibility(
                new CompatibilityReport(
                    "TajsCore",
                    "RuntimeRegistry",
                    CompatibilityState.Degraded,
                    "unique capability/component ownership",
                    message,
                    "The conflicting registration was rejected; the first authoritative owner remains active."));
            return new RuntimeRegistrationResult(RuntimeRegistrationStatus.Rejected, message);
        }

        private static bool SameCapabilityOwner(RuntimeCapabilityDescriptor left, RuntimeCapabilityDescriptor right) =>
            string.Equals(left.ModId, right.ModId, StringComparison.Ordinal) &&
            string.Equals(left.ComponentId, right.ComponentId, StringComparison.Ordinal) &&
            left.Lifetime == right.Lifetime;

        private static bool SameCapabilityValues(RuntimeCapabilityDescriptor left, RuntimeCapabilityDescriptor right) =>
            SameCapabilityOwner(left, right) &&
            left.State == right.State &&
            string.Equals(left.Version, right.Version, StringComparison.Ordinal) &&
            string.Equals(left.Details, right.Details, StringComparison.Ordinal) &&
            string.Equals(left.Reason, right.Reason, StringComparison.Ordinal);

        private static bool SameComponentValues(RuntimeComponentDescriptor left, RuntimeComponentDescriptor right) =>
            left.Lifetime == right.Lifetime &&
            string.Equals(left.ExpectedSeam, right.ExpectedSeam, StringComparison.Ordinal) &&
            left.HarmonyOwnerIds.SequenceEqual(right.HarmonyOwnerIds, StringComparer.Ordinal) &&
            left.RequiredCapabilityIds.SequenceEqual(right.RequiredCapabilityIds, StringComparer.Ordinal) &&
            left.OptionalCapabilityIds.SequenceEqual(right.OptionalCapabilityIds, StringComparer.Ordinal);

        private static ComponentKey CreateKey(string modId, string componentId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Runtime mod ID cannot be empty.", nameof(modId));
            }

            if (string.IsNullOrWhiteSpace(componentId))
            {
                throw new ArgumentException("Runtime component ID cannot be empty.", nameof(componentId));
            }

            return new ComponentKey(modId, componentId);
        }
    }
}
