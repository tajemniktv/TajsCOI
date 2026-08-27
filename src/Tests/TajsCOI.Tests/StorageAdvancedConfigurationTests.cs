// Taj's COI Mods | StorageAdvancedConfigurationTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using TajsCOI.Tweaks.Features.Storage;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class StorageAdvancedConfigurationTests
    {
        [Fact]
        public void AllTransferFieldsIncludeCapacityAndEveryExplicitPolicy()
        {
            Assert.Equal(
                StorageTransferFields.All,
                StorageTransferFields.ProductAssignment |
                StorageTransferFields.LogisticsThresholds |
                StorageTransferFields.ImportExportEnablement |
                StorageTransferFields.TruckPolicy |
                StorageTransferFields.Alerts |
                StorageTransferFields.KeepFullEmpty |
                StorageTransferFields.CapacityOverride);

            string description = TajsStorageAdvancedConfiguration.DescribeFields(StorageTransferFields.All);
            Assert.Contains("product assignment", description);
            Assert.Contains("capacity override", description);
        }

        [Fact]
        public void EmptyTransferSelectionIsExplicitlyReported() => Assert.Equal(
            "nothing",
            TajsStorageAdvancedConfiguration.DescribeFields(StorageTransferFields.None));

        [Fact]
        public void CapacityOverrideStateCanBeSetAndClearedWithoutInventoryData()
        {
            const int entityId = 7031;
            TajsStorageAdvancedState.Clear();

            Assert.Null(TajsStorageAdvancedState.GetCapacityOverride(entityId));
            TajsStorageAdvancedState.SetCapacityOverride(entityId, 12345);
            Assert.Equal(12345, TajsStorageAdvancedState.GetCapacityOverride(entityId));
            TajsStorageAdvancedState.ClearCapacityOverride(entityId);
            Assert.Null(TajsStorageAdvancedState.GetCapacityOverride(entityId));
        }
    }
}
