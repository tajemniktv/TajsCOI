# TajsProfiler runtime flight recorder

This document records the runtime flight recorder from [issue #38](https://github.com/tajemniktv/TajsCOI/issues/38)
and the runtime flight-recorder milestone from [issue #29](https://github.com/tajemniktv/TajsCOI/issues/29).

## Runtime design

`GameLoopTimingDiagnosticsService` subscribes once to the main-thread `InputUpdate` event. Each
callback harvests each newly completed entry from Captain of Industry's existing `GameLoopTimings`
rings and captures the public simulation state plus broad `GameRunner` durations. Sampling happens
inside `InputUpdate`, before the current `GameRunner` update-duration properties are finalized, so
the safe ring entries and runner values are intentionally treated as the previous completed update
boundary rather than being presented as an in-flight current-frame snapshot. Each event has an
independent producer cursor, so faster simulation phases are not skipped and slower phases are not
duplicated. `GameLoopTimings.End` advances the writer index before filling the entry, so the newest
slot may still be in flight; entries that overrun the 2,048-slot retention window are reported as
drops. The optional `GameRunner` surface is injected as a `LazyResolve<IGameIdProvider>` and is
discovered on the first post-initialization sampling callback, never by resolving from the profiler
constructor. This keeps GameLoopTimings as the primary source without creating a dependency-resolver
cycle during save/load startup.

The private timing adapter validates all of the following before it becomes active:

- the 20-event enum names and numeric order;
- the `Entry.Start`/`Entry.End` timestamp fields;
- the 2,048-entry ring capacity and backing array lengths; and
- the expected access methods and write-index array.

After validation, dynamic field readers are created once. No `FieldInfo.GetValue`, string
formatting, LINQ, sorting, or per-frame managed allocation is used by the sampling callback.
If the private shape changes, this component reports a degraded compatibility result while the
other TajsProfiler probes remain available.

Deep callback phase attribution is tied to the exact `Event`/`EventNonSaveable` instance rather
than its concrete type. COI reuses event types across main-thread and simulation-thread phases,
so this instance mapping is combined with a thread-local, nestable phase scope. Overlapping main
and simulation dispatches therefore retain their own `SYNC`, `RENDER`, `SIM_UPDATE`,
`SIM_READ_STATE`, and related phase labels without consulting global simulation state.

The resolver-scoped history retains 4,096 primitive frame samples. Console commands copy and sort
the bounded history only when requested, producing p50/p95/p99/max summaries for frame/update,
render, wait-for-simulation, and simulation work. Classifications are deliberately conservative:
waiting-for-simulation requires the overtime signal and a material wait interval; GC-related and
likely-GPU-bound are reported only when their corresponding telemetry is trusted; otherwise the
sample is classified by the relative broad main/render and simulation measurements.
The `gpu-frame` status is deliberately separate from memory: this build has no trusted per-frame
GPU timing source unless the player exposes Unity's `ProfilerRecorder` `Render/GPU Frame Time`
counter. `graphics-driver-memory` uses Unity's `GetAllocatedMemoryForGraphicsDriver` counter.
That counter is graphics-driver allocation, not a dedicated-VRAM measurement; zero is treated as
unavailable because unsupported graphics backends commonly return zero. Optional player counters
for main/render-thread time, draw calls, batches, triangles, vertices, and `GC.Alloc` are accepted
only when `ProfilerRecorder` reports the expected unit. `tajs_profiler_status` and the startup
compatibility report list the source and support state of each counter.

| Counter family | Source and unit | Sampling model | Zero/support rule |
| --- | --- | --- | --- |
| Managed heap | `GC.GetTotalMemory(false)`, bytes | rate-limited memory read; no forced collection | positive values only |
| Unity/Mono memory | Unity `Profiler` long getters, bytes | rate-limited reflection-resolved delegates | missing getter or zero is unavailable |
| GC collections | `GC.CollectionCount(0..2)`, cumulative counts/deltas | cheap primitive read on every frame sample | counter reset is treated as a zero delta |
| Render/GPU context | Unity `ProfilerRecorder`, time in nanoseconds or counts/bytes as reported | primitive `LastValue` reads each frame | recorder must be valid and expose the expected unit |

The current verified 0.8.7b reference exposes the `ProfilerRecorder` type, but a shipping player or
graphics backend may expose none of the named counters. That runtime result is recorded as
unsupported; Editor-only availability is not used as evidence. The graphics-driver value is
sampled at the configured interval, while ProfilerRecorder values are captured at each flight
recorder sample. The counter-overhead command measures this path separately.
Paused samples remain available in raw history but are excluded from gameplay summaries, rolling
spike baselines, and spike ranking. The runtime summary reports how many paused samples were
excluded. GC deltas are per-sample observations taken between counter reads; the runtime summary
also reports the interval total and peak so a later zero latest delta does not hide a collection
that caused an earlier classified frame.

Automatic spike capture uses absolute frame, wait, simulation, and major-phase thresholds plus an
optional relative multiplier. It stores a bounded pre/post window, applies cooldown and maximum
capture limits, and recognizes the existing simulation-overtime signal. `tajs_profiler_auto_deep`
can opt automatic spikes into the bounded callback recorder.

Deep tracing is inactive by default. When armed, the recorder patches the validated callback
invocation forms on the existing `Event` and `EventNonSaveable` objects. It records numeric spans
on per-thread bounded rings, interns owner/method/assembly strings through weak owner keys, keeps
callback order and exception behavior, and ranks total, share of captured callback time, average,
p95, p99, maximum, slow-call count, and the timestamp of the worst invocation. The separate
`tajs_profiler_deep_worst` view ranks individual callback executions by duration, which exposes
rare hitch contributors that total-time ranking can hide. Unsupported callback forms fail
independently and are shown in compatibility status. Phase-registration conflicts are counted in
compatibility output instead of silently leaving users with unexplained `UNKNOWN` labels.

The runtime summary includes the last deep-capture callback count and measured instrumentation
overhead per captured frame. `tajs_profiler_deep_overhead_bench` compares a direct callback, the
deep-disabled wrapper, the complete deep-enabled wrapper, and a separate deep-enabled core path
that includes metadata/owner lookup, timestamping, callback execution, and span recording. The
last case isolates the likely expensive work from phase-scope and overhead-accounting costs. It
uses an isolated temporary span ring and does not replace an existing capture.
`tajs_profiler_overhead_bench` separately measures the validated GameLoopTimings reader and the
bounded primitive flight-recorder write, while `tajs_profiler_counter_overhead_bench` measures
the optional counter sampler.

Managed/Unity memory queries are rate-limited by the immediate `counter_sampling_ms` setting
(50-2000 ms, default 250 ms). ProfilerRecorder values are cheap primitive reads and are sampled
with the frame record. `tajs_profiler_counter_overhead_bench` measures the sampler loop and
explicitly reports that it performs no forced GC. Counter handles are disposed when the gameplay
service terminates, so optional Unity diagnostics do not outlive the resolver scene.

Subsystem probes publish through a bounded Core-owned telemetry store. Counter registration happens
once during probe setup; each counter also records an explicit owner such as
`TajsProfiler.Dumping`, `TajsProfiler.Pathfinding`, or `TajsProfiler.Terrain`. Hot-path publication
is an atomic numeric add/increment with no strings,
LINQ, locks, or per-call allocation. Frame capture copies counter deltas into fixed value fields,
so the runtime summary, spike records, subsystem report, and trace export consume the same timeline
model. Sparse probe events use a preallocated ring and are intended for state changes or threshold
crossings such as dumping-profile transitions and unusually long dump searches.

The dumping probe owns the COI-specific Harmony bindings and detailed search breakdowns, while
Core owns the shared counter/event transport. `tajs_profiler_subsystems` aggregates the published
counters over a paused-excluded interval, prints each counter's owner, and adds a top-contributor
roll-up ranked only from stopwatch-duration counters. `tajs_profiler_subsystems_clear` resets the
shared counter interval and clears the frame timeline so a new experiment starts cleanly.
`tajs_profiler_dumping` is the unified-prefixed entry
point for the detailed dumping view. The existing `tajs_dump_*` commands remain compatibility
views for product/path/cache breakdowns and timed dump comparisons; their duplicate timed history
is retained until a future shared interval-comparison view can represent those probe-specific
dimensions without losing detail. Published duration counters are cumulative observed work, not
exclusive wall time; nested or concurrent work can therefore exceed the enclosing frame duration.

The exporter writes Chrome trace-event JSON with main/simulation phase spans, callback spans,
markers, sparse probe events, memory/GC counters, and interval-named subsystem counters. Duration
counters are emitted in milliseconds. Unsupported values are emitted as `"unavailable"`, never as
synthetic zeroes. GPU classification remains unavailable unless a trusted player-build telemetry
surface is added.
The explicit trace window is independent from deep mode: `trace_start`/`trace_stop` bound the
frames, callback spans, markers, and telemetry included by the following export. A CSV export is
also available for broad frame comparisons; unsupported numeric counters are blank rather than
being misrepresented as zero.

## Commands

```text
tajs_profiler_status
tajs_profiler_runtime [seconds]
tajs_profiler_subsystems [seconds]
tajs_profiler_subsystems_clear
tajs_profiler_dumping
tajs_profiler_spikes [count]
tajs_profiler_runtime_raw [count]
tajs_profiler_runtime_clear
tajs_profiler_arm [seconds]
tajs_profiler_deep_start [seconds]
tajs_profiler_deep_stop
tajs_profiler_deep_report [count]
tajs_profiler_deep_worst [count]
tajs_profiler_deep_overhead_bench [iterations]
tajs_profiler_counter_overhead_bench [iterations]
tajs_profiler_trace_start [seconds]
tajs_profiler_trace_stop
tajs_profiler_trace_export [name]
tajs_profiler_runtime_export_csv [name]
tajs_profiler_mark <label>
tajs_profiler_overhead_bench [iterations]
tajs_profiler_spike_policy [frameMs] [waitMs] [simMs] [phaseMs] [relative] [cooldown] [maxCaptures] [preSeconds] [postSeconds]
tajs_profiler_auto_deep [true|false]
```

The raw view shows all 20 captured phase values. `sim-update` is the most recent phase sample,
while `sim` uses `GameRunner.LatestSimUpdateDuration` when that broad surface is available; this
distinction matters when accelerated simulation runs multiple steps in one worker cycle.

Deep capture and export are command-driven so ordinary sampling does not perform JSON work,
reflection, sorting, or file I/O. The existing TajsCOI dashboard now exposes compact profiler
controls and the persisted threshold/window settings, while the console and external Chrome trace
viewer remain the detailed inspection surfaces. In-game acceptance and viewer interaction remain
runtime validation steps for the game author.
