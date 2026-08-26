// Taj's COI Mods | HarmonyInspectorTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TajsCOI.Common.Diagnostics;
using TajsCOI.Core.Diagnostics;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class HarmonyInspectorTests
    {
        [Fact]
        public void CaptureGroupsTajsPatchesAndSharedForeignOwnersDeterministically()
        {
            var tajs = new Harmony("TajsCOI.Tests.HarmonyInspector");
            var foreign = new Harmony("OtherMod.Tests.HarmonyInspector");
            try
            {
                MethodInfo target = AccessTools.Method(typeof(HarmonyInspectorTests), nameof(InspectorTarget))!;
                tajs.Patch(target, prefix: new HarmonyMethod(typeof(HarmonyInspectorTests), nameof(TajsPrefix)));
                foreign.Patch(target, postfix: new HarmonyMethod(typeof(HarmonyInspectorTests), nameof(ForeignPostfix)));

                HarmonyInspectionSnapshot first = HarmonyInspector.Capture();
                HarmonyTargetSnapshot inspected = Assert.Single(
                    first.Targets,
                    candidate => candidate.OriginalSignature.IndexOf("InspectorTarget", StringComparison.Ordinal) >= 0);
                HarmonyInspectionSnapshot second = HarmonyInspector.Capture();

                Assert.True(inspected.IsSharedTarget);
                Assert.Contains("OtherMod.Tests.HarmonyInspector", inspected.NonTajsOwners);
                Assert.Contains(inspected.Patches, patch => patch.IsTajsOwned && patch.Kind == HarmonyPatchKind.Prefix);
                Assert.Contains(inspected.Patches, patch => !patch.IsTajsOwned && patch.Kind == HarmonyPatchKind.Postfix);
                Assert.Contains(inspected.Patches, patch => patch.Priority >= 0);
                Assert.Contains(inspected.Patches, patch => patch.ReturnsBoolean);
                Assert.Equal(HarmonyCollisionRisk.Informational, inspected.Risk);
                Assert.Equal(
                    first.Targets.Select(candidate => candidate.OriginalSignature),
                    second.Targets.Select(candidate => candidate.OriginalSignature));
            }
            finally
            {
                tajs.UnpatchAll(tajs.Id);
                foreign.UnpatchAll(foreign.Id);
            }
        }

        [Fact]
        public void RiskClassificationFlagsDuplicateTranspilerAndOrderingHazards()
        {
            HarmonyPatchSnapshot duplicate = Patch(
                HarmonyPatchKind.Prefix,
                "TajsCOI.Tests.Duplicate",
                "TajsCOI.Tests.Prefix",
                400,
                returnsBoolean: true);
            (HarmonyCollisionRisk risk, string reason) = HarmonyInspector.ClassifyRisk(new[] { duplicate, duplicate });
            Assert.Equal(HarmonyCollisionRisk.High, risk);
            Assert.Contains("duplicate Tajs", reason);

            HarmonyPatchSnapshot transpiler = Patch(
                HarmonyPatchKind.Transpiler,
                "TajsCOI.Tests.Transpiler",
                "TajsCOI.Tests.Transpile",
                400);
            HarmonyPatchSnapshot foreignTranspiler = Patch(
                HarmonyPatchKind.Transpiler,
                "OtherMod.Tests.Transpiler",
                "OtherMod.Tests.Transpile",
                400);
            (risk, reason) = HarmonyInspector.ClassifyRisk(new[] { transpiler, foreignTranspiler });
            Assert.Equal(HarmonyCollisionRisk.High, risk);
            Assert.Contains("transpilers share", reason);

            HarmonyPatchSnapshot missingOrdering = Patch(
                HarmonyPatchKind.Postfix,
                "TajsCOI.Tests.Ordering",
                "TajsCOI.Tests.Postfix",
                400,
                before: new[] { "Missing.Mod" });
            (risk, reason) = HarmonyInspector.ClassifyRisk(new[] { missingOrdering });
            Assert.Equal(HarmonyCollisionRisk.None, risk);
            Assert.Empty(reason);

            HarmonyPatchSnapshot crossKindPrefix = Patch(
                HarmonyPatchKind.Prefix,
                "TajsCOI.Tests.CrossKindA",
                "TajsCOI.Tests.PrefixA",
                400,
                before: new[] { "TajsCOI.Tests.CrossKindB" });
            HarmonyPatchSnapshot crossKindPostfix = Patch(
                HarmonyPatchKind.Postfix,
                "TajsCOI.Tests.CrossKindB",
                "TajsCOI.Tests.PostfixB",
                400,
                before: new[] { "TajsCOI.Tests.CrossKindA" });
            (risk, reason) = HarmonyInspector.ClassifyRisk(new[] { crossKindPrefix, crossKindPostfix });
            Assert.Equal(HarmonyCollisionRisk.None, risk);
            Assert.Empty(reason);

            HarmonyPatchSnapshot firstTajsTranspiler = Patch(
                HarmonyPatchKind.Transpiler,
                "TajsCOI.Tests.TranspilerA",
                "TajsCOI.Tests.TranspileA",
                400);
            HarmonyPatchSnapshot secondTajsTranspiler = Patch(
                HarmonyPatchKind.Transpiler,
                "TajsCOI.Tests.TranspilerB",
                "TajsCOI.Tests.TranspileB",
                400);
            (risk, reason) = HarmonyInspector.ClassifyRisk(new[] { firstTajsTranspiler, secondTajsTranspiler });
            Assert.Equal(HarmonyCollisionRisk.High, risk);
            Assert.Contains("multiple Tajs transpiler owners", reason);

            HarmonyPatchSnapshot cycleLeft = Patch(
                HarmonyPatchKind.Postfix,
                "TajsCOI.Tests.CycleA",
                "TajsCOI.Tests.PostfixA",
                400,
                before: new[] { "TajsCOI.Tests.CycleB" });
            HarmonyPatchSnapshot cycleRight = Patch(
                HarmonyPatchKind.Postfix,
                "TajsCOI.Tests.CycleB",
                "TajsCOI.Tests.PostfixB",
                400,
                before: new[] { "TajsCOI.Tests.CycleA" });
            (risk, reason) = HarmonyInspector.ClassifyRisk(new[] { cycleLeft, cycleRight });
            Assert.Equal(HarmonyCollisionRisk.Medium, risk);
            Assert.Contains("cycle", reason);
        }

        [Fact]
        public void SnapshotCollectionsAreImmutableCopies()
        {
            var before = new List<string> { "z-owner", "a-owner", "a-owner" };
            var after = new List<string> { "after-owner" };
            var patch = new HarmonyPatchSnapshot(
                HarmonyPatchKind.Postfix,
                "TajsCOI.Tests",
                "Test.Type.Postfix()",
                400,
                before,
                after,
                true);

            before[0] = "mutated";
            before.Add("later");
            after[0] = "mutated";

            Assert.Equal(new[] { "a-owner", "z-owner" }, patch.Before);
            Assert.Equal(new[] { "after-owner" }, patch.After);
        }

        private static HarmonyPatchSnapshot Patch(
            HarmonyPatchKind kind,
            string owner,
            string method,
            int priority,
            IEnumerable<string>? before = null,
            bool returnsBoolean = false) =>
            new(kind, owner, method, priority, before, Array.Empty<string>(), owner.StartsWith("TajsCOI.", StringComparison.Ordinal), returnsBoolean);

        private static int InspectorTarget(int value) => value;

        private static bool TajsPrefix() => true;

        private static void ForeignPostfix()
        {
        }
    }
}
