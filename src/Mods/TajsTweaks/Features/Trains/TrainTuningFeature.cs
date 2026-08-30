// Taj's COI Mods | TrainTuningFeature.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
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
        private const int NumberNamespaceSize = 99999;
        private static readonly object s_numberGate = new();
        private static readonly HashSet<int> s_usedNumbers = new();
        private static WeakReference<IPropertiesDb>? s_propertiesDb;
        private static FieldInfo? s_locoNumberField;
        private static bool s_installed;

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
            s_locoNumberField = AccessTools.Field(typeof(Locomotive), "<LocoNumber>k__BackingField")
                                ?? AccessTools.Field(typeof(Locomotive), "LocoNumber");
            if (s_locoNumberField is null)
            {
                throw new MissingFieldException(typeof(Locomotive).FullName, "LocoNumber");
            }
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
        }

        internal static void Reset()
        {
            if (s_propertiesDb is not null && s_propertiesDb.TryGetTarget(out IPropertiesDb? propertiesDb))
            {
                RemoveModifiers(propertiesDb);
            }
            s_propertiesDb = null;
            s_locoNumberField = null;
            s_installed = false;
            lock (s_numberGate)
            {
                s_usedNumbers.Clear();
            }
        }

        private static void Apply(IPropertiesDb propertiesDb)
        {
            IProperty<Percent> slope = propertiesDb.GetProperty(IdsCore.PropertyIds.TrainSlopeDifficultyMultiplier);
            IProperty<Percent> fuel = propertiesDb.GetProperty(IdsCore.PropertyIds.TrainsFuelConsumptionMultiplier);
            IProperty<Percent> pollution = propertiesDb.GetProperty(IdsCore.PropertyIds.TrainsPollutionMultiplier);
            RemoveModifiers(propertiesDb);
            switch ((TajsTweaksRuntimeState.TrainTuningProfile ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "efficient":
                    fuel.AddOrSetModifier(ModifierOwner, 75.Percent(), Option<string>.None);
                    pollution.AddOrSetModifier(ModifierOwner, 75.Percent(), Option<string>.None);
                    break;
                case "power":
                    slope.AddOrSetModifier(ModifierOwner, 75.Percent(), Option<string>.None);
                    break;
            }
        }

        private static void RemoveModifiers(IPropertiesDb propertiesDb)
        {
            propertiesDb.GetProperty(IdsCore.PropertyIds.TrainSlopeDifficultyMultiplier).TryRemoveModifier(ModifierOwner);
            propertiesDb.GetProperty(IdsCore.PropertyIds.TrainsFuelConsumptionMultiplier).TryRemoveModifier(ModifierOwner);
            propertiesDb.GetProperty(IdsCore.PropertyIds.TrainsPollutionMultiplier).TryRemoveModifier(ModifierOwner);
        }

        private static void LocomotiveCreatedPostfix(Locomotive __instance)
        {
            if (s_locoNumberField is null || string.Equals(TajsTweaksRuntimeState.LocomotiveNumbering, "vanilla", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try
            {
                if (Convert.ToInt32(s_locoNumberField.GetValue(__instance)) != 0)
                {
                    return;
                }
                int id = __instance.Id.Value;
                lock (s_numberGate)
                {
                    IReadOnlyList<int> assigned = LocomotiveNumbering.Assign(
                        new[] { id },
                        s_usedNumbers,
                        NumberNamespaceSize,
                        string.Equals(TajsTweaksRuntimeState.LocomotiveNumbering, "random", StringComparison.OrdinalIgnoreCase)
                            ? LocomotiveNumberAssignment.Random
                            : LocomotiveNumberAssignment.Sequential,
                        id);
                    if (assigned.Count == 0)
                    {
                        return;
                    }
                    s_usedNumbers.Add(assigned[0]);
                    s_locoNumberField.SetValue(__instance, assigned[0]);
                }
            }
            catch
            {
                // Numbering is presentation-only; native locomotive creation must remain intact.
            }
        }
    }
}
