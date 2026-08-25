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

The resolver-scoped history retains 4,096 primitive frame samples. Console commands copy and sort
the bounded history only when requested, producing p50/p95/p99/max summaries for frame/update,
render, wait-for-simulation, and simulation work. Classifications are deliberately conservative:
waiting-for-simulation requires the overtime signal and a material wait interval; GC-related and
likely-GPU-bound are reported only when their corresponding telemetry is trusted; otherwise the
sample is classified by the relative broad main/render and simulation measurements.

Automatic spike capture uses absolute frame, wait, simulation, and major-phase thresholds plus an
optional relative multiplier. It stores a bounded pre/post window, applies cooldown and maximum
capture limits, and recognizes the existing simulation-overtime signal. `tajs_profiler_auto_deep`
can opt automatic spikes into the bounded callback recorder.

Deep tracing is inactive by default. When armed, the recorder patches the validated callback
invocation forms on the existing `Event` and `EventNonSaveable` objects. It records numeric spans
on per-thread bounded rings, interns owner/method/assembly strings through weak owner keys, keeps
callback order and exception behavior, and ranks total, average, p95, p99, maximum, and slow-call
metrics. Unsupported callback forms fail independently and are shown in compatibility status.

The exporter writes Chrome trace-event JSON with main/simulation phase spans, callback spans,
markers, memory/GC counters, and interval dumping/pathfinding counters. Unsupported values are emitted as
`"unavailable"`, never as synthetic zeroes. GPU classification remains unavailable unless a
trusted player-build telemetry surface is added.

## Commands

```text
tajs_profiler_status
tajs_profiler_runtime [seconds]
tajs_profiler_spikes [count]
tajs_profiler_runtime_raw [count]
tajs_profiler_runtime_clear
tajs_profiler_arm [seconds]
tajs_profiler_deep_start [seconds]
tajs_profiler_deep_stop
tajs_profiler_deep_report [count]
tajs_profiler_trace_export [name]
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
