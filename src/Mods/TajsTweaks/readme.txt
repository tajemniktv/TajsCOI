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
- Optional world operations, ship preload/auto-delivery, and bounded fleet status/order/scrap/
  replacement commands. All world, vehicle, and depot mutations use normal game commands/jobs.

Console commands:
- set_game_speed_unlocked <speed>
- get_game_speed_unlocked
- tajs_world_operations_status
- tajs_world_operations_apply <repair|cancel-repair|upgrade> <entity-id> CONFIRM
- tajs_fleet_status
- tajs_fleet_order <prototype-id> <count> CONFIRM
- tajs_fleet_scrap_type <prototype-id> <count> CONFIRM [assigned-only|unassigned-first|any]
- tajs_fleet_replace_type <source-id> <target-id> <count> CONFIRM [assigned-only|unassigned-first|any]
- tajs_fleet_apply <scrap|replace> <comma-separated IDs> CONFIRM [target-prototype-id]
- tajs_fleet_cancel <scrap|replace> <comma-separated IDs> CONFIRM
- tajs_hud_status
- tajs_hud_reset
