// Taj's COI Mods | RuntimeSceneLifecycle.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using Mafi;
using Mafi.Core.GameLoop;
using TajsCOI.Common.Runtime;

namespace TajsCOI.Core.Runtime
{
    /// <summary>
    ///     Bridges the gameplay-scene lifecycle to Core's process-lifetime registry without making
    ///     the registry retain resolver-scoped objects.
    /// </summary>
    [GlobalDependency(RegistrationMode.AsSelf)]
    internal sealed class RuntimeSceneLifecycle
    {
        private readonly ITajsRuntime m_runtime;

        public RuntimeSceneLifecycle(IGameLoopEvents gameLoop, ITajsRuntime runtime)
        {
            m_runtime = runtime;
            gameLoop.Terminate.AddNonSaveable(this, OnTerminate);
        }

        private void OnTerminate() => m_runtime.ClearGameplaySceneRegistrations();
    }
}
