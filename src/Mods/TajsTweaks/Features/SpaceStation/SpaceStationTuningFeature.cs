// Taj's COI Mods | SpaceStationTuningFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core.Prototypes;
using Mafi.Core.SpaceProgram;
using TajsCOI.Common.Tuning;
using TajsCOI.Tweaks.Features.Tuning;

namespace TajsCOI.Tweaks.Features.SpaceStation
{
    internal enum SpaceStationFieldLifecycle
    {
        LiveFutureTick,
        FutureUpgradeOnly,
        ReloadRequired,
    }

    /// <summary>
    /// Describes one exact 0.8.7b station seam.  The descriptor remains useful when a private
    /// member is unavailable: compatibility can report that member without hiding the other
    /// station controls.
    /// </summary>
    internal sealed class SpaceStationTuningDescriptor
    {
        internal SpaceStationTuningDescriptor(
            string key,
            string memberName,
            SpaceStationFieldLifecycle lifecycle,
            string settingKey,
            double minimum,
            double maximum)
        {
            Key = key;
            MemberName = memberName;
            Lifecycle = lifecycle;
            SettingKey = settingKey;
            Minimum = minimum;
            Maximum = maximum;
        }

        internal string Key { get; }
        internal string MemberName { get; }
        internal SpaceStationFieldLifecycle Lifecycle { get; }
        internal string SettingKey { get; }
        internal double Minimum { get; }
        internal double Maximum { get; }
    }

    /// <summary>
    /// Owns the version-validated space-station prototype fields from issue #129. Values are
    /// captured as logical doubles (including fixed-point wrappers), while the original native
    /// field type is restored by a narrow adapter setter. Each gameplay-scene registration is
    /// backed by a typed override and disposed with the host.
    /// </summary>
    internal sealed class SpaceStationTuningFeature : IDisposable
    {
        internal const string HarmonyId = "TajsCOI.Tweaks.SpaceStationTuning";

        internal const string ConstructionCostKey = "TajsTweaks.SpaceStation.ConstructionCost";
        internal const string MaintenanceKey = "TajsTweaks.SpaceStation.MaintenancePerMonthPerTier";
        internal const string CrewSuppliesKey = "TajsTweaks.SpaceStation.CrewSuppliesPerMemberPerMonth";
        internal const string ResearchPointsKey = "TajsTweaks.SpaceStation.ResearchPointsPerMonthPerTier";
        internal const string ResearchSuppliesKey = "TajsTweaks.SpaceStation.ResearchSuppliesPerMonthPerTier";
        internal const string UnityKey = "TajsTweaks.SpaceStation.UnityBonus";
        internal const string ResearchEfficiencyKey = "TajsTweaks.SpaceStation.ResearchEfficiencyBonus";
        internal const string CrewRequiredKey = "TajsTweaks.SpaceStation.CrewRequiredPerTier";
        internal const string CrewRotationDurationKey = "TajsTweaks.SpaceStation.CrewRotationDuration";
        internal const string CrewRotationRequestKey = "TajsTweaks.SpaceStation.CrewRotationRequestTime";
        internal const string DegradesAtKey = "TajsTweaks.SpaceStation.DegradesAt";
        internal const string MaintenanceReserveKey = "TajsTweaks.SpaceStation.MaintenancePartsBufferReserve";
        internal const string MaintenanceLifetimeKey = "TajsTweaks.SpaceStation.MaintenanceLevelLastsFor";
        internal const string CrewSuppliesReserveKey = "TajsTweaks.SpaceStation.CrewSuppliesBufferReserve";
        internal const string ResearchSuppliesReserveKey = "TajsTweaks.SpaceStation.ResearchSuppliesBufferReserve";
        internal const string ResearchPointsCapacityKey = "TajsTweaks.SpaceStation.ResearchPointsBufferCapacity";
        internal const string MinimumMaintenanceBufferKey = "TajsTweaks.SpaceStation.MinMaintenancePartsBufferCap";
        internal const string AdvancedPartsTierKey = "TajsTweaks.SpaceStation.AdvancedPartsTierFrom";
        internal const string ResearchTierKey = "TajsTweaks.SpaceStation.ResearchTierFrom";
        internal const string CrewRequiredFromKey = "TajsTweaks.SpaceStation.CrewRequiredFrom";
        internal const string AsteroidsSupportFromKey = "TajsTweaks.SpaceStation.AsteroidsSupportFrom";

