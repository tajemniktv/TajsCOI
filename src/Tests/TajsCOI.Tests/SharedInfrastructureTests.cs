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

            Assert.True(overrideValue.TrySetEffective(25));
            Assert.True(overrideValue.Apply());
            Assert.Equal(25, value);

            overrideValue.Reset();
            Assert.Equal(10, value);
            overrideValue.Dispose();
            Assert.Equal(10, overrideValue.BaseValue);
            Assert.False(overrideValue.TrySetEffective(30));
        }

        [Fact]
        public void SceneSelectionSortsAndBoundsStableIdsAndReleasesHighlightsOnEscape()
        {
            int nextHandle = 0;
            var released = new List<int>();
            var highlights = new PooledHighlightUtility<string, int>(
                () => ++nextHandle,
                (_, _) => { },
                handle => released.Add(handle));
            var selection = new SceneSelection<int>(
                _ => new[] { "entity-3", "entity-1", "entity-3" },
                _ => new[] { "entity-2", "entity-1" },
                maximumSelectionCount: 2,
                highlights);

            Assert.False(selection.SelectPoint(new SceneSelectionPoint(0, 0, 0)));
            Assert.True(selection.Activate());
            Assert.True(selection.SelectPoint(new SceneSelectionPoint(0, 0, 0)));
            Assert.Equal(new[] { "entity-1", "entity-3" }, selection.SelectedEntityIds.ToArray());
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
            var first = new SceneSelection<int>(_ => Array.Empty<string>(), _ => Array.Empty<string>(), 4);
            var second = new SceneSelection<int>(_ => Array.Empty<string>(), _ => Array.Empty<string>(), 4);

            Assert.True(first.Activate());
            Assert.False(second.Activate());
            first.Cancel();
            Assert.True(second.Activate());
            second.Dispose();
            first.Dispose();
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
    }
}
