using System;
using System.IO;
using System.Reflection;
using TajsCOI.Common.Build;
using TajsCOI.Common.Compatibility;
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
    }
}
