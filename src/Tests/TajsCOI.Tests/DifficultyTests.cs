// Taj's COI Mods | DifficultyTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Game;
using TajsCOI.Tweaks.Features.Difficulty;
using Xunit;
using XAssert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class DifficultyTests
    {
        [Fact]
        public void CatalogCoversEveryRequestedPercentDomainWithBoundedRanges()
        {
            string[] requested =
            {
                "ExtraContractsProfit",
                "TreesGrowthDiff",
                "ExtraStartingMaterial",
                "MaintenanceDiff",
                "FuelConsumptionDiff",
                "RainYieldDiff",
                "BaseHealthDiff",
                "ResourceMiningDiff",
                "SettlementConsumptionDiff",
                "SettlementFoodConsumptionDiff",
                "WorldMinesReservesDiff",
                "FarmsYieldDiff",
                "UnityProductionDiff",
                "SolarPowerDiff",
                "ConstructionCostsDiff",
                "ResearchCostDiff",
                "DiseaseMortalityDiff",
                "PollutionDiff",
                "QuickActionsCostDiff",
            };

            foreach (string memberName in requested)
            {
                TajsDifficultyDefinition definition = XAssert.Single(
                    TajsDifficultyOptionCatalog.Definitions,
                    value => value.MemberName == memberName);
                XAssert.NotNull(definition.Range);
                XAssert.InRange(definition.Range!.Minimum, -100, 0);
                XAssert.InRange(definition.Range.Maximum, 0, 2000);
                XAssert.True(definition.Range.Step > 0);
            }
        }

        [Fact]
        public void ExtendedRangesRetainVanillaPresetValues()
        {
            string[][] required =
            {
                new[] { "MaintenanceDiff", "-75", "-50", "-25", "0", "25", "50" },
                new[] { "FuelConsumptionDiff", "-30", "-15", "0", "15", "30" },
                new[] { "FarmsYieldDiff", "-50", "-25", "0", "25", "50" },
                new[] { "SolarPowerDiff", "-25", "0", "25", "50", "100" },
            };

            foreach (string[] requirement in required)
            {
                TajsDifficultyRange range = TajsDifficultyOptionCatalog.FindRange(requirement[0])!;
                int[] values = range.Values().ToArray();
                foreach (string expected in requirement.Skip(1))
                {
                    XAssert.Contains(int.Parse(expected), values);
                }
            }
        }

        [Fact]
        public void CatalogIncludesNativeEnumDifficultyDomainsAndConservativeExtremes()
        {
            XAssert.Contains(TajsDifficultyOptionCatalog.Definitions, value => value.MemberName == "TrainsNoFuel");
            XAssert.Contains(TajsDifficultyOptionCatalog.Definitions, value => value.MemberName == "Sandbox");
            XAssert.DoesNotContain(
                TajsDifficultyOptionCatalog.Definitions.Where(value => value.Range is not null),
                value => value.Range!.Maximum > 2000);
        }

        [Fact]
        public void UnknownPercentDomainsFailClosed()
        {
            XAssert.Null(TajsDifficultyOptionCatalog.FindRange("FuturePercentDifficulty"));

            TajsDifficultyDefinition discovered = TajsDifficultyOptionCatalog.CreateDiscovered(
                "FuturePercentDifficulty",
                typeof(Percent));
            XAssert.Equal(TajsDifficultyApplyMode.Unsupported, discovered.ApplyMode);
            XAssert.Null(discovered.Range);
        }

        [Fact]
        public void NativePercentOptionsRemainAvailableAfterExtension()
        {
            int[] values = TajsDifficultyOptionCatalog
                .BuildExtendedOptions(
                    new[] { -75.Percent(), -50.Percent(), -25.Percent(), 0.Percent(), 25.Percent(), 50.Percent() },
                    TajsDifficultyOptionCatalog.FindRange("MaintenanceDiff")!,
                    includeUnlimited: false)
                .Select(value => value.ToIntPercentRounded())
                .ToArray();

            XAssert.Contains(-75, values);
            XAssert.Contains(-50, values);
            XAssert.Contains(-25, values);
            XAssert.Contains(0, values);
            XAssert.Contains(25, values);
            XAssert.Contains(50, values);
        }

        [Fact]
        public void WorldMineExtensionRetainsUnlimitedSentinel()
        {
            Percent[] values = TajsDifficultyOptionCatalog.BuildExtendedOptions(
                new[] { -100.Percent(), 0.Percent(), 100.Percent() },
                TajsDifficultyOptionCatalog.FindRange("WorldMinesReservesDiff")!,
                includeUnlimited: true);

            XAssert.Contains(Percent.MaxValue, values);
            XAssert.Contains(-100.Percent(), values);
            XAssert.Contains(100.Percent(), values);
        }

        [Fact]
        public void SaveIdentitySeparatesNamesAndNativeFileMetadata()
        {
            DateTime timestamp = new(2026, 8, 27, 12, 30, 0, DateTimeKind.Utc);
            TajsDifficultySaveIdentity first = TajsDifficultySaveIdentity.FromSaveFile(
                new SaveFileInfo("slot/a", "world", timestamp, 100));
            TajsDifficultySaveIdentity sanitizedCollision = TajsDifficultySaveIdentity.FromSaveFile(
                new SaveFileInfo("slot:a", "world", timestamp, 100));
            TajsDifficultySaveIdentity replaced = TajsDifficultySaveIdentity.FromSaveFile(
                new SaveFileInfo("slot/a", "world", timestamp, 101));

            XAssert.NotEqual(first.Fingerprint, sanitizedCollision.Fingerprint);
            XAssert.NotEqual(first.Fingerprint, replaced.Fingerprint);
        }

        [Fact]
        public void CorruptSidecarIsNotOverwrittenOrTrusted()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-DifficultyTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                TajsDifficultySaveIdentity identity = TajsDifficultySaveIdentity.FromSaveFile(
                    new SaveFileInfo("slot", "world", DateTime.UtcNow, 100));
                GameDifficultyConfig current = (GameDifficultyConfig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GameDifficultyConfig));
                var properties = new Dictionary<string, System.Reflection.PropertyInfo>(StringComparer.Ordinal);
                var store = new TajsDifficultyStateStore(root);
                store.LoadOrCapture(identity, "world", current, properties);

                string path = Path.Combine(root, identity.Fingerprint, "state.txt");
                File.WriteAllText(path, "not a valid sidecar");
                var reloaded = new TajsDifficultyStateStore(root);
                reloaded.LoadOrCapture(identity, "world", current, properties);

                XAssert.False(reloaded.IsBaselineAvailable);
                XAssert.Equal("not a valid sidecar", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void V1SidecarIsPreservedAndNotMigratedByGuessing()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-DifficultyTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string legacy = Path.Combine(root, "world", "state.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
                File.WriteAllText(legacy, "TajsTweaksDifficultyV1\nMaintenanceDiff=25");

                TajsDifficultySaveIdentity identity = TajsDifficultySaveIdentity.FromSaveFile(
                    new SaveFileInfo("slot", "world", DateTime.UtcNow, 100));
                var store = new TajsDifficultyStateStore(root);
                store.LoadOrCapture(
                    identity,
                    "world",
                    (GameDifficultyConfig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GameDifficultyConfig)),
                    new Dictionary<string, System.Reflection.PropertyInfo>(StringComparer.Ordinal));

                XAssert.False(store.IsBaselineAvailable);
                XAssert.Equal("TajsTweaksDifficultyV1\nMaintenanceDiff=25", File.ReadAllText(legacy));
                XAssert.False(File.Exists(Path.Combine(root, identity.Fingerprint, "state.txt")));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void SuccessfulSaveRebindsBaselineWithoutDeletingPreviousSidecar()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-DifficultyTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string firstPath = Path.Combine(root, "slot.save");
                Directory.CreateDirectory(root);
                File.WriteAllBytes(firstPath, new byte[] { 1, 2, 3 });
                DateTime firstWrite = File.GetLastWriteTimeUtc(firstPath);
                TajsDifficultySaveIdentity firstIdentity = TajsDifficultySaveIdentity.FromSaveFile(
                    new SaveFileInfo("slot", "world", firstWrite, 3));
                var store = new TajsDifficultyStateStore(Path.Combine(root, "sidecars"));
                GameDifficultyConfig current = (GameDifficultyConfig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GameDifficultyConfig));
                store.LoadOrCapture(
                    firstIdentity,
                    "world",
                    current,
                    new Dictionary<string, System.Reflection.PropertyInfo>(StringComparer.Ordinal));

                string secondPath = Path.Combine(root, "slot-copy.save");
                File.WriteAllBytes(secondPath, new byte[] { 1, 2, 3, 4 });
                XAssert.True(store.RebindAfterSave(secondPath, "world"));

                XAssert.True(File.Exists(Path.Combine(root, "sidecars", firstIdentity.Fingerprint, "state.txt")));
                XAssert.NotEqual(firstIdentity.Fingerprint, store.IdentityFingerprint);
                XAssert.True(File.Exists(Path.Combine(root, "sidecars", store.IdentityFingerprint!, "state.txt")));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void StateStoreRoundTripsPercentAndEnumScalars()
        {
            XAssert.True(TajsDifficultyStateStore.TryEncode(25.Percent(), out string encodedPercent));
            XAssert.True(TajsDifficultyStateStore.TryDecode(encodedPercent, typeof(Percent), out object? decodedPercent));
            XAssert.Equal(25, ((Percent)decodedPercent!).ToIntPercentRounded());

            XAssert.True(TajsDifficultyStateStore.TryEncode(GameDifficultyConfig.TrainsNoFuelSetting.Stop, out string encodedEnum));
            XAssert.True(
                TajsDifficultyStateStore.TryDecode(
                    encodedEnum,
                    typeof(GameDifficultyConfig.TrainsNoFuelSetting),
                    out object? decodedEnum));
            XAssert.Equal(GameDifficultyConfig.TrainsNoFuelSetting.Stop, decodedEnum);
        }

        [Fact]
        public void StateStoreUsesExplicitUnlimitedMarker()
        {
            XAssert.True(TajsDifficultyStateStore.TryEncode(Percent.MaxValue, out string encoded));
            XAssert.Equal("unlimited", encoded);
            XAssert.True(TajsDifficultyStateStore.TryDecode(encoded, typeof(Percent), out object? decoded));
            XAssert.Equal(Percent.MaxValue, decoded);
        }
    }
}
