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
    }
}
