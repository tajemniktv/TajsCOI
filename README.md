# Taj's COI - Captain of Industry mods

Taj's COI is a collection of Captain of Industry mods by TajemnikTV. The suite adds quality-of-life
changes, optional performance experiments, and tools for understanding what the game is doing.

The current compatibility target is Captain of Industry 0.8.7b.

## Mods

### Taj's Core

Taj's Core provides the shared support used by the other Taj's COI mods. It also provides the in-game dashboard for
viewing suite status, changing available settings, and running audited save-repair tools.

Core is intended to be quiet on its own and does not add gameplay changes by itself.

#### Save sanitizer

The Core sanitizer is opt-in and type-specific. Start with a dry-run report:

```text
tajs_save_sanitize_report
```

For a supported finding, repair into a new save slot with an explicit confirmation:

```text
tajs_save_sanitize_repair <target> CONFIRM <new-save-name>
```

Supported targets currently include `infinite_groundwater`, `ship_auto_explore`,
`world_map_quick_trades`, and the allow-listed `stale_tajs_config` migration. Existing `tajs_infinite_groundwater_migrate` and
`tajs_ship_auto_explore_migrate` commands remain as detach-only compatibility aliases. Unknown or uncertain save data is
reported but not changed, and existing/current save files are never overwritten by the Core repair command.

### Taj's Tweaks

Taj's Tweaks contains quality-of-life and simulation changes.

The optional world efficiency overlay adds a dedicated toolbar toggle and compact toolbox. It shows the native
productivity history of buildings and supported vehicles as a percentage, status, or colored marker. Labels are pooled,
updated on a bounded cadence, and culled by camera distance; buildings and vehicles can be filtered independently.

The optional groundwater policy keeps the native weather-driven behavior by default and can instead
regenerate a bounded amount, maintain a minimum reserve, or fill missing capacity each in-game day.
It is implemented with one gameplay-scoped owner and non-saveable calendar callbacks, so loaded
saves do not retain stale service references. In sandbox mode, `tajs_groundwater_refill` manually
fills all deposits to their native capacity. Disable the separate standalone InfiniteGroundwater
mod before enabling a non-Vanilla policy to avoid duplicate callbacks.

#### Migrating an existing Infinite Groundwater save

If a save was created with the standalone InfiniteGroundwater mod, keep that mod enabled while
loading the save. With Taj's Tweaks loaded, run this console command:

```text
tajs_infinite_groundwater_migrate
```

Only save after the command reports success, preferably to a new save slot. The command detaches
the legacy saveable callback and resolver object. You can then disable the standalone mod and load
the new copy. If migration reports failure, do not save that game instance.

#### Migrating an existing ShipAutoExplore save

ShipAutoExplore also stores a runtime-only controller callback in the save. To uninstall it safely, keep the standalone
mod enabled while loading the save, then run:

```text
tajs_ship_auto_explore_migrate
```

Only save after the command reports success, preferably to a new save slot. Quit and reload that new copy once, then
disable the standalone ShipAutoExplore mod. Taj's Tweaks does not replace its auto-explore behavior; this command only
removes the stale save callback and resolver object.

#### Unlocked simulation speed

Allows simulation speeds above the normal 20x limit, up to the configured maximum.

To use it, choose the maximum speed in the Taj's COI dashboard and request a speed from the
in-game console:

```text
set_game_speed_unlocked <speed>
get_game_speed_unlocked
```

The current requested multiplier is also shown beside the native calendar speed controls.

#### Transport pillars

Taj's Tweaks exposes bounded transport and train pillar support/height settings with vanilla defaults. Changes require a
game restart. The native pillar toolbar supports single-tile add/remove operations; rectangular transport operations
are available through `tajs_transport_pillars_area` and retain the game's structural validation.
The advanced `Ignore pillar requirements` setting can disable pillar requirements for transports and elevated
layout entities; it is restart-scoped and leaves terrain/occupancy checks active.

### Taj's Profiler

Taj's Profiler provides optional, behavior-neutral diagnostics for investigating slowdowns and
runtime spikes. It observes save/load work, memory, garbage collection, rendering-related work,
simulation lifecycle events, and selected game subsystems without changing gameplay behavior.

### Taj's Performance

Taj's Performance contains optional performance experiments. Every feature is off by default and
can be enabled from the Taj's COI dashboard when you want to try it:

- larger buffered save/load reads;
- streaming save compression;
- lower product textures;
- paused-only manual asset trimming; and
- conservative product-buffer shrinking;
- opt-in lazy resource-visualization building.

These features are experimental. Settings that affect startup or save/load behavior may require a
game restart, and saves should be checked after trying them. Lazy resource-visualization building
defers the synchronous first-use initialization on the current 0.8.7b target; profile the first
overlay activation and verify overlay correctness before enabling it for regular play.

## Using the suite

When the suite is available in-game, use the Taj's COI dashboard to see which settings and features
are present. Feature descriptions in the dashboard explain whether a change is immediate or needs
a restart.
