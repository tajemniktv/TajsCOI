// Taj's COI Mods | HarmonyTargetContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Collections.ReadonlyCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Animations;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Transports;
using Mafi.Core.GameLoop;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.Vehicles.Trucks;
using Mafi.Core.Vehicles.Trucks.JobProviders;
using Mafi.Serialization;
using Mafi.Unity.InputControl;
using TajsCOI.Profiler.Probes.Dumping;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests.RuntimeContracts
{
    /// <summary>
    ///     Exact target contracts for the current 0.8.7b references.  These tests only discover
    ///     MethodInfo/FieldInfo; no Harmony patch is installed here.
    /// </summary>
    public sealed class HarmonyTargetContractTests
    {
        [Fact]
        public void SupportedGameAssemblyContextIsLoadedAndIdentifiable()
        {
            string[] expected = { "Mafi", "Mafi.Core", "Mafi.Base", "Mafi.Unity" };
            foreach (string name in expected)
            {
                Assembly assembly = RuntimeContractAssertions.RequireAssembly(name);
                Assert.Equal(name, assembly.GetName().Name);
                Assert.Equal(new Version(0, 8, 7, 0), assembly.GetName().Version);
            }

            Assert.DoesNotContain("<COI_ROOT unset>", GameAssemblyContext.Describe(), StringComparison.Ordinal);
        }

        [Fact]
        public void DumpingProbeDiscoveryMatchesItsExactProductionContracts()
        {
            MethodInfo dump = RuntimeContractAssertions.RequireMethod(
                typeof(TerrainDumpingManager),
                "TryFindClosestReadyToDump",
                typeof(bool),
                isStatic: false,
                typeof(Tile2i),
                typeof(Option<LooseProductProto>),
                typeof(Truck),
                typeof(ulong?),
                typeof(TerrainDesignation).MakeByRefType(),
                typeof(IIndexable<MineTower>),
                typeof(bool),
                typeof(Lyst<TerrainDesignation>));
            Assert.Same(dump, DumpSearchDiagnosticsService.FindDumpSearchTarget());

            MethodInfo tick = RuntimeContractAssertions.RequireMethod(
                typeof(VehiclePathFindingManager),
                "SimUpdateInternal",
                typeof(void),
                isStatic: false);
            Assert.Same(tick, DumpSearchDiagnosticsService.FindPathFindingTickTarget());

            MethodInfo enqueue = RuntimeContractAssertions.RequireMethod(
                typeof(VehiclePathFindingManager),
                "EnqueueTask",
                typeof(void),
                isStatic: false,
                typeof(IManagedVehiclePathFindingTask),
                typeof(int));
            Assert.Same(enqueue, DumpSearchDiagnosticsService.FindPathFindingEnqueueTarget());

            MethodInfo dumpingJob = Assert.Single(
                typeof(DumpingJob).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "handleFindMoreDesignations" && method.GetParameters().Length == 0);
            Assert.Equal("Mafi.Core.Vehicles.Jobs.DumpingJob+State", dumpingJob.ReturnType.FullName);
            Assert.Same(dumpingJob, DumpSearchDiagnosticsService.FindDumpingJobTarget());

            MethodInfo globalCache = RuntimeContractAssertions.RequireMethod(
                typeof(TerrainDumpingManager),
                "getAllEligibleCached",
                typeof(LystStruct<TerrainDesignation>),
                isStatic: false,
                typeof(bool));
            Assert.Same(globalCache, DumpSearchDiagnosticsService.FindGlobalEligibleCacheTarget());

            MethodInfo towerCache = RuntimeContractAssertions.RequireMethod(
                typeof(TerrainDumpingManager),
                "getAllEligibleCachedFor",
                typeof(Lyst<TerrainDesignation>),
                isStatic: false,
                typeof(MineTower),
                typeof(bool));
            Assert.Same(towerCache, DumpSearchDiagnosticsService.FindTowerEligibleCacheTarget());

            MethodInfo best = RuntimeContractAssertions.RequireMethod(
                typeof(TerrainDesignationsManager),
                "TryFindBestReadyToFulfill",
                typeof(bool),
                isStatic: false,
                typeof(System.Collections.Generic.IEnumerable<TerrainDesignation>),
                typeof(Tile2i),
                typeof(Vehicle),
                typeof(TerrainDesignation).MakeByRefType(),
                typeof(Option<LooseProductProto>),
                typeof(bool));
            Assert.Same(best, DumpSearchDiagnosticsService.FindBestDesignationTarget());

            MethodInfo nearby = RuntimeContractAssertions.RequireMethod(
                typeof(TerrainDumpingManager),
                "isEligibleAsNearbyFor",
                typeof(bool),
                isStatic: false,
                typeof(TerrainDesignation),
                typeof(TerrainDesignation),
                typeof(bool));
            Assert.Same(nearby, DumpSearchDiagnosticsService.FindNearbyEligibilityTarget());
        }

        [Fact]
        public void UnlockedSpeedAndOverclockTargetsRetainParameterAndShapeContracts()
        {
            RuntimeContractAssertions.RequireMethod(
                typeof(GameSpeedController),
                nameof(GameSpeedController.InputUpdate),
                typeof(bool),
                isStatic: false);
            RuntimeContractAssertions.RequireMethod(
                typeof(GameSpeedController),
                nameof(GameSpeedController.SetSpeed),
                typeof(void),
                isStatic: false,
                typeof(int));
            RuntimeContractAssertions.RequireMethod(
                typeof(SimLoopEvents),
                nameof(SimLoopEvents.SetSimSpeed),
                typeof(void),
                isStatic: false,
                typeof(int));

            RuntimeContractAssertions.RequireMethod(
                typeof(AnimationWithPauseState),
                nameof(AnimationWithPauseState.Start),
                typeof(void),
                isStatic: false,
                typeof(IEntity),
                typeof(Duration),
                typeof(int));
            RuntimeContractAssertions.RequireField(
                typeof(AnimationWithPauseState),
                "m_params",
                typeof(AnimationWithPauseParams),
                isStatic: false);

            foreach (Type type in new[] { typeof(Machine), typeof(Transport) })
            {
                RuntimeContractAssertions.RequireMethod(
                    type,
                    "AddToConfig",
                    typeof(void),
                    isStatic: false,
                    typeof(EntityConfigData));
                RuntimeContractAssertions.RequireMethod(
                    type,
                    "ApplyConfig",
                    typeof(void),
                    isStatic: false,
                    typeof(EntityConfigData));
            }
        }

        [Fact]
        public void PerformanceAndDesignationTargetsRetainTheirExactSeams()
        {
            RuntimeContractAssertions.RequireConstructor(
                typeof(BlobReader),
                typeof(Stream),
                typeof(int),
                typeof(ImmutableArray<ISpecialSerializerFactory>));

            Type gameSaver = typeof(SaveLoadFileUtils).Assembly.GetType("Mafi.Core.SaveGame.GameSaver", throwOnError: true)!;
            RuntimeContractAssertions.RequireMethod(
                gameSaver,
                "FinishSaveWriteToStream",
                typeof(void),
                isStatic: false,
                typeof(Stream));

            Type productsRenderer = typeof(GameSpeedController).Assembly.GetType(
                "Mafi.Unity.InstancedRendering.Products.ProductsRenderer",
                throwOnError: true)!;
            RuntimeContractAssertions.RequireMethod(
                productsRenderer,
                "uploadFrame",
                typeof(void),
                isStatic: false);

            Type productMeshTable = typeof(GameSpeedController).Assembly.GetType(
                "Mafi.Unity.InstancedRendering.Products.ProductMeshTable",
                throwOnError: true)!;
            RuntimeContractAssertions.RequireMethod(
                productMeshTable,
                "RebuildTextureArrays",
                typeof(void),
                isStatic: false,
                typeof(bool));

            Type designationRenderer = typeof(GameSpeedController).Assembly.GetType(
                "Mafi.Unity.Terrain.Designation.TerrainDesignationsRenderer",
                throwOnError: true)!;
            RuntimeContractAssertions.RequireMethod(
                designationRenderer,
                "renderUpdate",
                typeof(void),
                isStatic: false,
                typeof(GameTime));
        }

        [Fact]
        public void RuntimeTargetDiscoveryDoesNotInstallHarmonyPatches()
        {
            MethodInfo target = DumpSearchDiagnosticsService.FindDumpSearchTarget()!;
            Patches? before = Harmony.GetPatchInfo(target);

            _ = DumpSearchDiagnosticsService.FindPathFindingTickTarget();
            _ = DumpSearchDiagnosticsService.FindPathFindingEnqueueTarget();
            _ = DumpSearchDiagnosticsService.FindDumpingJobTarget();
            _ = DumpSearchDiagnosticsService.FindGlobalEligibleCacheTarget();
            _ = DumpSearchDiagnosticsService.FindTowerEligibleCacheTarget();
            _ = DumpSearchDiagnosticsService.FindBestDesignationTarget();
            _ = DumpSearchDiagnosticsService.FindNearbyEligibilityTarget();

            Patches? after = Harmony.GetPatchInfo(target);
            Assert.Equal(
                before?.Prefixes?.Count() ?? 0,
                after?.Prefixes?.Count() ?? 0);
            Assert.Equal(
                before?.Postfixes?.Count() ?? 0,
                after?.Postfixes?.Count() ?? 0);
            Assert.Equal(
                before?.Transpilers?.Count() ?? 0,
                after?.Transpilers?.Count() ?? 0);
            Assert.Equal(
                before?.Finalizers?.Count() ?? 0,
                after?.Finalizers?.Count() ?? 0);
        }
    }
}
