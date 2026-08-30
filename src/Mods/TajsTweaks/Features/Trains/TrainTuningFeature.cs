// Taj's COI Mods | TrainTuningFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Maintenance;
using Mafi.Core.PropertiesDb;
using Mafi.Core.Trains;

namespace TajsCOI.Tweaks.Features.Trains
{
    /// <summary>
    ///     Applies one named modifier per native train property and assigns an optional number to
    ///     newly constructed locomotives. Existing save values remain authoritative.
    /// </summary>
    internal static class TrainTuningFeature
    {
        private const string ModifierOwner = "TajsCOI.Tweaks.TrainTuning";
        private static readonly object s_numberGate = new();
        private static readonly HashSet<int> s_usedNumbers = new();
        private static WeakReference<IPropertiesDb>? s_propertiesDb;
        private static WeakReference<IEntitiesManager>? s_entities;
        private static bool s_installed;

        internal static bool SlopeAvailable { get; private set; }
        internal static bool FuelAvailable { get; private set; }
        internal static bool PollutionAvailable { get; private set; }

        internal static void Install(Harmony harmony)
        {
            if (s_installed)
            {
                return;
            }
            ConstructorInfo constructor = AccessTools.Constructor(
                                              typeof(Locomotive),
                                              new[]
                                              {
                                                  typeof(EntityId),
                                                  typeof(LocomotiveProto),
                                                  typeof(EntityContext),
                                                  typeof(IEntityMaintenanceProvidersFactory),
                                              })
                                          ?? throw new MissingMethodException(typeof(Locomotive).FullName, ".ctor");
            harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(TrainTuningFeature), nameof(LocomotiveCreatedPostfix)));
            s_installed = true;
        }

        internal static void Initialize(IPropertiesDb propertiesDb)
        {
            s_propertiesDb = new WeakReference<IPropertiesDb>(propertiesDb ?? throw new ArgumentNullException(nameof(propertiesDb)));
            Apply(propertiesDb);
        }

        internal static void ApplyFromSettings(DependencyResolver resolver)
        {
            if (resolver.TryResolve(out IPropertiesDb propertiesDb))
            {
                Initialize(propertiesDb);
            }

            if (resolver.TryResolve(out IEntitiesManager entities))
            {
                s_entities = new WeakReference<IEntitiesManager>(entities);
            }
        }

        internal static void Reset()
        {
            if (s_propertiesDb is not null && s_propertiesDb.TryGetTarget(out IPropertiesDb? propertiesDb))
            {
                RemoveModifiers(propertiesDb);
            }
            s_propertiesDb = null;
            s_entities = null;
            s_installed = false;
            SlopeAvailable = false;
            FuelAvailable = false;
            PollutionAvailable = false;
            lock (s_numberGate)
            {
                s_usedNumbers.Clear();
            }
        }

        private static void Apply(IPropertiesDb propertiesDb)
        {
            SlopeAvailable = TryApplyProperty(
                propertiesDb,
                IdsCore.PropertyIds.TrainSlopeDifficultyMultiplier,
                TrainModifierPolicy.ResolveMultiplier(
                    TajsTweaksRuntimeState.TrainSlopeMultiplier,
                    TrainModifierProperty.Climbing,
                    TajsTweaksRuntimeState.TrainTuningProfile));
            FuelAvailable = TryApplyProperty(
                propertiesDb,
                IdsCore.PropertyIds.TrainsFuelConsumptionMultiplier,
                TrainModifierPolicy.ResolveMultiplier(
                    TajsTweaksRuntimeState.TrainFuelMultiplier,
                    TrainModifierProperty.Fuel,
                    TajsTweaksRuntimeState.TrainTuningProfile));
            PollutionAvailable = TryApplyProperty(
                propertiesDb,
                IdsCore.PropertyIds.TrainsPollutionMultiplier,
                TrainModifierPolicy.ResolveMultiplier(
                    TajsTweaksRuntimeState.TrainPollutionMultiplier,
                    TrainModifierProperty.Pollution,
                    TajsTweaksRuntimeState.TrainTuningProfile));
        }

        private static bool TryApplyProperty(
            IPropertiesDb propertiesDb,
            PropertyId<Percent> propertyId,
            double multiplier)
        {
            try
            {
                IProperty<Percent> property = propertiesDb.GetProperty(propertyId);
                property.TryRemoveModifier(ModifierOwner);
                ApplyMultiplier(property, multiplier);
                return true;
            }
            catch
            {
                // An individual property can disappear in a future game version without
                // disabling the other train controls or changing native behavior.
                return false;
            }
        }

        private static void ApplyMultiplier(IProperty<Percent> property, double multiplier)
        {
            double modifier = TrainModifierPolicy.ToModifierPercent(multiplier);
            if (Math.Abs(modifier) < 0.0001d)
            {
                return;
            }

            property.AddOrSetModifier(ModifierOwner, modifier.Percent(), Option<string>.None);
        }

        private static void RemoveModifiers(IPropertiesDb propertiesDb)
        {
            TryRemoveModifier(propertiesDb, IdsCore.PropertyIds.TrainSlopeDifficultyMultiplier);
            TryRemoveModifier(propertiesDb, IdsCore.PropertyIds.TrainsFuelConsumptionMultiplier);
            TryRemoveModifier(propertiesDb, IdsCore.PropertyIds.TrainsPollutionMultiplier);
        }

        private static void TryRemoveModifier(IPropertiesDb propertiesDb, PropertyId<Percent> propertyId)
        {
            try
            {
                propertiesDb.GetProperty(propertyId).TryRemoveModifier(ModifierOwner);
            }
            catch
            {
                // Optional property; leave the native property untouched when absent.
            }
        }

        private static void LocomotiveCreatedPostfix(Locomotive __instance)
        {
            if (string.Equals(TajsTweaksRuntimeState.LocomotiveNumbering, "vanilla", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                if (__instance.LocoNumber != 0 ||
                    !LocomotiveNumbering.TryGetSupportedRange(__instance.Prototype.LocoTypeDigit, out int minimum, out int maximum))
                {
                    return;
                }
                int id = __instance.Id.Value;
                lock (s_numberGate)
                {
                    HashSet<int> used = CaptureUsedNumbers(__instance.Prototype.LocoTypeDigit, id);
                    used.UnionWith(s_usedNumbers);
                    IReadOnlyList<int> assigned = LocomotiveNumbering.AssignInRange(
                        new[] { id },
                        used,
                        minimum,
                        maximum,
                        string.Equals(TajsTweaksRuntimeState.LocomotiveNumbering, "random", StringComparison.OrdinalIgnoreCase)
                            ? LocomotiveNumberAssignment.Random
                            : LocomotiveNumberAssignment.Sequential,
                        id);
                    if (assigned.Count == 0)
                    {
                        return;
                    }
                    int number = assigned[0];
                    s_usedNumbers.Add(number);
                    __instance.SetLocoNumber(number);
                }
            }
            catch
            {
                // Numbering is presentation-only; native locomotive creation must remain intact.
            }
        }

        internal static bool TrySetNumber(Locomotive locomotive, int number, out string error)
        {
            error = string.Empty;
            if (locomotive is null || locomotive.IsDestroyed)
            {
                error = "Locomotive was not found or has been destroyed.";
                return false;
            }

            if (!LocomotiveNumbering.IsValidForType(number, locomotive.Prototype.LocoTypeDigit))
            {
                error = "Number " + number.ToString(CultureInfo.InvariantCulture) +
                        " is outside the supported range for locomotive type " +
                        locomotive.Prototype.LocoTypeDigit.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            locomotive.SetLocoNumber(number);
            lock (s_numberGate)
            {
                s_usedNumbers.Add(number);
            }
            return true;
        }

        internal static bool TryAssignUnique(
            IEnumerable<Locomotive> source,
            LocomotiveNumberAssignment mode,
            int randomSeed,
            out int assignedCount,
            out string message)
        {
            assignedCount = 0;
            message = string.Empty;
            if (!Enum.IsDefined(typeof(LocomotiveNumberAssignment), mode))
            {
                message = "Unknown locomotive numbering mode.";
                return false;
            }

            List<Locomotive> locomotives = (source ?? Enumerable.Empty<Locomotive>())
                .Where(locomotive => locomotive is not null && !locomotive.IsDestroyed)
                .OrderBy(locomotive => locomotive.Id.Value)
                .ToList();
            if (locomotives.Count == 0)
            {
                message = "No live locomotives were found.";
                return false;
            }

            var groups = locomotives
                .GroupBy(locomotive => locomotive.Prototype.LocoTypeDigit)
                .OrderBy(group => group.Key)
                .ToArray();
            var unsupported = groups.Where(group => !LocomotiveNumbering.TryGetSupportedRange(group.Key, out _, out _)).ToArray();
            var supported = groups.Where(group => LocomotiveNumbering.TryGetSupportedRange(group.Key, out _, out _)).ToArray();
            foreach (var group in supported)
            {
                LocomotiveNumbering.TryGetSupportedRange(group.Key, out int minimum, out int maximum);
                if (group.Count() > maximum - minimum + 1)
                {
                    message = "Cannot assign unique numbers: locomotive type " +
                              group.Key.ToString(CultureInfo.InvariantCulture) + " has " +
                              group.Count().ToString(CultureInfo.InvariantCulture) +
                              " locomotives but only " +
                              (maximum - minimum + 1).ToString(CultureInfo.InvariantCulture) + " numbers.";
                    return false;
                }
            }

            lock (s_numberGate)
            {
                foreach (var group in supported)
                {
                    List<Locomotive> groupLocomotives = group.ToList();
                    LocomotiveNumbering.TryGetSupportedRange(group.Key, out int minimum, out int maximum);
                    // The command intentionally renumbers the complete current fleet. There is
                    // no second same-type group outside groupLocomotives whose numbers need to be
                    // reserved; native type ranges are partitioned by LocoTypeDigit.
                    HashSet<int> used = new();
                    IReadOnlyList<int> relative = LocomotiveNumbering.AssignInRange(
                        groupLocomotives.Select(locomotive => locomotive.Id.Value),
                        used,
                        minimum,
                        maximum,
                        mode,
                        randomSeed ^ group.Key);
                    if (relative.Count != groupLocomotives.Count)
                    {
                        message = "Cannot assign unique numbers because the supported range is already occupied.";
                        return false;
                    }

                    for (int index = 0; index < groupLocomotives.Count; index++)
                    {
                        int number = relative[index];
                        groupLocomotives[index].SetLocoNumber(number);
                        s_usedNumbers.Add(number);
                        assignedCount++;
                    }
                }
            }

            message = assignedCount.ToString(CultureInfo.InvariantCulture) + " locomotive number(s) assigned.";
            if (unsupported.Length != 0)
            {
                message += " " + unsupported.Sum(group => group.Count()).ToString(CultureInfo.InvariantCulture) +
                           " locomotive(s) have no supported native number range and were skipped.";
            }
            return assignedCount != 0;
        }

        internal static string FormatList(IEnumerable<Locomotive> source, string? search)
        {
            string query = search?.Trim() ?? string.Empty;
            List<Locomotive> allLocomotives = (source ?? Enumerable.Empty<Locomotive>())
                .Where(locomotive => locomotive is not null && !locomotive.IsDestroyed)
                .ToList();
            Dictionary<int, int> counts = allLocomotives
                .Where(locomotive => locomotive.LocoNumber != 0)
                .GroupBy(locomotive => locomotive.LocoNumber)
                .ToDictionary(group => group.Key, group => group.Count());
            List<Locomotive> locomotives = allLocomotives
                .Where(locomotive => query.Length == 0 || Matches(locomotive, query))
                .OrderBy(locomotive => locomotive.LocoNumber)
                .ThenBy(locomotive => locomotive.Id.Value)
                .ToList();
            if (locomotives.Count == 0)
            {
                return "No locomotives match the search.";
            }

            return string.Join(
                Environment.NewLine,
                locomotives.Select(locomotive =>
                {
                    string train = locomotive.Train.ValueOrNull?.Name ?? "(unassigned)";
                    string duplicate = counts.TryGetValue(locomotive.LocoNumber, out int count) && count > 1 ? " DUPLICATE" : string.Empty;
                    return "id=" + locomotive.Id.Value.ToString(CultureInfo.InvariantCulture) +
                           " number=" + locomotive.LocoNumber.ToString(CultureInfo.InvariantCulture) +
                           " prototype=" + locomotive.Prototype.Id.Value +
                           " train=" + train + duplicate;
                }));
        }

        private static bool Matches(Locomotive locomotive, string query)
        {
            return locomotive.Id.Value.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   locomotive.LocoNumber.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   locomotive.Prototype.Id.Value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (locomotive.Train.ValueOrNull?.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HashSet<int> CaptureUsedNumbers(int typeDigit, int currentId)
        {
            var used = new HashSet<int>();
            if (s_entities is null || !s_entities.TryGetTarget(out IEntitiesManager? entities))
            {
                return used;
            }

            try
            {
                foreach (Locomotive locomotive in entities.GetAllEntitiesOfType<Locomotive>())
                {
                    if (locomotive.Id.Value == currentId || locomotive.IsDestroyed ||
                        locomotive.Prototype.LocoTypeDigit != typeDigit || locomotive.LocoNumber == 0)
                    {
                        continue;
                    }
                    used.Add(locomotive.LocoNumber);
                }
            }
            catch
            {
                // Numbering is presentation-only and remains fail-open when the scene index is unavailable.
            }

            return used;
        }
    }
}
