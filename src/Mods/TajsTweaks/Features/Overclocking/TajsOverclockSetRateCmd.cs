// Taj's COI Mods | TajsOverclockSetRateCmd.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Serialization;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TajsOverclockSetRateCmd : InputCommand
    {
        internal readonly EntityId TargetId;
        internal readonly Percent Rate;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TajsOverclockSetRateCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TajsOverclockSetRateCmd)obj).DeserializeData(reader);

        internal TajsOverclockSetRateCmd(EntityId entityId, Percent rate)
        {
            TargetId = entityId;
            Rate = rate;
        }

        public static void Serialize(TajsOverclockSetRateCmd value, BlobWriter writer)
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
            Percent.Serialize(Rate, writer);
        }

        public new static TajsOverclockSetRateCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TajsOverclockSetRateCmd? obj,
                    (Func<BlobReader, Type, TajsOverclockSetRateCmd>?)null,
                    (Func<BlobReader, string, TajsOverclockSetRateCmd>?)null,
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
            reader.SetField(this, "Rate", Percent.Deserialize(reader));
        }
    }
}
