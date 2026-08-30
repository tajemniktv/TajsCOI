// Taj's COI Mods | TrainNumberCommands.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Input;
using Mafi.Core.Trains;
using Mafi.Serialization;

namespace TajsCOI.Tweaks.Features.Trains
{
    internal enum TrainNumberOperation
    {
        Set = 1,
        AssignUnique = 2,
    }

    /// <summary>
    ///     Value-only locomotive identity command. The locomotive is resolved at execution time;
    ///     no train or scene object crosses the input/save boundary.
    /// </summary>
    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TrainNumberCmd : InputCommand
    {
        internal readonly TrainNumberOperation Operation;
        internal readonly EntityId TargetId;
        internal readonly int Number;
        internal readonly LocomotiveNumberAssignment Assignment;
        internal readonly int RandomSeed;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TrainNumberCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TrainNumberCmd)obj).DeserializeData(reader);

        private TrainNumberCmd(
            TrainNumberOperation operation,
            EntityId targetId = default,
            int number = 0,
            LocomotiveNumberAssignment assignment = LocomotiveNumberAssignment.Sequential,
            int randomSeed = 0)
        {
            Operation = operation;
            TargetId = targetId;
            Number = number;
            Assignment = assignment;
            RandomSeed = randomSeed;
        }

        internal static TrainNumberCmd Set(EntityId targetId, int number) =>
            new(TrainNumberOperation.Set, targetId: targetId, number: number);

        internal static TrainNumberCmd AssignUnique(LocomotiveNumberAssignment assignment, int randomSeed) =>
            new(TrainNumberOperation.AssignUnique, assignment: assignment, randomSeed: randomSeed);

        public static void Serialize(TrainNumberCmd value, BlobWriter writer)
        {
            if (writer.TryStartClassSerialization(value))
            {
                writer.EnqueueDataSerialization(value, s_serializeData);
            }
        }

        protected override void SerializeData(BlobWriter writer)
        {
            base.SerializeData(writer);
            writer.WriteInt((int)Operation);
            EntityId.Serialize(TargetId, writer);
            writer.WriteInt(Number);
            writer.WriteInt((int)Assignment);
            writer.WriteInt(RandomSeed);
        }

        public new static TrainNumberCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TrainNumberCmd? obj,
                    (Func<BlobReader, Type, TrainNumberCmd>?)null,
                    (Func<BlobReader, string, TrainNumberCmd>?)null,
                    false))
            {
                reader.EnqueueDataDeserialization(obj!, s_deserializeData);
            }

            return obj!;
        }

        protected override void DeserializeData(BlobReader reader)
        {
            base.DeserializeData(reader);
            reader.SetField(this, "Operation", (TrainNumberOperation)reader.ReadInt());
            reader.SetField(this, "TargetId", EntityId.Deserialize(reader));
            reader.SetField(this, "Number", reader.ReadInt());
            reader.SetField(this, "Assignment", (LocomotiveNumberAssignment)reader.ReadInt());
            reader.SetField(this, "RandomSeed", reader.ReadInt());
        }
    }

    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class TrainNumberCommandsProcessor :
        ICommandProcessor<TrainNumberCmd>,
        IAction<TrainNumberCmd>
    {
        private readonly IEntitiesManager m_entities;

        public TrainNumberCommandsProcessor(IEntitiesManager entities)
        {
            m_entities = entities;
        }

        public void Invoke(TrainNumberCmd command)
        {
            switch (command.Operation)
            {
                case TrainNumberOperation.Set:
                    ApplyOne(command);
                    return;
                case TrainNumberOperation.AssignUnique:
                    ApplyAll(command);
                    return;
                default:
                    command.SetResultError("Unknown locomotive-number operation.");
                    return;
            }
        }

        private void ApplyOne(TrainNumberCmd command)
        {
            if (!m_entities.TryGetEntity<Locomotive>(command.TargetId, out Locomotive? locomotive) ||
                locomotive is null)
            {
                command.SetResultError("Locomotive was not found.");
                return;
            }

            if (!TrainTuningFeature.TrySetNumber(locomotive, command.Number, out string error))
            {
                command.SetResultError(error);
                return;
            }

            command.SetResultSuccess();
        }

        private void ApplyAll(TrainNumberCmd command)
        {
            try
            {
                Locomotive[] locomotives = m_entities.GetAllEntitiesOfType<Locomotive>().ToArray();
                if (!TrainTuningFeature.TryAssignUnique(
                        locomotives,
                        command.Assignment,
                        command.RandomSeed,
                        out _,
                        out string message))
                {
                    command.SetResultError(message);
                    return;
                }

                command.SetResultSuccess();
            }
            catch (Exception exception)
            {
                command.SetResultError("Locomotive numbering failed: " + exception.GetType().Name + ".");
            }
        }
    }
}
