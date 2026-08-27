Taj's Profiler
==============

Behavior-neutral runtime diagnostics for Captain of Industry.
Requires Taj's Core 0.1.0 or newer.

Current probes:
- Dumping destination search and pathfinding-stage diagnostics.
- Save/load stage timing, memory, and product-renderer buffer diagnostics, including
  behavior-neutral per-pass scene-teardown GC telemetry (ordinal, elapsed time,
  finalizer-drain time, managed before/after, reclaimed bytes, and GC counts).
- Low-overhead runtime flight recorder backed by Captain of Industry's 0.8.7b
  GameLoopTimings ring and broad GameRunner timings.
- Configurable absolute/relative spike triggers with bounded pre/post captures.
- Opt-in deep callback tracing, callback ranking, markers, and Chrome trace JSON export.
- Managed/Unity/GC counters where supported, optional player ProfilerRecorder render counters,
  explicit unsupported graphics-memory handling, and shared dumping/pathfinding/terrain timeline correlation.
- One TajsProfiler subsystem report and trace model for all registered probe counters/events.

Console commands:
- tajs_profiler_dumping
- tajs_profiler_subsystems [seconds]
- tajs_profiler_subsystems_clear

Legacy detailed dumping compatibility views:
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
- tajs_profiler_arm [seconds]
- tajs_profiler_deep_start [seconds]
- tajs_profiler_deep_stop
- tajs_profiler_deep_report [count]
- tajs_profiler_deep_worst [count]
- tajs_profiler_deep_overhead_bench [iterations]
- tajs_profiler_counter_overhead_bench [iterations]
- tajs_profiler_trace_start [seconds]
- tajs_profiler_trace_stop
- tajs_profiler_trace_export [name]
- tajs_profiler_runtime_export_csv [name]
- tajs_profiler_mark <label>
- tajs_profiler_overhead_bench [iterations]
- tajs_profiler_spike_policy [frameMs] [waitMs] [simMs] [phaseMs] [relative] [cooldown] [maxCaptures] [preSeconds] [postSeconds]
- tajs_profiler_auto_deep [true|false]

The immediate `counter_sampling_ms` setting bounds managed/Unity memory query frequency (50-2000
ms, default 250 ms). `tajs_profiler_status` reports the exact resolved counter sources. Graphics
driver allocation is not dedicated VRAM; a zero/unsupported value is reported as unavailable.
GPU-bound classification requires a trusted player-build GPU frame-time counter.

Runtime captures are held in a bounded in-memory history. They do not modify saves or gameplay.
The runtime flight recorder keeps a separate bounded frame history and calculates percentiles only
when a command is requested. `tajs_profiler_subsystems` prints explicit counter owners and a
duration-based top-contributor roll-up; `tajs_profiler_subsystems_clear` starts a clean counter
interval. `tajs_profiler_deep_overhead_bench` also reports the enabled metadata/owner lookup plus
span-recording core separately from the complete wrapper. `tajs_profiler_overhead_bench` reports
both the validated timing reader and bounded flight-write cost; the counter benchmark remains separate. If the game's private timing surface changes, the probe reports the
unsupported detail and leaves the rest of Taj's Profiler active. Deep callback timing is off until
explicitly armed; trace files are written below the user's application-data `TajsCOI/Profiler`
directory and can be opened in Chrome's trace viewer. Runtime `gpu-frame` timing is separate from
the reported `graphics-driver-memory` allocation counter and is unavailable without a trusted
player-build GPU timing surface. The TajsCOI dashboard exposes the same profiler controls and
persisted threshold/window settings.
