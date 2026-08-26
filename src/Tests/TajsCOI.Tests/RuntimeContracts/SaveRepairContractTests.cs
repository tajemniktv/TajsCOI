// Taj's COI Mods | SaveRepairContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using TajsCOI.Common.Runtime;
using TajsCOI.Core.SaveRepair;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests.RuntimeContracts
{
    /// <summary>
    ///     Compatibility checks for the private event/resolver structures used by save repair.
    ///     These tests do not construct a resolver or mutate a save.
    /// </summary>
    public sealed class SaveRepairContractTests
    {
        [Fact]
        public void SaveRepairServiceAndEventApiKeepTheirAuditedShapes()
        {
            RuntimeContractAssertions.RequireConstructor(
                typeof(TajsSaveRepairService),
                typeof(DependencyResolver),
                typeof(ISaveManager),
                typeof(IFileSystemHelper),
                typeof(ProtosDb),
                typeof(ITajsRuntime));
            RuntimeContractAssertions.RequireMethod(
                typeof(TajsSaveRepairService),
                nameof(TajsSaveRepairService.Report),
                typeof(string),
                isStatic: false);
            RuntimeContractAssertions.RequireMethod(
                typeof(TajsSaveRepairService),
                nameof(TajsSaveRepairService.Repair),
                typeof(string),
                isStatic: false,
                typeof(string),
                typeof(string),
                typeof(string));

            MethodInfo[] eventMethods = typeof(IEvent).GetMethods();
            foreach (string name in new[] { "Add", "Remove", "IsAdded" })
            {
                MethodInfo method = Assert.Single(
                    eventMethods,
                    candidate => candidate.Name == name &&
                                 candidate.IsGenericMethodDefinition &&
                                 candidate.GetGenericArguments().Length == 1 &&
                                 candidate.GetParameters().Length == 2);
                Assert.Equal(method.GetGenericArguments()[0], method.GetParameters()[0].ParameterType);
                Assert.Equal(typeof(Action), method.GetParameters()[1].ParameterType);
                Assert.Equal(name == "IsAdded" ? typeof(bool) : typeof(void), method.ReturnType);
            }
        }

        [Fact]
        public void ResolverCollectionsUsedForLegacyCleanupRemainPresentAndTyped()
        {
            RuntimeContractAssertions.RequireField(
                typeof(DependencyResolver),
                "m_resolvedInstancesByRegisteredType",
                typeof(Dict<Type, object>),
                isStatic: false);
            RuntimeContractAssertions.RequireField(
                typeof(DependencyResolver),
                "m_resolvedInstancesByRealType",
                typeof(Dict<Type, object>),
                isStatic: false);
            RuntimeContractAssertions.RequireField(
                typeof(DependencyResolver),
                "m_resolvedObjects",
                typeof(Lyst<object>),
                isStatic: false);
            RuntimeContractAssertions.RequireField(
                typeof(DependencyResolver),
                "m_multiInstanceDeps",
                typeof(Lyst<object>),
                isStatic: false);
            RuntimeContractAssertions.RequireField(
                typeof(DependencyResolver),
                "m_instancedToBeDisposed",
                typeof(Lyst<object>),
                isStatic: false);
        }
    }
}
