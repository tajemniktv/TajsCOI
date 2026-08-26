// Taj's COI Mods | TajsOverclockCommandsProcessor.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Input;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    /// <summary>
    ///     Applies live overclock policy changes in input order. The legacy rate command remains
    ///     registered so an already-serialized command can still be processed after an upgrade;
    ///     new requests use TajsOverclockPolicyCmd.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class TajsOverclockCommandsProcessor :
        ICommandProcessor<TajsOverclockSetRateCmd>,
        IAction<TajsOverclockSetRateCmd>,
        ICommandProcessor<TajsOverclockPolicyCmd>,
        IAction<TajsOverclockPolicyCmd>
    {
        public void Invoke(TajsOverclockSetRateCmd command)
        {
            if (!TryGetFeature(command, out TajsOverclockingFeature feature))
            {
                OverclockingInspectorPatch.CommandRejected(
                    command.TargetId,
                    OverclockingInspectorPatch.OverclockPendingOperation.Manual,
                    command.Rate.ToIntPercentRounded(),
                    null);
                return;
            }

            ApplyManual(feature, command, command.TargetId, command.Rate.ToIntPercentRounded());
        }

        public void Invoke(TajsOverclockPolicyCmd command)
        {
            if (!TryGetFeature(command, out TajsOverclockingFeature feature))
            {
                RejectWithoutFeature(command);
                return;
            }

            int[] affected = Array.Empty<int>();
            switch (command.Operation)
            {
                case TajsOverclockPolicyOperation.SetManual:
                    ApplyManual(feature, command, command.TargetId, command.Percent);
                    return;

                case TajsOverclockPolicyOperation.SetAuto:
                    ApplyAuto(feature, command);
                    return;

                case TajsOverclockPolicyOperation.Reset:
                    ApplyReset(feature, command);
                    return;

                case TajsOverclockPolicyOperation.AddToGroup:
                case TajsOverclockPolicyOperation.RemoveFromGroup:
                    affected = new[] { command.TargetId.Value };
                    break;

                case TajsOverclockPolicyOperation.DeleteGroup:
                case TajsOverclockPolicyOperation.SetGroupDefault:
                case TajsOverclockPolicyOperation.ApplyGroup:
                case TajsOverclockPolicyOperation.SetGroupAuto:
                    affected = CaptureGroupMembers(feature, command.GroupId);
                    break;

                default:
                    command.SetResultError("The overclock policy command has an unknown operation.");
                    return;
            }

            bool applied;
            string message;
            switch (command.Operation)
            {
                case TajsOverclockPolicyOperation.AddToGroup:
                    applied = feature.ApplyAddToGroup(command.GroupId, command.TargetId);
                    message = applied ? string.Empty : "Entity/group missing, locked, or unsupported.";
                    break;

                case TajsOverclockPolicyOperation.RemoveFromGroup:
                    applied = feature.ApplyRemoveFromGroup(command.GroupId, command.TargetId);
                    message = applied ? string.Empty : "Entity/group missing, locked, or not a member.";
                    break;

                case TajsOverclockPolicyOperation.DeleteGroup:
                    applied = feature.ApplyDeleteGroup(command.GroupId);
                    message = applied ? string.Empty : "Group is missing or locked.";
                    break;

                case TajsOverclockPolicyOperation.SetGroupDefault:
                    applied = feature.ApplyGroupDefault(command.GroupId, command.Percent, out message);
                    break;

                case TajsOverclockPolicyOperation.ApplyGroup:
                    applied = feature.ApplyGroupToMembers(command.GroupId, command.Percent, out message);
                    break;

                case TajsOverclockPolicyOperation.SetGroupAuto:
                    applied = feature.ApplyGroupAuto(
                        command.GroupId,
                        command.Enabled,
                        command.HasMinimum ? command.Minimum : null,
                        command.HasMaximum ? command.Maximum : null,
                        out message);
                    break;

                default:
                    command.SetResultError("The overclock policy command has an unknown operation.");
                    return;
            }

            if (!applied)
            {
                command.SetResultError(message);
                RefreshEntities(affected);
                return;
            }

            command.SetResultSuccess();
            RefreshEntities(affected);
        }

        private static void ApplyManual(
            TajsOverclockingFeature feature,
            InputCommand command,
            EntityId entityId,
            int percent)
        {
            if (feature.ApplyManual(entityId, percent, out string message))
            {
                command.SetResultSuccess();
                OverclockingInspectorPatch.CommandApplied(
                    entityId,
                    OverclockingInspectorPatch.OverclockPendingOperation.Manual,
                    percent,
                    null);
                return;
            }

            command.SetResultError(message);
            OverclockingInspectorPatch.CommandRejected(
                entityId,
                OverclockingInspectorPatch.OverclockPendingOperation.Manual,
                percent,
                null);
        }

        private static void ApplyAuto(TajsOverclockingFeature feature, TajsOverclockPolicyCmd command)
        {
            int? minimum = command.HasMinimum ? command.Minimum : null;
            int? maximum = command.HasMaximum ? command.Maximum : null;
            if (feature.ApplyAutoPolicy(command.TargetId, command.Enabled, minimum, maximum, out string message))
            {
                command.SetResultSuccess();
                OverclockingInspectorPatch.CommandApplied(
                    command.TargetId,
                    OverclockingInspectorPatch.OverclockPendingOperation.Auto,
                    null,
                    command.Enabled);
                return;
            }

            command.SetResultError(message);
            OverclockingInspectorPatch.CommandRejected(
                command.TargetId,
                OverclockingInspectorPatch.OverclockPendingOperation.Auto,
                null,
                command.Enabled);
        }

        private static void ApplyReset(TajsOverclockingFeature feature, TajsOverclockPolicyCmd command)
        {
            if (feature.ApplyResetPolicy(command.TargetId, out string message))
            {
                command.SetResultSuccess();
                OverclockingInspectorPatch.CommandApplied(
                    command.TargetId,
                    OverclockingInspectorPatch.OverclockPendingOperation.Reset,
                    null,
                    null);
                return;
            }

            command.SetResultError(message);
            OverclockingInspectorPatch.CommandRejected(
                command.TargetId,
                OverclockingInspectorPatch.OverclockPendingOperation.Reset,
                null,
                null);
        }

        private static bool TryGetFeature(InputCommand command, out TajsOverclockingFeature feature)
        {
            if (TajsOverclockingFeature.Current is TajsOverclockingFeature current)
            {
                feature = current;
                return true;
            }

            feature = null!;
            command.SetResultError("Per-machine overclocking is unavailable in this scene.");
            return false;
        }

        private static void RejectWithoutFeature(TajsOverclockPolicyCmd command)
        {
            switch (command.Operation)
            {
                case TajsOverclockPolicyOperation.SetManual:
                    OverclockingInspectorPatch.CommandRejected(
                        command.TargetId,
                        OverclockingInspectorPatch.OverclockPendingOperation.Manual,
                        command.Percent,
                        null);
                    break;

                case TajsOverclockPolicyOperation.SetAuto:
                    OverclockingInspectorPatch.CommandRejected(
                        command.TargetId,
                        OverclockingInspectorPatch.OverclockPendingOperation.Auto,
                        null,
                        command.Enabled);
                    break;

                case TajsOverclockPolicyOperation.Reset:
                    OverclockingInspectorPatch.CommandRejected(
                        command.TargetId,
                        OverclockingInspectorPatch.OverclockPendingOperation.Reset,
                        null,
                        null);
                    break;
            }
        }

        private static int[] CaptureGroupMembers(TajsOverclockingFeature feature, int groupId) =>
            feature.GetGroup(groupId)?.Members.OrderBy(id => id).ToArray() ?? Array.Empty<int>();

        private static void RefreshEntities(int[] entityIds)
        {
            foreach (int entityId in entityIds)
            {
                OverclockingInspectorPatch.RefreshAllForEntity(new EntityId(entityId));
            }
        }
    }
}
