// Taj's COI Mods | SettingsProfileTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TajsCOI.Common.Profiles;
using TajsCOI.Common.Settings;
using TajsCOI.Core.Profiles;
using TajsCOI.Core.Runtime;
using TajsCOI.Core.Settings;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class SettingsProfileTests
    {
        [Fact]
        public void PreviewReportsUnknownUnsafeInvalidAndProposedEntries()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                settings.Register(SettingDescriptor.Boolean(
                    "ProfileMod", "Profile Mod", "safe", "Safe", "Safe setting", false,
                    flags: SettingFlags.ProfileSafe));
                settings.Register(SettingDescriptor.Integer(
                    "ProfileMod", "Profile Mod", "bounded", "Bounded", "Bounded setting", 10, 0, 20, 1,
                    flags: SettingFlags.ProfileSafe));
                settings.Register(SettingDescriptor.Boolean(
                    "ProfileMod", "Profile Mod", "unsafe", "Unsafe", "Not approved", false));

                var profile = new SettingsProfile(
                    1,
                    "test",
                    "demo",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    new Dictionary<string, object>
                    {
                        ["ProfileMod.safe"] = true,
                        ["ProfileMod.bounded"] = 999,
                        ["ProfileMod.unsafe"] = true,
                        ["FutureMod.missing"] = true,
                    });
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), root);

                SettingsProfilePreview preview = service.Preview(profile);
                Assert.Equal(SettingsProfilePreviewState.Proposed, State(preview, "ProfileMod.safe"));
                Assert.Equal(SettingsProfilePreviewState.Invalid, State(preview, "ProfileMod.bounded"));
                Assert.Equal(SettingsProfilePreviewState.Unavailable, State(preview, "ProfileMod.unsafe"));
                Assert.Equal(SettingsProfilePreviewState.Unavailable, State(preview, "FutureMod.missing"));

                SettingsProfileApplyResult result = service.Apply(profile);
                Assert.False(result.Success);
                Assert.Equal(0, result.AppliedCount);
                Assert.False(settings.Get<bool>("ProfileMod", "safe"));
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
        public void CaptureSaveReloadAndDuplicateUseAtomicProfileFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                settings.Register(SettingDescriptor.Boolean(
                    "ProfileMod", "Profile Mod", "safe", "Safe", "Safe setting", true,
                    flags: SettingFlags.ProfileSafe));
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), Path.Combine(root, "profiles"));
                Assert.Contains("1 profile-safe", service.CaptureProfile("demo"));
                Assert.True(service.TryDuplicate("demo", "copy", out _, out string error), error);
                Assert.True(service.TryGet("copy", out SettingsProfile? copy));
                Assert.NotNull(copy);

                var reloaded = new TajsSettingsProfileService(settings, new TajsRuntime(), Path.Combine(root, "profiles"));
                Assert.True(reloaded.TryGet("demo", out _));
                Assert.True(reloaded.TryGet("copy", out _));
                Assert.True(File.Exists(Path.Combine(root, "profiles", "demo.json")));
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
        public void ProfileValuesAreRestrictedToJsonPrimitives()
        {
            Assert.Throws<ArgumentException>(() => new SettingsProfile(
                1,
                "test",
                "invalid",
                Array.Empty<string>(),
                Array.Empty<string>(),
                new Dictionary<string, object> { ["ProfileMod.setting"] = new object() }));
            Assert.Throws<ArgumentException>(() => new SettingsProfile(
                1,
                "test",
                "invalid",
                Array.Empty<string>(),
                Array.Empty<string>(),
                new Dictionary<string, object> { [string.Empty] = true }));
        }

        private static SettingsProfilePreviewState State(SettingsProfilePreview preview, string stableId) =>
            preview.Entries.Single(entry => entry.StableId == stableId).State;

        private sealed class SettingsTestsNullLogger : TajsCOI.Common.Logging.ITajsLogger
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
