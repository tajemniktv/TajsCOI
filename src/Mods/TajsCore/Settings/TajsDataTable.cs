// Taj's COI Mods | TajsDataTable.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Ui;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using UiButton = Mafi.Unity.UiToolkit.Library.Button;

namespace TajsCOI.Core.Settings
{
    /// <summary>
    ///     Generic table shell backed by <see cref="DataTableModel{TRow}" />.
    ///     Rows are keyed by stable IDs, and refreshes rebind existing row/cell
    ///     controls where possible. The table stores snapshots and callbacks only;
    ///     owners remain responsible for resolving current gameplay objects.
    /// </summary>
    public sealed class TajsDataTable<TRow> : Column
    {
        private sealed class RowVisual : UiButton
        {
            private readonly IReadOnlyList<DataTableColumn<TRow>> m_columns;
            private readonly IReadOnlyList<Label> m_cells;

            internal RowVisual(
                IReadOnlyList<DataTableColumn<TRow>> columns,
                string id,
                Action<string> onClick)
                : base(Area, () => onClick(id))
            {
                m_columns = columns;
                var cells = new List<Label>(columns.Count);
                Row content = new Row(2.pt()).Width(100.Percent()).AlignItemsCenter();
                foreach (DataTableColumn<TRow> column in columns)
                {
                    Label cell = new Label().MinWidth(0.px());
                    ApplyWidth(cell, column.Width);
                    cell.AlignSelf(
                        column.Alignment == DataTableColumnAlignment.Center
                            ? Align.Center
                            : column.Alignment == DataTableColumnAlignment.End
                                ? Align.End
                                : Align.Start);
                    content.Add(cell);
                    cells.Add(cell);
                }
                m_cells = cells;
                Add(content);
            }

            internal void Bind(DataTableRow<TRow> row)
            {
                for (int index = 0; index < m_columns.Count; index++)
                {
                    m_cells[index].Value(m_columns[index].FormatCell(row.Value).AsLoc());
                }
            }

            internal void SetColumnVisibility(IReadOnlyCollection<string> visibleIds)
            {
                for (int index = 0; index < m_columns.Count; index++)
                {
                    m_cells[index].SetVisible(visibleIds.Contains(m_columns[index].Id, StringComparer.Ordinal));
                }
            }
        }

        private readonly DataTableModel<TRow> m_model;
        private readonly Column m_rows;
        private readonly Label m_summary;
        private readonly Label m_empty;
        private readonly Row m_header;
        private readonly List<ButtonText> m_headers = new();
        private readonly Dictionary<string, RowVisual> m_rowVisuals = new(StringComparer.Ordinal);
        private IReadOnlyList<string> m_renderedIds = Array.Empty<string>();
        private readonly Action<IReadOnlyCollection<string>>? m_onSelectionChanged;

        public TajsDataTable(
            DataTableModel<TRow> model,
            Action<IReadOnlyCollection<string>>? onSelectionChanged = null)
            : base(2.pt())
        {
            m_model = model ?? throw new ArgumentNullException(nameof(model));
            m_onSelectionChanged = onSelectionChanged;
            m_header = new Row(2.pt()).Width(100.Percent()).AlignItemsCenter();
            foreach (DataTableColumn<TRow> column in model.Columns)
            {
                var header = new ButtonText(
                    column.Sortable ? UiButton.Area : UiButton.None,
                    HeaderText(column),
                    column.Sortable ? () => ToggleSort(column.Id) : null);
                ApplyWidth(header, column.Width);
                if (!string.IsNullOrWhiteSpace(column.Tooltip))
                {
                    header.Tooltip(column.Tooltip.AsLoc());
                }
                m_header.Add(header);
                m_headers.Add(header);
            }
            m_rows = new Column(1.pt()).AlignItemsStretch();
            m_summary = new Label().FontSize(11);
            m_empty = new Label("No rows match this view.".AsLoc()).FontSize(12).Hide();
            Add(m_header, m_summary, m_empty, m_rows);
            RenderRows();
        }

        public DataTableModel<TRow> Model => m_model;

        public DataTableRefreshResult Refresh(IEnumerable<TRow> rows)
        {
            DataTableRefreshResult result = m_model.SetRows(rows);
            RenderRows(result);
            return result;
        }

        public void SetFilter(Func<TRow, bool>? filter)
        {
            m_model.SetFilter(filter);
            RenderRows();
        }

        public void SetAvailableWidth(float availableWidth)
        {
            IReadOnlyList<DataTableColumn<TRow>> visible = m_model.GetVisibleColumns(availableWidth);
            var visibleIds = new HashSet<string>(visible.Select(column => column.Id), StringComparer.Ordinal);
            for (int index = 0; index < m_headers.Count; index++)
            {
                m_headers[index].SetVisible(visibleIds.Contains(m_model.Columns[index].Id));
            }
            foreach (RowVisual visual in m_rowVisuals.Values)
            {
                visual.SetColumnVisibility(visibleIds);
            }
        }

