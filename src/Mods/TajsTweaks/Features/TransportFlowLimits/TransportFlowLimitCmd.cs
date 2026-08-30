// Taj's COI Mods | TransportFlowLimitCmd.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Globalization;
using Mafi;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Serialization;

namespace TajsCOI.Tweaks.Features.TransportFlowLimits
{
    /// <summary>
    /// Value-only input command for a live per-transport flow-policy mutation. The entity is
    /// resolved at execution time so queued/replayed commands cannot retain scene objects.
    /// </summary>
    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TransportFlowLimitCmd : InputCommand
    {
        internal readonly EntityId TargetId;
        internal readonly string Limit;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TransportFlowLimitCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TransportFlowLimitCmd)obj).DeserializeData(reader);

        internal TransportFlowLimitCmd(EntityId targetId, double limit)
        {
            TargetId = targetId;
            Limit = limit.ToString("R", CultureInfo.InvariantCulture);
        }

        public static void Serialize(TransportFlowLimitCmd value, BlobWriter writer)
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
            writer.WriteString(Limit);
        }

        public new static TransportFlowLimitCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TransportFlowLimitCmd? obj,
                    (Func<BlobReader, Type, TransportFlowLimitCmd>?)null,
                    (Func<BlobReader, string, TransportFlowLimitCmd>?)null,
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
            reader.SetField(this, "Limit", reader.ReadString());
        }

        internal bool TryReadLimit(out double limit) =>
            double.TryParse(Limit, NumberStyles.Float, CultureInfo.InvariantCulture, out limit) &&
            !double.IsNaN(limit) && !double.IsInfinity(limit);
    }

    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class TransportFlowLimitCommandsProcessor :
        ICommandProcessor<TransportFlowLimitCmd>,
        IAction<TransportFlowLimitCmd>
    {
        public void Invoke(TransportFlowLimitCmd command)
        {
            if (!command.TryReadLimit(out double limit) ||
                !TransportFlowLimitFeature.TrySetConfiguredLimit(command.TargetId.Value, limit))
            {
                command.SetResultError("Transport flow-limit policy rejected or transport is unavailable.");
            }
        }
    }
}
