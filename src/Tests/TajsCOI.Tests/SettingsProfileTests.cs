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
                settings.Register(
                    SettingDescriptor.Boolean(
                        "ProfileMod",
                        "Profile Mod",
                        "safe",
                        "Safe",
                        "Safe setting",
                        false,
                        flags: SettingFlags.ProfileSafe));
                settings.Register(
                    SettingDescriptor.Integer(
                        "ProfileMod",
                        "Profile Mod",
                        "bounded",
                        "Bounded",
                        "Bounded setting",
                        10,
                        0,
                        20,
                        1,
                        flags: SettingFlags.ProfileSafe));
                settings.Register(
                    SettingDescriptor.Boolean(
                        "ProfileMod",
                        "Profile Mod",
                        "unsafe",
                        "Unsafe",
                        "Not approved",
                        false));

                var profile = new SettingsProfile(
                    1,
                    "test",
                    "demo",
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    new Dictionary<string, object>
                    {
                        ["ProfileMod.safe"] = true, ["ProfileMod.bounded"] = 999, ["ProfileMod.unsafe"] = true, ["FutureMod.missing"] = true,
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
                settings.Register(
                    SettingDescriptor.Boolean(
                        "ProfileMod",
                        "Profile Mod",
                        "safe",
                        "Safe",
                        "Safe setting",
                        true,
                        flags: SettingFlags.ProfileSafe));
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), Path.Combine(root, "profiles"));
                Assert.Contains("1 profile-safe", service.CaptureProfile("demo"));
                Assert.True(service.TryDuplicate("demo", "copy", out _, out string error), error);
                Assert.True(service.TryGet("copy", out SettingsProfile? copy));
                Assert.NotNull(copy);

                var reloaded = new TajsSettingsProfileService(settings, new TajsRuntime(), Path.Combine(root, "profiles"));
                Assert.True(reloaded.TryGet("demo", out _));
                Assert.True(reloaded.TryGet("copy", out _));
                Assert.True(File.Exists(Path.Combine(
                    root,
                    "profiles",
                    TajsSettingsProfileService.GetStorageFileNameForTests("demo") + ".json")));
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
        public void PreviewApprovalBecomesStaleWhenUnderlyingSettingChanges()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                settings.Register(SettingDescriptor.Boolean("ProfileMod", "Profile Mod", "safe", "Safe", "Safe", false, flags: SettingFlags.ProfileSafe));
                Assert.True(settings.TrySet("ProfileMod", "safe", true).Success);
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), root);
                Assert.True(service.TryCapture("demo", Array.Empty<string>(), Array.Empty<string>(), out SettingsProfile? profile, out string captureError), captureError);
                Assert.NotNull(profile);

                SettingsProfilePreview approved = service.Preview(profile!);
                Assert.True(approved.CanApply);
                Assert.True(settings.TrySet("ProfileMod", "safe", false).Success);
                SettingsProfilePreview changed = service.Preview(profile!);
                Assert.False(approved.Matches(changed));
                Assert.True(service.Apply(profile!).Success);
                Assert.True(settings.Get<bool>("ProfileMod", "safe"));
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
        public void CaptureAndRestoreDefaultsStayWithinProfileSafeSettings()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                settings.Register(
                    SettingDescriptor.Boolean(
                        "ProfileMod",
                        "Profile Mod",
                        "safe",
                        "Safe",
                        "Safe setting",
                        false,
                        flags: SettingFlags.ProfileSafe));
                settings.Register(
                    SettingDescriptor.Boolean(
                        "ProfileMod",
                        "Profile Mod",
                        "unsafe",
                        "Unsafe",
                        "Unsafe setting",
                        false));
                Assert.True(settings.TrySet("ProfileMod", "safe", true).Success);
                Assert.True(settings.TrySet("ProfileMod", "unsafe", true).Success);

                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), Path.Combine(root, "profiles"));
                Assert.True(service.TryCapture("selected", Array.Empty<string>(), Array.Empty<string>(), out SettingsProfile? profile, out string captureError), captureError);
                Assert.NotNull(profile);
                Assert.True(profile!.Values.ContainsKey("ProfileMod.safe"));
                Assert.False(profile.Values.ContainsKey("ProfileMod.unsafe"));

                SettingsProfileApplyResult result = service.RestoreDefaults(profile);
                Assert.Equal(1, result.AppliedCount);
                Assert.False(settings.Get<bool>("ProfileMod", "safe"));
                Assert.True(settings.Get<bool>("ProfileMod", "unsafe"));
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
                new Dictionary<string, object> { ["ProfileMod.setting"] = new() }));
            Assert.Throws<ArgumentException>(() => new SettingsProfile(
                1,
                "test",
                "invalid",
                Array.Empty<string>(),
                Array.Empty<string>(),
                new Dictionary<string, object> { [string.Empty] = true }));
        }

        [Fact]
        public void RenameDeleteImportAndExportRoundTripThroughAtomicFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            string exportPath = Path.Combine(root, "export.json");
            try
            {
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                settings.Register(
                    SettingDescriptor.Boolean(
                        "ProfileMod",
                        "Profile Mod",
                        "safe",
                        "Safe",
                        "Safe setting",
                        true,
                        flags: SettingFlags.ProfileSafe));
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), Path.Combine(root, "profiles"));

                Assert.Contains("1 profile-safe", service.CaptureProfile("demo"));
                Assert.True(service.TryExport("demo", exportPath, out string exportError), exportError);
                Assert.True(File.Exists(exportPath));
                Assert.True(service.TryRename("demo", "renamed", out _, out string renameError), renameError);
                Assert.False(service.TryGet("demo", out _));
                Assert.True(service.TryGet("renamed", out _));
                Assert.True(service.TryImport(exportPath, "imported", out _, out string importError), importError);
                Assert.True(service.TryGet("imported", out _));
                Assert.True(service.TryDelete("imported", out string deleteError), deleteError);
                Assert.False(service.TryGet("imported", out _));
                Assert.False(File.Exists(Path.Combine(root, "profiles", "imported.json")));
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
        public void StorageIdentityKeepsSanitizedNamesDistinctAndCaseInsensitiveNamesStable()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                settings.Register(SettingDescriptor.Boolean("ProfileMod", "Profile Mod", "safe", "Safe", "Safe", false, flags: SettingFlags.ProfileSafe));
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), Path.Combine(root, "profiles"));

                Assert.True(service.TryCapture("a.b", Array.Empty<string>(), Array.Empty<string>(), out _, out string firstError), firstError);
                Assert.True(service.TryCapture("a_b", Array.Empty<string>(), Array.Empty<string>(), out _, out string secondError), secondError);
                Assert.True(service.TryCapture("Δ punctuation!", Array.Empty<string>(), Array.Empty<string>(), out _, out string unicodeError), unicodeError);
                Assert.True(service.TryCapture("a:b/c?", Array.Empty<string>(), Array.Empty<string>(), out _, out string punctuationError), punctuationError);
                Assert.True(service.TryCapture("Case", Array.Empty<string>(), Array.Empty<string>(), out _, out string caseError), caseError);
                Assert.True(service.TryCapture("case", Array.Empty<string>(), Array.Empty<string>(), out _, out string caseVariantError), caseVariantError);

                Assert.Equal(5, service.List().Count);
                Assert.NotEqual(
                    TajsSettingsProfileService.GetStorageFileNameForTests("a.b"),
                    TajsSettingsProfileService.GetStorageFileNameForTests("a_b"));
                Assert.Equal(
                    TajsSettingsProfileService.GetStorageFileNameForTests("Case"),
                    TajsSettingsProfileService.GetStorageFileNameForTests("case"));
                Assert.Equal(5, Directory.EnumerateFiles(Path.Combine(root, "profiles"), "*.json").Count());
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
        public void LegacyProfileMigratesWithoutOverwritingCollisionAndRenameDuplicateDeleteUseNewIdentity()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            string profilesRoot = Path.Combine(root, "profiles");
            try
            {
                Directory.CreateDirectory(profilesRoot);
                File.WriteAllText(
                    Path.Combine(profilesRoot, "a_b.json"),
                    "{\"schema\":1,\"suite_version\":\"test\",\"name\":\"a.b\",\"categories\":[],\"modules\":[],\"values\":{}}");
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), profilesRoot);

                string migratedPath = Path.Combine(profilesRoot, TajsSettingsProfileService.GetStorageFileNameForTests("a.b") + ".json");
                Assert.True(service.TryGet("a.b", out _));
                Assert.True(File.Exists(migratedPath));
                Assert.True(File.Exists(Path.Combine(profilesRoot, "a_b.json")));

                Assert.True(service.TryRename("a.b", "a_b", out _, out string renameError), renameError);
                Assert.False(service.TryGet("a.b", out _));
                Assert.True(service.TryGet("a_b", out _));
                Assert.False(File.Exists(migratedPath));
                Assert.False(File.Exists(Path.Combine(profilesRoot, "a_b.json")));

                Assert.True(service.TryDuplicate("a_b", "a.b", out _, out string duplicateError), duplicateError);
                Assert.True(service.TryDelete("a.b", out string deleteError), deleteError);
                Assert.False(service.TryGet("a.b", out _));
                Assert.True(service.TryGet("a_b", out _));
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
        public void StorageIdentityCollisionRefusesToOverwriteExistingJson()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI.ProfileTests", Guid.NewGuid().ToString("N"));
            string profilesRoot = Path.Combine(root, "profiles");
            try
            {
                Directory.CreateDirectory(profilesRoot);
                string target = Path.Combine(profilesRoot, TajsSettingsProfileService.GetStorageFileNameForTests("a.b") + ".json");
                const string existing = "{\"schema\":1,\"suite_version\":\"test\",\"name\":\"a_b\",\"categories\":[],\"modules\":[],\"values\":{}}";
                File.WriteAllText(target, existing);
                var settings = new TajsSettings(Path.Combine(root, "settings.json"), new SettingsTestsNullLogger());
                var service = new TajsSettingsProfileService(settings, new TajsRuntime(), profilesRoot);

                Assert.False(service.TryCapture("a.b", Array.Empty<string>(), Array.Empty<string>(), out _, out string error));
                Assert.Contains("collision", error, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(existing, File.ReadAllText(target));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static SettingsProfilePreviewState State(SettingsProfilePreview preview, string stableId) =>
            preview.Entries.Single(entry => entry.StableId == stableId).State;

        private sealed class SettingsTestsNullLogger : Common.Logging.ITajsLogger
        {
            public void Info(string message)
            {
            }

            public void Warning(string message)
            {
            }

            public void WarningOnce(string message)
            {
            }

            public void Error(string message)
            {
            }

            public void ErrorOnce(string message)
            {
            }

            public void Exception(Exception exception, string? message = null)
            {
            }
        }
    }
}