        public bool SelectRow(string id, bool additive = false)
        {
            bool changed = m_model.Select(id, additive);
            if (changed)
            {
                UpdateSelectionVisuals();
                m_onSelectionChanged?.Invoke(m_model.SelectedRowIds);
            }
            return changed;
        }

        public bool ToggleRowSelection(string id)
        {
            bool changed = m_model.ToggleSelection(id);
            if (changed)
            {
                UpdateSelectionVisuals();
                m_onSelectionChanged?.Invoke(m_model.SelectedRowIds);
            }
            return changed;
        }

        /// <summary>
        ///     Invokes an optional column action for a stable row ID. Action delegates are supplied
        ///     by the consumer and receive the immutable row snapshot; the table never resolves or
        ///     mutates gameplay objects itself.
        /// </summary>
        public bool InvokeRowAction(string columnId, string rowId)
        {
            if (string.IsNullOrWhiteSpace(columnId) ||
                !m_model.TryGetRow(rowId, out DataTableRow<TRow>? row))
            {
                return false;
            }

            DataTableColumn<TRow>? column = m_model.Columns.FirstOrDefault(item =>
                string.Equals(item.Id, columnId, StringComparison.Ordinal));
            if (column?.Action is null)
            {
                return false;
            }

            column.Action(row!.Value);
            return true;
        }

        public override void Clear()
        {
            m_rowVisuals.Clear();
            m_renderedIds = Array.Empty<string>();
            base.Clear();
        }

        private void ToggleSort(string columnId)
        {
            if (m_model.ToggleSort(columnId))
            {
                UpdateHeaderText();
                RenderRows();
            }
        }

        private void RenderRows(DataTableRefreshResult? refresh = null)
        {
            IReadOnlyList<DataTableRow<TRow>> visibleRows = m_model.GetVisibleRows();
            string[] visibleIds = visibleRows.Select(row => row.Id).ToArray();
            bool sameOrder = m_renderedIds.SequenceEqual(visibleIds, StringComparer.Ordinal);
            if (!sameOrder)
            {
                foreach (string id in m_renderedIds)
                {
                    if (!visibleIds.Contains(id, StringComparer.Ordinal) && m_rowVisuals.TryGetValue(id, out RowVisual? removed))
                    {
                        removed.RemoveFromHierarchy();
                        m_rowVisuals.Remove(id);
                    }
                }
            }

            foreach (DataTableRow<TRow> row in visibleRows)
            {
                if (!m_rowVisuals.TryGetValue(row.Id, out RowVisual? visual))
                {
                    visual = new RowVisual(m_model.Columns, row.Id, OnRowClicked);
                    m_rowVisuals.Add(row.Id, visual);
                    m_rows.Add(visual);
                }
                visual.Bind(row);
            }

            if (!sameOrder)
            {
                // Reattach existing visuals in view order. This changes only
                // parent ordering; row and cell controls are reused.
                foreach (DataTableRow<TRow> row in visibleRows)
                {
                    RowVisual visual = m_rowVisuals[row.Id];
                    visual.RemoveFromHierarchy();
                    m_rows.Add(visual);
                }
            }
            m_renderedIds = visibleIds;
            m_summary.Value(m_model.CountSummary.ToString().AsLoc());
            m_empty.SetVisible(visibleRows.Count == 0);
            UpdateSelectionVisuals();
        }

        private void OnRowClicked(string id) => SelectRow(id);

        private void UpdateSelectionVisuals()
        {
            foreach (KeyValuePair<string, RowVisual> pair in m_rowVisuals)
            {
                pair.Value.Selected(m_model.SelectedRowIds.Contains(pair.Key, StringComparer.Ordinal));
            }
        }

        private void UpdateHeaderText()
        {
            for (int index = 0; index < m_model.Columns.Count; index++)
            {
                DataTableColumn<TRow> column = m_model.Columns[index];
                string text = HeaderText(column).Value;
                m_headers[index].Value(text.AsLoc());
            }
        }

        private LocStrFormatted HeaderText(DataTableColumn<TRow> column)
        {
            if (!string.Equals(m_model.SortColumnId, column.Id, StringComparison.Ordinal))
            {
                return column.Header.AsLoc();
            }
            string indicator = m_model.SortDirection == DataTableSortDirection.Descending ? " ▼" : " ▲";
            return (column.Header + indicator).AsLoc();
        }

        private static void ApplyWidth(UiComponent component, DataTableColumnWidth width)
        {
            switch (width.Mode)
            {
                case DataTableColumnWidthMode.Fixed:
                    component.Width(width.Value.px()).MinWidth(width.Minimum.px()).MaxWidth(width.Maximum.px())
                        .FlexGrow(0f).FlexShrink(0f);
                    break;
                case DataTableColumnWidthMode.Constrained:
                    component.MinWidth(width.Minimum.px()).MaxWidth(width.Maximum.px())
                        .FlexGrow(width.Value).FlexShrink(1f);
                    break;
                default:
                    component.FlexGrow(width.Value).FlexShrink(1f).MinWidth(0.px());
                    break;
            }
        }
    }
}
