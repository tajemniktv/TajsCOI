// Taj's COI Mods | CommandContractTests.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mafi;
using Mafi.Core;
using Mafi.Core.Game;
using Mafi.Core.Input;
using Mafi.Core.Vehicles;
using Mafi.Serialization;
using TajsCOI.Tweaks.Features.Difficulty;
using TajsCOI.Tweaks.Features.Overclocking;
using TajsCOI.Tweaks.Features.Storage;
using TajsCOI.Tweaks.Features.TransportFlowLimits;
using TajsCOI.Tweaks.Features.Trains;
using Xunit;
using Assert = Xunit.Assert;

namespace TajsCOI.Tests.RuntimeContracts
{
    public sealed class CommandContractTests
    {
        [Fact]
        public void TajsCommandsAreInputCommandsWithStableValuePayloads()
        {
            Assert.True(typeof(InputCommand).IsAssignableFrom(typeof(TajsDifficultySetCmd)));
            Assert.True(typeof(InputCommand).IsAssignableFrom(typeof(TajsDifficultyResetCmd)));
            Assert.True(typeof(InputCommand).IsAssignableFrom(typeof(TajsOverclockSetRateCmd)));
            Assert.True(typeof(InputCommand).IsAssignableFrom(typeof(TajsOverclockPolicyCmd)));
            Assert.True(typeof(InputCommand).IsAssignableFrom(typeof(TajsStorageInstantEmptyCmd)));
            Assert.True(typeof(InputCommand).IsAssignableFrom(typeof(TransportFlowLimitCmd)));
            Assert.True(typeof(InputCommand).IsAssignableFrom(typeof(TrainNumberCmd)));

            Assert.Equal(
                new[] { typeof(string), typeof(string), typeof(bool) },
                typeof(TajsDifficultySetCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
            Assert.Equal(
                new[] { typeof(string[]), typeof(string[]) },
                typeof(TajsDifficultyResetCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
            Assert.Equal(
                new[] { typeof(EntityId), typeof(Percent) },
                typeof(TajsOverclockSetRateCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
            Assert.Equal(
                new[]
                {
                    typeof(TajsOverclockPolicyOperation),
                    typeof(EntityId),
                    typeof(int),
                    typeof(int),
                    typeof(bool),
                    typeof(bool),
                    typeof(int),
                    typeof(bool),
                    typeof(int),
                },
                typeof(TajsOverclockPolicyCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
            Assert.Equal(
                new[] { typeof(EntityId), typeof(bool) },
                typeof(TajsStorageInstantEmptyCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
            Assert.DoesNotContain(
                typeof(TajsDifficultySetCmd).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                field => typeof(GameDifficultyConfig).IsAssignableFrom(field.FieldType));
            Assert.Equal(
                new[] { typeof(EntityId), typeof(string) },
                typeof(TransportFlowLimitCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
            Assert.Equal(
                new[]
                {
                    typeof(TrainNumberOperation), typeof(EntityId), typeof(int),
                    typeof(LocomotiveNumberAssignment), typeof(int),
                },
                typeof(TrainNumberCmd)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsStatic)
                    .Select(field => field.FieldType));
        }

        [Fact]
        public void DifficultyAndOverclockProcessorsExposeNativeActionRoutes()
        {
            Type difficultyProcessor = typeof(TajsDifficultyCommandsProcessor);
            Assert.Contains(typeof(ICommandProcessor<TajsDifficultySetCmd>), difficultyProcessor.GetInterfaces());
            Assert.Contains(typeof(ICommandProcessor<TajsDifficultyResetCmd>), difficultyProcessor.GetInterfaces());
            Assert.Contains(typeof(IAction<TajsDifficultySetCmd>), difficultyProcessor.GetInterfaces());
            Assert.Contains(typeof(IAction<TajsDifficultyResetCmd>), difficultyProcessor.GetInterfaces());
            RuntimeContractAssertions.RequireMethod(
                difficultyProcessor,
                nameof(IAction<TajsDifficultySetCmd>.Invoke),
                typeof(void),
                isStatic: false,
                typeof(TajsDifficultySetCmd));
            RuntimeContractAssertions.RequireMethod(
                difficultyProcessor,
                nameof(IAction<TajsDifficultyResetCmd>.Invoke),
                typeof(void),
                isStatic: false,
                typeof(TajsDifficultyResetCmd));

            Type storageProcessor = typeof(TajsStorageConfigurationCommandsProcessor);
            Assert.Contains(typeof(ICommandProcessor<TajsStorageInstantEmptyCmd>), storageProcessor.GetInterfaces());
            Assert.Contains(typeof(IAction<TajsStorageInstantEmptyCmd>), storageProcessor.GetInterfaces());
            RuntimeContractAssertions.RequireMethod(
                storageProcessor,
                nameof(IAction<TajsStorageInstantEmptyCmd>.Invoke),
                typeof(void),
                isStatic: false,
                typeof(TajsStorageInstantEmptyCmd));

            Type overclockProcessor = typeof(TajsOverclockCommandsProcessor);
            Assert.Contains(typeof(ICommandProcessor<TajsOverclockSetRateCmd>), overclockProcessor.GetInterfaces());
            Assert.Contains(typeof(ICommandProcessor<TajsOverclockPolicyCmd>), overclockProcessor.GetInterfaces());
            Assert.Contains(typeof(IAction<TajsOverclockSetRateCmd>), overclockProcessor.GetInterfaces());
            Assert.Contains(typeof(IAction<TajsOverclockPolicyCmd>), overclockProcessor.GetInterfaces());
            RuntimeContractAssertions.RequireMethod(
                overclockProcessor,
                nameof(IAction<TajsOverclockSetRateCmd>.Invoke),
                typeof(void),
                isStatic: false,
                typeof(TajsOverclockSetRateCmd));
            RuntimeContractAssertions.RequireMethod(
                overclockProcessor,
                nameof(IAction<TajsOverclockPolicyCmd>.Invoke),
                typeof(void),
                isStatic: false,
                typeof(TajsOverclockPolicyCmd));

            Type flowProcessor = typeof(TransportFlowLimitCommandsProcessor);
            Assert.Contains(typeof(ICommandProcessor<TransportFlowLimitCmd>), flowProcessor.GetInterfaces());
            Assert.Contains(typeof(IAction<TransportFlowLimitCmd>), flowProcessor.GetInterfaces());
            RuntimeContractAssertions.RequireMethod(
                flowProcessor,
                nameof(IAction<TransportFlowLimitCmd>.Invoke),
                typeof(void),
                isStatic: false,
                typeof(TransportFlowLimitCmd));

            Type trainProcessor = typeof(TrainNumberCommandsProcessor);
            Assert.Contains(typeof(ICommandProcessor<TrainNumberCmd>), trainProcessor.GetInterfaces());
            Assert.Contains(typeof(IAction<TrainNumberCmd>), trainProcessor.GetInterfaces());
            RuntimeContractAssertions.RequireMethod(
                trainProcessor,
                nameof(IAction<TrainNumberCmd>.Invoke),
                typeof(void),
                isStatic: false,
                typeof(TrainNumberCmd));

            RuntimeContractAssertions.RequireConstructor(typeof(ChangeGameDifficultyCmd), typeof(GameDifficultyConfig));
            RuntimeContractAssertions.RequireConstructor(
                typeof(AddVehicleReplacementTaskCmd),
                typeof(Mafi.Core.Entities.Dynamic.DynamicEntityProto.ID),
                typeof(Mafi.Core.Entities.Dynamic.DynamicEntityProto.ID),
                typeof(LogisticsZoneId),
                typeof(EntityId?),
                typeof(bool),
                typeof(EntityId?),
                typeof(int));
            RuntimeContractAssertions.RequireConstructor(typeof(RemoveVehicleReplacementTaskCmd), typeof(uint));
            Assert.Contains(typeof(ICommandProcessor<ChangeGameDifficultyCmd>), typeof(GameDifficultyApplier).GetInterfaces());
        }

        [Fact]
        public void DifficultyCommandsRoundTripWithoutBecomingFullConfigCommands()
        {
            TajsDifficultySetCmd original = new("MaintenanceDiff", "25", confirmed: true);
            TajsDifficultySetCmd restored = RoundTrip(original, TajsDifficultySetCmd.Serialize, TajsDifficultySetCmd.Deserialize);
            Assert.Equal(original.MemberName, restored.MemberName);
            Assert.Equal(original.RawValue, restored.RawValue);
            Assert.Equal(original.Confirmed, restored.Confirmed);

            TajsDifficultyResetCmd reset = new(
                new[] { "MaintenanceDiff", "FuelConsumptionDiff" },
                new[] { "25", "-15" });
            TajsDifficultyResetCmd restoredReset = RoundTrip(reset, TajsDifficultyResetCmd.Serialize, TajsDifficultyResetCmd.Deserialize);
            Assert.Equal(reset.MemberNames, restoredReset.MemberNames);
            Assert.Equal(reset.EncodedValues, restoredReset.EncodedValues);
            Assert.DoesNotContain(
                typeof(TajsDifficultyResetCmd).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                field => field.FieldType == typeof(GameDifficultyConfig));

            TajsStorageInstantEmptyCmd empty = new(new EntityId(42), confirmed: true);
            TajsStorageInstantEmptyCmd restoredEmpty = RoundTrip(empty, TajsStorageInstantEmptyCmd.Serialize, TajsStorageInstantEmptyCmd.Deserialize);
            Assert.Equal(empty.StorageId, restoredEmpty.StorageId);
            Assert.Equal(empty.Confirmed, restoredEmpty.Confirmed);

            TransportFlowLimitCmd flow = new(new EntityId(17), 12.5d);
            TransportFlowLimitCmd restoredFlow = RoundTrip(flow, TransportFlowLimitCmd.Serialize, TransportFlowLimitCmd.Deserialize);
            Assert.Equal(flow.TargetId, restoredFlow.TargetId);
            Assert.True(restoredFlow.TryReadLimit(out double restoredLimit));
            Assert.Equal(12.5d, restoredLimit);

            TrainNumberCmd train = TrainNumberCmd.Set(new EntityId(29), 301);
            TrainNumberCmd restoredTrain = RoundTrip(train, TrainNumberCmd.Serialize, TrainNumberCmd.Deserialize);
            Assert.Equal(train.Operation, restoredTrain.Operation);
            Assert.Equal(train.TargetId, restoredTrain.TargetId);
            Assert.Equal(train.Number, restoredTrain.Number);

            TrainNumberCmd assign = TrainNumberCmd.AssignUnique(LocomotiveNumberAssignment.Random, 1234);
            TrainNumberCmd restoredAssign = RoundTrip(assign, TrainNumberCmd.Serialize, TrainNumberCmd.Deserialize);
            Assert.Equal(assign.Operation, restoredAssign.Operation);
            Assert.Equal(assign.Assignment, restoredAssign.Assignment);
            Assert.Equal(assign.RandomSeed, restoredAssign.RandomSeed);
        }

        private static T RoundTrip<T>(
            T value,
            Action<T, BlobWriter> serialize,
            Func<BlobReader, T> deserialize)
            where T : InputCommand
        {
            using var stream = new MemoryStream();
            using (var writer = new BlobWriter(stream))
            {
                serialize(value, writer);
                writer.FinalizeSerialization();
            }

            stream.Position = 0;
            var reader = new BlobReader(stream, 0);
            T restored = deserialize(reader);
            reader.FinalizeLoading(Option<DependencyResolver>.None);
            return restored;
        }
    }
}
