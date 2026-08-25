Taj's Profiler
==============

Behavior-neutral runtime diagnostics for Captain of Industry.
Requires Taj's Core 0.1.0 or newer.

Current probes:
- Dumping destination search and pathfinding-stage diagnostics.
- Save/load stage timing, memory, GC, and product-renderer buffer diagnostics.
- Low-overhead runtime flight recorder backed by Captain of Industry's 0.8.7b
  GameLoopTimings ring and broad GameRunner timings.

Console commands:
- tajs_dump_search_stats
- tajs_dump_search_stats_reset
- tajs_dump_pf_stats
- tajs_dump_pf_stats_reset
- tajs_dump_profile <seconds> [label] [warmupSeconds]
- tajs_dump_profile_status
- tajs_dump_profile_stop
- tajs_dump_profile_cancel
- tajs_dump_profiles
- tajs_dump_profile_show <label>
- tajs_dump_profile_clear
- tajs_dump_profile_compare <labelA> <labelB>
- tajs_runtime_profile_capture <label>
- tajs_runtime_profiles
- tajs_runtime_profile_show <label>
- tajs_runtime_profile_compare <before> <after>
- tajs_runtime_profile_clear
- tajs_runtime_profile_reset
- tajs_profiler_status
- tajs_profiler_runtime [seconds]
- tajs_profiler_spikes [count]
- tajs_profiler_runtime_raw [count]
- tajs_profiler_runtime_clear

Runtime captures are held in a bounded in-memory history. They do not modify saves or gameplay.
The runtime flight recorder keeps a separate bounded frame history and calculates percentiles only
when a command is requested. If the game's private timing surface changes, the probe reports the
unsupported detail and leaves the rest of Taj's Profiler active.
