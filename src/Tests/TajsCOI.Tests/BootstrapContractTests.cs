// Taj's COI Mods | BootstrapContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using TajsCOI.Bootstrap;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class BootstrapContractTests
    {
        [Fact]
        public void BootstrapAssemblyHasNoGameOrHarmonyReferences()
        {
            string[] references = typeof(BootstrapApi).Assembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)
                .ToArray();

            Assert.DoesNotContain("Mafi", references, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mafi.Core", references, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("0Harmony", references, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("HarmonyLib", references, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void MissingCanonicalHarmonyFailsClosedAndDisableIsExplicit()
        {
            string missing = Path.Combine(Path.GetTempPath(), "TajsCOI-no-harmony-" + Guid.NewGuid().ToString("N"), "0Harmony.dll");
            BootstrapStatus failed = BootstrapApi.Initialize(missing);
            Assert.Equal(BootstrapState.Failed, failed.State);
            Assert.False(failed.IsReady);

            BootstrapStatus disabled = BootstrapApi.Disable();
            Assert.Equal(BootstrapState.Disabled, disabled.State);
            Assert.Contains("no-bootstrap", disabled.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void InstallerRecordsOwnershipAndSupportsVerifyDisableRepairAndUninstall()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-bootstrap-" + Guid.NewGuid().ToString("N"));
            string sources = Path.Combine(root, "sources");
            Directory.CreateDirectory(sources);
            string bootstrapSource = Path.Combine(sources, "bootstrap.dll");
            string harmonySource = Path.Combine(sources, "harmony.dll");
            File.WriteAllText(bootstrapSource, "bootstrap-v1");
            File.WriteAllText(harmonySource, "harmony-v1");
            string winhttp = Path.Combine(root, "winhttp.dll");
            File.WriteAllText(winhttp, "external-doorstop");

            try
            {
                var request = new BootstrapInstallRequest(root, bootstrapSource, harmonySource);
                BootstrapInstallResult installed = BootstrapInstaller.Install(request);
                Assert.Equal(BootstrapInstallState.Installed, installed.State);
                Assert.Equal("external-doorstop", File.ReadAllText(winhttp));
                Assert.Equal(BootstrapInstallState.Verified, BootstrapInstaller.Verify(root).State);

                File.WriteAllText(bootstrapSource, "bootstrap-v2");
                Assert.Equal(BootstrapInstallState.Installed, BootstrapInstaller.Repair(request).State);
                Assert.Equal(BootstrapInstallState.Verified, BootstrapInstaller.Verify(root).State);

                Assert.Equal(BootstrapInstallState.Disabled, BootstrapInstaller.Disable(root).State);
                Assert.Equal(BootstrapInstallState.Disabled, BootstrapInstaller.Verify(root).State);
                Assert.Equal(BootstrapInstallState.Uninstalled, BootstrapInstaller.Uninstall(root).State);
                Assert.False(File.Exists(installed.ManifestPath));
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
        public void InstallerRefusesUnknownPayloadAndDiscoversRuntimeRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-bootstrap-" + Guid.NewGuid().ToString("N"));
            string managed = Path.Combine(root, "Captain of Industry_Data", "Managed");
            string sources = Path.Combine(root, "sources");
            Directory.CreateDirectory(managed);
            Directory.CreateDirectory(sources);
            string executable = Path.Combine(root, "Captain of Industry.exe");
            File.WriteAllText(executable, "runtime");
            string bootstrapSource = Path.Combine(sources, "bootstrap.dll");
            string harmonySource = Path.Combine(sources, "harmony.dll");
            File.WriteAllText(bootstrapSource, "expected");
            File.WriteAllText(harmonySource, "harmony");
            string target = Path.Combine(root, "TajsCOI", "Bootstrap", "TajsBootstrap.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "unknown-owner");

            try
            {
                Assert.Equal(root, BootstrapInstaller.DiscoverGameRoot(executable));
                BootstrapInstallResult result = BootstrapInstaller.Install(
                    new BootstrapInstallRequest(root, bootstrapSource, harmonySource));
                Assert.Equal(BootstrapInstallState.Refused, result.State);
                Assert.Equal("unknown-owner", File.ReadAllText(target));
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
