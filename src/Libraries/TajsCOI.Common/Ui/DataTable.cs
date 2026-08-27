// Taj's COI Mods | DataTable.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TajsCOI.Common.Ui
{
    public enum DataTableColumnAlignment
    {
        Start,
        Center,
        End,
    }

    public enum DataTableColumnWidthMode
    {
        Fixed,
        Flex,
        Constrained,
    }

    public readonly struct DataTableColumnWidth
    {
        private DataTableColumnWidth(DataTableColumnWidthMode mode, float value, float minimum, float maximum)
        {
            Mode = mode;
            Value = value;
            Minimum = minimum;
            Maximum = maximum;
        }

        public DataTableColumnWidthMode Mode { get; }
        public float Value { get; }
        public float Minimum { get; }
        public float Maximum { get; }

        public static DataTableColumnWidth Fixed(float pixels)
        {
            RequireFiniteNonNegative(pixels, nameof(pixels));
            return new DataTableColumnWidth(DataTableColumnWidthMode.Fixed, pixels, pixels, pixels);
        }

        public static DataTableColumnWidth Flex(float weight = 1f)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), "Flex weight must be positive and finite.");
            }
            return new DataTableColumnWidth(DataTableColumnWidthMode.Flex, weight, 0f, float.PositiveInfinity);
        }

        public static DataTableColumnWidth Constrained(float minimum, float maximum, float flexWeight = 1f)
        {
            RequireFiniteNonNegative(minimum, nameof(minimum));
            RequireFiniteNonNegative(maximum, nameof(maximum));
            if (minimum > maximum)
            {
                throw new ArgumentException("Column minimum cannot exceed its maximum.", nameof(minimum));
            }
            if (float.IsNaN(flexWeight) || float.IsInfinity(flexWeight) || flexWeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(flexWeight), "Flex weight must be positive and finite.");
            }
            return new DataTableColumnWidth(DataTableColumnWidthMode.Constrained, flexWeight, minimum, maximum);
        }

        private static void RequireFiniteNonNegative(float value, string parameter)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameter, "Column width must be finite and non-negative.");
            }
        }
    }

    public enum DataTableSortDirection
    {
        None,
        Ascending,
        Descending,
    }

    public enum DataTableSelectionMode
    {
        None,
        Single,
        Multiple,
    }

    public sealed class DataTableColumn<TRow>
    {
        private DataTableColumn(
            string id,
            string header,
            string tooltip,
            DataTableColumnWidth width,
            DataTableColumnAlignment alignment,
            bool sortable,
            int visibilityPriority,
            Func<TRow, string> formatCell,
            Comparison<TRow>? compareRows,
            Action<TRow>? action)
        {
            Id = RequireText(id, nameof(id));
            Header = RequireText(header, nameof(header));
            Tooltip = tooltip?.Trim() ?? string.Empty;
            Width = width;
            Alignment = alignment;
            Sortable = sortable;
            VisibilityPriority = visibilityPriority;
            FormatCell = formatCell ?? throw new ArgumentNullException(nameof(formatCell));
            CompareRows = compareRows;
            Action = action;
            if (sortable && compareRows is null)
            {
                throw new ArgumentException("Sortable columns require a row comparer.", nameof(compareRows));
            }
        }

        public string Id { get; }
        public string Header { get; }
        public string Tooltip { get; }
        public DataTableColumnWidth Width { get; }
        public DataTableColumnAlignment Alignment { get; }
        public bool Sortable { get; }
        public int VisibilityPriority { get; }
        public Func<TRow, string> FormatCell { get; }
        internal Comparison<TRow>? CompareRows { get; }
        public Action<TRow>? Action { get; }

        public static DataTableColumn<TRow> Create<TValue>(
            string id,
            string header,
            Func<TRow, TValue> valueSelector,
            Func<TValue, string>? formatCell = null,
            IComparer<TValue>? comparer = null,
            string tooltip = "",
            DataTableColumnWidth? width = null,
            DataTableColumnAlignment alignment = DataTableColumnAlignment.Start,
            bool sortable = true,
            int visibilityPriority = 0,
            Action<TRow>? action = null)
        {
            if (valueSelector is null)
            {
                throw new ArgumentNullException(nameof(valueSelector));
            }
            IComparer<TValue> valueComparer = comparer ?? Comparer<TValue>.Default;
            Func<TValue, string> formatter = formatCell ?? (value => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty);
            return new DataTableColumn<TRow>(
                id,
                header,
                tooltip,
                width ?? DataTableColumnWidth.Flex(),
                alignment,
                sortable,
                visibilityPriority,
                row => formatter(valueSelector(row)),
                sortable
                    ? (left, right) => valueComparer.Compare(valueSelector(left), valueSelector(right))
                    : null,
                action);
        }

        public static DataTableColumn<TRow> CreateText(
            string id,
            string header,
            Func<TRow, string> valueSelector,
            string tooltip = "",
            DataTableColumnWidth? width = null,
            DataTableColumnAlignment alignment = DataTableColumnAlignment.Start,
            bool sortable = true,
            int visibilityPriority = 0,
            StringComparer? comparer = null,
            Action<TRow>? action = null)
        {
            return Create(
                id,
                header,
                valueSelector,
                value => value,
                comparer ?? StringComparer.OrdinalIgnoreCase,
                tooltip,
                width,
                alignment,
                sortable,
                visibilityPriority,
                action: action);
        }

        private static string RequireText(string value, string parameter) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Table metadata cannot be empty.", parameter)
                : value.Trim();
    }

    public sealed class DataTableRow<TRow>
    {
        public DataTableRow(string id, TRow value)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Row IDs cannot be empty.", nameof(id));
            }
            Id = id.Trim();
            Value = value;
        }

        public string Id { get; }
        public TRow Value { get; }
    }

    public sealed class DataTableCountSummary
    {
        public DataTableCountSummary(int shown, int total)
        {
            if (shown < 0 || total < 0 || shown > total)
            {
                throw new ArgumentOutOfRangeException(nameof(shown), "Shown count must be between zero and total.");
            }
            Shown = shown;
            Total = total;
        }

        public int Shown { get; }
        public int Total { get; }
        public bool IsFiltered => Shown != Total;

        public override string ToString() =>
            $"{Shown.ToString(CultureInfo.InvariantCulture)} shown / {Total.ToString(CultureInfo.InvariantCulture)} total";
    }

    public sealed class DataTableRefreshResult
    {
        internal DataTableRefreshResult(
            IReadOnlyList<string> added,
            IReadOnlyList<string> updated,
            IReadOnlyList<string> removed,
            bool viewChanged)
        {
            AddedRowIds = added;
            UpdatedRowIds = updated;
            RemovedRowIds = removed;
            ViewChanged = viewChanged;
        }

        public IReadOnlyList<string> AddedRowIds { get; }
        public IReadOnlyList<string> UpdatedRowIds { get; }
        public IReadOnlyList<string> RemovedRowIds { get; }
        public bool ViewChanged { get; }
    }

    /// <summary>
    /// A source-independent table view model. It stores only row snapshots
    /// supplied by the owning view and stable row IDs, so sorting/filtering and
    /// selection never mutate an authoritative gameplay collection.
    /// </summary>
    public sealed class DataTableModel<TRow>
    {
        private readonly IReadOnlyList<DataTableColumn<TRow>> m_columns;
        private readonly Dictionary<string, DataTableColumn<TRow>> m_columnsById;
        private readonly Func<TRow, string> m_rowIdSelector;
        private readonly Dictionary<string, TRow> m_rows = new(StringComparer.Ordinal);
        private readonly List<string> m_order = new();
        private readonly HashSet<string> m_selected = new(StringComparer.Ordinal);
        private IReadOnlyList<DataTableRow<TRow>> m_visibleRows = Array.Empty<DataTableRow<TRow>>();
        private Func<TRow, bool>? m_filter;
        private string? m_sortColumnId;
        private DataTableSortDirection m_sortDirection;
        private bool m_viewDirty = true;

        public DataTableModel(
            IEnumerable<DataTableColumn<TRow>> columns,
            Func<TRow, string> rowIdSelector,
            DataTableSelectionMode selectionMode = DataTableSelectionMode.None)
        {
            if (columns is null)
            {
                throw new ArgumentNullException(nameof(columns));
            }
            m_columns = columns.ToArray();
            if (m_columns.Count == 0)
            {
                throw new ArgumentException("A table requires at least one column.", nameof(columns));
            }
            m_columnsById = new Dictionary<string, DataTableColumn<TRow>>(StringComparer.Ordinal);
            foreach (DataTableColumn<TRow> column in m_columns)
            {
                if (m_columnsById.ContainsKey(column.Id))
                {
                    throw new ArgumentException($"Column '{column.Id}' is declared more than once.", nameof(columns));
                }
                m_columnsById.Add(column.Id, column);
            }
            m_rowIdSelector = rowIdSelector ?? throw new ArgumentNullException(nameof(rowIdSelector));
            SelectionMode = selectionMode;
        }

        public IReadOnlyList<DataTableColumn<TRow>> Columns => m_columns;
        public DataTableSelectionMode SelectionMode { get; }
        public string? SortColumnId => m_sortColumnId;
        public DataTableSortDirection SortDirection => m_sortDirection;
        public int TotalCount => m_rows.Count;
        public int VisibleCount => GetVisibleRows().Count;
        public DataTableCountSummary CountSummary => new(VisibleCount, TotalCount);
        public IReadOnlyCollection<string> SelectedRowIds => m_selected.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        /// <summary>
        /// Chooses the highest-priority columns that fit the available width. The original
        /// declaration order is retained for rendering, so narrow views collapse lower
        /// priority columns instead of forcing a horizontal escape.
        /// </summary>
        public IReadOnlyList<DataTableColumn<TRow>> GetVisibleColumns(float availableWidth)
        {
            if (float.IsNaN(availableWidth) || float.IsInfinity(availableWidth) || availableWidth < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(availableWidth));
            }

            var selected = new HashSet<string>(StringComparer.Ordinal);
            float used = 0f;
            foreach (DataTableColumn<TRow> column in m_columns
                         .OrderByDescending(item => item.VisibilityPriority)
                         .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                float minimum = column.Width.Minimum;
                if (selected.Count == 0 || used + minimum <= availableWidth)
                {
                    selected.Add(column.Id);
                    used += minimum;
                }
            }

            return m_columns.Where(column => selected.Contains(column.Id)).ToArray();
        }

        public DataTableRefreshResult SetRows(IEnumerable<TRow> rows)
        {
            if (rows is null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            var incoming = new Dictionary<string, TRow>(StringComparer.Ordinal);
            var incomingOrder = new List<string>();
            foreach (TRow row in rows)
            {
                string rawId = m_rowIdSelector(row);
                string id = rawId?.Trim() ?? string.Empty;
                if (id.Length == 0)
                {
                    throw new ArgumentException("The row ID selector returned an empty ID.", nameof(rows));
                }
                if (incoming.ContainsKey(id))
                {
                    throw new ArgumentException($"Row ID '{id}' is not unique.", nameof(rows));
                }
                incoming.Add(id, row);
                incomingOrder.Add(id);
            }

            var added = incoming.Keys.Where(id => !m_rows.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var updated = incoming.Keys
                .Where(id => m_rows.ContainsKey(id) && !EqualityComparer<TRow>.Default.Equals(m_rows[id], incoming[id]))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var removed = m_rows.Keys.Where(id => !incoming.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            bool orderChanged = !m_order.SequenceEqual(incomingOrder, StringComparer.Ordinal);

            m_rows.Clear();
            foreach (KeyValuePair<string, TRow> pair in incoming)
            {
                m_rows.Add(pair.Key, pair.Value);
            }
            m_order.Clear();
            m_order.AddRange(incomingOrder);
            m_selected.RemoveWhere(id => !m_rows.ContainsKey(id));
            m_viewDirty = true;
            return new DataTableRefreshResult(
                added,
                updated,
                removed,
                orderChanged || added.Length != 0 || updated.Length != 0 || removed.Length != 0);
        }

        public IReadOnlyList<DataTableRow<TRow>> GetVisibleRows()
        {
            if (!m_viewDirty)
            {
                return m_visibleRows;
            }

            IEnumerable<DataTableRow<TRow>> query = m_order
                .Where(id => m_filter is null || m_filter(m_rows[id]))
                .Select(id => new DataTableRow<TRow>(id, m_rows[id]));
            if (m_sortColumnId is not null && m_sortDirection != DataTableSortDirection.None)
            {
                DataTableColumn<TRow> column = m_columnsById[m_sortColumnId];
                query = query.OrderBy(row => row.Value, ComparerFor(column));
            }
            m_visibleRows = query.ToArray();
            m_viewDirty = false;
            return m_visibleRows;
        }

        public bool TryGetRow(string id, out DataTableRow<TRow>? row)
        {
            if (id is not null && m_rows.TryGetValue(id, out TRow value))
            {
                row = new DataTableRow<TRow>(id, value);
                return true;
            }
            row = null;
            return false;
        }

        public void SetFilter(Func<TRow, bool>? filter)
        {
            m_filter = filter;
            m_viewDirty = true;
        }

        public bool ToggleSort(string columnId)
        {
            if (string.IsNullOrWhiteSpace(columnId) || !m_columnsById.TryGetValue(columnId, out DataTableColumn<TRow>? column) || !column.Sortable)
            {
                return false;
            }

            if (string.Equals(m_sortColumnId, column.Id, StringComparison.Ordinal))
            {
                m_sortDirection = m_sortDirection == DataTableSortDirection.Ascending
                    ? DataTableSortDirection.Descending
                    : DataTableSortDirection.Ascending;
            }
            else
            {
                m_sortColumnId = column.Id;
                m_sortDirection = DataTableSortDirection.Ascending;
            }
            m_viewDirty = true;
            return true;
        }

        public void ClearSort()
        {
            m_sortColumnId = null;
            m_sortDirection = DataTableSortDirection.None;
            m_viewDirty = true;
        }

        public bool Select(string id, bool additive = false)
        {
            if (SelectionMode == DataTableSelectionMode.None || !m_rows.ContainsKey(id))
            {
                return false;
            }
            if (SelectionMode == DataTableSelectionMode.Single || !additive)
            {
                m_selected.Clear();
            }
            return m_selected.Add(id);
        }

        public bool ToggleSelection(string id)
        {
            if (SelectionMode == DataTableSelectionMode.None || !m_rows.ContainsKey(id))
            {
                return false;
            }
            if (m_selected.Remove(id))
            {
                return true;
            }
            return Select(id, additive: true);
        }

        public void ClearSelection() => m_selected.Clear();

        private IComparer<TRow> ComparerFor(DataTableColumn<TRow> column)
        {
            Comparison<TRow> comparison = column.CompareRows!;
            int direction = m_sortDirection == DataTableSortDirection.Descending ? -1 : 1;
            return Comparer<TRow>.Create((left, right) =>
            {
                int result = comparison(left, right) * direction;
                if (result != 0)
                {
                    return result;
                }
                string leftId = m_rowIdSelector(left) ?? string.Empty;
                string rightId = m_rowIdSelector(right) ?? string.Empty;
                return StringComparer.Ordinal.Compare(leftId, rightId);
            });
        }
    }
}
