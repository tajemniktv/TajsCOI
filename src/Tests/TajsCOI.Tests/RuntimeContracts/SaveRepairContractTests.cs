// Taj's COI Mods | SaveRepairContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.World.Entities;
using Mafi.Core.World.QuickTrade;
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

        [Fact]
        public void SaveRepairDataSeamsKeepTheirAudited087bShapes()
        {
            RuntimeContractAssertions.RequireField(
                typeof(QuickTradePairProto),
                nameof(QuickTradePairProto.MinReputationRequired),
                typeof(int),
                isStatic: false);
            FieldInfo reputation = typeof(QuickTradePairProto).GetField(
                nameof(QuickTradePairProto.MinReputationRequired),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.True(reputation.IsInitOnly, "Quick-trade reputation must remain an immutable 0.8.7b prototype field.");

            RuntimeContractAssertions.RequireField(
                typeof(WorldMapVillageProto),
                nameof(WorldMapVillageProto.QuickTrades),
                typeof(ImmutableArray<QuickTradePairProto>),
                isStatic: false);
            RuntimeContractAssertions.RequireProperty(
                typeof(ModJsonConfig),
                nameof(ModJsonConfig.ModId),
                typeof(string),
                isStatic: false);
            RuntimeContractAssertions.RequireProperty(
                typeof(ModJsonConfig),
                nameof(ModJsonConfig.Parameters),
                typeof(IReadOnlyDictionary<string, ModJsonConfigParam>),
                isStatic: false);
        }

        [Fact]
        public void SaveableCallbackRecordRetainsOwnerDeclaringTypeAndMethodName()
        {
            Type callbackSaveData = typeof(EventBase<>).MakeGenericType(typeof(Action))
                .GetNestedType("CallbackSaveData", BindingFlags.Public | BindingFlags.NonPublic)!;
            Assert.NotNull(callbackSaveData);
            RuntimeContractAssertions.RequireField(callbackSaveData, "Owner", typeof(object), isStatic: false);
            RuntimeContractAssertions.RequireField(callbackSaveData, "DeclaringType", typeof(Type), isStatic: false);
            RuntimeContractAssertions.RequireField(callbackSaveData, "MethodName", typeof(string), isStatic: false);
        }
    }
}
