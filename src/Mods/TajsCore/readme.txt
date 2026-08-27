Taj's Core
==========

Runtime foundation for the Taj's COI mod suite.

Core deliberately has no gameplay-visible features. Other Taj's COI mods use it as
their required suite-level lifecycle and compatibility boundary.

Console commands:
- tajs_core_info
- tajs_core_status
- tajs_dashboard
- tajs_settings_list
- tajs_settings_set <ModId.key> <value>

Global suite settings are stored outside savegames in:
%APPDATA%\Captain of Industry\TajsCOI\settings.json

The dashboard is available in a running game, and the main menu exposes a global/profile-safe
Control Center entry when the supported 0.8.7b menu seam is available. Gameplay-scoped services
remain unavailable from that main-menu surface. The running-game dashboard has a dedicated
Bootstrap page. Its Install action writes a Doorstop configuration targeting the currently
installed TajsBootstrap.dll in this mod directory, and copies the Tajs-managed repair payload,
bundled x64 UnityDoorstop `winhttp.dll`, and `doorstop_config.ini` into the game root for the next
launch. Doorstop does not expand `%APPDATA%`, so the installer records the resolved mod path;
updating the mod then updates the managed bootstrap target automatically. It never replaces the
existing `version.dll`; unknown root Doorstop files are refused rather than overwritten.
