Taj's Tweaks
============

A small Captain of Industry tweak/QoL mod and development playground.
Requires Taj's Core 0.1.0 or newer.

Current feature:
- Unlocked requested simulation speed.
- Maximum unlocked speed is configurable (default 100x, allowed range 20x-500x).
- Optional line-placement limits, pinned-product sorting/filtering/columns/colors, storage
  defaults, bounded resource/tower labels, camera/HUD controls, designation limits, notification
  filters, storage capacity/throughput overrides, mine-truck staging, and stranded-truck recovery.
- Optional world operations, ship preload/auto-delivery, and native management windows for
  repairs, mines/rigs, settlements, ship cargo, and fleet status/order/scrap/replacement.
  All world, vehicle, and depot mutations use normal game commands/jobs.
- The optional terrain-grid toolbar toggle uses the game's native counted grid activator,
  remembers its enabled preference across gameplay-scene recreation, and remains independent
  while switching tools and overlays.
- The optional world efficiency overlay adds a toolbar toggle and toolbox for percentage, status,
  or compact colored-marker labels above buildings and supported vehicles. Labels use native
  productivity history, bounded refresh cadence, pooling, camera culling, and independent filters.
- Line placement uses the native placement controller. Hold the configured `LeftAlt` shortcut
  while anchoring a single building to start a straight row; the row cap is configurable and
  invalid previews remain native-invalid. The controller cleans up on cancel/deactivate.
- Resource depth labels are grouped by contiguous native resource chunks. The first toggle can
  sample a large map once; later terrain changes debounce through native dirty chunks and reuse
  unaffected cluster labels. Toggling the option may hitch once on a large save.
- Transport and train pillar rules have bounded advanced controls for support spacing and maximum
  heights. Defaults match vanilla and changes require a game restart. The game's native pillar
  toolbar remains the single-tile Add/Remove tool; rectangular transport batches use the area
  command below and still route through native support/collision validation.
- The advanced Ignore pillar requirements setting disables pillar requirements for transports and
  elevated layout entities while retaining native terrain and occupancy checks. It requires a restart.
- TajsDifficulty widens the native new-game difficulty choices with bounded custom percentage
  ranges for economy, resources, settlements, environment, population, power, and progression.
  The native COI Difficulty Settings window is the runtime editor: it stages changes, shows the
  native diff/history/cooldown workflow, and saves one combined transaction. Tajs console edits
  rebase on the latest native config, while original-save values are kept in an identity-checked
  scalar sidecar so they can be restored separately from the save's vanilla preset defaults.

Console commands:
- set_game_speed_unlocked <speed>
- get_game_speed_unlocked
- tajs_world_operations_status
- tajs_world_operations
- tajs_world_operations_apply <repair|cancel-repair|upgrade> <entity-id> CONFIRM
- tajs_transport_pillars_status
- tajs_transport_pillars_reset
- tajs_transport_pillars_area <add|remove> <min-x> <min-y> <max-x> <max-y> [CONFIRM]
- tajs_fleet_status
- tajs_fleet_manager
- tajs_fleet_order <prototype-id> <count> CONFIRM
- tajs_fleet_scrap_type <prototype-id> <count> CONFIRM [assigned-only|unassigned-first|any]
- tajs_fleet_replace_type <source-id> <target-id> <count> CONFIRM [assigned-only|unassigned-first|any]
- tajs_fleet_apply <scrap|replace> <comma-separated IDs> CONFIRM [target-prototype-id]
- tajs_fleet_cancel <scrap|replace> <comma-separated IDs> CONFIRM
- tajs_hud_status
- tajs_hud_reset
- tajs_difficulty (points to the native COI Difficulty Settings window)
- tajs_difficulty_status
- tajs_difficulty_set <GameDifficultyConfig-member> <value> [CONFIRM]
- tajs_difficulty_reset <original|vanilla> CONFIRM
