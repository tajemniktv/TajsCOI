# Taj's COI - Captain of Industry mods

Taj's COI is a collection of Captain of Industry mods by TajemnikTV. The suite adds quality-of-life
changes, optional performance experiments, and tools for understanding what the game is doing.

The current compatibility target is Captain of Industry 0.8.7b.

## Mods

### Taj's Core

Taj's Core provides the shared support used by the other Taj's COI mods. It also provides the
in-game dashboard for viewing suite status and changing available settings.

Core is intended to be quiet on its own and does not add gameplay changes by itself.

### Taj's Tweaks

Taj's Tweaks contains quality-of-life and simulation changes.

The optional Infinite ground water setting refills virtual groundwater deposits to their configured
capacity at game initialization and at the start of each in-game day. It is implemented with
non-saveable lifecycle callbacks so loaded saves do not retain stale service references. Disable
the separate standalone InfiniteGroundwater mod before enabling this setting to avoid duplicate
callbacks.

#### Migrating an existing Infinite Groundwater save

If a save was created with the standalone InfiniteGroundwater mod, keep that mod enabled while
loading the save. With Taj's Tweaks loaded, run this console command:

```text
tajs_infinite_groundwater_migrate
```

Only save after the command reports success, preferably to a new save slot. The command detaches
the legacy saveable callback and resolver object. You can then disable the standalone mod and load
the new copy. If migration reports failure, do not save that game instance.

#### Unlocked simulation speed

Allows simulation speeds above the normal 20x limit, up to the configured maximum.

To use it, choose the maximum speed in the Taj's COI dashboard and request a speed from the
in-game console:

```text
set_game_speed_unlocked <speed>
get_game_speed_unlocked
```

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
- conservative product-buffer shrinking.

These features are experimental. Settings that affect startup or save/load behavior may require a
game restart, and saves should be checked after trying them. Lazy resource-visualization building
is intentionally not available on the current 0.8.7b target because its first-use initialization
is synchronous and can freeze when the native resource overlay is opened.

## Using the suite

When the suite is available in-game, use the Taj's COI dashboard to see which settings and features
are present. Feature descriptions in the dashboard explain whether a change is immediate or needs
a restart.
