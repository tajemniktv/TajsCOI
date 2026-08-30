// Taj's COI Mods | Wave8ContractsTests.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TajsCOI.Core.Blueprints;
using TajsCOI.Core.Flow;
using TajsCOI.Core.Production;
using TajsCOI.Core.Undo;
using TajsCOI.Profiler.Core;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class Wave8ContractsTests
    {
        [Fact]
        public void ConfiguredBlueprintRecipesNormalizeNetFlowsAndKeepPollutionSeparate()
        {
            var recipes = new Dictionary<string, ProductionRecipe>
            {
                ["smelt"] = new ProductionRecipe("smelt", 60, new[] { new RecipeFlow("ore", 2) }, new[] { new RecipeFlow("ingot", 1), new RecipeFlow("virtual-progress", 1, ProductClassification.Virtual), new RecipeFlow("air", 1, ProductClassification.Pollution) }),
                ["roll"] = new ProductionRecipe("roll", 60, new[] { new RecipeFlow("ingot", 1) }, new[] { new RecipeFlow("plate", 1) }),
            };
            ProductionSummary summary = ProductionRecipeNormalizer.Normalize(
                new[] { new ConfiguredBlueprintEntity("a", "smelter", "smelt", 2), new ConfiguredBlueprintEntity("b", "roller", "roll") }, recipes);

            Assert.Equal(FixedRate.FromDouble(4), summary.NetInputs["ore"]);
            Assert.Equal(FixedRate.FromDouble(1), summary.NetOutputs["ingot"]);
            Assert.Equal(FixedRate.FromDouble(1), summary.NetOutputs["plate"]);
            Assert.Equal(FixedRate.FromDouble(2), summary.Pollution["air"]);
            Assert.DoesNotContain("virtual-progress", summary.NetOutputs.Keys);
            ConfiguredBlueprintEntity configured = ConfiguredBlueprintEntity.FromConfiguration("c", "smelter", new Dictionary<string, string> { ["recipe_id"] = "smelt" });
            Assert.Equal("smelt", configured.RecipeId);
            ProductionSummary fallback = ProductionRecipeNormalizer.Normalize(new[] { new ConfiguredBlueprintEntity("missing", "smelter", "removed", fallbackRecipeId: "smelt") }, recipes);
            Assert.True(fallback.FallbackUsed);
            Assert.NotEmpty(fallback.Diagnostics);
            ProductionSummary mismatched = ProductionRecipeNormalizer.Normalize(
                new[] { new ConfiguredBlueprintEntity("wrong", "smelter-a", "smelt") },
                new Dictionary<string, ProductionRecipe>
                {
                    ["smelt"] = new ProductionRecipe("smelt", 60m, Array.Empty<RecipeFlow>(), new[] { new RecipeFlow("ingot", 1m) }, prototypeId: "smelter-b"),
                });
            Assert.Empty(mismatched.NetOutputs);
            Assert.Contains(mismatched.Diagnostics, x => x.IndexOf("belongs to prototype", StringComparison.Ordinal) >= 0);

            ProductionSummary balanced = ProductionRecipeNormalizer.Normalize(
                new[] { new ConfiguredBlueprintEntity("c", "splitter", "balanced") },
                new Dictionary<string, ProductionRecipe> { ["balanced"] = new ProductionRecipe("balanced", 60m, new[] { new RecipeFlow("water", 1m) }, new[] { new RecipeFlow("water", 1m) }) });
            Assert.Empty(balanced.NetInputs);
            Assert.Empty(balanced.NetOutputs);
        }

        [Fact]
        public void PlannerRequiresExplicitRouteAndReportsCycles()
        {
            var catalog = new ProductionCatalog(new[]
            {
                new ProductionRecipe("a", 60, new[] { new RecipeFlow("ore", 1) }, new[] { new RecipeFlow("plate", 1) }),
                new ProductionRecipe("b", 60, new[] { new RecipeFlow("scrap", 1) }, new[] { new RecipeFlow("plate", 1) }),
            });
            ProductionPlan ambiguous = new ProductionPlanner(catalog).Solve("plate", FixedRate.FromDouble(1));
            Assert.Contains(ambiguous.Diagnostics, x => x.IndexOf("multiple routes", StringComparison.Ordinal) >= 0);
            ProductionPlan selected = new ProductionPlanner(catalog).Solve("plate", FixedRate.FromDouble(2), new Dictionary<string, string> { ["plate"] = "a" });
            Assert.Equal(FixedRate.FromDouble(2), selected.RawRequirements["ore"]);
            var byproductCatalog = new ProductionCatalog(new[]
            {
                new ProductionRecipe("co", 60m, new[] { new RecipeFlow("ore", 1m), new RecipeFlow("slag", 1m) }, new[] { new RecipeFlow("plate", 1m), new RecipeFlow("slag", 2m) }),
            });
            ProductionPlan byproducts = new ProductionPlanner(byproductCatalog).Solve("plate", FixedRate.FromDouble(1));
            Assert.DoesNotContain("slag", byproducts.RawRequirements.Keys);
            Assert.Equal(FixedRate.FromDouble(1), byproducts.Byproducts["slag"]);
            Assert.Equal(FixedRate.FromDouble(1), byproducts.RawRequirements["ore"]);
            var classifiedCatalog = new ProductionCatalog(new[]
            {
                new ProductionRecipe("classified", 60m, Array.Empty<RecipeFlow>(), new[]
                {
                    new RecipeFlow("plate", 1m),
                    new RecipeFlow("smoke", 2m, ProductClassification.Pollution),
                    new RecipeFlow("progress", 1m, ProductClassification.Virtual),
                }),
            });
            ProductionPlan classified = new ProductionPlanner(classifiedCatalog).Solve("plate", FixedRate.FromDouble(1));
            Assert.Equal(FixedRate.FromDouble(2), classified.Pollution["smoke"]);
            Assert.DoesNotContain("progress", classified.Byproducts.Keys);
            Assert.Contains(classified.Diagnostics, x => x.IndexOf("non-logistics", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void FlowIndexBootstrapsOnceAndExplorerReleasesTemporaryHandles()
        {
            var index = new ProductFlowIndex();
            int scans = 0;
            index.BootstrapOnce(() => { scans++; return new[] { new ProductFlowEntitySnapshot("1", "mine", ProductFlowEntityKind.Producer, new[] { new ProductFlowQuantity("ore", 4) }) }; });
            index.BootstrapOnce(() => { scans++; return Array.Empty<ProductFlowEntitySnapshot>(); });
            Assert.Equal(1, scans);
            var highlights = new TestHandles();
            var resources = new TestHandles();
            using (var session = new ProductFlowExplorerSession(index))
            {
                Assert.Equal(1, session.Select("ore", highlights, resources).Count);
                session.Clear();
                Assert.Equal(0, highlights.Active + resources.Active);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProductFlowQuantity("ore", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProductFlowEntitySnapshot("bad", "x", ProductFlowEntityKind.Storage, capacity: double.NaN));
            var failedBootstrap = new ProductFlowIndex();
            Assert.Throws<InvalidOperationException>(() => failedBootstrap.BootstrapOnce(() => throw new InvalidOperationException("scan")));
            Assert.Equal(0, failedBootstrap.Count);
        }

        [Fact]
        public void BlueprintLibrarySoftDeleteAndPayloadPreviewAreSafe()
        {
            var entry = new BlueprintLibraryEntry(new BlueprintIdentity("bp-1", "hash-1"), "Factory", "Factories", new[] { "smelter" }, new Dictionary<string, string> { ["recipe"] = "smelt" }, new BlueprintOperationalStats(2, 3, 4, 5));
            var store = new BlueprintLibraryStore();
            Assert.True(store.Write(entry, BlueprintWriteMode.Create).Success);
            string payload = BlueprintPayloadCodec.Export(entry);
            Assert.True(store.PreviewImport(payload, new HashSet<string> { "smelter" }).CanImport);
            Assert.True(store.SoftDelete("bp-1"));
            Assert.Empty(store.Snapshot());
            Assert.True(store.Restore("bp-1"));
            Assert.Single(store.Snapshot());
            var rehydrated = new BlueprintLibraryStore();
            Assert.True(rehydrated.LoadSidecar(store.ExportSidecar(), new HashSet<string> { "smelter" }, out IReadOnlyList<string> loadErrors));
            Assert.Empty(loadErrors);
            Assert.Single(rehydrated.Snapshot());
            Assert.Equal(2m, rehydrated.Snapshot()[0].Stats!.Workers);
            Assert.Contains("smelter", store.PreviewImport(payload, new HashSet<string>()).MissingPrototypeIds);
            Assert.False(store.PreviewImport("{\"Version\":1}", new HashSet<string>()).CanImport);
            Assert.False(store.PreviewImport("{\"Version\":1,\"StableId\":\"bad\",\"ContentHash\":\"h\",\"State\":9,\"PrototypeIds\":[],\"Configuration\":{}}", new HashSet<string>()).CanImport);
            BlueprintLibraryEntry updated = new BlueprintLibraryEntry(new BlueprintIdentity("bp-1", "hash-2"), "Changed name", "Other", new[] { "smelter" }, new Dictionary<string, string>());
            Assert.True(store.Write(updated, BlueprintWriteMode.Update).Success);
            Assert.Equal("Factory", store.Snapshot()[0].Name);
            Assert.Equal("Factories", store.Snapshot()[0].Folder);
        }

        [Fact]
        public void PortableBlueprintEnvelopeValidatesContentAndRecycleBinPersistsValueOnly()
        {
            const string nativePayload = "B4:test";
            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(nativePayload)).Select(value => value.ToString("x2")));
            }

            var envelope = new BlueprintPortableEnvelope
            {
                StableId = "native:" + hash,
                ContentHash = hash,
                NativePayload = nativePayload,
                ItemKind = "blueprint",
                Name = "Factory",
                FolderPath = "Factories",
                NativeGameVersion = "0.8.7b",
            };
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.BlueprintRecycleTests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new BlueprintRecycleBinStore(Path.Combine(root, "recycle.json"));
                Assert.True(store.TryAdd(envelope, out string addError), addError);
                Assert.True(store.Save(out string saveError), saveError);

                var loaded = new BlueprintRecycleBinStore(Path.Combine(root, "recycle.json"));
                Assert.True(loaded.Load(out string loadError), loadError);
                Assert.Single(loaded.Snapshot());
                Assert.Equal("Factories", loaded.Snapshot()[0].FolderPath);

                envelope.NativePayload = "B7:changed";
                Assert.False(loaded.TryAdd(envelope, out _));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void UndoRecorderGroupsNestedEntitiesAndRejectsUnsafeRestore()
        {
            var recorder = new UndoRecorder(2);
            using (IUndoActionScope action = recorder.BeginAction(UndoActionKind.BlueprintPlacement, "Pasted factory"))
            {
                Assert.True(recorder.Record(new UndoEntitySnapshot("1", "smelter", new UndoTransform(1, 2, 3))));
                Assert.True(recorder.Record(new UndoEntitySnapshot("2", "smelter", new UndoTransform(4, 5, 6))));
                action.Complete();
            }
            Assert.Equal(1, recorder.Count);
            var scheduler = new TestScheduler();
            Assert.False(recorder.TryUndo(new TestValidator(false), scheduler, out _));
            Assert.Equal(1, recorder.Count);
            Assert.True(recorder.TryUndo(new TestValidator(true), scheduler, out _));
            Assert.Single(scheduler.Records);
            Assert.Empty(recorder.Snapshot());

            using (IUndoActionScope action = recorder.BeginAction(UndoActionKind.Placement, "partial"))
            {
                recorder.Record(new UndoEntitySnapshot("3", "smelter", new UndoTransform(0, 0, 0)));
            }
            Assert.Empty(recorder.Snapshot()); // failed outer scope did not add a new entry
            using (IUndoActionScope action = recorder.BeginAction(UndoActionKind.Placement, "retry"))
            {
                recorder.Record(new UndoEntitySnapshot("4", "smelter", new UndoTransform(0, 0, 0)));
                action.Complete();
            }
            var failingScheduler = new TestScheduler(true);
            Assert.False(recorder.TryUndo(new TestValidator(true), failingScheduler, out _));
            Assert.Equal(1, recorder.Count);
        }

        [Fact]
        public void DailyHistoryClosesDaysAndCachesRollingAverage()
        {
            var history = new DailyValueHistory(4, 2);
            history.Add(1, 10);
            Assert.Equal(10, history.RollingAverage);
            history.Add(2, 20);
            Assert.Equal(15, history.RollingAverage);
            history.Add(3, 30);
            Assert.Equal(25, history.RollingAverage);
            history.CloseCurrentDay();
            Assert.Equal(25, history.RollingAverage);
            history.Add(3, 100); // late event for the closed day is ignored
            Assert.Equal(25, history.RollingAverage);
            history.Add(long.MaxValue, 5); // far-future clock jumps remain bounded
            Assert.True(history.Count <= history.CapacityDays);

            var throughput = new ThroughputHistoryService(4, 2);
            throughput.SetMonitored("machine", true);
            throughput.RecordTransfer("machine", "ore", double.NaN, 1);
            throughput.RecordTransfer("machine", "ore", 10, 1);
            IReadOnlyDictionary<ThroughputKey, DailyValueHistorySnapshot> snapshot = throughput.Snapshot();
            Assert.Equal(10, snapshot[new ThroughputKey("machine", "ore")].RollingAverage);
            throughput.RecordTransfer("machine", "ore", 20, 1);
            Assert.Equal(10, snapshot[new ThroughputKey("machine", "ore")].RollingAverage);
        }

        private sealed class TestHandles : IProductFlowHighlightService, IProductResourceVisualizationActivator
        {
            internal int Active;
            public IDisposable Highlight(string entityId, ProductFlowEntityKind kind) => Open();
            public IDisposable Activate(string productId) => Open();
            private IDisposable Open() { Active++; return new Handle(this); }
            private sealed class Handle : IDisposable { private TestHandles? m_owner; internal Handle(TestHandles owner) => m_owner = owner; public void Dispose() { if (m_owner is null) return; m_owner.Active--; m_owner = null; } }
        }
        private sealed class TestValidator : IUndoValidator { private readonly bool m_result; internal TestValidator(bool result) => m_result = result; public bool CanUndo(UndoRecord record, out string reason) { reason = m_result ? string.Empty : "blocked"; return m_result; } }
        private sealed class TestScheduler : IUndoCommandScheduler { private readonly bool m_throw; internal TestScheduler(bool shouldThrow = false) => m_throw = shouldThrow; internal readonly List<UndoRecord> Records = new(); public void ScheduleUndo(UndoRecord record) { if (m_throw) throw new InvalidOperationException("queue unavailable"); Records.Add(record); } }
    }
}
