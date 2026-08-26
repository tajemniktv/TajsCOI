// Taj's COI Mods | TajsDifficultyCommands.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi.Core.Input;
using Mafi.Serialization;

namespace TajsCOI.Tweaks.Features.Difficulty
{
    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TajsDifficultySetCmd : InputCommand
    {
        internal readonly string MemberName;
        internal readonly string RawValue;
        internal readonly bool Confirmed;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TajsDifficultySetCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TajsDifficultySetCmd)obj).DeserializeData(reader);

        internal TajsDifficultySetCmd(string memberName, string rawValue, bool confirmed)
        {
            MemberName = memberName;
            RawValue = rawValue;
            Confirmed = confirmed;
        }

        public static void Serialize(TajsDifficultySetCmd value, BlobWriter writer)
        {
            if (writer.TryStartClassSerialization(value))
            {
                writer.EnqueueDataSerialization(value, s_serializeData);
            }
        }

        protected override void SerializeData(BlobWriter writer)
        {
            base.SerializeData(writer);
            writer.WriteString(MemberName);
            writer.WriteString(RawValue);
            writer.WriteBool(Confirmed);
        }

        public new static TajsDifficultySetCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TajsDifficultySetCmd? obj,
                    (Func<BlobReader, Type, TajsDifficultySetCmd>?)null,
                    (Func<BlobReader, string, TajsDifficultySetCmd>?)null,
                    false))
            {
                reader.EnqueueDataDeserialization(obj!, s_deserializeData);
            }

            return obj!;
        }

        protected override void DeserializeData(BlobReader reader)
        {
            base.DeserializeData(reader);
            reader.SetField(this, "MemberName", reader.ReadString());
            reader.SetField(this, "RawValue", reader.ReadString());
            reader.SetField(this, "Confirmed", reader.ReadBool());
        }
    }

    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TajsDifficultyResetCmd : InputCommand
    {
        internal readonly string[] MemberNames;
        internal readonly string[] EncodedValues;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TajsDifficultyResetCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TajsDifficultyResetCmd)obj).DeserializeData(reader);

        internal TajsDifficultyResetCmd(string[] memberNames, string[] encodedValues)
        {
            MemberNames = memberNames;
            EncodedValues = encodedValues;
        }

        public static void Serialize(TajsDifficultyResetCmd value, BlobWriter writer)
        {
            if (writer.TryStartClassSerialization(value))
            {
                writer.EnqueueDataSerialization(value, s_serializeData);
            }
        }

        protected override void SerializeData(BlobWriter writer)
        {
            base.SerializeData(writer);
            writer.WriteArray(MemberNames);
            writer.WriteArray(EncodedValues);
        }

        public new static TajsDifficultyResetCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TajsDifficultyResetCmd? obj,
                    (Func<BlobReader, Type, TajsDifficultyResetCmd>?)null,
                    (Func<BlobReader, string, TajsDifficultyResetCmd>?)null,
                    false))
            {
                reader.EnqueueDataDeserialization(obj!, s_deserializeData);
            }

            return obj!;
        }

        protected override void DeserializeData(BlobReader reader)
        {
            base.DeserializeData(reader);
            reader.SetField(this, "MemberNames", reader.ReadArray<string>());
            reader.SetField(this, "EncodedValues", reader.ReadArray<string>());
        }
    }
}
