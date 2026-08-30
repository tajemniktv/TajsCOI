// Taj's COI Mods | UndoRecorder.cs
// Copyright (C) 2026 Grzegorz Kaczmarski (TajemnikTV)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace TajsCOI.Core.Undo
{
    public enum UndoActionKind { Placement, Removal, BlueprintPlacement }

    public readonly struct UndoTransform
    {
        public UndoTransform(double x, double y, double z, double yaw = 0)
        { X = x; Y = y; Z = z; Yaw = yaw; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Yaw { get; }
    }

    public sealed class UndoEntitySnapshot
    {
        public UndoEntitySnapshot(string entityId, string prototypeId, UndoTransform transform,
            IDictionary<string, string>? configuration = null)
        {
            EntityId = Require(entityId, nameof(entityId));
            PrototypeId = Require(prototypeId, nameof(prototypeId));
            Transform = transform;
            Configuration = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(configuration ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        }
        public string EntityId { get; }
        public string PrototypeId { get; }
        public UndoTransform Transform { get; }
        public IReadOnlyDictionary<string, string> Configuration { get; }
        private static string Require(string value, string parameter) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Undo identifiers cannot be empty.", parameter) : value.Trim();
    }

    public sealed class UndoRecord
    {
        internal UndoRecord(UndoActionKind kind, string description, IEnumerable<UndoEntitySnapshot> entities)
        {
            Kind = kind;
            Description = string.IsNullOrWhiteSpace(description) ? kind.ToString() : description.Trim();
            Entities = new ReadOnlyCollection<UndoEntitySnapshot>((entities ?? Enumerable.Empty<UndoEntitySnapshot>()).ToArray());
            EstimatedBytes = Entities.Sum(entity => 128 + entity.EntityId.Length * 2 + entity.PrototypeId.Length * 2 + entity.Configuration.Sum(pair => (pair.Key.Length + pair.Value.Length) * 2 + 16));
        }
        public UndoActionKind Kind { get; }
        public string Description { get; }
        public IReadOnlyList<UndoEntitySnapshot> Entities { get; }
        internal int EstimatedBytes { get; }
    }

    public interface IUndoValidator
    {
        bool CanUndo(UndoRecord record, out string reason);
    }

    public interface IUndoCommandScheduler
    {
        void ScheduleUndo(UndoRecord record);
    }

    public interface IUndoActionScope : IDisposable
    {
        void Complete();
        void Cancel();
    }

    /// <summary>Bounded outer-action recorder. Nested construction calls are folded into one record.</summary>
    public sealed class UndoRecorder
    {
        private readonly LinkedList<UndoRecord> m_history = new();
        private readonly int m_maxRecords;
        private readonly int m_maxEntitiesPerRecord;
        private readonly int m_maxConfigurationValuesPerEntity;
        private readonly int m_maxHistoryBytes;
        private int m_historyBytes;
        private ActionBuilder? m_active;

        public UndoRecorder(int maxRecords = 20, int maxEntitiesPerRecord = 4096,
            int maxConfigurationValuesPerEntity = 128, int maxHistoryBytes = 4 * 1024 * 1024)
        {
            if (maxRecords < 1) throw new ArgumentOutOfRangeException(nameof(maxRecords));
            if (maxEntitiesPerRecord < 1) throw new ArgumentOutOfRangeException(nameof(maxEntitiesPerRecord));
            if (maxConfigurationValuesPerEntity < 0) throw new ArgumentOutOfRangeException(nameof(maxConfigurationValuesPerEntity));
            if (maxHistoryBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maxHistoryBytes));
            m_maxRecords = maxRecords;
            m_maxEntitiesPerRecord = maxEntitiesPerRecord;
            m_maxConfigurationValuesPerEntity = maxConfigurationValuesPerEntity;
            m_maxHistoryBytes = maxHistoryBytes;
        }
        public int Count => m_history.Count;
        public int MaxRecords => m_maxRecords;
        public IReadOnlyList<UndoRecord> Snapshot() => new ReadOnlyCollection<UndoRecord>(m_history.ToArray());

        public IUndoActionScope BeginAction(UndoActionKind kind, string description)
        {
            if (m_active is not null) throw new InvalidOperationException("An outer undo action is already active.");
            m_active = new ActionBuilder(kind, description, kind == UndoActionKind.Removal);
            return new Scope(this);
        }

        public bool Record(UndoEntitySnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            if (m_active is null) return false;
            if (m_active.Invalid || m_active.Entities.Count >= m_maxEntitiesPerRecord || snapshot.Configuration.Count > m_maxConfigurationValuesPerEntity)
            {
                m_active.Invalid = true;
                return false;
            }
            m_active.EstimatedBytes += EstimateBytes(snapshot);
            if (m_active.EstimatedBytes > m_maxHistoryBytes)
            {
                m_active.Invalid = true;
                return false;
            }
            m_active.Entities.Add(snapshot);
            return true;
        }

        public bool Commit()
        {
            if (m_active is null) return false;
            ActionBuilder builder = m_active;
            m_active = null;
            if (builder.Invalid || builder.Entities.Count == 0) return false;
            var record = new UndoRecord(builder.Kind, builder.Description, builder.Entities);
            m_history.AddLast(record);
            m_historyBytes += record.EstimatedBytes;
            while (m_history.Count > m_maxRecords || m_historyBytes > m_maxHistoryBytes)
            {
                UndoRecord oldest = m_history.First!.Value;
                m_historyBytes -= oldest.EstimatedBytes;
                m_history.RemoveFirst();
            }
            return true;
        }

        public void Cancel() => m_active = null;

        public bool TryUndo(IUndoValidator validator, IUndoCommandScheduler scheduler, out string message)
        {
            if (validator is null) throw new ArgumentNullException(nameof(validator));
            if (scheduler is null) throw new ArgumentNullException(nameof(scheduler));
            LinkedListNode<UndoRecord>? node = m_history.Last;
            if (node is null) { message = "Nothing to undo."; return false; }
            UndoRecord record = node.Value;
            if (!validator.CanUndo(record, out string reason)) { message = string.IsNullOrWhiteSpace(reason) ? "Undo is not currently safe." : reason; return false; }
            try { scheduler.ScheduleUndo(record); }
            catch (Exception ex) { message = "Undo could not be scheduled: " + ex.Message; return false; }
            m_history.Remove(node);
            m_historyBytes -= record.EstimatedBytes;
            message = "Undo scheduled: " + record.Description;
            return true;
        }

        public void Clear() { m_history.Clear(); m_historyBytes = 0; m_active = null; }
        public void OnSceneChanged() => Clear();

        private static int EstimateBytes(UndoEntitySnapshot snapshot)
        {
            int bytes = 128 + snapshot.EntityId.Length * 2 + snapshot.PrototypeId.Length * 2;
            foreach (KeyValuePair<string, string> pair in snapshot.Configuration)
                bytes = checked(bytes + (pair.Key.Length + pair.Value.Length) * 2 + 16);
            return bytes;
        }

        private sealed class ActionBuilder
        {
            internal ActionBuilder(UndoActionKind kind, string description, bool invalid) { Kind = kind; Description = description; Invalid = invalid; }
            internal UndoActionKind Kind { get; }
            internal string Description { get; }
            internal bool Invalid { get; set; }
            internal int EstimatedBytes { get; set; }
            internal List<UndoEntitySnapshot> Entities { get; } = new();
        }
        private sealed class Scope : IUndoActionScope
        {
            private UndoRecorder? m_owner;
            private bool m_completed;
            internal Scope(UndoRecorder owner) => m_owner = owner;
            public void Complete() { if (m_owner is null || m_completed) return; m_owner.Commit(); m_completed = true; m_owner = null; }
            public void Cancel() { if (m_owner is null) return; m_owner.Cancel(); m_completed = true; m_owner = null; }
            public void Dispose() { if (m_owner is null) return; m_owner.Cancel(); m_completed = true; m_owner = null; }
        }
    }
}
