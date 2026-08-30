// Taj's COI Mods | ProductionContracts.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace TajsCOI.Core.Production
{
    /// <summary>Fixed point rate used by planning and blueprint summaries.</summary>
    public readonly struct FixedRate : IEquatable<FixedRate>, IComparable<FixedRate>
    {
        public const long Scale = 1_000_000;

        public FixedRate(long raw) => Raw = raw;

        public long Raw { get; }
        public double Value => (double)Raw / Scale;

        public static FixedRate FromDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return new FixedRate(checked((long)Math.Round(value * Scale, MidpointRounding.AwayFromZero)));
        }

        public static FixedRate FromDecimal(decimal value) =>
            new FixedRate(checked((long)Math.Round(value * Scale, 0, MidpointRounding.AwayFromZero)));

        public static FixedRate operator +(FixedRate left, FixedRate right) => new(checked(left.Raw + right.Raw));
        public static FixedRate operator -(FixedRate left, FixedRate right) => new(checked(left.Raw - right.Raw));
        public static FixedRate operator *(FixedRate value, long multiplier) => new(checked(value.Raw * multiplier));
        public static bool operator ==(FixedRate left, FixedRate right) => left.Raw == right.Raw;
        public static bool operator !=(FixedRate left, FixedRate right) => left.Raw != right.Raw;
        public static bool operator >(FixedRate left, FixedRate right) => left.Raw > right.Raw;
        public static bool operator <(FixedRate left, FixedRate right) => left.Raw < right.Raw;
        public int CompareTo(FixedRate other) => Raw.CompareTo(other.Raw);
        public bool Equals(FixedRate other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is FixedRate other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public override string ToString() => Value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    public enum ProductClassification
    {
        Logistics,
        Pollution,
        Radioactive,
        Virtual,
        Obsolete,
        Unknown,
    }

    public sealed class ProductDefinition
    {
        public ProductDefinition(string productId, ProductClassification classification = ProductClassification.Logistics,
            string? emissionCategory = null)
        {
            ProductId = Require(productId, nameof(productId));
            Classification = classification;
            EmissionCategory = emissionCategory is null || string.IsNullOrWhiteSpace(emissionCategory) ? null : emissionCategory.Trim();
        }

        public string ProductId { get; }
        public ProductClassification Classification { get; }
        public string? EmissionCategory { get; }
        public bool IsLogisticsProduct => Classification == ProductClassification.Logistics || Classification == ProductClassification.Radioactive;

        private static string Require(string value, string parameter) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Product identifiers cannot be empty.", parameter) : value.Trim();
    }

    public readonly struct RecipeFlow
    {
        public RecipeFlow(string productId, decimal quantity, ProductClassification classification = ProductClassification.Logistics,
            string? emissionCategory = null)
        {
            if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            ProductId = string.IsNullOrWhiteSpace(productId) ? throw new ArgumentException("Product identifiers cannot be empty.", nameof(productId)) : productId.Trim();
            Quantity = quantity;
            Classification = classification;
            EmissionCategory = emissionCategory is null || string.IsNullOrWhiteSpace(emissionCategory) ? null : emissionCategory.Trim();
        }

        public string ProductId { get; }
        public decimal Quantity { get; }
        public ProductClassification Classification { get; }
        public string? EmissionCategory { get; }

    }

    public sealed class ProductionRecipe
    {
        public ProductionRecipe(string recipeId, decimal durationSeconds, IEnumerable<RecipeFlow>? inputs,
            IEnumerable<RecipeFlow>? outputs, IEnumerable<RecipeFlow>? emissions = null, string? prototypeId = null)
        {
            RecipeId = Require(recipeId, nameof(recipeId));
            if (durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            DurationSeconds = durationSeconds;
            Inputs = ReadFlows(inputs);
            Outputs = ReadFlows(outputs);
            Emissions = ReadFlows(emissions);
            PrototypeId = prototypeId is null || string.IsNullOrWhiteSpace(prototypeId) ? null : prototypeId.Trim();
        }

        public string RecipeId { get; }
        public string? PrototypeId { get; }
        public decimal DurationSeconds { get; }
        public IReadOnlyList<RecipeFlow> Inputs { get; }
        public IReadOnlyList<RecipeFlow> Outputs { get; }
        public IReadOnlyList<RecipeFlow> Emissions { get; }


        private static IReadOnlyList<RecipeFlow> ReadFlows(IEnumerable<RecipeFlow>? flows) =>
            new ReadOnlyCollection<RecipeFlow>((flows ?? Enumerable.Empty<RecipeFlow>()).ToArray());
        private static string Require(string value, string parameter) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Recipe identifiers cannot be empty.", parameter) : value.Trim();
    }

    /// <summary>Entity data extracted from a blueprint, never from a live scene entity.</summary>
    public sealed class ConfiguredBlueprintEntity
    {
        public static ConfiguredBlueprintEntity FromConfiguration(string entityId, string prototypeId,
            IReadOnlyDictionary<string, string>? configuration, int count = 1, string? fallbackRecipeId = null)
        {
            string? recipeId = null;
            if (configuration is not null)
            {
                configuration.TryGetValue("recipe_id", out recipeId);
                if (string.IsNullOrWhiteSpace(recipeId)) configuration.TryGetValue("recipe", out recipeId);
            }
            return new ConfiguredBlueprintEntity(entityId, prototypeId, recipeId, count, fallbackRecipeId);
        }

        public ConfiguredBlueprintEntity(string entityId, string prototypeId, string? recipeId, int count = 1,
            string? fallbackRecipeId = null)
        {
            EntityId = Require(entityId, nameof(entityId));
            PrototypeId = Require(prototypeId, nameof(prototypeId));
            RecipeId = recipeId is null || string.IsNullOrWhiteSpace(recipeId) ? null : recipeId.Trim();
            FallbackRecipeId = fallbackRecipeId is null || string.IsNullOrWhiteSpace(fallbackRecipeId) ? null : fallbackRecipeId.Trim();
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
        }

        public string EntityId { get; }
        public string PrototypeId { get; }
        public string? RecipeId { get; }
        public string? FallbackRecipeId { get; }
        public int Count { get; }

        private static string Require(string value, string parameter) => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Blueprint identifiers cannot be empty.", parameter) : value.Trim();
    }

    public sealed class ProductionSummary
    {
        internal ProductionSummary(IDictionary<string, FixedRate> inputs, IDictionary<string, FixedRate> outputs,
            IDictionary<string, FixedRate> pollution, IDictionary<string, FixedRate> grossInputs,
            IDictionary<string, FixedRate> grossOutputs, IEnumerable<string> diagnostics, bool fallbackUsed)
        {
            NetInputs = Freeze(inputs);
            NetOutputs = Freeze(outputs);
            Pollution = Freeze(pollution);
            GrossInputs = Freeze(grossInputs);
            GrossOutputs = Freeze(grossOutputs);
            Diagnostics = Array.AsReadOnly((diagnostics ?? Enumerable.Empty<string>()).ToArray());
            FallbackUsed = fallbackUsed;
        }

        public IReadOnlyDictionary<string, FixedRate> NetInputs { get; }
        public IReadOnlyDictionary<string, FixedRate> NetOutputs { get; }
        public IReadOnlyDictionary<string, FixedRate> Pollution { get; }
        public IReadOnlyDictionary<string, FixedRate> GrossInputs { get; }
        public IReadOnlyDictionary<string, FixedRate> GrossOutputs { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public bool FallbackUsed { get; }
        public bool IsEmpty => NetInputs.Count == 0 && NetOutputs.Count == 0 && Pollution.Count == 0;

        private static IReadOnlyDictionary<string, FixedRate> Freeze(IDictionary<string, FixedRate> source) =>
            new ReadOnlyDictionary<string, FixedRate>(new Dictionary<string, FixedRate>(source, StringComparer.Ordinal));
    }

    public static class ProductionRecipeNormalizer
    {
        /// <summary>Normalizes configured blueprint recipes to per-minute fixed-point flows.</summary>
        public static ProductionSummary Normalize(IEnumerable<ConfiguredBlueprintEntity>? entities,
            IReadOnlyDictionary<string, ProductionRecipe> recipes,
            IReadOnlyDictionary<string, ProductDefinition>? products = null)
        {
            if (recipes is null) throw new ArgumentNullException(nameof(recipes));
            var grossIn = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var grossOut = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var pollution = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            bool fallback = false;

            foreach (ConfiguredBlueprintEntity entity in entities ?? Enumerable.Empty<ConfiguredBlueprintEntity>())
            {
                ProductionRecipe? recipe = null;
                bool usedFallback = false;
                if (entity.RecipeId is not null) recipes.TryGetValue(entity.RecipeId, out recipe);
                if (recipe is null && entity.FallbackRecipeId is not null && recipes.TryGetValue(entity.FallbackRecipeId, out recipe))
                {
                    usedFallback = true;
                    fallback = true;
                    diagnostics.Add($"{entity.EntityId}: configured recipe unavailable; fallback '{entity.FallbackRecipeId}' used.");
                }
                if (recipe is null)
                {
                    diagnostics.Add(entity.RecipeId is null
                        ? $"{entity.EntityId}: no configured recipe; entity omitted."
                        : $"{entity.EntityId}: recipe '{entity.RecipeId}' unavailable; entity omitted.");
                    continue;
                }
                if (usedFallback) fallback = true;
                if (recipe.PrototypeId is not null &&
                    !string.Equals(recipe.PrototypeId, entity.PrototypeId, StringComparison.Ordinal))
                {
                    diagnostics.Add($"{entity.EntityId}: recipe '{recipe.RecipeId}' belongs to prototype '{recipe.PrototypeId}', not '{entity.PrototypeId}'; entity omitted.");
                    continue;
                }

                decimal perMinute = 60m * entity.Count / recipe.DurationSeconds;
                AddFlows(grossIn, recipe.Inputs, perMinute, products, diagnostics, externalOnly: true, pollutionTarget: pollution);
                AddFlows(grossOut, recipe.Outputs, perMinute, products, diagnostics, externalOnly: false, pollutionTarget: pollution);
                AddFlows(pollution, recipe.Emissions, perMinute, products, diagnostics, externalOnly: false, pollutionOnly: true);
            }

            var inputs = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, FixedRate> pair in grossIn)
            {
                grossOut.TryGetValue(pair.Key, out FixedRate produced);
                FixedRate remaining = pair.Value - produced;
                if (remaining.Raw > 0) inputs[pair.Key] = remaining;
            }
            var outputs = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, FixedRate> pair in grossOut)
            {
                grossIn.TryGetValue(pair.Key, out FixedRate consumed);
                FixedRate remaining = pair.Value - consumed;
                if (remaining.Raw > 0) outputs[pair.Key] = remaining;
            }
            return new ProductionSummary(inputs, outputs, pollution, grossIn, grossOut, diagnostics, fallback);
        }

        private static void AddFlows(IDictionary<string, FixedRate> target, IEnumerable<RecipeFlow> flows, decimal multiplier,
            IReadOnlyDictionary<string, ProductDefinition>? products, ICollection<string> diagnostics,
            bool externalOnly, bool pollutionOnly = false, IDictionary<string, FixedRate>? pollutionTarget = null)
        {
            foreach (RecipeFlow flow in flows)
            {
                ProductDefinition? definition = null;
                if (products is not null) products.TryGetValue(flow.ProductId, out definition);
                ProductClassification classification = definition?.Classification ?? flow.Classification;
                if (pollutionOnly && classification != ProductClassification.Pollution)
                    classification = ProductClassification.Pollution;
                if (classification == ProductClassification.Pollution)
                {
                    if (pollutionTarget is not null)
                    {
                        FixedRate emission = FixedRate.FromDecimal(flow.Quantity * multiplier);
                        pollutionTarget.TryGetValue(flow.ProductId, out FixedRate oldEmission);
                        pollutionTarget[flow.ProductId] = oldEmission + emission;
                    }
                    continue;
                }
                if (classification == ProductClassification.Virtual || classification == ProductClassification.Obsolete ||
                    (classification != ProductClassification.Logistics && classification != ProductClassification.Radioactive))
                {
                    diagnostics.Add($"{flow.ProductId}: non-logistics/obsolete flow filtered.");
                    continue;
                }
                FixedRate amount = FixedRate.FromDecimal(flow.Quantity * multiplier);
                if (amount.Raw == 0) continue;
                target.TryGetValue(flow.ProductId, out FixedRate previous);
                target[flow.ProductId] = previous + amount;
            }
        }
    }

    public readonly struct ProductionCatalogKey : IEquatable<ProductionCatalogKey>
    {
        public ProductionCatalogKey(string identity, string contentHash)
        {
            Identity = string.IsNullOrWhiteSpace(identity) ? throw new ArgumentException("Identity is required.", nameof(identity)) : identity.Trim();
            ContentHash = string.IsNullOrWhiteSpace(contentHash) ? throw new ArgumentException("Content hash is required.", nameof(contentHash)) : contentHash.Trim();
        }
        public string Identity { get; }
        public string ContentHash { get; }
        public bool Equals(ProductionCatalogKey other) => string.Equals(Identity, other.Identity, StringComparison.Ordinal) && string.Equals(ContentHash, other.ContentHash, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ProductionCatalogKey other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity) * 397 ^ StringComparer.Ordinal.GetHashCode(ContentHash);
    }

    public sealed class ProductionSummaryCache
    {
        private readonly Dictionary<ProductionCatalogKey, ProductionSummary> m_cache = new();
        public ProductionSummary GetOrAdd(ProductionCatalogKey key, Func<ProductionSummary> factory)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            if (m_cache.TryGetValue(key, out ProductionSummary? value)) return value;
            value = factory();
            m_cache[key] = value ?? throw new InvalidOperationException("Summary factory returned null.");
            return value;
        }
        public ProductionSummary GetOrAdd(string blueprintIdentity, string contentHash, Func<ProductionSummary> factory) =>
            GetOrAdd(new ProductionCatalogKey(blueprintIdentity, contentHash), factory);
        public bool Remove(ProductionCatalogKey key) => m_cache.Remove(key);
        public void Clear() => m_cache.Clear();
        public int Count => m_cache.Count;
    }

    public sealed class ProductionCatalog
    {
        private readonly Dictionary<string, ProductionRecipe> m_recipes;
        public ProductionCatalog(IEnumerable<ProductionRecipe> recipes)
        {
            m_recipes = new Dictionary<string, ProductionRecipe>(StringComparer.Ordinal);
            foreach (ProductionRecipe recipe in recipes ?? Enumerable.Empty<ProductionRecipe>())
                if (!m_recipes.ContainsKey(recipe.RecipeId)) m_recipes.Add(recipe.RecipeId, recipe);
        }
        public IReadOnlyDictionary<string, ProductionRecipe> Recipes => new ReadOnlyDictionary<string, ProductionRecipe>(m_recipes);
        public IReadOnlyList<ProductionRecipe> ForOutput(string productId) => m_recipes.Values.Where(r => r.Outputs.Any(f => f.ProductId == productId)).OrderBy(r => r.RecipeId, StringComparer.Ordinal).ToArray();
    }

    public sealed class ProductionPlan
    {
        internal ProductionPlan(IDictionary<string, FixedRate> raw, IDictionary<string, FixedRate> products,
            IDictionary<string, FixedRate> utilization, IDictionary<string, FixedRate> byproducts,
            IDictionary<string, FixedRate> pollution, IEnumerable<string> diagnostics)
        {
            RawRequirements = new ReadOnlyDictionary<string, FixedRate>(new Dictionary<string, FixedRate>(raw, StringComparer.Ordinal));
            IntermediateFlows = new ReadOnlyDictionary<string, FixedRate>(new Dictionary<string, FixedRate>(products, StringComparer.Ordinal));
            RecipeUtilization = new ReadOnlyDictionary<string, FixedRate>(new Dictionary<string, FixedRate>(utilization, StringComparer.Ordinal));
            Byproducts = new ReadOnlyDictionary<string, FixedRate>(new Dictionary<string, FixedRate>(byproducts, StringComparer.Ordinal));
            Pollution = new ReadOnlyDictionary<string, FixedRate>(new Dictionary<string, FixedRate>(pollution, StringComparer.Ordinal));
            Diagnostics = Array.AsReadOnly((diagnostics ?? Enumerable.Empty<string>()).ToArray());
        }
        public IReadOnlyDictionary<string, FixedRate> RawRequirements { get; }
        public IReadOnlyDictionary<string, FixedRate> IntermediateFlows { get; }
        public IReadOnlyDictionary<string, FixedRate> RecipeUtilization { get; }
        public IReadOnlyDictionary<string, FixedRate> Byproducts { get; }
        public IReadOnlyDictionary<string, FixedRate> Pollution { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public bool IsValid => Diagnostics.Count == 0;
    }

    public sealed class ProductionPlanner
    {
        private readonly ProductionCatalog m_catalog;
        public ProductionPlanner(ProductionCatalog catalog) => m_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        public ProductionPlan Solve(string targetProductId, FixedRate targetRate, IReadOnlyDictionary<string, string>? pinnedRecipes = null,
            ISet<string>? disabledRecipes = null, int maxNodes = 4096)
        {
            if (string.IsNullOrWhiteSpace(targetProductId)) throw new ArgumentException("Target product is required.", nameof(targetProductId));
            if (targetRate.Raw <= 0) throw new ArgumentOutOfRangeException(nameof(targetRate));
            if (maxNodes < 1) throw new ArgumentOutOfRangeException(nameof(maxNodes));
            var raw = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var flows = new Dictionary<string, FixedRate>(StringComparer.Ordinal) { [targetProductId.Trim()] = targetRate };
            var utilization = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var byproducts = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var available = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var pollution = new Dictionary<string, FixedRate>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            var path = new HashSet<string>(StringComparer.Ordinal);
            int nodes = 0;
            Expand(targetProductId.Trim(), targetRate, pinnedRecipes, disabledRecipes, raw, flows, utilization,
                byproducts, available, pollution, diagnostics, path, ref nodes, maxNodes);
            foreach (string product in byproducts.Keys.ToArray())
            {
                flows.TryGetValue(product, out FixedRate consumed);
                FixedRate remaining = byproducts[product] - consumed;
                if (remaining.Raw > 0) byproducts[product] = remaining;
                else byproducts.Remove(product);
            }
            return new ProductionPlan(raw, flows, utilization, byproducts, pollution, diagnostics);
        }

        private void Expand(string product, FixedRate rate, IReadOnlyDictionary<string, string>? pinned, ISet<string>? disabled,
            IDictionary<string, FixedRate> raw, IDictionary<string, FixedRate> flows, IDictionary<string, FixedRate> utilization,
            IDictionary<string, FixedRate> byproducts, IDictionary<string, FixedRate> available,
            IDictionary<string, FixedRate> pollution,
            ICollection<string> diagnostics, ISet<string> path, ref int nodes, int maxNodes)
        {
            if (available.TryGetValue(product, out FixedRate supply) && supply.Raw > 0)
            {
                FixedRate used = supply.Raw >= rate.Raw ? rate : supply;
                rate -= used;
                supply -= used;
                if (supply.Raw > 0) available[product] = supply;
                else available.Remove(product);
                if (rate.Raw <= 0) return;
            }
            if (++nodes > maxNodes) { diagnostics.Add("Planner node bound exceeded."); return; }
            if (!path.Add(product)) { diagnostics.Add("Cycle detected at product '" + product + "'."); return; }
            try
            {
                IReadOnlyList<ProductionRecipe> allCandidates = m_catalog.ForOutput(product);
                IReadOnlyList<ProductionRecipe> candidates = allCandidates.Where(r => disabled is null || !disabled.Contains(r.RecipeId)).ToArray();
                if (pinned is not null && pinned.TryGetValue(product, out string? pinnedId))
                {
                    candidates = candidates.Where(r => r.RecipeId == pinnedId).ToArray();
                    if (candidates.Count == 0) { diagnostics.Add($"Pinned recipe '{pinnedId}' is unavailable for '{product}'."); return; }
                }
                if (candidates.Count == 0)
                {
                    if (allCandidates.Count != 0) diagnostics.Add($"All routes for '{product}' are disabled.");
                    else Add(raw, product, rate);
                    return;
                }
                if (candidates.Count > 1) { diagnostics.Add($"Product '{product}' has multiple routes; pin a recipe."); return; }
                ProductionRecipe recipe = candidates[0];
                decimal outputQuantity = recipe.Outputs.Where(f => f.ProductId == product).Sum(f => f.Quantity);
                if (outputQuantity <= 0) { diagnostics.Add($"Recipe '{recipe.RecipeId}' has no positive output for '{product}'."); return; }
                decimal cycles = ((decimal)rate.Raw / FixedRate.Scale) * recipe.DurationSeconds / 60m / outputQuantity;
                FixedRate utilizationRate = FixedRate.FromDecimal(cycles * 60m / recipe.DurationSeconds);
                Add(utilization, recipe.RecipeId, utilizationRate);
                foreach (RecipeFlow output in recipe.Outputs)
                {
                    if (output.ProductId == product) continue;
                    FixedRate outputRate = FixedRate.FromDecimal(output.Quantity * (decimal)cycles * 60m / recipe.DurationSeconds);
                    if (outputRate.Raw <= 0) continue;
                    switch (output.Classification)
                    {
                        case ProductClassification.Pollution:
                            Add(pollution, output.ProductId, outputRate);
                            break;
                        case ProductClassification.Logistics:
                        case ProductClassification.Radioactive:
                            Add(byproducts, output.ProductId, outputRate);
                            Add(available, output.ProductId, outputRate);
                            break;
                        default:
                            diagnostics.Add($"Recipe '{recipe.RecipeId}' output '{output.ProductId}' is non-logistics and was filtered.");
                            break;
                    }
                }
                foreach (RecipeFlow input in recipe.Inputs)
                {
                    if (input.Classification != ProductClassification.Logistics && input.Classification != ProductClassification.Radioactive)
                    {
                        diagnostics.Add($"Recipe '{recipe.RecipeId}' input '{input.ProductId}' is non-logistics and was filtered.");
                        continue;
                    }
                    FixedRate inputRate = FixedRate.FromDecimal(input.Quantity * (decimal)cycles * 60m / recipe.DurationSeconds);
                    Add(flows, input.ProductId, inputRate);
                    Expand(input.ProductId, inputRate, pinned, disabled, raw, flows, utilization, byproducts,
                        available, pollution, diagnostics, path, ref nodes, maxNodes);
                }
            }
            finally { path.Remove(product); }
        }

        private static void Add(IDictionary<string, FixedRate> values, string key, FixedRate value)
        {
            values.TryGetValue(key, out FixedRate old);
            values[key] = old + value;
        }
    }
}
