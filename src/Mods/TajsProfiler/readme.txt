Taj's Profiler
==============

Behavior-neutral runtime diagnostics for Captain of Industry.
Requires Taj's Core 0.1.0 or newer.

Current probes:
- Dumping destination search and pathfinding-stage diagnostics.
- Save/load stage timing, memory, GC, and product-renderer buffer diagnostics.

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

Runtime captures are held in a bounded in-memory history. They do not modify saves or gameplay.
