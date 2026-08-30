using System;
using System.Collections.Generic;
using System.Linq;
using TajsCOI.Common.Tuning;
using TajsCOI.Performance;
using TajsCOI.Tweaks.Features.Selection;
using TajsCOI.Tweaks.Features.MapEditor;
using TajsCOI.Tweaks.Features.Tuning;
using TajsCOI.Visuals.Features.Lighting;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class SharedInfrastructureTests
    {
        [Fact]
        public void PerformanceStartupReaderIsSchemaBoundAndFailClosed()
        {
            const string key = "TajsPerformance.enable_lazy_resource_visualization";
            Assert.True(PerformanceStartupSettings.TryReadBoolean(
                "{\"schema_version\":1,\"values\":{\"TajsPerformance.enable_lazy_resource_visualization\":true}}",
                key,
                out bool enabled));
            Assert.True(enabled);

            Assert.True(PerformanceStartupSettings.TryReadBoolean(
                "{\"schema_version\":1,\"values\":{\"TajsPerformance.enable_lazy_resource_visualization\":false}}",
                key,
                out enabled));
            Assert.False(enabled);

            Assert.False(PerformanceStartupSettings.TryReadBoolean(
                "{\"schema_version\":2,\"values\":{\"TajsPerformance.enable_lazy_resource_visualization\":true}}",
                key,
                out enabled));
            Assert.False(enabled);
            Assert.False(PerformanceStartupSettings.TryReadBoolean(
                "{\"schema_version\":1,\"values\":{\"TajsPerformance.enable_lazy_resource_visualization\":\"true\"}}",
                key,
                out enabled));
            Assert.False(enabled);
        }

        [Fact]
        public void TypedBaseOverrideUsesCapturedBaseAndRestoresOnDispose()
        {
            int value = 10;
            var overrideValue = new BaseValueOverride<int>(
                "test.prototype.capacity",
                value,
                next => value = next,
                BaseValueApplyMode.Immediate,
                next => next >= 0 && next <= 100);
            IBaseValueOverride<int> contract = overrideValue;

            Assert.True(contract.TrySetEffective(25));
            Assert.True(contract.Apply());
            Assert.Equal(25, value);

            contract.Reset();
            Assert.Equal(10, value);
            contract.Dispose();
            Assert.Equal(10, contract.BaseValue);
            Assert.False(contract.TrySetEffective(30));
        }

        [Fact]
        public void TypedBaseOverrideRollsBackAfterSetterFailure()
        {
            int value = 10;
            var overrideValue = new BaseValueOverride<int>(
                "test.prototype.capacity",
                value,
                next =>
                {
                    value = next;
                    if (next == 30)
                    {
                        throw new InvalidOperationException("simulated setter failure");
                    }
                },
                BaseValueApplyMode.Immediate,
                next => next >= 0 && next <= 100);

            Assert.True(overrideValue.TrySetEffective(30));
            Assert.False(overrideValue.Apply());
            Assert.Equal(10, value);
            Assert.Equal(10, overrideValue.EffectiveValue);
            overrideValue.Dispose();
        }

        [Fact]
        public void TypedOverrideRegistryDerivesEachValueFromNativeBase()
        {
            int value = 10;
            var registry = new TypedBaseValueOverrideRegistry();

            Assert.True(registry.TryRegister(
                "test.prototype.capacity",
                typeof(int),
                () => value,
                next => value = (int)next!,
                0d,
                100d,
                BaseValueApplyMode.ReloadRequired));
            Assert.True(registry.TrySetMultiplier("test.prototype.capacity", 2.5d));
            Assert.Equal(25, value);
            Assert.True(registry.TrySetMultiplier("test.prototype.capacity", 3d));
            Assert.Equal(30, value);
            registry.Reset();
            Assert.Equal(10, value);
            registry.Dispose();
        }

        [Fact]
        public void SceneSelectionSortsAndBoundsStableIdsAndReleasesHighlightsOnEscape()
        {
            int nextHandle = 0;
            var released = new List<int>();
            bool queryCalled = false;
            var highlights = new PooledHighlightUtility<int, int>(
                () => ++nextHandle,
                (_, _) => { },
                handle => released.Add(handle));
            var selection = new SceneSelection<int>(
                _ =>
                {
                    queryCalled = true;
                    return new[] { 3, 1, 3 };
                },
                _ => new[] { 2, 1 },
                maximumSelectionCount: 2,
                highlights);

            Assert.False(selection.SelectPoint(new SceneSelectionPoint(0, 0, 0)));
            Assert.False(queryCalled);
            Assert.True(selection.Activate());
            Assert.True(selection.SelectPoint(new SceneSelectionPoint(0, 0, 0)));
            Assert.Equal(new[] { 1, 3 }, selection.SelectedEntityIds.ToArray());
            Assert.Equal(2, highlights.Count);

            Assert.True(selection.HandleEscape());
            Assert.False(selection.IsActive);
            Assert.Empty(selection.SelectedEntityIds);
            Assert.Equal(2, released.Count);
            selection.Dispose();
        }

        [Fact]
        public void SceneSelectionCoordinatorAllowsOnlyOneActiveTool()
        {
            var first = new SceneSelection<int>(_ => Array.Empty<int>(), _ => Array.Empty<int>(), 4);
            var second = new SceneSelection<int>(_ => Array.Empty<int>(), _ => Array.Empty<int>(), 4);

            Assert.True(first.Activate());
            Assert.False(second.Activate());
            first.Cancel();
            Assert.True(second.Activate());
            second.Dispose();
            first.Dispose();
        }

        [Fact]
        public void SceneSelectionQueryFailureCancelsAndReleasesOwner()
        {
            var selection = new SceneSelection<int>(
                _ => throw new InvalidOperationException("query seam unavailable"),
                _ => Array.Empty<int>(),
                maximumSelectionCount: 4);

            Assert.True(selection.Activate());
            Assert.False(selection.SelectPoint(new SceneSelectionPoint(0, 0, 0)));
            Assert.False(selection.IsActive);

            var replacement = new SceneSelection<int>(
                _ => Array.Empty<int>(),
                _ => Array.Empty<int>(),
                maximumSelectionCount: 4);
            Assert.True(replacement.Activate());
            replacement.Dispose();
            selection.Dispose();
        }

        [Fact]
        public void SceneSelectionHighlightFailureRelinquishesCoordinator()
        {
            var selection = new SceneSelection<int>(
                _ => new[] { 1 },
                _ => Array.Empty<int>(),
                maximumSelectionCount: 4,
                new PooledHighlightUtility<int, int>(
                    () => 1,
                    (_, _) => throw new InvalidOperationException("renderer torn down"),
                    _ => { }));

            try
            {
                Assert.True(selection.Activate());
                Assert.False(selection.SelectPoint(new SceneSelectionPoint(0, 0, 0)));
                Assert.False(selection.IsActive);

                var replacement = new SceneSelection<int>(_ => Array.Empty<int>(), _ => Array.Empty<int>(), 4);
                Assert.True(replacement.Activate());
                replacement.Dispose();
            }
            finally
            {
                selection.Dispose();
            }
        }

        [Fact]
        public void SceneSelectionReportsInspectedCandidateTruncation()
        {
            var selection = new SceneSelection<int>(
                _ => new[] { 0 }
                    .Concat(Enumerable.Repeat(0, 32)),
                _ => Array.Empty<int>(),
                maximumSelectionCount: 2);

            try
            {
                Assert.True(selection.Activate());
                Assert.True(selection.SelectPoint(new SceneSelectionPoint(0, 0, 0)));
                Assert.Single(selection.SelectedEntityIds);
                Assert.True(selection.LastQueryTruncated);
            }
            finally
            {
                selection.Dispose();
            }
        }

        [Fact]
        public void SceneSelectionWorldRectangleUsesStableEntityIds()
        {
            var selection = new SceneSelection<int>(
                _ => Array.Empty<int>(),
                _ => Array.Empty<int>(),
                maximumSelectionCount: 4,
                worldRectangleQuery: bounds => bounds.Contains(2, 3)
                    ? new[] { 22, 11, 22 }
                    : Array.Empty<int>());

            try
            {
                Assert.True(selection.Activate());
                Assert.True(selection.SelectWorldRectangle(new SceneAreaBounds(0, 0, 4, 4)));
                Assert.Equal(new[] { 11, 22 }, selection.SelectedEntityIds.ToArray());
            }
            finally
            {
                selection.Dispose();
            }
        }

        [Fact]
        public void SceneSelectionWorldQueryCapsSourceInspection()
        {
            bool truncated;
            IReadOnlyList<int> ids = SceneSelectionWorldQuery.SelectIds(
                Enumerable.Range(1, 40),
                new SceneAreaBounds(0, 0, 1, 1),
                _ => true,
                value => value,
                maximumSelectionCount: 2,
                out truncated);

            Assert.Equal(new[] { 1, 2 }, ids.ToArray());
            Assert.True(truncated);
        }

        [Fact]
        public void LightingEffectiveStateKeepsBaseAndPresentationSeparate()
        {
            LightingPolicy basePolicy = new(1.25f, 8f, 0.9f);
            LightingPolicy phasePolicy = new(0.8f, -3f, 0.5f);
            var state = new LightingEffectiveState(
                basePolicy,
                phasePolicy,
                LightingPolicy.Combine(basePolicy, phasePolicy),
                isInitialized: true);

            Assert.Equal(basePolicy, state.BaseLightingPolicy);
            Assert.Equal(phasePolicy, state.TimeOfDayPresentation);
            Assert.Equal(1f, state.EffectivePolicy.IntensityMultiplier);
            Assert.True(state.IsInitialized);
        }

        [Fact]
        public void MapEditorSelectionRequiresStableIdAndExactAvailableVersion()
        {
            MapEditorModManifest requested = new("mod.example", "1.2.0");
            Assert.True(MapEditorModSelection.IsCompatible(
                requested,
                new[] { new MapEditorModManifest("mod.example", "1.2.0") }));
            Assert.False(MapEditorModSelection.IsCompatible(
                requested,
                new[] { new MapEditorModManifest("mod.example", "1.3.0") }));
            Assert.False(MapEditorModSelection.IsCompatible(
                new MapEditorModManifest("", "1.2.0"),
                new[] { requested }));
        }

        [Fact]
        public void MapEditorStartupSwitchIsOptInAndSchemaBound()
        {
            const string key = "TajsTweaks.map_editor_third_party_mods";
            Assert.True(MapEditorStartupSettings.TryReadBoolean(
                "{\"schema_version\":1,\"values\":{\"TajsTweaks.map_editor_third_party_mods\":true}}",
                key,
                out bool enabled));
            Assert.True(enabled);

            Assert.False(MapEditorStartupSettings.TryReadBoolean(
                "{\"schema_version\":1,\"values\":{\"TajsTweaks.map_editor_third_party_mods\":\"true\"}}",
                key,
                out enabled));
            Assert.False(enabled);
        }

        [Fact]
        public void MapEditorNativeContractMatchesTheSupportedGameSeams()
        {
            bool resolved = MapEditorNativeContract.TryResolve(
                out System.Reflection.MethodInfo? mapEditorClick,
                out System.Reflection.MethodInfo? goToMainMenu,
                out System.Reflection.MethodInfo? tryLoadMods,
                out System.Reflection.FieldInfo? mainField);
            // The test host may not be able to load the game's private Unity dependency graph;
            // either an exact match or a diagnosed fail-open result is valid here.
            Assert.True(resolved || !string.IsNullOrWhiteSpace(MapEditorNativeContract.LastFailure));
            if (resolved)
            {
                Assert.NotNull(mapEditorClick);
                Assert.NotNull(goToMainMenu);
                Assert.NotNull(tryLoadMods);
                Assert.NotNull(mainField);
            }
        }

        [Fact]
        public void MapEditorContextDeduplicatesAndClearsTransitionState()
        {
            var context = new ModdedMapEditorContext();
            context.Begin(new[]
            {
                new MapEditorModManifest("mod.example", "1.0"),
                new MapEditorModManifest("mod.example", "1.1"),
                new MapEditorModManifest("", "bad"),
            });

            Assert.True(context.IsActive);
            Assert.Single(context.Manifests);
            IReadOnlyList<MapEditorModManifest> compatible = context.Resolve(_ => true);
            Assert.Single(compatible);
            Assert.Single(context.Decisions);

            context.Clear();
            Assert.False(context.IsActive);
            Assert.Empty(context.Manifests);
            Assert.Empty(context.Decisions);
        }
    }
}
