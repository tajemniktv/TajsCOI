// Taj's COI Mods | TajsOverclockCommandsProcessor.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi;
using Mafi.Core.Input;

namespace TajsCOI.Tweaks.Features.Overclocking
{
    [GlobalDependency(RegistrationMode.AsAllInterfaces, false, false)]
    internal sealed class TajsOverclockCommandsProcessor : ICommandProcessor<TajsOverclockSetRateCmd>, IAction<TajsOverclockSetRateCmd>
    {
        public void Invoke(TajsOverclockSetRateCmd command)
        {
            TajsOverclockingFeature? feature = TajsOverclockingFeature.Current;
            if (feature is null)
            {
                command.SetResultError("Per-machine overclocking is unavailable in this scene.");
                return;
            }

            if (feature.ApplyManual(command.TargetId, command.Rate.ToIntPercentRounded(), out string message))
            {
                OverclockingInspectorPatch.RefreshAllForEntity(command.TargetId);
                command.SetResultSuccess();
            }
            else
            {
                command.SetResultError(message);
            }
        }
    }
}
