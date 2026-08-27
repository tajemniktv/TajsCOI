// Taj's COI Mods | TajsStorageConfigurationCmd.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Input;
using Mafi.Serialization;
using CoiStorage = Mafi.Core.Buildings.Storages.Storage;

namespace TajsCOI.Tweaks.Features.Storage
{
    /// <summary>
    ///     Replays a storage configuration transfer through the normal input command path. The
    ///     source is resolved at execution time, so a queued command never captures gameplay objects.
    /// </summary>
    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TajsStorageConfigurationCmd : InputCommand
    {
        internal readonly int SourceId;
        internal readonly int[] TargetIds;
        internal readonly StorageTransferFields Fields;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TajsStorageConfigurationCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TajsStorageConfigurationCmd)obj).DeserializeData(reader);

        internal TajsStorageConfigurationCmd(int sourceId, IEnumerable<int> targetIds, StorageTransferFields fields)
        {
            SourceId = sourceId;
            TargetIds = (targetIds ?? Enumerable.Empty<int>()).Distinct().Take(512).ToArray();
            Fields = fields & StorageTransferFields.All;
        }

        internal static TajsStorageConfigurationCmd ForTarget(int sourceId, int targetId, StorageTransferFields fields) =>
            new(sourceId, new[] { targetId }, fields);

        public static void Serialize(TajsStorageConfigurationCmd value, BlobWriter writer)
        {
            if (writer.TryStartClassSerialization(value))
            {
                writer.EnqueueDataSerialization(value, s_serializeData);
            }
        }

        protected override void SerializeData(BlobWriter writer)
        {
            base.SerializeData(writer);
            writer.WriteInt(SourceId);
            writer.WriteInt((int)Fields);
            writer.WriteIntNotNegative(TargetIds.Length);
            foreach (int targetId in TargetIds)
            {
                writer.WriteInt(targetId);
            }
        }

        public new static TajsStorageConfigurationCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TajsStorageConfigurationCmd? obj,
                    (Func<BlobReader, Type, TajsStorageConfigurationCmd>?)null,
                    (Func<BlobReader, string, TajsStorageConfigurationCmd>?)null,
                    false))
            {
                reader.EnqueueDataDeserialization(obj!, s_deserializeData);
            }

            return obj!;
        }

        protected override void DeserializeData(BlobReader reader)
        {
            base.DeserializeData(reader);
            reader.SetField(this, "SourceId", reader.ReadInt());
            reader.SetField(this, "Fields", (StorageTransferFields)reader.ReadInt());
            int count = Math.Min(reader.ReadIntNotNegative(), 512);
            int[] targetIds = new int[count];
            for (int i = 0; i < count; i++)
            {
                targetIds[i] = reader.ReadInt();
            }

            reader.SetField(this, "TargetIds", targetIds);
        }
    }

    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class TajsStorageConfigurationCommandsProcessor :
        ICommandProcessor<TajsStorageConfigurationCmd>,
        IAction<TajsStorageConfigurationCmd>,
        ICommandProcessor<TajsStorageCapacityCmd>,
        IAction<TajsStorageCapacityCmd>
    {
        private readonly IEntitiesManager m_entities;
        private readonly EntitiesCloneConfigHelper m_cloneConfig;

        public TajsStorageConfigurationCommandsProcessor(
            IEntitiesManager entities,
            EntitiesCloneConfigHelper cloneConfig)
        {
            m_entities = entities;
            m_cloneConfig = cloneConfig;
        }

        public void Invoke(TajsStorageConfigurationCmd command)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls)
            {
                command.SetResultError("Advanced storage inspector controls are disabled.");
                return;
            }

            if (command.Fields == StorageTransferFields.None)
            {
                command.SetResultError("Select at least one storage configuration field.");
                return;
            }

            if (!m_entities.TryGetEntity<CoiStorage>(new EntityId(command.SourceId), out CoiStorage? source) ||
                source is null || source.IsDestroyed)
            {
                command.SetResultError("Source storage was not found.");
                return;
            }

            int applied = 0;
            int skipped = 0;
            var reasons = new List<string>();
            foreach (int targetId in command.TargetIds)
            {
                if (!m_entities.TryGetEntity<CoiStorage>(new EntityId(targetId), out CoiStorage? target))
                {
                    skipped++;
                    reasons.Add("storage " + targetId + ": not found");
                    continue;
                }

                if (target.Id == source.Id)
                {
                    continue;
                }

                if (!TajsStorageAdvancedConfiguration.IsCompatible(source, target, out string compatibilityReason))
                {
                    skipped++;
                    reasons.Add("storage " + targetId + ": " + compatibilityReason);
                    continue;
                }

                StorageTransferFields fields = command.Fields;
                if ((fields & StorageTransferFields.ProductAssignment) != 0 &&
                    !TajsStorageAdvancedConfiguration.CanTransferProduct(source, target, out string productReason))
                {
                    fields &= ~StorageTransferFields.ProductAssignment;
                    reasons.Add("storage " + targetId + ": product skipped (" + productReason + ")");
                }

                if ((fields & StorageTransferFields.CapacityOverride) != 0)
                {
                    int? sourceCapacity = TajsStorageAdvancedState.GetCapacityOverride(source.Id.Value);
                    if (sourceCapacity.HasValue && sourceCapacity.Value > 0 &&
                        !TajsStorageAdvancedConfiguration.CanTransferCapacity(target, sourceCapacity.Value, out string capacityReason))
                    {
                        fields &= ~StorageTransferFields.CapacityOverride;
                        reasons.Add("storage " + targetId + ": capacity skipped (" + capacityReason + ")");
                    }
                    else if (!sourceCapacity.HasValue &&
                             TajsStorageAdvancedState.GetCapacityOverride(target.Id.Value).HasValue &&
                             !TajsStorageAdvancedConfiguration.CanClearCapacityOverride(target, out string resetReason))
                    {
                        fields &= ~StorageTransferFields.CapacityOverride;
                        reasons.Add("storage " + targetId + ": capacity reset skipped (" + resetReason + ")");
                    }
                }

                if (fields == StorageTransferFields.None)
                {
                    skipped++;
                    reasons.Add("storage " + targetId + ": no selected fields were compatible");
                    continue;
                }

                try
                {
                    EntityConfigData config = m_cloneConfig.CreateConfigFrom(source);
                    EntityConfigData destinationConfig = m_cloneConfig.CreateConfigFrom(target);
                    TajsStorageAdvancedConfiguration.RemoveUnselectedFields(config, fields);
                    TajsStorageAdvancedConfiguration.PreserveUnselectedFields(config, destinationConfig, fields);
                    m_cloneConfig.ApplyConfigTo(config, target);
                    applied++;
                }
                catch (Exception exception)
                {
                    skipped++;
                    reasons.Add("storage " + targetId + ": " + exception.GetType().Name);
                }
            }

            TajsStorageAdvancedState.RecordTransfer(applied, skipped, reasons);
            if (applied == 0 && skipped > 0)
            {
                command.SetResultError(TajsStorageAdvancedState.LastTransferReport);
            }
            else
            {
                command.SetResultSuccess();
            }
        }

        public void Invoke(TajsStorageCapacityCmd command)
        {
            if (!TajsTweaksRuntimeState.StorageInspectorControls)
            {
                command.SetResultError("Advanced storage inspector controls are disabled.");
                return;
            }

            if (!m_entities.TryGetEntity<CoiStorage>(command.TargetId, out CoiStorage? target) || target is null)
            {
                command.SetResultError("Target storage was not found.");
                return;
            }

            if (TajsStorageAdvancedConfiguration.IsRestricted(target))
            {
                command.SetResultError("This storage is restricted from capacity changes.");
                return;
            }

            if (!TajsStorageAdvancedConfiguration.TryApplyCapacity(target, command.Capacity, out string reason))
            {
                command.SetResultError(reason);
                return;
            }

            command.SetResultSuccess();
        }
    }

    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TajsStorageCapacityCmd : InputCommand
    {
        internal readonly EntityId TargetId;
        internal readonly int Capacity;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TajsStorageCapacityCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TajsStorageCapacityCmd)obj).DeserializeData(reader);

        internal TajsStorageCapacityCmd(EntityId targetId, int capacity)
        {
            TargetId = targetId;
            Capacity = capacity;
        }

        public static void Serialize(TajsStorageCapacityCmd value, BlobWriter writer)
        {
            if (writer.TryStartClassSerialization(value))
            {
                writer.EnqueueDataSerialization(value, s_serializeData);
            }
        }

        protected override void SerializeData(BlobWriter writer)
        {
            base.SerializeData(writer);
            EntityId.Serialize(TargetId, writer);
            writer.WriteInt(Capacity);
        }

        public new static TajsStorageCapacityCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TajsStorageCapacityCmd? obj,
                    (Func<BlobReader, Type, TajsStorageCapacityCmd>?)null,
                    (Func<BlobReader, string, TajsStorageCapacityCmd>?)null,
                    false))
            {
                reader.EnqueueDataDeserialization(obj!, s_deserializeData);
            }

            return obj!;
        }

        protected override void DeserializeData(BlobReader reader)
        {
            base.DeserializeData(reader);
            reader.SetField(this, "TargetId", EntityId.Deserialize(reader));
            reader.SetField(this, "Capacity", reader.ReadInt());
        }
    }
}
