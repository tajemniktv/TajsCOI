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
        public void LocalizationFormattingUsesCurrentCultureAndDegradesToTemplateOnFailure()
        {
            var service = new TajsLocalizationService();
            service.Register(
                new LocalizationCatalog(
                    "FormatTests",
                    "default",
                    new Dictionary<string, string>
                    {
                        ["count"] = "Count: {0}",
                        ["broken"] = "Broken {1}",
                    }));

            Assert.Equal("Count: 3", service.Format("FormatTests", "count", null, 3));
            Assert.Equal("Broken {1}", service.Format("FormatTests", "broken", null, 3));
            Assert.Equal(service.GetFormattingFailuresSnapshot(), service.GetFormattingFailuresSnapshot().Distinct(StringComparer.Ordinal).ToArray());
            Assert.Single(service.GetFormattingFailuresSnapshot());
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
            Assert.Single(snapshot.Errors);
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
        public void ConfigurationHandlersRouteDistinctPrototypesSharingOneEntityTypeAndRoundTrip()
        {
            var registry = new ConfigurationBlueprintRegistry();
            var sourceState = new Dictionary<string, object> { ["value"] = 11 };
            var restoredState = new Dictionary<string, object> { ["value"] = 0 };
            registry.Register(
                new ConfigurationHandlerDescriptor(
                    "Tests.PrototypeA",
                    "Tests",
                    1,
                    entity => entity.PrototypeId == "proto-a",
                    _ => new Dictionary<string, object> { ["value"] = sourceState["value"] },
                    (_, values) =>
                    {
                        restoredState["value"] = values["value"];
                        return true;
                    }));
            registry.Register(
                new ConfigurationHandlerDescriptor(
                    "Tests.PrototypeB",
                    "Tests",
                    1,
                    entity => entity.PrototypeId == "proto-b",
                    _ => new Dictionary<string, object> { ["value"] = 22 },
                    (_, values) =>
                    {
                        restoredState["value"] = Convert.ToInt32(values["value"]);
                        return true;
                    }));

            var entityA = new ConfigurationEntityDescriptor("1", "Shared.Entity", "proto-a");
            var entityB = new ConfigurationEntityDescriptor("2", "Shared.Entity", "proto-b");
            ConfigurationSnapshot capturedA = registry.Capture(entityA, new object());
            ConfigurationSnapshot capturedB = registry.Capture(entityB, new object());
            Assert.Equal("Tests.PrototypeA", Assert.Single(capturedA.Payloads).HandlerId);
            Assert.Equal("Tests.PrototypeB", Assert.Single(capturedB.Payloads).HandlerId);

            Assert.True(ConfigurationPayloadCodec.TrySerialize(capturedA, out string encodedA, out string encodeErrorA), encodeErrorA);
            Assert.True(ConfigurationPayloadCodec.TryDeserialize(encodedA, out ConfigurationSnapshot decodedA, out string decodeErrorA), decodeErrorA);
            Assert.Equal(1, registry.Apply(entityA, new object(), decodedA).Applied);
            Assert.Equal(11, restoredState["value"]);

            Assert.True(ConfigurationPayloadCodec.TrySerialize(capturedB, out string encodedB, out string encodeErrorB), encodeErrorB);
            Assert.True(ConfigurationPayloadCodec.TryDeserialize(encodedB, out ConfigurationSnapshot decodedB, out string decodeErrorB), decodeErrorB);
            Assert.Equal(1, registry.Apply(entityB, new object(), decodedB).Applied);
            Assert.Equal(22, restoredState["value"]);
            Assert.Equal("Shared.Entity", entityA.TypeId);
            Assert.NotEqual(entityA.PrototypeId, entityB.PrototypeId);
            ConfigurationEntityDescriptor noPrototype = new("3", "Shared.Entity", null);
            Assert.False(noPrototype.HasPrototype);
            Assert.Null(noPrototype.PrototypeId);
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

        [Fact]
        public void ConfigurationRegistryUnregisterRequiresTheOriginalOwner()
        {
            var registry = new ConfigurationBlueprintRegistry();
            registry.Register(
                new ConfigurationHandlerDescriptor(
                    "Tests.Owned",
                    "Tests.Owner",
                    1,
                    _ => true,
                    _ => new Dictionary<string, object> { ["enabled"] = true },
                    (_, _) => true));

            Assert.False(registry.Unregister("Tests.Owned", "Tests.Other"));
            Assert.Single(registry.GetHandlerSnapshot());
            Assert.True(registry.Unregister("Tests.Owned", "Tests.Owner"));
            Assert.Empty(registry.GetHandlerSnapshot());
            Assert.False(registry.Unregister("Tests.Owned", "Tests.Owner"));
        }

        [Fact]
        public void ShortcutRegistryFailedLoadIsTransactional()
        {
            string directory = Path.Combine(Path.GetTempPath(), "tajs-shortcut-transaction-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "shortcuts.txt");
            try
            {
                var registry = new ShortcutRegistry();
                registry.Register(Descriptor("TajsTests.First", "F1"));
                registry.Register(Descriptor("TajsTests.Second", "F2"));
                registry.TrySetBinding("TajsTests.First", new ShortcutCombination("CTRL+F1"), default);
                File.WriteAllText(
                    path,
                    "TajsCOIShortcutBindingsV1\n" +
                    "TajsTests.First\tF3\t\n" +
                    "TajsTests.Second\tF3\t\n");

                Assert.False(registry.TryLoad(path, out string error));
                Assert.Contains("rejected", error, StringComparison.OrdinalIgnoreCase);
                Assert.True(registry.TryGet("TajsTests.First", out ShortcutBindingSnapshot first));
                Assert.Equal("CTRL+F1", first.Primary.Serialized);
                Assert.True(registry.TryGet("TajsTests.Second", out ShortcutBindingSnapshot second));
                Assert.Equal("F2", second.Primary.Serialized);
                Assert.True(registry.TryResolveBinding(new ShortcutCombination("CTRL+F1"), out ShortcutBindingSnapshot resolved));
                Assert.Equal("TajsTests.First", resolved.Descriptor.ActionId);
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
        public void ShortcutRegistryPersistsExplicitAcceptedConflictsAndResolvesOrdinalFirst()
        {
            string directory = Path.Combine(Path.GetTempPath(), "tajs-shortcut-conflict-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "shortcuts.txt");
            try
            {
                var registry = new ShortcutRegistry();
                registry.Register(Descriptor("TajsTests.First", "F1"));
                registry.Register(Descriptor("TajsTests.Second", "F2"));
                ShortcutCombination combination = new("CTRL+K");
                Assert.True(registry.TrySetBinding("TajsTests.First", combination, default).Success);
                Assert.Equal(ShortcutSetStatus.Conflict, registry.TrySetBinding("TajsTests.Second", combination, default).Status);
                Assert.Equal(
                    ShortcutSetStatus.Applied,
                    registry.TryAcceptConflict("TajsTests.Second", combination, "TajsTests.First").Status);
                Assert.True(registry.TrySetBinding("TajsTests.Second", combination, default).Success);
                ShortcutConflictSnapshot conflict = Assert.Single(registry.GetConflictSnapshot());
                Assert.True(conflict.IsAccepted);
                Assert.True(registry.TryResolveBinding(combination, out ShortcutBindingSnapshot resolved));
                Assert.Equal("TajsTests.First", resolved.Descriptor.ActionId);
                Assert.True(resolved.IsConflict);
                Assert.True(registry.TrySave(path, out string saveError), saveError);

                var restored = new ShortcutRegistry();
                restored.Register(Descriptor("TajsTests.First", "F1"));
                restored.Register(Descriptor("TajsTests.Second", "F2"));
                Assert.True(restored.TryLoad(path, out string loadError), loadError);
                Assert.True(restored.TryResolveBinding(combination, out ShortcutBindingSnapshot restoredResolved));
                Assert.Equal("TajsTests.First", restoredResolved.Descriptor.ActionId);
                Assert.True(Assert.Single(restored.GetConflictSnapshot()).IsAccepted);

                Assert.True(restored.TrySetBinding("TajsTests.Second", new ShortcutCombination("F3"), default).Success);
                Assert.Empty(restored.GetConflictSnapshot());
                Assert.Equal(ShortcutSetStatus.Conflict, restored.TrySetBinding("TajsTests.Second", combination, default).Status);
                Assert.True(restored.TryResetBinding("TajsTests.Second").Success);
                Assert.Equal("F2", restored.GetSnapshot().Single(item => item.Descriptor.ActionId == "TajsTests.Second").Primary.Serialized);
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
        public void ShortcutRegistryRepresentsAndAcceptsVanillaConflictsExplicitly()
        {
            string directory = Path.Combine(Path.GetTempPath(), "tajs-shortcut-vanilla-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "shortcuts.txt");
            try
            {
                var registry = new ShortcutRegistry();
                registry.Register(Descriptor("TajsTests.Action", "F1"));
                registry.CacheVanillaBindings(
                    new[] { new KeyValuePair<string, ShortcutCombination>("Vanilla.Build", new ShortcutCombination("F9")) });

                ShortcutCombination combination = new("F9");
                Assert.Equal(ShortcutSetStatus.Conflict, registry.TrySetBinding("TajsTests.Action", combination, default).Status);
                Assert.Equal(
                    ShortcutSetStatus.Applied,
                    registry.TryAcceptConflict("TajsTests.Action", combination, "vanilla:Vanilla.Build").Status);
                Assert.True(registry.TrySetBinding("TajsTests.Action", combination, default).Success);
                ShortcutConflictSnapshot conflict = Assert.Single(registry.GetConflictSnapshot());
                Assert.Contains("vanilla:Vanilla.Build", conflict.VanillaActionIds);
                Assert.True(conflict.IsAccepted);

                Assert.True(registry.TrySave(path, out string saveError), saveError);
                var restored = new ShortcutRegistry();
                restored.Register(Descriptor("TajsTests.Action", "F1"));
                Assert.True(restored.TryLoad(path, out string loadError), loadError);
                restored.CacheVanillaBindings(
                    new[] { new KeyValuePair<string, ShortcutCombination>("Vanilla.Build", combination) });
                Assert.True(Assert.Single(restored.GetConflictSnapshot()).IsAccepted);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
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
