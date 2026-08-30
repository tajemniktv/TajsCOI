// Taj's COI Mods | SafeAreaCleanupTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System.Collections.Generic;
using System.Linq;
using TajsCOI.Tweaks.Features.Cleanup;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class SafeAreaCleanupTests
    {
        [Fact]
        public void CleanupRequiresConfirmationAndQuickPolicy()
        {
            Assert.False(SafeAreaCleanupPolicy.TryValidateCommit(false, null, null, out _));
            Assert.True(SafeAreaCleanupPolicy.TryValidateCommit(false, "CONFIRM", null, out _));
            Assert.False(SafeAreaCleanupPolicy.TryValidateCommit(true, "CONFIRM", "NORMAL", out _));
            Assert.True(SafeAreaCleanupPolicy.TryValidateCommit(true, "CONFIRM", "ALLOW-QUICK", out _));
        }

        [Fact]
        public void BoundedSelectionIsDeterministicAndReportsTruncation()
        {
            int[] values = Enumerable.Range(1, 5).ToArray();

            IReadOnlyList<int> result = SafeAreaCleanupPolicy.TakeBounded(values, 3, out bool truncated);

            Assert.Equal(new[] { 1, 2, 3 }, result);
            Assert.True(truncated);
        }

        [Fact]
        public void SelectionEntryStoresOnlyPreviewValues()
        {
            var entry = new SafeAreaSelectionEntry(
                17,
                "Storage",
                new[] { new SafeAreaProductPreview("Product_Wood", 25) });

            Assert.Equal(17, entry.EntityId);
            Assert.Equal("Storage", entry.Title);
            Assert.Single(entry.Products);
            Assert.Equal("Product_Wood", entry.Products[0].ProductId);
            Assert.Equal(25, entry.Products[0].Quantity);
        }
    }
}
