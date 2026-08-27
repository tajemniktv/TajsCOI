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
                string? gameRoot = TajsCOI.Bootstrap.BootstrapInstaller.DiscoverGameRoot();
                TajsCOI.Bootstrap.BootstrapApi.InitializeFromGameRoot(gameRoot);
            }
            catch
            {
                // Doorstop must leave the normal no-bootstrap installation available.
            }
        }
    }
}
