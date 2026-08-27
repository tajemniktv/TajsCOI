// Taj's COI Mods | SharedUiFoundationTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Globalization;
using System.Linq;
using TajsCOI.Common.Settings;
using TajsCOI.Common.Ui;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class SharedUiFoundationTests
    {
        [Fact]
        public void NumericEditorAcceptsInvariantAndActiveCultureSeparatorsButRejectsMixedInput()
        {
            SettingDescriptor descriptor = SettingDescriptor.Float(
                "Test",
                "Test",
                "ratio",
                "Ratio",
                "A ratio.",
                1,
                0,
                100,
                0.1,
                valueFormat: SettingValueFormat.Percentage);
            var polish = CultureInfo.GetCultureInfo("pl-PL");

            Assert.True(SettingValueEditorFormatting.TryParse(descriptor, "1,5", polish, out object comma, out _));
            Assert.Equal(1.5, comma);
            Assert.True(SettingValueEditorFormatting.TryParse(descriptor, "1.5", polish, out object point, out _));
            Assert.Equal(1.5, point);
            Assert.False(SettingValueEditorFormatting.TryParse(descriptor, "1,234.56", polish, out _, out _));
            Assert.True(SettingValueEditorFormatting.TryParse(descriptor, "25%", CultureInfo.InvariantCulture, out object percent, out _));
            Assert.Equal(25d, percent);
            SettingDescriptor ordinary = SettingDescriptor.Float(
                "Test",
                "Test",
                "ordinary",
                "Ordinary",
                "An ordinary value.",
                1,
                0,
                100,
                0.1);
            Assert.False(SettingValueEditorFormatting.TryParse(ordinary, "1.5%", CultureInfo.InvariantCulture, out _, out _));
        }

        [Fact]
        public void NumericEditorFormatsPercentagesAndModelPreservesPreviousValueOnFailure()
        {
            SettingDescriptor descriptor = SettingDescriptor.Float(
                "Test",
                "Test",
                "ratio",
                "Ratio",
                "A ratio.",
                1,
                0,
                100,
                0.5,
                valueFormat: SettingValueFormat.Percentage);
            object? applied = null;
            var model = new SettingValueEditorModel(
                descriptor,
                1d,
                value =>
                {
                    applied = value;
                    return SettingSetResult.Accepted(value, SettingApplyMode.RestartGame);
                },
                CultureInfo.GetCultureInfo("pl-PL"));

            Assert.Equal("1%", model.Text);
            model.SetInput("not-a-number");
            Assert.False(model.TryCommit(out SettingSetResult rejected));
            Assert.Equal(SettingValueEditorState.Invalid, model.State);
            Assert.Equal(1d, model.AuthoritativeValue);
            Assert.Null(applied);

            model.SetInput("2,5%");
            Assert.True(model.TryCommit(out SettingSetResult accepted));
            Assert.Equal(2.5d, accepted.Value);
            Assert.Equal(SettingValueEditorState.RequiresRestart, model.State);
            Assert.Equal("2,5%", model.Text);
            model.SetInput("3%");
            model.Revert();
            Assert.Equal("2,5%", model.Text);
            Assert.False(model.IsDirty);

            model.SetAvailable(false, "Requires the optional inspector.");
            Assert.Equal(SettingValueEditorState.Unavailable, model.State);
            Assert.False(model.TryCommit(out SettingSetResult unavailable));
            Assert.Contains("optional inspector", unavailable.Error, StringComparison.OrdinalIgnoreCase);
            model.SetAvailable(true);
            Assert.Equal(SettingValueEditorState.RequiresRestart, model.State);
        }

        [Fact]
        public void DataTableSortsWithStableTieBreaksAndRetainsSelectionAcrossFiltering()
        {
            DataTableColumn<Row> name = DataTableColumn<Row>.CreateText(
                "name",
                "Name",
                row => row.Name,
                width: DataTableColumnWidth.Constrained(80, 240),
                visibilityPriority: 10);
            DataTableColumn<Row> count = DataTableColumn<Row>.Create(
                "count",
                "Count",
                row => row.Count,
                alignment: DataTableColumnAlignment.End,
                width: DataTableColumnWidth.Fixed(40));
            var table = new DataTableModel<Row>(new[] { name, count }, row => row.Id, DataTableSelectionMode.Multiple);
            table.SetRows(
                new[] { new Row("b", "Same", 2), new Row("a", "Same", 2), new Row("c", "Other", 1) });

            Assert.True(table.ToggleSort("count"));
            Assert.Equal(new[] { "c", "a", "b" }, table.GetVisibleRows().Select(row => row.Id));
            Assert.True(table.ToggleSort("count"));
            Assert.Equal(new[] { "a", "b", "c" }, table.GetVisibleRows().Select(row => row.Id));

            Assert.True(table.Select("b"));
            table.SetFilter(row => row.Name == "Same");
            Assert.Equal("2 shown / 3 total", table.CountSummary.ToString());
            Assert.Contains("b", table.SelectedRowIds);
            table.SetRows(new[] { new Row("a", "Same", 3), new Row("b", "Same", 2) });
            Assert.Contains("b", table.SelectedRowIds);
            Assert.DoesNotContain("c", table.SelectedRowIds);
            Assert.Contains("a", table.GetVisibleRows().Select(row => row.Id));

            Assert.Equal(
                new[] { "name" },
                table.GetVisibleColumns(90).Select(column => column.Id));
        }

        private readonly struct Row : IEquatable<Row>
        {
            internal Row(string id, string name, int count)
            {
                Id = id;
                Name = name;
                Count = count;
            }

            internal string Id { get; }
            internal string Name { get; }
            internal int Count { get; }

            public bool Equals(Row other) => Id == other.Id && Name == other.Name && Count == other.Count;
            public override bool Equals(object? obj) => obj is Row other && Equals(other);

            public override int GetHashCode() =>
                (Id?.GetHashCode() ?? 0) * 397 ^ (Name?.GetHashCode() ?? 0) * 17 ^ Count;
        }
    }
}
