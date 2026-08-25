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
- lazy resource-visualization building;
- paused-only manual asset trimming; and
- conservative product-buffer shrinking.

These features are experimental. Settings that affect startup or save/load behavior may require a
game restart, and saves should be checked after trying them.

## Using the suite

When the suite is available in-game, use the Taj's COI dashboard to see which settings and features
are present. Feature descriptions in the dashboard explain whether a change is immediate or needs
a restart.
