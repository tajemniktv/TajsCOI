// Taj's COI Mods | TajsDifficultyOptionCatalog.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core.Game;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    internal enum TajsDifficultyApplyMode
    {
        Immediate,
        FutureCalculations,
        ReloadSave,
        NewGameOnly,
        Unsupported,
    }

    internal sealed class TajsDifficultyRange
    {
        internal TajsDifficultyRange(int minimum, int maximum, int step, params int[] additions)
        {
            if (step <= 0 || minimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            Minimum = minimum;
            Maximum = maximum;
            Step = step;
            Additions = additions ?? Array.Empty<int>();
        }

        internal int Minimum { get; }
        internal int Maximum { get; }
        internal int Step { get; }
        internal IReadOnlyList<int> Additions { get; }

        internal IEnumerable<int> Values()
        {
            for (int value = Minimum; value <= Maximum; value += Step)
            {
                yield return value;
                if (value > int.MaxValue - Step)
                {
                    break;
                }
            }

            foreach (int value in Additions)
            {
                yield return value;
            }
        }
    }

    internal sealed class TajsDifficultyDefinition
    {
        internal TajsDifficultyDefinition(
            string memberName,
            string displayName,
            string category,
            string description,
            TajsDifficultyApplyMode applyMode,
            TajsDifficultyRange? range = null)
        {
            MemberName = memberName;
            DisplayName = displayName;
            Category = category;
            Description = description;
            ApplyMode = applyMode;
            Range = range;
        }

        internal string MemberName { get; }
        internal string DisplayName { get; }
        internal string Category { get; }
        internal string Description { get; }
        internal TajsDifficultyApplyMode ApplyMode { get; }
        internal TajsDifficultyRange? Range { get; }
    }

    /// <summary>
    ///     Owns the bounded values exposed by the advanced difficulty path. The range is
    ///     intentionally conservative: a fixed-point value being representable is not enough to
    ///     make it a useful or safe gameplay choice.
    /// </summary>
    internal static class TajsDifficultyOptionCatalog
    {
        private static readonly List<string> s_unsupportedPercentMembers = new();

        private static readonly IReadOnlyDictionary<string, TajsDifficultyRange> s_percentRanges =
            new Dictionary<string, TajsDifficultyRange>(StringComparer.Ordinal)
            {
                ["ExtraContractsProfit"] = new(-90, 500, 10),
                ["TreesGrowthDiff"] = new(-90, 500, 10, -25, 25),
                ["ExtraStartingMaterial"] = new(-100, 500, 10),
                ["MaintenanceDiff"] = new(-100, 200, 10, -99, -75, -50, -25, 25, 50),
                ["FuelConsumptionDiff"] = new(-100, 200, 10, -99, -15, 15),
                ["RainYieldDiff"] = new(-90, 300, 10),
                ["BaseHealthDiff"] = new(-75, 500, 25),
                ["ResourceMiningDiff"] = new(-75, 1000, 25, -50, -25, -15, -10, 10, 15, 30),
                ["SettlementConsumptionDiff"] = new(-100, 500, 10, -99),
                ["SettlementFoodConsumptionDiff"] = new(-100, 500, 10, -99),
                ["WorldMinesReservesDiff"] = new(-100, 2000, 10, -99),
                ["FarmsYieldDiff"] = new(-90, 500, 10, -25, 25),
                ["UnityProductionDiff"] = new(-100, 500, 10, -99),
                // -25% is a native preset; keep it explicitly because the -80 origin does not
                // land on that value with a ten-point step.
                ["SolarPowerDiff"] = new(-80, 500, 10, -25, 25),
                ["ConstructionCostsDiff"] = new(-100, 500, 5),
                ["ResearchCostDiff"] = new(-100, 500, 5),
                ["DiseaseMortalityDiff"] = new(-100, 500, 5),
                ["PollutionDiff"] = new(-100, 500, 5, -99),
                ["QuickActionsCostDiff"] = new(-100, 500, 5, -99),
            };

        private static readonly IReadOnlyList<TajsDifficultyDefinition> s_definitions =
            new TajsDifficultyDefinition[]
            {
                MakePercent(
                    "ExtraContractsProfit",
                    "Contract profit",
                    "Economy",
                    "Extra free goods received through contracts.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "TreesGrowthDiff",
                    "Tree growth",
                    "Nature",
                    "Changes the growth speed of trees planted in the world.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "ExtraStartingMaterial",
                    "Starting materials",
                    "Resources",
                    "Changes materials granted when a new game is created.",
                    TajsDifficultyApplyMode.NewGameOnly),
                MakePercent(
                    "MaintenanceDiff",
                    "Maintenance consumption",
                    "Costs",
                    "Changes maintenance consumption for consumers.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "FuelConsumptionDiff",
                    "Vehicle and ship fuel",
                    "Vehicles",
                    "Changes fuel consumption for vehicles and cargo ships.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "RainYieldDiff",
                    "Rain contribution",
                    "Nature",
                    "Changes water produced by rain and its crop contribution.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "BaseHealthDiff",
                    "Base population health",
                    "Population",
                    "Changes the baseline health of the population.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "ResourceMiningDiff",
                    "Resource mining yield",
                    "Resources",
                    "Changes mined yield from resource deposits.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "SettlementConsumptionDiff",
                    "Settlement consumption",
                    "Population",
                    "Changes settlement goods and services consumption.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "SettlementFoodConsumptionDiff",
                    "Settlement food consumption",
                    "Population",
                    "Changes settlement food consumption.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "WorldMinesReservesDiff",
                    "World-mine reserves",
                    "Resources",
                    "Changes reserves consumed by world mines, rigs, and related sites.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "FarmsYieldDiff",
                    "Farm yield",
                    "Farming",
                    "Changes yield from farms and greenhouses.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "UnityProductionDiff",
                    "Unity production",
                    "Unity",
                    "Changes Unity produced by settlements.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "SolarPowerDiff",
                    "Solar-power output",
                    "Power",
                    "Changes electricity generated by solar panels.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "ConstructionCostsDiff",
                    "Construction costs",
                    "Costs",
                    "Changes construction costs for buildings, machines, and vehicles.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "ResearchCostDiff",
                    "Research costs",
                    "Progression",
                    "Changes time and resources required by research.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "DiseaseMortalityDiff",
                    "Disease mortality",
                    "Population",
                    "Changes mortality caused by disease.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "PollutionDiff",
                    "Pollution severity",
                    "Environment",
                    "Changes air, water, and landfill pollution intensity.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakePercent(
                    "QuickActionsCostDiff",
                    "Quick-action Unity cost",
                    "Unity",
                    "Changes Unity costs for quick actions such as delivery.",
                    TajsDifficultyApplyMode.FutureCalculations),
                MakeEnum("WeatherDifficulty", "Weather", "Nature", "Selects the weather profile used by the world.", TajsDifficultyApplyMode.NewGameOnly),
                MakeEnum("QuickRepair", "Quick repair", "Mechanics", "Controls whether quick repair is available.", TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "PowerSetting",
                    "Logistics power",
                    "Power",
                    "Controls whether belts and storages consume power.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "DeconstructionRefund",
                    "Deconstruction refund",
                    "Costs",
                    "Controls the refund returned by deconstruction.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "LoansDifficulty",
                    "Loan conditions",
                    "Economy",
                    "Controls loan conditions selected at game creation.",
                    TajsDifficultyApplyMode.ReloadSave),
                MakeEnum(
                    "ShipsNoFuel",
                    "Ships out of fuel",
                    "Vehicles",
                    "Controls whether ships run on Unity or stop without fuel.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "GroundwaterPumpLow",
                    "Pumps out of water",
                    "Resources",
                    "Controls pump behavior when groundwater is low.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "Starvation",
                    "Starvation effects",
                    "Population",
                    "Controls whether starvation reduces workforce or causes death.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "WorldMinesNoUnity",
                    "World mines out of Unity",
                    "Resources",
                    "Controls world-mine behavior without Unity.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "VehiclesNoFuel",
                    "Vehicles out of fuel",
                    "Vehicles",
                    "Controls whether vehicles slow down or stop without fuel.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "TrainsNoFuel",
                    "Trains out of fuel",
                    "Vehicles",
                    "Controls whether trains slow down or stop without fuel.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "ConsumerBroken",
                    "Consumers out of maintenance",
                    "Costs",
                    "Controls whether broken consumers slow down or stop.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "PowerLow",
                    "Consumers out of power",
                    "Power",
                    "Controls whether low-power consumers slow down or stop.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum(
                    "ComputingLow",
                    "Consumers out of computing",
                    "Power",
                    "Controls whether low-computing consumers slow down or stop.",
                    TajsDifficultyApplyMode.Immediate),
                MakeEnum("OreSorting", "Mixed-ore sorting", "Mechanics", "Controls the mixed-ore sorting mechanic.", TajsDifficultyApplyMode.NewGameOnly),
                MakeEnum(
                    "Sandbox",
                    "Sandbox",
                    "Mechanics",
                    "Controls sandbox mode. This is intentionally new-game only.",
                    TajsDifficultyApplyMode.NewGameOnly),
            };

        internal static IReadOnlyList<TajsDifficultyDefinition> Definitions => s_definitions;

        internal static IReadOnlyList<string> UnsupportedPercentMembers => s_unsupportedPercentMembers;

        internal static void ApplyExtendedOptions()
        {
            s_unsupportedPercentMembers.Clear();
            Type configType = typeof(GameDifficultyConfig);
            Type percentInfoType = typeof(DiffSettingInfo<Percent>);
            FieldInfo[] fields = configType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields.Where(field => percentInfoType.IsAssignableFrom(field.FieldType)))
            {
                try
                {
                    if (field.GetValue(null) is not DiffSettingInfo<Percent> info)
                    {
                        continue;
                    }

                    if (!TryFindRange(info.ValueMemberName, out TajsDifficultyRange? range))
                    {
                        if (!s_unsupportedPercentMembers.Contains(info.ValueMemberName, StringComparer.Ordinal))
                        {
                            s_unsupportedPercentMembers.Add(info.ValueMemberName);
                        }

                        Log.Warning(
                            "TajsDifficulty discovered unsupported percent setting " +
                            info.ValueMemberName + "; native options remain unchanged.");
                        continue;
                    }

                    // Preserve every vanilla option and add only the explicitly audited Tajs
                    // values. Native AdvancedSettingsTab consumes this same array in both the
                    // new-game and in-game difficulty surfaces.
                    Percent[] options = BuildExtendedOptions(
                        info.Options,
                        range!,
                        string.Equals(info.ValueMemberName, "WorldMinesReservesDiff", StringComparison.Ordinal));

                    FieldInfo? optionsField = percentInfoType.GetField("Options", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    optionsField?.SetValue(info, options);
                }
                catch (Exception exception)
                {
                    // Each static field is an independent compatibility seam. A newer game can
                    // remove or reshape one setting without disabling all other ranges.
                    Log.Warning("TajsDifficulty could not extend " + field.Name + ": " + exception.GetType().Name);
                }
            }
        }

        internal static TajsDifficultyDefinition? Find(string memberName) =>
            s_definitions.FirstOrDefault(definition => string.Equals(definition.MemberName, memberName, StringComparison.Ordinal));

        internal static TajsDifficultyRange? FindRange(string memberName) =>
            s_percentRanges.TryGetValue(memberName, out TajsDifficultyRange? range) ? range : null;

        internal static bool TryFindRange(string memberName, out TajsDifficultyRange? range) =>
            s_percentRanges.TryGetValue(memberName, out range);

        internal static Percent[] BuildExtendedOptions(
            IEnumerable<Percent>? vanillaOptions,
            TajsDifficultyRange range,
            bool includeUnlimited)
        {
            IEnumerable<Percent> values = (vanillaOptions ?? Array.Empty<Percent>())
                .Concat(range.Values().Select(value => value.Percent()));
            if (includeUnlimited)
            {
                values = values.Append(Percent.MaxValue);
            }

            return values.Distinct().OrderBy(value => value.RawValue).ToArray();
        }

        internal static TajsDifficultyDefinition CreateDiscovered(string memberName, Type propertyType)
        {
            bool isPercent = propertyType == typeof(Percent);
            return new TajsDifficultyDefinition(
                memberName,
                SplitWords(memberName),
                "Additional",
                "Discovered from the current game's difficulty configuration. Runtime editing is disabled until its semantics are audited.",
                TajsDifficultyApplyMode.Unsupported,
                isPercent ? FindRange(memberName) : null);
        }

        private static TajsDifficultyDefinition MakePercent(
            string memberName,
            string displayName,
            string category,
            string description,
            TajsDifficultyApplyMode applyMode) =>
            new(memberName, displayName, category, description, applyMode, FindRange(memberName));

        private static TajsDifficultyDefinition MakeEnum(
            string memberName,
            string displayName,
            string category,
            string description,
            TajsDifficultyApplyMode applyMode) =>
            new(memberName, displayName, category, description, applyMode);

        private static string SplitWords(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Difficulty setting";
            }

            var chars = new List<char>(value.Length + 8);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (index > 0 && char.IsUpper(current) && !char.IsUpper(value[index - 1]))
                {
                    chars.Add(' ');
                }
                chars.Add(current);
            }
            return new string(chars.ToArray());
        }
    }
}
