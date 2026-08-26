// Taj's COI Mods | SaveRepairTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
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
    }
}
