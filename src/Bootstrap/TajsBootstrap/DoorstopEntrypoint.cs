// Taj's COI Mods | DoorstopEntrypoint.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

namespace Doorstop
{
    /// <summary>
    ///     UnityDoorstop's minimal managed entrypoint. Early startup must remain fail-open so a
    ///     missing or incompatible canonical Harmony assembly never prevents vanilla startup.
    /// </summary>
    public static class Entrypoint
    {
        public static void Start()
        {
            try
            {
                // Doorstop supplies DOORSTOP_PROCESS_PATH before invoking the managed entrypoint;
                // the fallback keeps direct/manual invocation useful as well.
                string? executable = System.Environment.GetEnvironmentVariable("DOORSTOP_PROCESS_PATH");
                string? bootstrapAssembly = System.Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH");
                string? gameRoot = TajsCOI.Bootstrap.BootstrapInstaller.DiscoverGameRoot(executable);
                TajsCOI.Bootstrap.BootstrapApi.InitializeFromGameRoot(gameRoot, bootstrapAssembly);
            }
            catch
            {
                // Doorstop must leave the normal no-bootstrap installation available.
            }
        }
    }
}
