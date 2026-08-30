// Taj's COI Mods | TerrainXRayTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Unity.Terrain;
using TajsCOI.Common.Settings;
using TajsCOI.Tweaks;
using Xunit;
using XunitAssert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class TerrainXRayTests
    {
        [Fact]
        public void CircleIndexClampsAtMapEdges()
        {
            var chunks = new HashSet<Chunk2i>();

            TerrainXRayChunkIndex.ComputeAffectedChunks(new Tile2i(0, 0), 10, 256, 256, chunks);

            XunitAssert.Single(chunks);
            XunitAssert.Contains(new Chunk2i(0, 0), chunks);
        }

        [Fact]
        public void CircleIndexIncludesOnlyChunksTouchingCircle()
        {
            var chunks = new HashSet<Chunk2i>();

            TerrainXRayChunkIndex.ComputeAffectedChunks(new Tile2i(300, 300), 10, 512, 512, chunks);

            XunitAssert.Single(chunks);
            XunitAssert.Contains(new Chunk2i(1, 1), chunks);
        }

        [Fact]
        public void DiffSeparatesEnteredExitedAndChangedChunks()
        {
            var previous = new HashSet<Chunk2i> { new(0, 0), new(1, 0) };
            var next = new HashSet<Chunk2i> { new(1, 0), new(2, 0) };

            TerrainXRayChunkDiff diff = TerrainXRayChunkIndex.Diff(previous, next, stateChanged: true);

            XunitAssert.Equal(new[] { new Chunk2i(2, 0) }, diff.Entered);
            XunitAssert.Equal(new[] { new Chunk2i(0, 0) }, diff.Exited);
            XunitAssert.Equal(new[] { new Chunk2i(1, 0) }, diff.Changed);
            XunitAssert.Equal(3, diff.UpdateChunks().Count());
        }

        [Fact]
        public void UnchangedStateDoesNotRequestChunkUpdates()
        {
            var chunks = new HashSet<Chunk2i> { new(0, 0) };

            TerrainXRayChunkDiff diff = TerrainXRayChunkIndex.Diff(chunks, chunks, stateChanged: false);

            XunitAssert.Empty(diff.Changed);
            XunitAssert.Empty(diff.UpdateChunks());
        }

        [Fact]
        public void TerrainXRayIsAnImmediateOptInPreference()
        {
            SettingDescriptor descriptor = TajsTweaksSettingsCatalog.All
                .Single(x => x.Key == TajsTweaksSettingsCatalog.TerrainXRay);

            XunitAssert.False((bool)descriptor.DefaultValue);
            XunitAssert.Equal(SettingApplyMode.Immediate, descriptor.ApplyMode);
            XunitAssert.Equal(SettingScope.Global, descriptor.Scope);
        }

        [Fact]
        public void SupportedRendererExposesTheAuditedXRaySeam()
        {
            MethodInfo? set = typeof(TerrainRenderer).GetMethod(
                "SetXRayData",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(Tile2i), typeof(RelTile1i), typeof(ThicknessTilesI) },
                modifiers: null);
            MethodInfo? disable = typeof(TerrainRenderer).GetMethod(
                "DisableXRay",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            XunitAssert.NotNull(set);
            XunitAssert.NotNull(disable);
        }
    }
}
