// Taj's COI Mods | DailyValueHistory.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TajsCOI.Profiler.Core
{
    /// <summary>Bounded daily ring buffer shared by throughput and environmental probes.</summary>
    public sealed class DailyValueHistory
    {
        private readonly long[] m_days;
        private readonly double[] m_values;
        private int m_count;
        private int m_next;
        private long m_currentDay = long.MinValue;
        private long m_lastClosedDay = long.MinValue;
        private double m_current;
        private int m_window;
        private double m_cachedAverage;
        private bool m_averageDirty = true;

        public DailyValueHistory(int capacityDays = 360, int averagingWindow = 30)
        {
            if (capacityDays < 1) throw new ArgumentOutOfRangeException(nameof(capacityDays));
            m_days = new long[capacityDays];
            m_values = new double[capacityDays];
            m_window = Math.Max(1, Math.Min(capacityDays, averagingWindow));
        }

        public int CapacityDays => m_days.Length;
        public int Count => m_count;
        public int AveragingWindow => m_window;
        public long CurrentDay => m_currentDay;
        public long LastClosedDay => m_lastClosedDay;
        public double CurrentValue => m_current;

        public void Add(long day, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (day == long.MinValue) throw new ArgumentOutOfRangeException(nameof(day));
            if (day <= m_lastClosedDay) return;
            if (m_currentDay == long.MinValue) m_currentDay = day;
            if (day < m_currentDay) return; // Late events belong to a closed simulation day and are ignored conservatively.
            if (unchecked((ulong)day - (ulong)m_currentDay) > (ulong)CapacityDays)
            {
                // A corrupted or paused clock must not make a bounded history loop
                // once per elapsed game day. Retain the current bucket and jump.
                CloseDay(m_currentDay);
                m_currentDay = day;
            }
            while (m_currentDay < day)
            {
                CloseDay(m_currentDay);
                m_currentDay++;
            }
            m_current += value;
            m_averageDirty = true;
        }

        public void CloseCurrentDay()
        {
            if (m_currentDay == long.MinValue) return;
            CloseDay(m_currentDay);
            m_currentDay = long.MinValue;
            m_current = 0;
        }

        public void AdvanceToDay(long day)
        {
            if (day == long.MinValue) throw new ArgumentOutOfRangeException(nameof(day));
            if (m_currentDay == long.MinValue)
            {
                if (day > m_lastClosedDay) m_lastClosedDay = day - 1;
                return;
            }
            if (day <= m_currentDay) return;
            if (unchecked((ulong)day - (ulong)m_currentDay) > (ulong)CapacityDays)
            {
                CloseDay(m_currentDay);
                m_currentDay = day;
                return;
            }
            while (m_currentDay < day)
            {
                CloseDay(m_currentDay);
                m_currentDay++;
            }
        }

        public void SetAveragingWindow(int days)
        {
            if (days < 1 || days > CapacityDays) throw new ArgumentOutOfRangeException(nameof(days));
            if (m_window == days) return;
            m_window = days;
            m_averageDirty = true;
        }

        public double RollingAverage
        {
            get
            {
                if (!m_averageDirty) return m_cachedAverage;
                bool hasCurrent = m_currentDay != long.MinValue;
                int takeClosed = Math.Min(m_count, m_window - (hasCurrent ? 1 : 0));
                int included = takeClosed + (hasCurrent ? 1 : 0);
                if (included == 0) return m_cachedAverage = 0;
                double total = hasCurrent ? m_current : 0;
                for (int i = 0; i < takeClosed; i++) total += m_values[(m_next - 1 - i + CapacityDays) % CapacityDays];
                m_cachedAverage = included == 0 ? 0 : total / included;
                m_averageDirty = false;
                return m_cachedAverage;
            }
        }

        public IReadOnlyList<DailyValueSample> Snapshot()
        {
            var result = new List<DailyValueSample>(m_count);
            int start = (m_next - m_count + CapacityDays) % CapacityDays;
            for (int i = 0; i < m_count; i++)
            {
                int index = (start + i) % CapacityDays;
                result.Add(new DailyValueSample(m_days[index], m_values[index]));
            }
            return new ReadOnlyCollection<DailyValueSample>(result);
        }

        public DailyValueHistorySnapshot CreateSnapshot() =>
            new DailyValueHistorySnapshot(CapacityDays, Count, AveragingWindow, CurrentDay, LastClosedDay,
                CurrentValue, RollingAverage, Snapshot());

        public void Clear()
        {
            Array.Clear(m_days, 0, m_days.Length);
            Array.Clear(m_values, 0, m_values.Length);
            m_count = 0; m_next = 0; m_currentDay = long.MinValue; m_lastClosedDay = long.MinValue; m_current = 0; m_cachedAverage = 0; m_averageDirty = true;
        }

        private void CloseDay(long day)
        {
            m_days[m_next] = day;
            m_values[m_next] = m_current;
            m_next = (m_next + 1) % CapacityDays;
            if (m_count < CapacityDays) m_count++;
            m_lastClosedDay = day;
            m_current = 0;
            m_averageDirty = true;
        }
    }

    public readonly struct DailyValueSample
    {
        public DailyValueSample(long day, double value) { Day = day; Value = value; }
        public long Day { get; }
        public double Value { get; }
    }

    /// <summary>Immutable diagnostic view; callers cannot mutate the live ring buffer.</summary>
    public sealed class DailyValueHistorySnapshot
    {
        internal DailyValueHistorySnapshot(int capacityDays, int count, int averagingWindow, long currentDay,
            long lastClosedDay, double currentValue, double rollingAverage, IReadOnlyList<DailyValueSample> samples)
        {
            CapacityDays = capacityDays;
            Count = count;
            AveragingWindow = averagingWindow;
            CurrentDay = currentDay;
            LastClosedDay = lastClosedDay;
            CurrentValue = currentValue;
            RollingAverage = rollingAverage;
            Samples = new ReadOnlyCollection<DailyValueSample>((samples ?? Array.Empty<DailyValueSample>()).ToArray());
        }
        public int CapacityDays { get; }
        public int Count { get; }
        public int AveragingWindow { get; }
        public long CurrentDay { get; }
        public long LastClosedDay { get; }
        public double CurrentValue { get; }
        public double RollingAverage { get; }
        public IReadOnlyList<DailyValueSample> Samples { get; }
    }

    public readonly struct ThroughputKey : IEquatable<ThroughputKey>
    {
        public ThroughputKey(string entityId, string productId)
        {
            EntityId = Require(entityId, nameof(entityId)); ProductId = Require(productId, nameof(productId));
        }
        public string EntityId { get; }
        public string ProductId { get; }
        public bool Equals(ThroughputKey other) => EntityId == other.EntityId && ProductId == other.ProductId;
        public override bool Equals(object? obj) => obj is ThroughputKey other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(EntityId) * 397 ^ StringComparer.Ordinal.GetHashCode(ProductId);
        private static string Require(string value, string parameter) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Identifiers cannot be empty.", parameter) : value.Trim();
    }

    public sealed class ThroughputHistoryService
    {
        private readonly Dictionary<ThroughputKey, DailyValueHistory> m_histories = new();
        private readonly HashSet<string> m_monitoredEntities = new(StringComparer.Ordinal);
        private readonly int m_capacityDays;
        private int m_window;

        public ThroughputHistoryService(int capacityDays = 360, int averagingWindow = 30)
        { m_capacityDays = Math.Max(1, capacityDays); m_window = Math.Max(1, Math.Min(m_capacityDays, averagingWindow)); }
        public bool Enabled { get; set; } = true;
        public int MonitoredEntityCount => m_monitoredEntities.Count;
        public void SetMonitored(string entityId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return;
            if (enabled) m_monitoredEntities.Add(entityId.Trim()); else m_monitoredEntities.Remove(entityId.Trim());
        }
        public bool IsMonitored(string entityId) => !string.IsNullOrWhiteSpace(entityId) && m_monitoredEntities.Contains(entityId.Trim());
        public void RecordTransfer(string entityId, string productId, double quantity, long gameDay)
        {
            if (!Enabled || double.IsNaN(quantity) || double.IsInfinity(quantity) || quantity <= 0 || !IsMonitored(entityId)) return;
            var key = new ThroughputKey(entityId, productId);
            if (!m_histories.TryGetValue(key, out DailyValueHistory? history))
                m_histories.Add(key, history = new DailyValueHistory(m_capacityDays, m_window));
            history.Add(gameDay, quantity);
        }
        public bool TryGetAverage(string entityId, string productId, out double average)
        {
            if (m_histories.TryGetValue(new ThroughputKey(entityId, productId), out DailyValueHistory? history)) { average = history.RollingAverage; return true; }
            average = 0; return false;
        }
        public void SetAveragingWindow(int days) { m_window = Math.Max(1, Math.Min(m_capacityDays, days)); foreach (DailyValueHistory h in m_histories.Values) h.SetAveragingWindow(m_window); }
        public void AdvanceToDay(long gameDay) { foreach (DailyValueHistory h in m_histories.Values) h.AdvanceToDay(gameDay); }
        public IReadOnlyDictionary<ThroughputKey, DailyValueHistorySnapshot> Snapshot() =>
            new ReadOnlyDictionary<ThroughputKey, DailyValueHistorySnapshot>(m_histories.ToDictionary(pair => pair.Key, pair => pair.Value.CreateSnapshot()));
        public IReadOnlyList<ThroughputHeatmapEntry> BuildHeatmap(IEnumerable<ThroughputHeatmapSample> samples, int maximumEntries = 256)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            return new ReadOnlyCollection<ThroughputHeatmapEntry>(samples
                .Where(s => IsMonitored(s.EntityId) && s.MeasuredThroughput >= 0)
                .Select(s => new ThroughputHeatmapEntry(s.EntityId, s.ProductId, s.MeasuredThroughput, s.Capacity > 0 ? (double?)Math.Min(1, s.MeasuredThroughput / s.Capacity) : null))
                .OrderByDescending(s => s.Utilization ?? s.MeasuredThroughput)
                .ThenBy(s => s.EntityId, StringComparer.Ordinal)
                .Take(maximumEntries).ToArray());
        }
        public void Clear() { m_histories.Clear(); m_monitoredEntities.Clear(); }
    }

    public readonly struct ThroughputHeatmapSample
    {
        public ThroughputHeatmapSample(string entityId, string productId, double measuredThroughput, double capacity)
        { EntityId = entityId; ProductId = productId; MeasuredThroughput = measuredThroughput; Capacity = capacity; }
        public string EntityId { get; }
        public string ProductId { get; }
        public double MeasuredThroughput { get; }
        public double Capacity { get; }
    }

    public sealed class ThroughputHeatmapEntry
    {
        internal ThroughputHeatmapEntry(string entityId, string productId, double measuredThroughput, double? utilization)
        { EntityId = entityId; ProductId = productId; MeasuredThroughput = measuredThroughput; Utilization = utilization; }
        public string EntityId { get; }
        public string ProductId { get; }
        public double MeasuredThroughput { get; }
        public double? Utilization { get; }
        public bool HasKnownCapacity => Utilization.HasValue;
    }

    public enum EnvironmentalSourceCategory { AirPollution, WaterPollution, GroundPollution, SolidWaste, VehicleEmission, TrainEmission, ShipEmission, Radioactivity }

    public readonly struct EnvironmentalSourceKey : IEquatable<EnvironmentalSourceKey>
    {
        public EnvironmentalSourceKey(string sourceId, EnvironmentalSourceCategory category)
        { SourceId = string.IsNullOrWhiteSpace(sourceId) ? throw new ArgumentException("Source is required.", nameof(sourceId)) : sourceId.Trim(); Category = category; }
        public string SourceId { get; }
        public EnvironmentalSourceCategory Category { get; }
        public bool Equals(EnvironmentalSourceKey other) => SourceId == other.SourceId && Category == other.Category;
        public override bool Equals(object? obj) => obj is EnvironmentalSourceKey other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(SourceId) * 397 ^ (int)Category;
    }

    public sealed class EnvironmentalHistoryService
    {
        private readonly Dictionary<EnvironmentalSourceKey, DailyValueHistory> m_histories = new();
        private readonly HashSet<string> m_tracked = new(StringComparer.Ordinal);
        private readonly List<string> m_diagnostics = new();
        private readonly int m_capacityDays;
        private int m_window;
        public EnvironmentalHistoryService(int capacityDays = 360, int averagingWindow = 30)
        { m_capacityDays = Math.Max(1, capacityDays); m_window = Math.Max(1, Math.Min(m_capacityDays, averagingWindow)); }
        public bool Enabled { get; set; }
        public void SetTracked(string sourceId, bool enabled) { if (string.IsNullOrWhiteSpace(sourceId)) return; if (enabled) m_tracked.Add(sourceId.Trim()); else m_tracked.Remove(sourceId.Trim()); }
        public bool IsTracked(string sourceId) => !string.IsNullOrWhiteSpace(sourceId) && m_tracked.Contains(sourceId.Trim());
        public void RecordEmission(string sourceId, EnvironmentalSourceCategory category, double effectiveAmount, long gameDay)
        {
            if (!Enabled || double.IsNaN(effectiveAmount) || double.IsInfinity(effectiveAmount) || effectiveAmount <= 0 || !IsTracked(sourceId)) return;
            var key = new EnvironmentalSourceKey(sourceId, category);
            if (!m_histories.TryGetValue(key, out DailyValueHistory? history)) m_histories.Add(key, history = new DailyValueHistory(m_capacityDays, m_window));
            history.Add(gameDay, effectiveAmount);
        }
        public void RecordEffectiveEmission(string sourceId, EnvironmentalSourceCategory category, double effectiveAmount, long gameDay) =>
            RecordEmission(sourceId, category, effectiveAmount, gameDay);
        public void UpdateRadioactiveInventory(string sourceId, double quantity, double radioactivityPerUnit, long gameDay)
            => RecordEmission(sourceId, EnvironmentalSourceCategory.Radioactivity, Math.Max(0, quantity) * Math.Max(0, radioactivityPerUnit), gameDay);
        public void ReportUnsupportedAttribution(EnvironmentalSourceCategory category, string reason)
        {
            if (m_diagnostics.Count >= 32) return;
            m_diagnostics.Add(category + ": " + (string.IsNullOrWhiteSpace(reason) ? "attribution unavailable" : reason.Trim()));
        }
        public bool TryGetAverage(string sourceId, EnvironmentalSourceCategory category, out double average)
        { if (m_histories.TryGetValue(new EnvironmentalSourceKey(sourceId, category), out DailyValueHistory? history)) { average = history.RollingAverage; return true; } average = 0; return false; }
        public IReadOnlyList<EnvironmentalContribution> Rank(EnvironmentalSourceCategory category, int maximumEntries = 32)
        {
            if (maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            return new ReadOnlyCollection<EnvironmentalContribution>(m_histories
                .Where(pair => pair.Key.Category == category)
                .Select(pair => new EnvironmentalContribution(pair.Key.SourceId, pair.Value.RollingAverage))
                .OrderByDescending(item => item.Average)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .Take(maximumEntries).ToArray());
        }
        public void SetAveragingWindow(int days) { m_window = Math.Max(1, Math.Min(m_capacityDays, days)); foreach (DailyValueHistory h in m_histories.Values) h.SetAveragingWindow(m_window); }
        public void AdvanceToDay(long gameDay) { foreach (DailyValueHistory h in m_histories.Values) h.AdvanceToDay(gameDay); }
        public IReadOnlyDictionary<EnvironmentalSourceKey, DailyValueHistorySnapshot> Snapshot() =>
            new ReadOnlyDictionary<EnvironmentalSourceKey, DailyValueHistorySnapshot>(m_histories.ToDictionary(pair => pair.Key, pair => pair.Value.CreateSnapshot()));
        public IReadOnlyList<string> Diagnostics => new ReadOnlyCollection<string>(m_diagnostics.ToArray());
        public void Clear() { m_histories.Clear(); m_tracked.Clear(); m_diagnostics.Clear(); }
    }

    public sealed class EnvironmentalContribution
    {
        internal EnvironmentalContribution(string sourceId, double average) { SourceId = sourceId; Average = average; }
        public string SourceId { get; }
        public double Average { get; }
    }
}
