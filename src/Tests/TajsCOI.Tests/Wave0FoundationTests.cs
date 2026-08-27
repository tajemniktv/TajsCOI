// Taj's COI Mods | Wave0FoundationTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TajsCOI.Common.Configuration;
using TajsCOI.Common.Localization;
using TajsCOI.Common.Shortcuts;
using TajsCOI.Core.Configuration;
using TajsCOI.Core.Localization;
using TajsCOI.Core.Shortcuts;
using Xunit;

namespace TajsCOI.Tests
{
    public sealed class Wave0FoundationTests
    {
        [Fact]
        public void ShortcutRegistryNormalizesBindingsRejectsConflictsAndCachesVanillaOnce()
        {
            var registry = new ShortcutRegistry();
            ShortcutDescriptor first = Descriptor("TajsTests.First", "CTRL+K");
            ShortcutDescriptor second = Descriptor("TajsTests.Second", "CTRL+L");

            Assert.Equal(ShortcutRegistrationStatus.Added, registry.Register(first).Status);
            Assert.Equal(ShortcutRegistrationStatus.AlreadyRegistered, registry.Register(first).Status);
            Assert.Equal(ShortcutRegistrationStatus.Added, registry.Register(second).Status);

            ShortcutSetResult conflict = registry.TrySetBinding("TajsTests.Second", new ShortcutCombination("CTRL+K"), default);
            Assert.Equal(ShortcutSetStatus.Conflict, conflict.Status);
            Assert.Equal("TajsTests.First", conflict.ConflictingActionId);

            registry.CacheVanillaBindings(new[] { new KeyValuePair<string, ShortcutCombination>("Vanilla.Action", new ShortcutCombination("ALT+V")) });
            registry.CacheVanillaBindings(new[] { new KeyValuePair<string, ShortcutCombination>("Vanilla.Other", new ShortcutCombination("ALT+O")) });
            Assert.Single(registry.GetVanillaBindingsSnapshot());
            Assert.Equal(
                "CTRL+K",
                registry.GetSnapshot().Single(snapshot => snapshot.Descriptor.ActionId == "TajsTests.First").Primary.Serialized);
        }

