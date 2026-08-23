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
            Assert.Throws<ArgumentException>(() => new CompatibilityReport(
                " ", "Dumping", CompatibilityState.Disabled, "", "", ""));
            Assert.Throws<ArgumentException>(() => new CompatibilityReport(
                "TajsProfiler", "", CompatibilityState.Disabled, "", "", ""));
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
                new[]
                {
                    new SettingChoice("low", "Low"),
                    new SettingChoice("very_low", "Very low"),
                },
                applyMode: SettingApplyMode.ReloadSave,
                flags: SettingFlags.Experimental);

            Assert.True(descriptor.TryNormalize("very_low", out object normalized, out _));
            Assert.Equal("very_low", normalized);
            Assert.False(descriptor.TryNormalize("ultra", out _, out _));
        }
    }
}
