using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Logging;
using TajsCOI.Common.Compatibility;
using TajsCOI.Core.Infrastructure;
using TajsCOI.Core.Runtime;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests
{
    public sealed class RuntimeTests
    {
        [Fact]
        public void CompatibilityReportsReplaceByOrdinalKeyAndSnapshotsAreSortedCopies()
        {
            var runtime = new TajsRuntime();
            runtime.ReportCompatibility(Report("TajsTweaks", "UnlockedSpeed", CompatibilityState.Disabled));
            runtime.ReportCompatibility(Report("TajsProfiler", "Dumping", CompatibilityState.Degraded));
            runtime.ReportCompatibility(Report("TajsTweaks", "UnlockedSpeed", CompatibilityState.Compatible));

            IReadOnlyList<CompatibilityReport> first = runtime.GetCompatibilitySnapshot();
            Assert.Equal(2, first.Count);
            Assert.Equal("TajsProfiler", first[0].ModId);
            Assert.Equal("Dumping", first[0].ComponentId);
            Assert.Equal(CompatibilityState.Degraded, first[0].State);
            Assert.Equal("TajsTweaks", first[1].ModId);
            Assert.Equal(CompatibilityState.Compatible, first[1].State);

            runtime.ReportCompatibility(Report("TajsCore", "HarmonyRuntime", CompatibilityState.Compatible));
            Assert.Equal(2, first.Count);
            Assert.Equal(3, runtime.GetCompatibilitySnapshot().Count);
        }

        [Fact]
        public void LoggerPreservesPrefixSeverityOnceSemanticsAndException()
        {
            var runtime = new TajsRuntime();
            var logger = runtime.GetLogger("TajsProfiler", "Dumping");
            string unique = Guid.NewGuid().ToString("N");
            var entries = new List<LogEntry>();
            Action<LogEntry> handler = entry => entries.Add(entry);
            LogType acceptedBefore = Log.AcceptedLogTypes;
            Log.LogReceivedThreadStatic += handler;
            Log.AcceptedLogTypes = LogType.All;
            Log.ClearPriorWarningsAndErrors();

            try
            {
                logger.Info("info-" + unique);
                logger.WarningOnce("once-" + unique);
                logger.Info("separator-" + unique);
                logger.WarningOnce("once-" + unique);
                logger.ErrorOnce("error-once-" + unique);
                logger.Info("error-separator-" + unique);
                logger.ErrorOnce("error-once-" + unique);
                var exception = new InvalidOperationException("failure-" + unique);
                logger.Exception(exception, "context-" + unique);

                Assert.Contains(entries, entry =>
                    entry.Type == LogType.Info &&
                    entry.Message == $"[TajsCOI][TajsProfiler][Dumping] info-{unique}");
                Assert.Single(entries.FindAll(entry =>
                    entry.Type == LogType.Warning &&
                    entry.Message == $"[TajsCOI][TajsProfiler][Dumping] once-{unique}"));
                Assert.Single(entries.FindAll(entry =>
                    entry.Type == LogType.Error &&
                    entry.Message == $"[TajsCOI][TajsProfiler][Dumping] error-once-{unique}"));
                LogEntry exceptionEntry = Assert.Single(entries.FindAll(entry => entry.Type == LogType.Exception));
                Assert.Equal($"[TajsCOI][TajsProfiler][Dumping] context-{unique}", exceptionEntry.Message);
                Assert.True(exceptionEntry.Exception.HasValue);
                Assert.Same(exception, exceptionEntry.Exception.Value);
            }
            finally
            {
                Log.AcceptedLogTypes = acceptedBefore;
                Log.LogReceivedThreadStatic -= handler;
            }
        }

        [Fact]
        public void HarmonyRuntimeInspectionComparesPhysicalAndLoadedAssemblies()
        {
            Assembly harmonyAssembly = typeof(Harmony).Assembly;
            string outputRoot = Path.GetDirectoryName(harmonyAssembly.Location)!;

            HarmonyRuntimeInfo matching = HarmonyRuntimeInfo.Inspect(outputRoot);

            Assert.Equal(CompatibilityState.Compatible, matching.State);
            Assert.Equal(matching.PackagedVersion, matching.LoadedVersion);
        }

        [Fact]
        public void HarmonyRuntimeInspectionDisablesMissingPhysicalAssembly()
        {
            string missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            HarmonyRuntimeInfo missing = HarmonyRuntimeInfo.Inspect(missingRoot);

            Assert.Equal(CompatibilityState.Disabled, missing.State);
            Assert.Equal("unavailable", missing.PackagedVersion);
        }

        private static CompatibilityReport Report(
            string modId,
            string componentId,
            CompatibilityState state) =>
            new(modId, componentId, state, "expected", "observed", "reason");
    }
}