        [Fact]
        public void ShortcutInputServiceHonorsCaptureAndContextBeforeDispatch()
        {
            var registry = new ShortcutRegistry();
            registry.Register(Descriptor("TajsTests.Dispatch", "F9", ShortcutActivationContext.Gameplay));
            var service = new ShortcutInputService(registry);
            int calls = 0;
            using IDisposable registration = service.RegisterHandler("TajsTests.Dispatch", () => calls++);

            var gate = new TestGate { ContextActive = true };
            Assert.True(service.TryDispatch(new ShortcutCombination("f9"), gate).Handled);
            gate.UiCapturesInput = true;
            Assert.False(service.TryDispatch(new ShortcutCombination("f9"), gate).Handled);
            gate.UiCapturesInput = false;
            gate.ContextActive = false;
            Assert.False(service.TryDispatch(new ShortcutCombination("f9"), gate).Handled);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ShortcutRegistryPersistsOnlyRegisteredValueBindings()
        {
            string directory = Path.Combine(Path.GetTempPath(), "tajs-shortcut-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "shortcuts.txt");
            try
            {
                var source = new ShortcutRegistry();
                source.Register(Descriptor("TajsTests.Persist", "F10"));
                source.TrySetBinding("TajsTests.Persist", new ShortcutCombination("CTRL+F10"), default);
                Assert.True(source.TrySave(path, out string saveError), saveError);

                var restored = new ShortcutRegistry();
                restored.Register(Descriptor("TajsTests.Persist", "F10"));
                Assert.True(restored.TryLoad(path, out string loadError), loadError);
                Assert.True(restored.TryGet("TajsTests.Persist", out ShortcutBindingSnapshot binding));
                Assert.Equal("CTRL+F10", binding.Primary.Serialized);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public void LocalizationUsesExactLanguageAndDefaultFallbackAndDeduplicatesMissingKeys()
        {
            var service = new TajsLocalizationService();
            service.Register(
                new LocalizationCatalog(
                    "Core",
                    "default",
                    new Dictionary<string, string> { ["hello"] = "Hello", ["fallback"] = "Default" }));
            service.Register(
                new LocalizationCatalog(
                    "Core",
                    "pl",
                    new Dictionary<string, string> { ["hello"] = "Cześć" }));
            service.Register(
                new LocalizationCatalog(
                    "Fallback",
                    "pl-PL",
                    new Dictionary<string, string> { ["only_there"] = "Tylko" }));

            service.SetLocale("pl-PL");
            Assert.Equal("Cześć", service.Get("Core", "hello"));
            Assert.Equal("Default", service.Get("Core", "fallback"));
            Assert.Equal("Tylko", service.Get("Core", "only_there", fallbackSource: "Fallback"));
            Assert.Equal("missing", service.Get("Core", "missing"));
            Assert.Equal("missing", service.Get("Core", "missing"));
            Assert.Single(service.GetMissingKeysSnapshot());
        }

        [Fact]
        public void ConfigurationRegistryMigratesAndContinuesAfterOneHandlerFails()
        {
            var registry = new ConfigurationBlueprintRegistry();
            var state = new Dictionary<string, object> { ["value"] = 12 };
            var entity = new ConfigurationEntityDescriptor("42", "Test.Storage", "proto");
            registry.Register(
                new ConfigurationHandlerDescriptor(
                    "Tests.Value",
                    "Tests",
                    2,
                    _ => true,
                    _ => state,
                    (_, values) =>
                    {
                        state["value"] = values["value"];
                        return true;
                    },
                    (version, values) => version == 1
                        ? new Dictionary<string, object> { ["value"] = Convert.ToInt32(values["value"]) + 1 }
                        : null));
            registry.Register(
                new ConfigurationHandlerDescriptor(
                    "Tests.Failing",
                    "Tests",
                    1,
                    _ => true,
                    _ => throw new InvalidOperationException("read"),
                    (_, _) => throw new InvalidOperationException("apply")));

            ConfigurationSnapshot snapshot = registry.Capture(entity, new object());
            Assert.Single(snapshot.Payloads);
            ConfigurationSnapshot oldSnapshot = new(
                new[]
                {
                    new ConfigurationPayload("Tests.Value", "Tests", 1, new Dictionary<string, object> { ["value"] = 7 }),
                    new ConfigurationPayload("Tests.Failing", "Tests", 1, new Dictionary<string, object> { ["value"] = 9 }),
                });
            ConfigurationApplyResult result = registry.Apply(entity, new object(), oldSnapshot);
            Assert.Equal(1, result.Applied);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(8, state["value"]);
            Assert.Single(result.Errors);
        }

        [Fact]
        public void ConfigurationPayloadCodecRoundTripsPrimitiveVersionedRecords()
        {
            ConfigurationSnapshot source = new(
                new[]
                {
                    new ConfigurationPayload(
                        "Tests.Storage",
                        "Tests",
                        2,
                        new Dictionary<string, object> { ["enabled"] = true, ["capacity"] = 123, ["label"] = "A\tB" }),
                });

            Assert.True(ConfigurationPayloadCodec.TrySerialize(source, out string encoded, out string encodeError), encodeError);
            Assert.True(ConfigurationPayloadCodec.TryDeserialize(encoded, out ConfigurationSnapshot restored, out string decodeError), decodeError);
            ConfigurationPayload payload = Assert.Single(restored.Payloads);
            Assert.Equal(2, payload.SchemaVersion);
            Assert.Equal(true, payload.Values["enabled"]);
            Assert.Equal(123, payload.Values["capacity"]);
            Assert.Equal("A\tB", payload.Values["label"]);
        }

        private static ShortcutDescriptor Descriptor(
            string id,
            string primary,
            ShortcutActivationContext context = ShortcutActivationContext.Gameplay) =>
            new(id, id, "Tests", new ShortcutCombination(primary), default, context);

        private sealed class TestGate : IShortcutDispatchGate
        {
            public bool HasTextFieldFocus { get; set; }
            public bool ModalCapturesInput { get; set; }
            public bool ToolOwnsInput { get; set; }
            public bool UiCapturesInput { get; set; }
            public bool ContextActive { get; set; }

            public bool IsContextActive(ShortcutActivationContext context) => ContextActive;
        }
    }
}
