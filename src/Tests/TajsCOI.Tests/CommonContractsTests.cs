// Taj's COI Mods | CommonContractsTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TajsCOI.Common.Build;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Persistence;
using TajsCOI.Common.Settings;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class CommonContractsTests
    {
        [Fact]
        public void SaveIdentityKeepsLineageAcrossRevisionsAndRenamesButNotCopies()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveIdentity-" + Guid.NewGuid().ToString("N"));
            string original = Path.Combine(root, "same-name.save");
            string renamed = Path.Combine(root, "renamed.save");
            string copy = Path.Combine(root, "copy.save");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(original, new byte[] { 1, 2, 3 });
                TajsSaveIdentity first = TajsSaveIdentity.FromFile(original, "world")!;
                using (var stream = new FileStream(original, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.WriteByte(4);
                }
                TajsSaveIdentity revision = TajsSaveIdentity.FromFile(original, "world")!;

                Assert.Equal(first.OwnershipKey, revision.OwnershipKey);
                Assert.NotEqual(first.RevisionKey, revision.RevisionKey);

                File.Move(original, renamed);
                TajsSaveIdentity renamedIdentity = TajsSaveIdentity.FromFile(renamed, "world")!;
                Assert.Equal(first.OwnershipKey, renamedIdentity.OwnershipKey);

                File.Copy(renamed, copy);
                TajsSaveIdentity copied = TajsSaveIdentity.FromFile(copy, "world")!;
                Assert.NotEqual(first.OwnershipKey, copied.OwnershipKey);
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
        public void SaveIdentityDoesNotReuseDeletedAndRecreatedFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveIdentity-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "same-name.save");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(path, new byte[] { 1 });
                var registry = new TajsSaveIdentityRegistry(Path.Combine(root, "sidecars"));
                TajsSaveIdentity first = registry.Resolve(path, "world")!;
                File.Delete(path);
                File.WriteAllBytes(path, new byte[] { 1 });
                TajsSaveIdentity recreated = registry.Resolve(path, "world")!;
                Assert.NotEqual(first.OwnershipKey, recreated.OwnershipKey);
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
        public void SaveIdentityRegistryDistinguishesRenameFromSaveAs()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveIdentity-" + Guid.NewGuid().ToString("N"));
            string original = Path.Combine(root, "original.save");
            string renamed = Path.Combine(root, "renamed.save");
            string copy = Path.Combine(root, "copy.save");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(original, new byte[] { 1, 2, 3 });
                var registry = new TajsSaveIdentityRegistry(Path.Combine(root, "sidecars"));
                TajsSaveIdentityBindingResult firstBinding = registry.ResolveDetailed(original, "world");
                Assert.Equal(
                    TajsSaveIdentityBindingStatus.IdentityResolvedAndBindingPersisted,
                    firstBinding.Status);
                TajsSaveIdentity first = firstBinding.Identity!;

                File.Move(original, renamed);
                TajsSaveIdentity renamedRaw = TajsSaveIdentity.FromFile(renamed, "world")!;
                TajsSaveIdentity renamedIdentity = registry.Rebind(renamed, "world", first)!;
                Assert.Equal(first.OwnershipKey, renamedIdentity.OwnershipKey);
                Assert.Equal(first.RevisionKey, renamedRaw.RevisionKey);

                File.Copy(renamed, copy);
                TajsSaveIdentity copied = registry.Rebind(copy, "world", renamedIdentity)!;
                Assert.NotEqual(first.OwnershipKey, copied.OwnershipKey);
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
        public void SaveIdentityRegistryReportsWriteFailureWithoutCrossContamination()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveIdentityFailure-" + Guid.NewGuid().ToString("N"));
            string original = Path.Combine(root, "original.save");
            string copy = Path.Combine(root, "copy.save");
            string invalidRegistryRoot = Path.Combine(root, "registry-root");
            var diagnostics = new System.Collections.Generic.List<string>();
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(original, new byte[] { 1, 2, 3 });
                File.Copy(original, copy);
                File.WriteAllText(invalidRegistryRoot, "a file, not a directory");

                var registry = new TajsSaveIdentityRegistry(invalidRegistryRoot, diagnostics.Add);
                TajsSaveIdentityBindingResult first = registry.ResolveDetailed(original, "world");
                TajsSaveIdentityBindingResult second = registry.ResolveDetailed(copy, "world");

                Assert.Equal(
                    TajsSaveIdentityBindingStatus.IdentityUsableForSessionBindingPersistenceFailed,
                    first.Status);
                Assert.True(first.IsUsableForSession);
                Assert.False(first.IsBindingPersisted);
                Assert.Equal(first.Status, second.Status);
                Assert.NotEqual(first.Identity!.OwnershipKey, second.Identity!.OwnershipKey);
                Assert.Single(diagnostics);

                TajsSaveIdentityBindingResult rebound = registry.RebindDetailed(original, "world", first.Identity);
                Assert.Equal(
                    TajsSaveIdentityBindingStatus.IdentityUsableForSessionBindingPersistenceFailed,
                    rebound.Status);
                Assert.Equal(first.Identity.OwnershipKey, rebound.Identity!.OwnershipKey);
                Assert.Single(diagnostics);
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
        public void SaveIdentityRegistryFailsClosedOnInvalidRegistryData()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveIdentityAmbiguous-" + Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "slot.save");
            string registryRoot = Path.Combine(root, "sidecars");
            string registryPath = Path.Combine(registryRoot, "_identity-bindings.tsv");
            try
            {
                Directory.CreateDirectory(registryRoot);
                File.WriteAllBytes(savePath, new byte[] { 1, 2, 3 });
                File.WriteAllText(registryPath, "not-a-registry");
                var registry = new TajsSaveIdentityRegistry(registryRoot);

                TajsSaveIdentityBindingResult result = registry.ResolveDetailed(savePath, "world");

                Assert.Equal(TajsSaveIdentityBindingStatus.IdentityAmbiguous, result.Status);
                Assert.False(result.IsUsableForSession);
                Assert.NotNull(result.Identity);
                Assert.Equal("not-a-registry", File.ReadAllText(registryPath));
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
        public void SaveIdentityRegistryReadsAndUpgradesExistingV1Bindings()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveIdentityV1-" + Guid.NewGuid().ToString("N"));
            string savePath = Path.Combine(root, "slot.save");
            string registryPath = Path.Combine(root, "_identity-bindings.tsv");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(savePath, new byte[] { 1, 2, 3 });
                TajsSaveIdentity raw = TajsSaveIdentity.FromFile(savePath, "world")!;
                File.WriteAllLines(
                    registryPath,
                    new[]
                    {
                        "TajsSaveIdentityRegistryV1",
                        string.Join(
                            "\t",
                            "B",
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw.PhysicalPath!)),
                            Convert.ToBase64String(Encoding.UTF8.GetBytes("world")),
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw.PhysicalKey)),
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw.OwnershipKey)))
                    });

                var registry = new TajsSaveIdentityRegistry(root);
                TajsSaveIdentityBindingResult result = registry.ResolveDetailed(savePath, "world");

                Assert.Equal(TajsSaveIdentityBindingStatus.IdentityResolvedAndBindingPersisted, result.Status);
                Assert.Equal(raw.OwnershipKey, result.Identity!.OwnershipKey);
                Assert.Equal("TajsSaveIdentityRegistryV2", File.ReadLines(registryPath).First());
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
        public void CompatibilityReportIsImmutableAndRequiresStableIds()
        {
            var report = new CompatibilityReport(
                "TajsProfiler",
                "Dumping",
                CompatibilityState.Degraded,
                "expected",
                "observed",
                "reason");

            Assert.Equal("TajsProfiler", report.ModId);
            Assert.Equal("Dumping", report.ComponentId);
            Assert.Equal(CompatibilityState.Degraded, report.State);
            Assert.Equal("expected", report.Expected);
            Assert.Equal("observed", report.Observed);
            Assert.Equal("reason", report.Reason);
            var nullText = new CompatibilityReport(
                "TajsProfiler",
                "Runtime",
                CompatibilityState.Disabled,
                null,
                null,
                null);
            Assert.Equal(string.Empty, nullText.Expected);
            Assert.Equal(string.Empty, nullText.Observed);
            Assert.Equal(string.Empty, nullText.Reason);
            Assert.Throws<ArgumentException>(() => new CompatibilityReport(
                " ",
                "Dumping",
                CompatibilityState.Disabled,
                "",
                "",
                ""));
            Assert.Throws<ArgumentException>(() => new CompatibilityReport(
                "TajsProfiler",
                "",
                CompatibilityState.Disabled,
                "",
                "",
                ""));
        }

        [Fact]
        public void AssemblyBuildInfoReadsMetadataAndOnlyUsesExplicitTimestampPath()
        {
            Assembly assembly = typeof(CommonContractsTests).Assembly;
            AssemblyBuildInfo withoutPath = AssemblyBuildInfo.Read(assembly);

            Assert.Equal("9.8.7", withoutPath.Version);
            Assert.Equal("Test", withoutPath.Configuration);
            Assert.Equal("abcdef123456", withoutPath.GitCommit);
            Assert.Null(withoutPath.BuildTimestampUtc);

            string temporaryPath = Path.GetTempFileName();
            var expectedTimestamp = new DateTime(2026, 8, 23, 10, 11, 12, DateTimeKind.Utc);
            try
            {
                File.SetLastWriteTimeUtc(temporaryPath, expectedTimestamp);
                AssemblyBuildInfo withPath = AssemblyBuildInfo.Read(assembly, temporaryPath);

                Assert.NotNull(withPath.BuildTimestampUtc);
                Assert.Equal(expectedTimestamp, withPath.BuildTimestampUtc!.Value);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        [Fact]
        public void IntegerSettingDescriptorNormalizesAndValidatesBoundsAndStep()
        {
            SettingDescriptor descriptor = SettingDescriptor.Integer(
                "TajsTweaks",
                "Tweaks",
                "unlocked_speed_max",
                "Maximum unlocked speed",
                "Maximum accepted simulation speed.",
                defaultValue: 100,
                minimum: 20,
                maximum: 500,
                step: 5,
                applyMode: SettingApplyMode.Immediate);

            Assert.Equal("TajsTweaks.unlocked_speed_max", descriptor.StableId);
            Assert.True(descriptor.TryNormalize(125L, out object normalized, out _));
            Assert.Equal(125, normalized);
            Assert.False(descriptor.TryNormalize(126, out _, out string stepError));
            Assert.Contains("increments", stepError);
            Assert.False(descriptor.TryNormalize(501, out _, out string rangeError));
            Assert.Contains("between", rangeError);
        }

        [Fact]
        public void ChoiceSettingRequiresADeclaredStableValue()
        {
            SettingDescriptor descriptor = SettingDescriptor.Choice(
                "TajsPerformance",
                "Performance",
                "quality",
                "Quality",
                "Example choice.",
                "low",
                new[] { new SettingChoice("low", "Low"), new SettingChoice("very_low", "Very low") },
                applyMode: SettingApplyMode.ReloadSave,
                flags: SettingFlags.Experimental);

            Assert.True(descriptor.TryNormalize("very_low", out object normalized, out _));
            Assert.Equal("very_low", normalized);
            Assert.False(descriptor.TryNormalize("ultra", out _, out _));
        }

        [Fact]
        public void SettingDescriptorNormalizesBooleanFloatAndStringBranches()
        {
            SettingDescriptor boolean = SettingDescriptor.Boolean(
                "TajsCore",
                "Core",
                "enabled",
                "Enabled",
                "Boolean test.",
                false);
            Assert.True(boolean.TryNormalize("true", out object normalizedBoolean, out _));
            Assert.True(Assert.IsType<bool>(normalizedBoolean));

            SettingDescriptor floating = SettingDescriptor.Float(
                "TajsCore",
                "Core",
                "ratio",
                "Ratio",
                "Float test.",
                0.5,
                0,
                1,
                0.1);
            Assert.True(floating.TryNormalize("0.7", out object normalizedFloat, out _));
            Assert.Equal(0.7, normalizedFloat);
            Assert.False(floating.TryNormalize("not-a-number", out _, out string conversionError));
            Assert.Contains("Value could not be converted to", conversionError);
            Assert.False(floating.TryNormalize(null, out _, out _));

            SettingDescriptor text = SettingDescriptor.String(
                "TajsCore",
                "Core",
                "label",
                "Label",
                "String test.",
                "default");
            Assert.True(text.TryNormalize("custom", out object normalizedText, out _));
            Assert.Equal("custom", normalizedText);
            Assert.False(text.TryNormalize(42, out _, out _));
        }

        [Fact]
        public void SettingDescriptorRejectsDuplicateChoicesAndNonFiniteNumericMetadata()
        {
            Assert.Throws<ArgumentException>(() => SettingDescriptor.Choice(
                "TajsCore",
                "Core",
                "choice",
                "Choice",
                "Choice test.",
                "same",
                new[] { new SettingChoice("same", "First"), new SettingChoice("same", "Second") }));
            var minimumException = Assert.Throws<ArgumentOutOfRangeException>(() => SettingDescriptor.Float(
                "TajsCore",
                "Core",
                "nan",
                "NaN",
                "NaN test.",
                0,
                double.NaN,
                1,
                0.1));
            Assert.Equal("minimum", minimumException.ParamName);
            var maximumException = Assert.Throws<ArgumentOutOfRangeException>(() => SettingDescriptor.Float(
                "TajsCore",
                "Core",
                "infinity",
                "Infinity",
                "Infinity test.",
                0,
                0,
                double.PositiveInfinity,
                0.1));
            Assert.Equal("maximum", maximumException.ParamName);
            var nonFiniteStepException = Assert.Throws<ArgumentOutOfRangeException>(() => SettingDescriptor.Float(
                "TajsCore",
                "Core",
                "step",
                "Step",
                "Step test.",
                0,
                0,
                1,
                double.NegativeInfinity));
            Assert.Equal("step", nonFiniteStepException.ParamName);
            var nonPositiveStepException = Assert.Throws<ArgumentOutOfRangeException>(() => SettingDescriptor.Float(
                "TajsCore",
                "Core",
                "step_zero",
                "Step zero",
                "Step test.",
                0,
                0,
                1,
                0));
            Assert.Equal("step", nonPositiveStepException.ParamName);
        }

        [Fact]
        public void SettingDescriptorRejectsDotsInStableIdComponents()
        {
            Assert.Throws<ArgumentException>(() => SettingDescriptor.Boolean(
                "Tajs.Core",
                "Core",
                "enabled",
                "Enabled",
                "Dot in mod ID.",
                false));
            Assert.Throws<ArgumentException>(() => SettingDescriptor.Boolean(
                "TajsCore",
                "Core",
                "feature.enabled",
                "Enabled",
                "Dot in setting key.",
                false));
        }
    }
}
