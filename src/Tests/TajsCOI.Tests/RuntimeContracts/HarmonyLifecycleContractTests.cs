// Taj's COI Mods | HarmonyLifecycleContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mafi.Serialization;
using TajsCOI.Core.Runtime;
using TajsCOI.Performance.Features.SaveLoadReadBuffer;
using Xunit;

namespace TajsCOI.Tests.RuntimeContracts
{
    /// <summary>
    ///     Process-lifetime ownership contract for one representative performance feature.  The
    ///     test deliberately installs twice to model two recreated gameplay resolvers, then tears
    ///     the test owner down so no Harmony state leaks into another test.
    /// </summary>
    public sealed class HarmonyLifecycleContractTests
    {
        private const string Owner = "TajsCOI.Performance.SaveLoadReadBuffer";

        [Fact]
        public void RecreatedRuntimeDoesNotDuplicateAProcessLifetimePatch()
        {
            ConstructorInfo target = RuntimeContractAssertions.RequireConstructor(
                typeof(BlobReader),
                typeof(System.IO.Stream),
                typeof(int),
                typeof(Mafi.Collections.ImmutableCollections.ImmutableArray<ISpecialSerializerFactory>));
            MethodInfo patchMethod = typeof(SaveLoadReadBufferFeature).GetMethod(
                "ReplaceBufferSize",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var harmony = new Harmony(Owner);
            var firstRuntime = new TajsRuntime();
            var secondRuntime = new TajsRuntime();
            var firstFeature = new SaveLoadReadBufferFeature();
            var secondFeature = new SaveLoadReadBufferFeature();

            try
            {
                harmony.UnpatchAll(Owner);
                Assert.Equal(0, CountOwner(Harmony.GetPatchInfo(target), Owner));

                firstFeature.Install(firstRuntime, firstRuntime.GetLogger("TajsPerformance", "SaveLoadReadBuffer"));
                Assert.Equal(1, CountOwner(Harmony.GetPatchInfo(target), Owner));
                Assert.Contains(
                    Harmony.GetPatchInfo(target)!.Transpilers,
                    patch => patch.owner == Owner && patch.PatchMethod == patchMethod);

                // A second resolver/runtime must observe the compatible process patch rather than
                // register another transpiler or remove the first one.
                secondFeature.Install(secondRuntime, secondRuntime.GetLogger("TajsPerformance", "SaveLoadReadBuffer"));
                Assert.Equal(1, CountOwner(Harmony.GetPatchInfo(target), Owner));
                Assert.Equal(1, firstRuntime.GetCompatibilitySnapshot().Count(report => report.ComponentId == "SaveLoadReadBuffer"));
                Assert.Equal(1, secondRuntime.GetCompatibilitySnapshot().Count(report => report.ComponentId == "SaveLoadReadBuffer"));
            }
            finally
            {
                harmony.UnpatchAll(Owner);
            }

            Assert.Equal(0, CountOwner(Harmony.GetPatchInfo(target), Owner));
        }

        private static int CountOwner(Patches? patches, string owner) =>
            patches?.Transpilers?.Count(patch => string.Equals(patch.owner, owner, StringComparison.Ordinal)) ?? 0;
    }
}
