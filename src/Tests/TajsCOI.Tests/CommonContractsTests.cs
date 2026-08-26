// Taj's COI Mods | CommonContractsTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Reflection;
using TajsCOI.Common.Build;
using TajsCOI.Common.Compatibility;
using TajsCOI.Common.Settings;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class CommonContractsTests
    {
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
