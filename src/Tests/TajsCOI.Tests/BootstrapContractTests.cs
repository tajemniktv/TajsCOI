// Taj's COI Mods | BootstrapContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public void BootstrapExposesTheUnityDoorstopEntrypoint()
        {
            Type? entrypoint = typeof(BootstrapApi).Assembly.GetType("Doorstop.Entrypoint");
            Assert.NotNull(entrypoint);
            MethodInfo? start = entrypoint!.GetMethod(
                "Start",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            Assert.NotNull(start);
            Assert.Equal(typeof(void), start!.ReturnType);
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
            string proxySource = Path.Combine(sources, "winhttp.dll");
            File.WriteAllText(bootstrapSource, "bootstrap-v1");
            File.WriteAllText(harmonySource, "harmony-v1");
            File.WriteAllText(proxySource, "doorstop-v1");
            string version = Path.Combine(root, "version.dll");
            File.WriteAllText(version, "external-version-proxy");

            try
            {
                var request = new BootstrapInstallRequest(root, bootstrapSource, harmonySource, proxySource);
                BootstrapInstallResult installed = BootstrapInstaller.Install(request);
                Assert.Equal(BootstrapInstallState.Installed, installed.State);
                Assert.Equal("doorstop-v1", File.ReadAllText(Path.Combine(root, "winhttp.dll")));
                Assert.Equal("external-version-proxy", File.ReadAllText(version));
                Assert.Contains(
                    "target_assembly=TajsCOI/Bootstrap/TajsBootstrap.dll",
                    File.ReadAllText(Path.Combine(root, "doorstop_config.ini")));
                Assert.Equal(BootstrapInstallState.Verified, BootstrapInstaller.Verify(root).State);

                File.WriteAllText(bootstrapSource, "bootstrap-v2");
                Assert.Equal(BootstrapInstallState.Installed, BootstrapInstaller.Repair(request).State);
                Assert.Equal(BootstrapInstallState.Verified, BootstrapInstaller.Verify(root).State);

                Assert.Equal(BootstrapInstallState.Disabled, BootstrapInstaller.Disable(root).State);
                Assert.Equal(BootstrapInstallState.Disabled, BootstrapInstaller.Verify(root).State);
                Assert.Equal(BootstrapInstallState.Uninstalled, BootstrapInstaller.Uninstall(root).State);
                Assert.False(File.Exists(installed.ManifestPath));
                Assert.True(File.Exists(version));
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
        public void DisabledInstallManifestPreventsEarlyBootstrapInitialization()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-bootstrap-disabled-" + Guid.NewGuid().ToString("N"));
            string sources = Path.Combine(root, "sources");
            Directory.CreateDirectory(sources);
            string bootstrapSource = Path.Combine(sources, "bootstrap.dll");
            string harmonySource = Path.Combine(sources, "harmony.dll");
            string proxySource = Path.Combine(sources, "winhttp.dll");
            File.WriteAllText(bootstrapSource, "bootstrap");
            File.WriteAllText(harmonySource, "harmony");
            File.WriteAllText(proxySource, "doorstop");

            try
            {
                var request = new BootstrapInstallRequest(root, bootstrapSource, harmonySource, proxySource);
                Assert.Equal(BootstrapInstallState.Installed, BootstrapInstaller.Install(request).State);
                Assert.Equal(BootstrapInstallState.Disabled, BootstrapInstaller.Disable(root).State);

                BootstrapStatus status = BootstrapApi.InitializeFromGameRoot(root);
                Assert.Equal(BootstrapState.Disabled, status.State);
                Assert.Contains("install manifest", status.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                BootstrapApi.Disable();
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
            string proxySource = Path.Combine(sources, "winhttp.dll");
            File.WriteAllText(bootstrapSource, "expected");
            File.WriteAllText(harmonySource, "harmony");
            File.WriteAllText(proxySource, "doorstop");
            string target = Path.Combine(root, "TajsCOI", "Bootstrap", "TajsBootstrap.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "unknown-owner");

            try
            {
                Assert.Equal(root, BootstrapInstaller.DiscoverGameRoot(executable));
                BootstrapInstallResult result = BootstrapInstaller.Install(
                    new BootstrapInstallRequest(root, bootstrapSource, harmonySource, proxySource));
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

        [Fact]
        public void InstallerRefusesForeignDoorstopProxyOrConfiguration()
        {
            string root = Path.Combine(Path.GetTempPath(), "TajsCOI-bootstrap-foreign-doorstop-" + Guid.NewGuid().ToString("N"));
            string sources = Path.Combine(root, "sources");
            Directory.CreateDirectory(sources);
            string bootstrapSource = Path.Combine(sources, "bootstrap.dll");
            string harmonySource = Path.Combine(sources, "harmony.dll");
            string proxySource = Path.Combine(sources, "winhttp.dll");
            File.WriteAllText(bootstrapSource, "bootstrap");
            File.WriteAllText(harmonySource, "harmony");
            File.WriteAllText(proxySource, "doorstop");
            File.WriteAllText(Path.Combine(root, "winhttp.dll"), "foreign-doorstop");

            try
            {
                BootstrapInstallResult result = BootstrapInstaller.Install(
                    new BootstrapInstallRequest(root, bootstrapSource, harmonySource, proxySource));
                Assert.Equal(BootstrapInstallState.Refused, result.State);
                Assert.Equal("foreign-doorstop", File.ReadAllText(Path.Combine(root, "winhttp.dll")));
                Assert.False(File.Exists(Path.Combine(root, "doorstop_config.ini")));
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
