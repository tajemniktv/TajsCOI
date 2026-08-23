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

The dashboard is available in a running game. Main-menu dashboard registration is not currently supported.
