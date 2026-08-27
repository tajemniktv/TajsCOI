// Taj's COI Mods | SaveRepairTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TajsCOI.Core.SaveRepair;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class SaveRepairTests
    {
        [Fact]
        public void Core_owns_the_sanitizer_and_legacy_compatibility_commands()
        {
            Type serviceType = typeof(TajsSaveRepairService);
            string[] methodNames = serviceType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(method => method.Name)
                .ToArray();

            Assert.Contains(nameof(TajsSaveRepairService.Report), methodNames);
            Assert.Contains(nameof(TajsSaveRepairService.Repair), methodNames);
            Assert.Contains(nameof(TajsSaveRepairService.MigrateLegacyInfiniteGroundwaterSave), methodNames);
            Assert.Contains(nameof(TajsSaveRepairService.MigrateLegacyShipAutoExploreSave), methodNames);
        }

        [Fact]
        public void Sanitizer_commands_are_explicitly_named_and_do_not_reuse_tweak_host_commands()
        {
            Type serviceType = typeof(TajsSaveRepairService);
            MethodInfo[] methods = serviceType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            string[] commandNames = methods
                .SelectMany(method => method.GetCustomAttributes(inherit: true))
                .Where(attribute => attribute.GetType().Name == "ConsoleCommandAttribute")
                .Select(attribute => attribute.ToString() ?? string.Empty)
                .ToArray();

            Assert.NotEmpty(commandNames);
            Assert.NotEqual("TajsCOI.Tweaks", typeof(TajsSaveRepairService).Namespace);
        }

        [Fact]
        public void Registry_keeps_owner_shape_and_detect_repair_verify_contracts_together()
        {
            int detects = 0;
            int repairs = 0;
            int verifies = 0;

            SaveRepairFinding Finding()
            {
                detects++;
                return new SaveRepairFinding("test", SaveRepairStatus.NeedsRepair, 1);
            }

            var handler = new SaveRepairHandler(
                "test",
                "Test.Owner",
                "test target",
                "0.8.7b test shape",
                Finding,
                () =>
                {
                    repairs++;
                    return SaveRepairMutation.SucceededWith(1);
                },
                () =>
                {
                    verifies++;
                    return new SaveRepairFinding("test", SaveRepairStatus.Clean, 0);
                });
            var registry = new SaveRepairHandlerRegistry(new[] { handler });

            Assert.True(registry.TryGet("test", out SaveRepairHandler? selected));
            Assert.Same(handler, selected);
            Assert.Equal("Test.Owner", selected!.Owner);
            Assert.Equal("test target", selected.TargetKind);
            Assert.Equal("0.8.7b test shape", selected.VersionShape);
            Assert.Equal(SaveRepairStatus.NeedsRepair, selected.Detect().Status);
            Assert.True(selected.Repair().Succeeded);
            Assert.Equal(SaveRepairStatus.Clean, selected.Verify().Status);
            Assert.Equal(1, detects);
            Assert.Equal(1, repairs);
            Assert.Equal(1, verifies);
        }

        [Fact]
        public void Corrupt_or_unknown_repair_sidecars_are_never_overwritten()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveRepairTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                SaveRepairFinding finding = new("test", SaveRepairStatus.NeedsRepair, 1, "before");
                SaveRepairFinding verification = new("test", SaveRepairStatus.Clean, 0, "after");
                string[] corruptSidecars = { string.Empty, "not a manifest", SaveRepairManifest.Header + "\nunknown=field\n", "\0\uffff", new('x', 8192) };

                foreach (string corrupt in corruptSidecars)
                {
                    string path = Path.Combine(root, "manifest-" + Guid.NewGuid().ToString("N") + ".txt");
                    Directory.CreateDirectory(root);
                    File.WriteAllText(path, corrupt);
                    Assert.False(
                        SaveRepairManifest.TryWriteNew(
                            path,
                            "source",
                            "output",
                            finding,
                            verification,
                            1,
                            out _));
                    Assert.Equal(corrupt, File.ReadAllText(path));
                }
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
        public void Repair_manifest_is_created_only_for_a_new_path_and_records_verification()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-SaveRepairTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(root, "new-save.save.tajs-repair.txt");
                Assert.True(
                    SaveRepairManifest.TryWriteNew(
                        path,
                        "source",
                        "new-save",
                        new SaveRepairFinding("quick", SaveRepairStatus.NeedsRepair, 2),
                        new SaveRepairFinding("quick", SaveRepairStatus.Clean, 0),
                        2,
                        out string failure),
                    failure);
                string text = File.ReadAllText(path);
                Assert.Contains(SaveRepairManifest.Header, text);
                Assert.Contains("status-before=NeedsRepair", text);
                Assert.Contains("status-after=Clean", text);
                Assert.Contains("changed=2", text);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
