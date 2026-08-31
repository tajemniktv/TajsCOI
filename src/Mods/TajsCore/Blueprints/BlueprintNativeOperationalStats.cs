// Taj's COI Mods | BlueprintNativeOperationalStats.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Blueprints;
using Mafi.Core.Prototypes;

namespace TajsCOI.Core.Blueprints
{
    /// <summary>
    /// Read-only, build-time information derived from the native blueprint item snapshot.
    /// This is not a simulation estimate: it only sums values exposed by native prototypes.
    /// </summary>
    public sealed class BlueprintNativeOperationalStats
    {
        internal BlueprintNativeOperationalStats(
            decimal workers,
            decimal electricity,
            decimal computing,
            decimal maintenance,
            IEnumerable<string> unavailableElectricity,
            IEnumerable<string> unavailableComputing,
            IEnumerable<string> unavailablePrototypes)
        {
            Workers = Math.Max(0, workers);
            Electricity = Math.Max(0, electricity);
            Computing = Math.Max(0, computing);
            Maintenance = Math.Max(0, maintenance);
            UnavailableElectricity = Freeze(unavailableElectricity);
            UnavailableComputing = Freeze(unavailableComputing);
            UnavailablePrototypes = Freeze(unavailablePrototypes);
        }

        public decimal Workers { get; }
        public decimal Electricity { get; }
        public decimal Computing { get; }
        public decimal Maintenance { get; }
        public IReadOnlyList<string> UnavailableElectricity { get; }
        public IReadOnlyList<string> UnavailableComputing { get; }
        public IReadOnlyList<string> UnavailablePrototypes { get; }

        private static IReadOnlyList<string> Freeze(IEnumerable<string>? values) =>
            new ReadOnlyCollection<string>((values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    public static class BlueprintNativeOperationalStatsCalculator
    {
        public static BlueprintNativeOperationalStats Calculate(IBlueprint blueprint)
        {
            if (blueprint is null) throw new ArgumentNullException(nameof(blueprint));

            decimal workers = 0;
            decimal electricity = 0;
            decimal computing = 0;
            decimal maintenance = 0;
            var unavailableElectricity = new List<string>();
            var unavailableComputing = new List<string>();
            var unavailablePrototypes = new List<string>();

            foreach (EntityConfigData item in blueprint.Items)
            {
                IEntityProto? proto = item.Prototype.ValueOrNull as IEntityProto;
                if (proto is null)
                {
                    unavailablePrototypes.Add("missing prototype");
                    continue;
                }

                string id = proto.Id.Value;
                workers += Math.Max(0, proto.Costs.Workers);
                maintenance += (decimal)Math.Max(0d, proto.Costs.Maintenance.MaintenancePerMonth.Value.ToDouble());

                if (proto is IProtoWithPowerConsumption power)
                {
                    electricity += Math.Max(0, power.ElectricityConsumed.Value);
                }
                else
                {
                    unavailableElectricity.Add(id);
                }

                if (proto is IProtoWithComputingConsumption computingProvider)
                {
                    computing += Math.Max(0, computingProvider.ComputingConsumed.Value);
                }
                else
                {
                    unavailableComputing.Add(id);
                }
            }

            return new BlueprintNativeOperationalStats(
                workers,
                electricity,
                computing,
                maintenance,
                unavailableElectricity,
                unavailableComputing,
                unavailablePrototypes);
        }
    }
}
