// Taj's COI Mods | TajsOverclockPolicyCmd.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Serialization;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    ///     Value-only input for a live overclock policy mutation. Unused fields are deliberately
    ///     kept at their default values for the selected operation; no gameplay or resolver object
    ///     is carried across the input boundary.
    /// </summary>
    internal enum TajsOverclockPolicyOperation
    {
        SetManual = 1,
        SetAuto = 2,
        Reset = 3,
        AddToGroup = 4,
        RemoveFromGroup = 5,
        DeleteGroup = 6,
        SetGroupDefault = 7,
        ApplyGroup = 8,
        SetGroupAuto = 9,
    }

    [GenerateSerializer(false, null, 0, null)]
    internal sealed class TajsOverclockPolicyCmd : InputCommand
    {
        internal readonly TajsOverclockPolicyOperation Operation;
        internal readonly EntityId TargetId;
        internal readonly int GroupId;
        internal readonly int Percent;
        internal readonly bool Enabled;
        internal readonly bool HasMinimum;
        internal readonly int Minimum;
        internal readonly bool HasMaximum;
        internal readonly int Maximum;

        private static readonly Action<object, BlobWriter> s_serializeData = (obj, writer) =>
            ((TajsOverclockPolicyCmd)obj).SerializeData(writer);

        private static readonly Action<object, BlobReader> s_deserializeData = (obj, reader) =>
            ((TajsOverclockPolicyCmd)obj).DeserializeData(reader);

        private TajsOverclockPolicyCmd(
            TajsOverclockPolicyOperation operation,
            EntityId targetId = default,
            int groupId = -1,
            int percent = 0,
            bool enabled = false,
            int? minimum = null,
            int? maximum = null)
        {
            Operation = operation;
            TargetId = targetId;
            GroupId = groupId;
            Percent = percent;
            Enabled = enabled;
            HasMinimum = minimum.HasValue;
            Minimum = minimum ?? 0;
            HasMaximum = maximum.HasValue;
            Maximum = maximum ?? 0;
        }

        internal static TajsOverclockPolicyCmd SetManual(EntityId targetId, int percent) =>
            new(TajsOverclockPolicyOperation.SetManual, targetId: targetId, percent: percent);

        internal static TajsOverclockPolicyCmd SetAuto(EntityId targetId, bool enabled, int? minimum, int? maximum) =>
            new(
                TajsOverclockPolicyOperation.SetAuto,
                targetId: targetId,
                enabled: enabled,
                minimum: minimum,
                maximum: maximum);

        internal static TajsOverclockPolicyCmd Reset(EntityId targetId) =>
            new(TajsOverclockPolicyOperation.Reset, targetId: targetId);

        internal static TajsOverclockPolicyCmd AddToGroup(int groupId, EntityId targetId) =>
            new(TajsOverclockPolicyOperation.AddToGroup, targetId: targetId, groupId: groupId);

        internal static TajsOverclockPolicyCmd RemoveFromGroup(int groupId, EntityId targetId) =>
            new(TajsOverclockPolicyOperation.RemoveFromGroup, targetId: targetId, groupId: groupId);

        internal static TajsOverclockPolicyCmd DeleteGroup(int groupId) =>
            new(TajsOverclockPolicyOperation.DeleteGroup, groupId: groupId);

        internal static TajsOverclockPolicyCmd SetGroupDefault(int groupId, int percent) =>
            new(TajsOverclockPolicyOperation.SetGroupDefault, groupId: groupId, percent: percent);

        internal static TajsOverclockPolicyCmd ApplyGroup(int groupId, int percent) =>
            new(TajsOverclockPolicyOperation.ApplyGroup, groupId: groupId, percent: percent);

        internal static TajsOverclockPolicyCmd SetGroupAuto(
            int groupId,
            bool enabled,
            int? minimum,
            int? maximum) =>
            new(
                TajsOverclockPolicyOperation.SetGroupAuto,
                groupId: groupId,
                enabled: enabled,
                minimum: minimum,
                maximum: maximum);

        public static void Serialize(TajsOverclockPolicyCmd value, BlobWriter writer)
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
            writer.WriteInt(GroupId);
            writer.WriteInt(Percent);
            writer.WriteBool(Enabled);
            writer.WriteBool(HasMinimum);
            writer.WriteInt(Minimum);
            writer.WriteBool(HasMaximum);
            writer.WriteInt(Maximum);
        }

        public new static TajsOverclockPolicyCmd Deserialize(BlobReader reader)
        {
            if (reader.TryStartClassDeserialization(
                    out TajsOverclockPolicyCmd? obj,
                    (Func<BlobReader, Type, TajsOverclockPolicyCmd>?)null,
                    (Func<BlobReader, string, TajsOverclockPolicyCmd>?)null,
                    false))
            {
                reader.EnqueueDataDeserialization(obj!, s_deserializeData);
            }

            return obj!;
        }

        protected override void DeserializeData(BlobReader reader)
        {
            base.DeserializeData(reader);
            reader.SetField(this, "Operation", (TajsOverclockPolicyOperation)reader.ReadInt());
            reader.SetField(this, "TargetId", EntityId.Deserialize(reader));
            reader.SetField(this, "GroupId", reader.ReadInt());
            reader.SetField(this, "Percent", reader.ReadInt());
            reader.SetField(this, "Enabled", reader.ReadBool());
            reader.SetField(this, "HasMinimum", reader.ReadBool());
            reader.SetField(this, "Minimum", reader.ReadInt());
            reader.SetField(this, "HasMaximum", reader.ReadBool());
            reader.SetField(this, "Maximum", reader.ReadInt());
        }
    }
}
