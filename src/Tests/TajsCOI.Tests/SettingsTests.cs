// Taj's COI Mods | SettingsTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Text;
using TajsCOI.Common.Logging;
using TajsCOI.Common.Settings;
using TajsCOI.Core.Settings;
using TajsCOI.Performance;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class SettingsTests
    {
        [Fact]
        public void Performance_catalog_migrates_every_legacy_config_key()
        {
            Assert.Equal(9, PerformanceSettingsCatalog.All.Count);
            Assert.Equal(9, PerformanceSettingsCatalog.All.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(PerformanceSettingsCatalog.All, descriptor =>
            {
                Assert.Equal(PerformanceSettingsCatalog.ModId, descriptor.ModId);
                Assert.Equal(SettingScope.Global, descriptor.Scope);
                Assert.True((descriptor.Flags & SettingFlags.Experimental) != 0);
            });

            SettingDescriptor immediate = Assert.Single(
                PerformanceSettingsCatalog.All, x => x.Key == "enable_manual_asset_trim");
            Assert.Equal(SettingApplyMode.Immediate, immediate.ApplyMode);
            Assert.All(
                PerformanceSettingsCatalog.All.Where(x => x.Key != "enable_manual_asset_trim"),
                descriptor => Assert.Equal(SettingApplyMode.RestartGame, descriptor.ApplyMode));
        }

        [Fact]
        public void Core_settings_do_not_reference_desktop_only_system_web_extensions()
        {
            Assert.DoesNotContain(
                typeof(TajsSettings).Assembly.GetReferencedAssemblies(),
                assembly => string.Equals(assembly.Name, "System.Web.Extensions", StringComparison.Ordinal));
        }

        [Fact]
        public void GlobalSettingPersistsNotifiesAndPreservesUnknownKeys()
        {
            string directory = CreateTemporaryDirectory();
            string path = Path.Combine(directory, "settings.json");
            try
            {
                File.WriteAllText(
                    path,
                    "{\"schema_version\":1,\"values\":{\"FutureMod.future_key\":\"keep-me\"}}",
                    Encoding.UTF8);
                var first = new TajsSettings(path, new NullLogger());
                SettingDescriptor descriptor = CreateSpeedDescriptor();
                first.Register(descriptor);
                Assert.Equal(100, first.Get<int>("TajsTweaks", "unlocked_speed_max"));

                SettingChangedEventArgs? observed = null;
                first.Changed += (_, change) => observed = change;
                SettingSetResult result = first.TrySet("TajsTweaks", "unlocked_speed_max", "125");

                Assert.True(result.Success);
                Assert.Equal(SettingApplyMode.Immediate, result.ApplyMode);
                Assert.NotNull(observed);
                Assert.Equal(100, observed!.OldValue);
                Assert.Equal(125, observed.NewValue);
                Assert.Contains("FutureMod.future_key", File.ReadAllText(path));

                var reloaded = new TajsSettings(path, new NullLogger());
                reloaded.Register(descriptor);
                Assert.Equal(125, reloaded.Get<int>("TajsTweaks", "unlocked_speed_max"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void InvalidPrimaryRecoversBackupAndInvalidValueFallsBackToDefault()
        {
            string directory = CreateTemporaryDirectory();
            string path = Path.Combine(directory, "settings.json");
            try
            {
                File.WriteAllText(path, "not json", Encoding.UTF8);
                File.WriteAllText(
                    path + ".bak",
                    "{\"schema_version\":1,\"values\":{\"TajsTweaks.unlocked_speed_max\":9999}}",
                    Encoding.UTF8);

                var settings = new TajsSettings(path, new NullLogger());
                settings.Register(CreateSpeedDescriptor());

                Assert.Equal(100, settings.Get<int>("TajsTweaks", "unlocked_speed_max"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void PerSaveSettingChangesInMemoryWithoutWritingGlobalFile()
        {
            string directory = CreateTemporaryDirectory();
            string path = Path.Combine(directory, "settings.json");
            try
            {
                var settings = new TajsSettings(path, new NullLogger());
                settings.Register(SettingDescriptor.Boolean(
                    "TajsTweaks",
                    "Tweaks",
                    "island_option",
                    "Island option",
                    "Test per-save setting.",
                    false,
                    scope: SettingScope.PerSave));

                Assert.True(settings.TrySet("TajsTweaks", "island_option", true).Success);
                Assert.True(settings.Get<bool>("TajsTweaks", "island_option"));
                Assert.False(File.Exists(path));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static SettingDescriptor CreateSpeedDescriptor() =>
            SettingDescriptor.Integer(
                "TajsTweaks",
                "Tweaks",
                "unlocked_speed_max",
                "Maximum unlocked speed",
                "Maximum accepted speed.",
                100,
                20,
                500,
                1);

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "tajs-settings-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private sealed class NullLogger : ITajsLogger
        {
            public void Info(string message) { }
            public void Warning(string message) { }
            public void WarningOnce(string message) { }
            public void Error(string message) { }
            public void ErrorOnce(string message) { }
            public void Exception(Exception exception, string? message = null) { }
        }
    }
}
