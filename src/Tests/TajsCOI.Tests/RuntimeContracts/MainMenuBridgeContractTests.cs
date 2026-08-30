// Taj's COI Mods | MainMenuBridgeContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using System.Reflection;
using Mafi.Unity.UiToolkit;
using Xunit;

namespace TajsCOI.Tests.RuntimeContracts
{
    public sealed class MainMenuBridgeContractTests
    {
        [Fact]
        public void Supported088MainMenuControllerConstructorRemainsNarrowAndDiscoverable()
        {
            Type controller = typeof(UiRoot).Assembly.GetType("Mafi.Unity.MainMenu.MainMenuController")!;
            Assert.NotNull(controller);
            ConstructorInfo[] constructors = controller.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            ConstructorInfo? target = constructors.SingleOrDefault(item =>
                item.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(
                    new[] { "Mafi.Unity.IMain", "Mafi.Unity.UiToolkit.UiRoot", "Mafi.Core.IFileSystemHelper", "Mafi.DependencyResolver" }));
            Assert.NotNull(target);
        }
    }
}
