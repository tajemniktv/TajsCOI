// Taj's COI Mods | BlueprintLibrary.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using TajsCOI.Core.Production;

namespace TajsCOI.Core.Blueprints
{
    public enum BlueprintEntryState { Active, Deleted }
    public enum BlueprintWriteMode { Create, Duplicate, Overwrite, Update }

    public readonly struct BlueprintIdentity : IEquatable<BlueprintIdentity>
    {
        public BlueprintIdentity(string stableId, string contentHash)
        {
            StableId = Require(stableId, nameof(stableId));
            ContentHash = Require(contentHash, nameof(contentHash));
        }
        public string StableId { get; }
        public string ContentHash { get; }
        public bool Equals(BlueprintIdentity other) => string.Equals(StableId, other.StableId, StringComparison.Ordinal) && string.Equals(ContentHash, other.ContentHash, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is BlueprintIdentity other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(StableId) * 397 ^ StringComparer.Ordinal.GetHashCode(ContentHash);
        public override string ToString() => StableId + "@" + ContentHash;
        private static string Require(string value, string parameter) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Blueprint identity cannot be empty.", parameter) : value.Trim();
    }

    public sealed class BlueprintOperationalStats
    {
        public BlueprintOperationalStats(decimal workers, decimal electricity, decimal computing, decimal maintenance,
            ProductionSummary? production = null)
        {
            Workers = Math.Max(0, workers);
            Electricity = Math.Max(0, electricity);
            Computing = Math.Max(0, computing);
            Maintenance = Math.Max(0, maintenance);
            Production = production;
        }
        public decimal Workers { get; }
        public decimal Electricity { get; }
        public decimal Computing { get; }
        public decimal Maintenance { get; }
        public ProductionSummary? Production { get; }
    }

    public readonly struct BlueprintCostContribution
    {
        public BlueprintCostContribution(decimal workers, decimal electricity, decimal computing, decimal maintenance)
        { Workers = workers; Electricity = electricity; Computing = computing; Maintenance = maintenance; }
        public decimal Workers { get; }
        public decimal Electricity { get; }
        public decimal Computing { get; }
        public decimal Maintenance { get; }
    }

    public static class BlueprintOperationalStatsAggregator
    {
        public static BlueprintOperationalStats Sum(IEnumerable<BlueprintCostContribution>? contributions, ProductionSummary? production = null)
        {
            decimal workers = 0, electricity = 0, computing = 0, maintenance = 0;
            foreach (BlueprintCostContribution contribution in contributions ?? Enumerable.Empty<BlueprintCostContribution>())
            {
                workers += Math.Max(0, contribution.Workers);
                electricity += Math.Max(0, contribution.Electricity);
                computing += Math.Max(0, contribution.Computing);
                maintenance += Math.Max(0, contribution.Maintenance);
            }
            return new BlueprintOperationalStats(workers, electricity, computing, maintenance, production);
        }
    }

    public sealed class BlueprintLibraryEntry
    {
        public BlueprintLibraryEntry(BlueprintIdentity identity, string name, string folder,
            IEnumerable<string>? prototypeIds, IReadOnlyDictionary<string, string>? configuration,
            BlueprintOperationalStats? stats = null, BlueprintEntryState state = BlueprintEntryState.Active)
        {
            Identity = identity;
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed blueprint" : name.Trim();
            Folder = string.IsNullOrWhiteSpace(folder) ? "Default" : folder.Trim();
            PrototypeIds = new ReadOnlyCollection<string>((prototypeIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Configuration = new ReadOnlyDictionary<string, string>(
                (configuration ?? new Dictionary<string, string>()).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            Stats = stats;
            State = state;
        }
        public BlueprintIdentity Identity { get; }
        public string Name { get; }
        public string Folder { get; }
        public IReadOnlyList<string> PrototypeIds { get; }
        public IReadOnlyDictionary<string, string> Configuration { get; }
        public BlueprintOperationalStats? Stats { get; }
        public BlueprintEntryState State { get; }

        public BlueprintLibraryEntry With(string? name = null, string? folder = null, BlueprintOperationalStats? stats = null,
            BlueprintEntryState? state = null, string? contentHash = null)
        {
            return new BlueprintLibraryEntry(new BlueprintIdentity(Identity.StableId, contentHash ?? Identity.ContentHash),
                name ?? Name, folder ?? Folder, PrototypeIds, Configuration, stats ?? Stats, state ?? State);
        }
    }

    public sealed class BlueprintWriteResult
    {
        internal BlueprintWriteResult(bool success, string message, BlueprintLibraryEntry? entry)
        { Success = success; Message = message; Entry = entry; }
        public bool Success { get; }
        public string Message { get; }
        public BlueprintLibraryEntry? Entry { get; }
    }

    public sealed class BlueprintImportPreview
    {
        internal BlueprintImportPreview(BlueprintExportPayload? payload, IEnumerable<string> missing, string error)
        { Payload = payload; MissingPrototypeIds = new ReadOnlyCollection<string>((missing ?? Enumerable.Empty<string>()).ToArray()); Error = error; }
        public BlueprintExportPayload? Payload { get; }
        public IReadOnlyList<string> MissingPrototypeIds { get; }
        public string Error { get; }
        public bool CanImport => Payload is not null && Error.Length == 0 && MissingPrototypeIds.Count == 0;
    }

    public sealed class BlueprintExportPayload
    {
        public int Version { get; set; } = BlueprintPayloadCodec.CurrentVersion;
        public string StableId { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Folder { get; set; } = "Default";
        public int State { get; set; }
        public List<string> PrototypeIds { get; set; } = new();
        public Dictionary<string, string> Configuration { get; set; } = new(StringComparer.Ordinal);
        public BlueprintOperationalExportStats? Stats { get; set; }
    }

    public sealed class BlueprintOperationalExportStats
    {
        public decimal Workers { get; set; }
        public decimal Electricity { get; set; }
        public decimal Computing { get; set; }
        public decimal Maintenance { get; set; }
    }

    public static class BlueprintPayloadCodec
    {
        public const int CurrentVersion = 1;

        internal static BlueprintExportPayload ToPayload(BlueprintLibraryEntry entry)
        {
            return new BlueprintExportPayload
            {
                StableId = entry.Identity.StableId,
                ContentHash = entry.Identity.ContentHash,
                Name = entry.Name,
                Folder = entry.Folder,
                State = (int)entry.State,
                PrototypeIds = entry.PrototypeIds.ToList(),
                Configuration = entry.Configuration.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                Stats = entry.Stats is null ? null : new BlueprintOperationalExportStats
                {
                    Workers = entry.Stats.Workers, Electricity = entry.Stats.Electricity,
                    Computing = entry.Stats.Computing, Maintenance = entry.Stats.Maintenance,
                },
            };
        }

        public static string Export(BlueprintLibraryEntry entry)
        {
            if (entry is null) throw new ArgumentNullException(nameof(entry));
            return Serialize(ToPayload(entry));
        }

        /// <summary>Human-facing report kept separate from the machine payload.</summary>
        public static string CreateHumanReadableSummary(BlueprintLibraryEntry entry)
        {
            if (entry is null) throw new ArgumentNullException(nameof(entry));
            var builder = new StringBuilder();
            builder.AppendLine("# " + entry.Name);
            builder.AppendLine("Folder: " + entry.Folder);
            builder.AppendLine("Blueprint: " + entry.Identity);
            if (entry.Stats is not null)
            {
                builder.AppendLine("Workers: " + entry.Stats.Workers.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                builder.AppendLine("Electricity: " + entry.Stats.Electricity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                builder.AppendLine("Computing: " + entry.Stats.Computing.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                builder.AppendLine("Maintenance: " + entry.Stats.Maintenance.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (entry.Stats?.Production is ProductionSummary production)
            {
                AppendFlows(builder, "Net inputs", production.NetInputs);
                AppendFlows(builder, "Net outputs", production.NetOutputs);
                AppendFlows(builder, "Pollution", production.Pollution);
            }
            return builder.ToString();
        }

        private static void AppendFlows(StringBuilder builder, string title, IReadOnlyDictionary<string, FixedRate> flows)
        {
            builder.AppendLine(title + ":");
            foreach (KeyValuePair<string, FixedRate> flow in flows.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.AppendLine("- " + flow.Key + ": " + flow.Value);
        }

        public static BlueprintImportPreview Preview(string text, ISet<string> availablePrototypeIds)
        {
            if (availablePrototypeIds is null) throw new ArgumentNullException(nameof(availablePrototypeIds));
            try
            {
                BlueprintExportPayload? payload = Deserialize<BlueprintExportPayload>(text);
                if (payload is null) return new BlueprintImportPreview(null, Array.Empty<string>(), "Payload is empty.");
                return PreviewPayload(payload, availablePrototypeIds);
            }
            catch (Exception ex) { return new BlueprintImportPreview(null, Array.Empty<string>(), "Invalid blueprint payload: " + ex.Message); }
        }

        internal static BlueprintImportPreview PreviewPayload(BlueprintExportPayload? payload, ISet<string> availablePrototypeIds)
        {
            try
            {
                if (payload is null) return new BlueprintImportPreview(null, Array.Empty<string>(), "Payload is empty.");
                if (payload.Version != CurrentVersion) return new BlueprintImportPreview(payload, Array.Empty<string>(), $"Unsupported export schema version {payload.Version}; expected {CurrentVersion}.");
                if (string.IsNullOrWhiteSpace(payload.StableId) || string.IsNullOrWhiteSpace(payload.ContentHash))
                    return new BlueprintImportPreview(payload, Array.Empty<string>(), "Blueprint payload must include stableId and contentHash.");
                if (!Enum.IsDefined(typeof(BlueprintEntryState), payload.State))
                    return new BlueprintImportPreview(payload, Array.Empty<string>(), "Blueprint payload contains an invalid deletion state.");
                if (payload.PrototypeIds is null || payload.PrototypeIds.Any(id => string.IsNullOrWhiteSpace(id)))
                    return new BlueprintImportPreview(payload, Array.Empty<string>(), "Blueprint payload contains an invalid prototype list.");
                var missing = (payload.PrototypeIds ?? new List<string>()).Where(id => !availablePrototypeIds.Contains(id)).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                return new BlueprintImportPreview(payload, missing, string.Empty);
            }
            catch (Exception ex) { return new BlueprintImportPreview(null, Array.Empty<string>(), "Invalid blueprint payload: " + ex.Message); }
        }

        private static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T? Deserialize<T>(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return default;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (T?)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    /// <summary>Process-independent sidecar metadata store. Native library mutations are delegated to callers.</summary>
    public sealed class BlueprintLibraryStore
    {
        private readonly Dictionary<string, BlueprintLibraryEntry> m_entries = new(StringComparer.Ordinal);
        public IReadOnlyList<BlueprintLibraryEntry> Snapshot(bool includeDeleted = false) =>
            new ReadOnlyCollection<BlueprintLibraryEntry>(m_entries.Values.Where(e => includeDeleted || e.State == BlueprintEntryState.Active).OrderBy(e => e.Folder, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal).ToArray());

        public BlueprintWriteResult Write(BlueprintLibraryEntry entry, BlueprintWriteMode mode)
        {
            if (entry is null) throw new ArgumentNullException(nameof(entry));
            string key = entry.Identity.StableId;
            bool exists = m_entries.TryGetValue(key, out BlueprintLibraryEntry? current);
            if (mode == BlueprintWriteMode.Create && exists) return new BlueprintWriteResult(false, "Blueprint already exists.", current);
            if (mode == BlueprintWriteMode.Update && !exists) return new BlueprintWriteResult(false, "Blueprint does not exist.", null);
            if (mode == BlueprintWriteMode.Duplicate && exists)
            {
                key = entry.Identity.StableId + "-copy";
                int suffix = 2;
                while (m_entries.ContainsKey(key)) key = entry.Identity.StableId + "-copy-" + suffix++;
                entry = new BlueprintLibraryEntry(new BlueprintIdentity(key, entry.Identity.ContentHash), entry.Name + " (copy)", entry.Folder, entry.PrototypeIds, entry.Configuration, entry.Stats);
            }
            if (mode == BlueprintWriteMode.Update && current is not null)
            {
                // Updating a layout changes content/configuration but preserves user-owned
                // folder, display name, and recycle-bin state.
                entry = new BlueprintLibraryEntry(
                    new BlueprintIdentity(current.Identity.StableId, entry.Identity.ContentHash),
                    current.Name,
                    current.Folder,
                    entry.PrototypeIds,
                    entry.Configuration,
                    entry.Stats,
                    current.State);
            }
            if (mode == BlueprintWriteMode.Overwrite || mode == BlueprintWriteMode.Update || !exists)
                m_entries[key] = entry;
            return new BlueprintWriteResult(true, "Blueprint saved.", entry);
        }

        public bool SoftDelete(string stableId) => SetState(stableId, BlueprintEntryState.Deleted);
        public bool Restore(string stableId) => SetState(stableId, BlueprintEntryState.Active);
        public bool Purge(string stableId) => !string.IsNullOrWhiteSpace(stableId) && m_entries.Remove(stableId.Trim());

        public BlueprintImportPreview PreviewImport(string payload, ISet<string> availablePrototypeIds) => BlueprintPayloadCodec.Preview(payload, availablePrototypeIds);

        public string ExportSidecar() => SerializeSidecar(m_entries.Values.Select(BlueprintPayloadCodec.ToPayload).ToArray());

        /// <summary>Loads the complete sidecar atomically; missing prototypes reject the whole import.</summary>
        public bool LoadSidecar(string payload, ISet<string> availablePrototypeIds, out IReadOnlyList<string> diagnostics)
        {
            if (availablePrototypeIds is null) throw new ArgumentNullException(nameof(availablePrototypeIds));
            var errors = new List<string>();
            List<BlueprintExportPayload>? values;
            try { values = DeserializeSidecar(payload); }
            catch (Exception ex) { diagnostics = new ReadOnlyCollection<string>(new[] { "Invalid blueprint sidecar: " + ex.Message }); return false; }
            if (values is null) { diagnostics = new ReadOnlyCollection<string>(new[] { "Blueprint sidecar is empty." }); return false; }
            var imported = new List<BlueprintLibraryEntry>();
            foreach (BlueprintExportPayload? value in values)
            {
                BlueprintImportPreview preview = BlueprintPayloadCodec.PreviewPayload(value, availablePrototypeIds);
                if (!preview.CanImport) { errors.Add(preview.Error.Length == 0 ? "Missing required prototypes: " + string.Join(", ", preview.MissingPrototypeIds) : preview.Error); continue; }
                BlueprintExportPayload source = preview.Payload!;
                if (!Enum.IsDefined(typeof(BlueprintEntryState), source.State)) { errors.Add($"Blueprint '{source.StableId}' has an invalid deletion state."); continue; }
                BlueprintOperationalStats? stats = source.Stats is null ? null : new BlueprintOperationalStats(source.Stats.Workers, source.Stats.Electricity, source.Stats.Computing, source.Stats.Maintenance);
                BlueprintEntryState state = (BlueprintEntryState)source.State;
                imported.Add(new BlueprintLibraryEntry(new BlueprintIdentity(source.StableId, source.ContentHash), source.Name, source.Folder, source.PrototypeIds, source.Configuration, stats, state));
            }
            if (errors.Count != 0) { diagnostics = new ReadOnlyCollection<string>(errors); return false; }
            m_entries.Clear();
            foreach (BlueprintLibraryEntry entry in imported) m_entries[entry.Identity.StableId] = entry;
            diagnostics = Array.Empty<string>();
            return true;
        }

        private static string SerializeSidecar(IEnumerable<BlueprintExportPayload> values)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(BlueprintExportPayload[])).WriteObject(stream, values.ToArray());
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static List<BlueprintExportPayload>? DeserializeSidecar(string payload)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload ?? string.Empty)))
                return ((BlueprintExportPayload[]?)new DataContractJsonSerializer(typeof(BlueprintExportPayload[])).ReadObject(stream))?.ToList();
        }

        public BlueprintWriteResult Import(string payload, ISet<string> availablePrototypeIds, BlueprintWriteMode mode)
        {
            BlueprintImportPreview preview = PreviewImport(payload, availablePrototypeIds);
            if (!preview.CanImport) return new BlueprintWriteResult(false, preview.Error.Length == 0 ? "Missing required prototypes: " + string.Join(", ", preview.MissingPrototypeIds) : preview.Error, null);
            BlueprintExportPayload source = preview.Payload!;
            BlueprintOperationalStats? stats = source.Stats is null ? null : new BlueprintOperationalStats(source.Stats.Workers, source.Stats.Electricity, source.Stats.Computing, source.Stats.Maintenance);
            BlueprintEntryState state = Enum.IsDefined(typeof(BlueprintEntryState), source.State) ? (BlueprintEntryState)source.State : BlueprintEntryState.Active;
            return Write(new BlueprintLibraryEntry(new BlueprintIdentity(source.StableId, source.ContentHash), source.Name, source.Folder, source.PrototypeIds, source.Configuration, stats, state), mode);
        }

        private bool SetState(string stableId, BlueprintEntryState state)
        {
            if (string.IsNullOrWhiteSpace(stableId) || !m_entries.TryGetValue(stableId.Trim(), out BlueprintLibraryEntry? current)) return false;
            m_entries[current.Identity.StableId] = current.With(state: state);
            return true;
        }
    }
}