        private const string NoSetting = "";
        // Fix32 and its PartialQuantity/Upoints wrappers store a signed 32-bit raw value.
        // Keep the logical value below the conversion ceiling so a bounded setting can never
        // overflow while being converted back to the native fixed-point representation.
        private const double FixedPointMaximum = int.MaxValue / (double)Fix32.FRACTION_RANGE;
        private const double FixedPointPercentMaximum = 1d;
        private const double DurationMaximum = int.MaxValue;

        private readonly TypedBaseValueOverrideRegistry m_values;
        private readonly HashSet<string> m_ownedKeys = new(StringComparer.Ordinal);

        internal SpaceStationTuningFeature(TypedBaseValueOverrideRegistry values)
        {
            m_values = values ?? throw new ArgumentNullException(nameof(values));
        }

        private static readonly IReadOnlyList<SpaceStationTuningDescriptor> s_descriptors =
            new SpaceStationTuningDescriptor[]
            {
                new(ConstructionCostKey + ".FirstTier", "m_constructionCostFirstTier", SpaceStationFieldLifecycle.FutureUpgradeOnly, TajsTweaksSettingsCatalog.TuningSpaceStationConstructionMultiplier, 0d, FixedPointMaximum),
                new(ConstructionCostKey + ".PerTier", "m_constructionCostPerTier", SpaceStationFieldLifecycle.FutureUpgradeOnly, TajsTweaksSettingsCatalog.TuningSpaceStationConstructionMultiplier, 0d, FixedPointMaximum),
                new(MaintenanceKey, "m_maintenancePerMonthPerTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationMaintenanceMultiplier, 0d, FixedPointMaximum),
                new(CrewSuppliesKey, "m_crewSuppliesPerMemberPerMonth", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationCrewSuppliesMultiplier, 0d, FixedPointMaximum),
                new(ResearchPointsKey, "m_researchPointsProvidedPerMonthPerTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationResearchPointsMultiplier, 0d, FixedPointMaximum),
                new(ResearchSuppliesKey, "m_researchSuppliesConsumedPerMonthPerTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationResearchSuppliesMultiplier, 0d, FixedPointMaximum),
                new(UnityKey + ".FirstTier", "m_unityBonusFirstTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationUnityMultiplier, 0d, 100d),
                new(UnityKey + ".PerTier", "m_unityBonusPerTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationUnityMultiplier, 0d, 100d),
                new(ResearchEfficiencyKey + ".FirstTier", "m_researchEfficiencyBonusFirstTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationResearchEfficiencyMultiplier, 0d, FixedPointPercentMaximum),
                new(ResearchEfficiencyKey + ".PerTier", "m_researchEfficiencyBonusPerTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationResearchEfficiencyMultiplier, 0d, FixedPointPercentMaximum),
                new(CrewRequiredKey, "m_crewRequiredPerTier", SpaceStationFieldLifecycle.ReloadRequired, TajsTweaksSettingsCatalog.TuningSpaceStationCrewCapacityMultiplier, 0d, 100d),
                new(CrewRotationDurationKey, "CREW_ROTATION_DURATION", SpaceStationFieldLifecycle.LiveFutureTick, TajsTweaksSettingsCatalog.TuningSpaceStationCrewRotationMultiplier, 1d, DurationMaximum),
                new(CrewRotationRequestKey, "CREW_ROTATION_REQUEST_TIME", SpaceStationFieldLifecycle.LiveFutureTick, NoSetting, 1d, DurationMaximum),
                new(DegradesAtKey, "DEGRADES_AT", SpaceStationFieldLifecycle.LiveFutureTick, NoSetting, 0d, FixedPointPercentMaximum),
                new(MaintenanceReserveKey, "MAINTENANCE_PARTS_BUFFER_RESERVE", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, DurationMaximum),
                new(MaintenanceLifetimeKey, "MAINTENANCE_LEVEL_LASTS_FOR", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, DurationMaximum),
                new(CrewSuppliesReserveKey, "CREW_SUPPLIES_BUFFER_RESERVE", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, DurationMaximum),
                new(ResearchSuppliesReserveKey, "RESEARCH_SUPPLIES_BUFFER_RESERVE", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, DurationMaximum),
                new(ResearchPointsCapacityKey, "RESEARCH_POINTS_BUFFER_CAPACITY", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, DurationMaximum),
                new(MinimumMaintenanceBufferKey, "MIN_MAINTENANCE_PARTS_BUFFER_CAP", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 0d, FixedPointMaximum),
                new(AdvancedPartsTierKey, "ADVANCED_PARTS_TIER_FROM", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, 100d),
                new(ResearchTierKey, "RESEARCH_TIER_FROM", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, 100d),
                new(CrewRequiredFromKey, "CREW_REQUIRED_FROM", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, 100d),
                new(AsteroidsSupportFromKey, "ASTEROIDS_SUPPORT_FROM", SpaceStationFieldLifecycle.FutureUpgradeOnly, NoSetting, 1d, 100d),
            };

        internal static IReadOnlyList<SpaceStationTuningDescriptor> Descriptors => s_descriptors;
        internal IReadOnlyDictionary<string, IBaseValueOverride<double>> Values => m_values.Values;

        internal bool IsAvailable(string key) => m_values.HasAvailablePrefix(key);

        internal bool TryGetBaseValue(string key, out double value)
        {
            if (!m_values.TryGet(key, out IBaseValueOverride<double>? registration) || registration is null)
            {
                KeyValuePair<string, IBaseValueOverride<double>>? match = m_values.Values
                    .FirstOrDefault(pair => pair.Key.StartsWith(key + ".", StringComparison.Ordinal) &&
                                            m_values.IsAvailable(pair.Key));
                if (match is null)
                {
                    value = 0d;
                    return false;
                }
                registration = match.Value.Value;
            }

            value = registration.BaseValue;
            return true;
        }

        internal void ApplyFromPrototypes(ProtosDb protosDb)
        {
            if (protosDb is null)
            {
                return;
            }

            SpaceStationProto[] protos;
            try
            {
                protos = protosDb.All<SpaceStationProto>().ToArray();
            }
            catch
            {
                protos = Array.Empty<SpaceStationProto>();
            }

            if (protos.Length == 0)
            {
                return;
            }

            Type protoType = typeof(SpaceStationProto);
            foreach (SpaceStationProto proto in protos)
            {
                string identity = PrototypeIdentity(proto);
                RegisterInstanceField(proto, protoType, "m_constructionCostFirstTier", ConstructionCostKey + ".FirstTier." + identity, 0d, FixedPointMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationConstructionMultiplier);
                RegisterInstanceField(proto, protoType, "m_constructionCostPerTier", ConstructionCostKey + ".PerTier." + identity, 0d, FixedPointMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationConstructionMultiplier);
                RegisterInstanceField(proto, protoType, "m_maintenancePerMonthPerTier", MaintenanceKey + "." + identity, 0d, FixedPointMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationMaintenanceMultiplier);
                RegisterInstanceField(proto, protoType, "m_crewSuppliesPerMemberPerMonth", CrewSuppliesKey + "." + identity, 0d, FixedPointMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationCrewSuppliesMultiplier);
                RegisterInstanceField(proto, protoType, "m_researchPointsProvidedPerMonthPerTier", ResearchPointsKey + "." + identity, 0d, FixedPointMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationResearchPointsMultiplier);
                RegisterInstanceField(proto, protoType, "m_researchSuppliesConsumedPerMonthPerTier", ResearchSuppliesKey + "." + identity, 0d, FixedPointMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationResearchSuppliesMultiplier);
                RegisterInstanceField(proto, protoType, "m_unityBonusFirstTier", UnityKey + ".FirstTier." + identity, 0d, 100d, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationUnityMultiplier);
                RegisterInstanceField(proto, protoType, "m_unityBonusPerTier", UnityKey + ".PerTier." + identity, 0d, 100d, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationUnityMultiplier);
                RegisterInstanceField(proto, protoType, "m_researchEfficiencyBonusFirstTier", ResearchEfficiencyKey + ".FirstTier." + identity, 0d, FixedPointPercentMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationResearchEfficiencyMultiplier);
                RegisterInstanceField(proto, protoType, "m_researchEfficiencyBonusPerTier", ResearchEfficiencyKey + ".PerTier." + identity, 0d, FixedPointPercentMaximum, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationResearchEfficiencyMultiplier);
                RegisterInstanceField(proto, protoType, "m_crewRequiredPerTier", CrewRequiredKey + "." + identity, 0d, 100d, BaseValueApplyMode.ReloadRequired, TajsTweaksRuntimeState.TuningSpaceStationCrewCapacityMultiplier);
            }

            Type staticType = typeof(SpaceStationProto);
            RegisterStaticField(staticType, "CREW_ROTATION_DURATION", CrewRotationDurationKey, 1d, DurationMaximum, BaseValueApplyMode.Immediate, TajsTweaksRuntimeState.TuningSpaceStationCrewRotationMultiplier);
            RegisterStaticField(staticType, "CREW_ROTATION_REQUEST_TIME", CrewRotationRequestKey, 1d, DurationMaximum, BaseValueApplyMode.Immediate, 1d);
            RegisterStaticField(staticType, "DEGRADES_AT", DegradesAtKey, 0d, FixedPointPercentMaximum, BaseValueApplyMode.Immediate, 1d);
            RegisterStaticField(staticType, "MAINTENANCE_PARTS_BUFFER_RESERVE", MaintenanceReserveKey, 1d, DurationMaximum, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "MAINTENANCE_LEVEL_LASTS_FOR", MaintenanceLifetimeKey, 1d, DurationMaximum, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "CREW_SUPPLIES_BUFFER_RESERVE", CrewSuppliesReserveKey, 1d, DurationMaximum, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "RESEARCH_SUPPLIES_BUFFER_RESERVE", ResearchSuppliesReserveKey, 1d, DurationMaximum, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "RESEARCH_POINTS_BUFFER_CAPACITY", ResearchPointsCapacityKey, 1d, DurationMaximum, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "MIN_MAINTENANCE_PARTS_BUFFER_CAP", MinimumMaintenanceBufferKey, 0d, FixedPointMaximum, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "ADVANCED_PARTS_TIER_FROM", AdvancedPartsTierKey, 1d, 100d, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "RESEARCH_TIER_FROM", ResearchTierKey, 1d, 100d, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "CREW_REQUIRED_FROM", CrewRequiredFromKey, 1d, 100d, BaseValueApplyMode.ReloadRequired, 1d);
            RegisterStaticField(staticType, "ASTEROIDS_SUPPORT_FROM", AsteroidsSupportFromKey, 1d, 100d, BaseValueApplyMode.ReloadRequired, 1d);
        }

        private static string PrototypeIdentity(SpaceStationProto proto)
        {
            if (proto is null)
            {
                throw new ArgumentNullException(nameof(proto));
            }

            // Proto.Id is the native stable identity used by ProtosDb and save/config surfaces.
            // Keep it in the registration key so multiple station prototypes cannot overwrite one
            // another in the shared typed registry.
            string identity = proto.Id.ToString();
            if (string.IsNullOrWhiteSpace(identity))
            {
                throw new InvalidOperationException("The station prototype has no stable identity.");
            }

            return identity;
        }

        internal bool ApplyImmediateSetting(string key)
        {
            if (string.Equals(key, TajsTweaksSettingsCatalog.TuningSpaceStationCrewRotationMultiplier, StringComparison.Ordinal))
            {
                return TrySetMultiplier(CrewRotationDurationKey, TajsTweaksRuntimeState.TuningSpaceStationCrewRotationMultiplier);
            }

            return false;
        }

        internal bool TrySetMultiplier(string key, double multiplier)
        {
            if (!m_values.IsAvailable(key) ||
                !m_values.TryGet(key, out IBaseValueOverride<double>? value) || value is null ||
                double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier < 0d)
            {
                return false;
            }

            return m_values.TrySetMultiplier(key, multiplier);
        }

        internal void Reset()
        {
            foreach (string key in m_ownedKeys.ToArray())
            {
                m_values.TryUnregister(key);
            }
            m_ownedKeys.Clear();
        }

        public void Dispose() => Reset();

        private void RegisterInstanceField(
            object target,
            Type type,
            string memberName,
            string key,
            double minimum,
            double maximum,
            BaseValueApplyMode applyMode,
            double multiplier)
        {
            FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                RegisterField(target, field, key, minimum, maximum, applyMode, multiplier);
            }
        }

        private void RegisterStaticField(
            Type type,
            string memberName,
            string key,
            double minimum,
            double maximum,
            BaseValueApplyMode applyMode,
            double multiplier)
        {
            FieldInfo? field = type.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                RegisterField(null, field, key, minimum, maximum, applyMode, multiplier);
            }
        }

        private void RegisterField(
            object? target,
            FieldInfo field,
            string key,
            double minimum,
            double maximum,
            BaseValueApplyMode applyMode,
            double multiplier)
        {
            bool existed = m_values.Keys.Any(existingKey =>
                string.Equals(existingKey, key, StringComparison.Ordinal));
            Func<object?> getter = () => field.GetValue(target);
            Action<object?> setter = nativeValue =>
            {
                double expected = ReadScalar(nativeValue);
                field.SetValue(target, nativeValue);

                // Readonly prototype/static members are a compatibility seam. Do not claim an
                // adapter is available when the runtime silently ignores a reflected write.
                double actual = ReadScalar(field.GetValue(target));
                double tolerance = field.FieldType == typeof(Fix32) ||
                                   field.FieldType == typeof(PartialQuantity) ||
                                   field.FieldType == typeof(Upoints)
                    ? 1d / Fix32.FRACTION_RANGE
                    : field.FieldType == typeof(Percent) ? 1d / 100000d : 0d;
                if (Math.Abs(actual - expected) > tolerance)
                {
                    throw new InvalidOperationException("The station member did not accept the reflected value.");
                }
            };

            if (!m_values.TryRegister(key, field.FieldType, getter, setter, minimum, maximum, applyMode) ||
                !m_values.TrySetMultiplier(key, multiplier))
            {
                m_values.MarkUnavailable(key);
                if (!existed)
                {
                    m_values.TryUnregister(key);
                }
            }
            else
            {
                m_ownedKeys.Add(key);
                m_values.MarkAvailable(key);
            }
        }

        private static double ReadScalar(object? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("Station value is null.");
            }

            if (value is Quantity quantity)
            {
                return quantity.Value;
            }
            if (value is Duration duration)
            {
                return duration.Ticks;
            }
            if (value is PartialQuantity partial)
            {
                return partial.Value.RawValue / (double)Fix32.FRACTION_RANGE;
            }
            if (value is Fix32 fix32)
            {
                return fix32.RawValue / (double)Fix32.FRACTION_RANGE;
            }
            if (value is Upoints upoints)
            {
                return upoints.Value.RawValue / (double)Fix32.FRACTION_RANGE;
            }
            if (value is Percent percent)
            {
                return percent.RawValue / 100000d;
            }

            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

    }
}
