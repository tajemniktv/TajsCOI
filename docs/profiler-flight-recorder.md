# TajsProfiler runtime flight recorder

This document records the Phase A slice of [issue #38](https://github.com/tajemniktv/TajsCOI/issues/38)
and the runtime flight-recorder milestone from [issue #29](https://github.com/tajemniktv/TajsCOI/issues/29).

## Runtime design

`GameLoopTimingDiagnosticsService` subscribes once to the main-thread `InputUpdate` event. Each
callback reads the completed entries from Captain of Industry's existing `GameLoopTimings` rings
and captures the public simulation state plus broad `GameRunner` durations. The callback intentionally
reads the previous safe ring slot: `GameLoopTimings.End` advances the writer index before filling
the entry, so the newest slot may still be in flight.

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
waiting-for-simulation requires the overtime signal and a material wait interval; otherwise the
sample is classified by the relative broad main/render and simulation measurements.

## Commands

```text
tajs_profiler_status
tajs_profiler_runtime [seconds]
tajs_profiler_spikes [count]
tajs_profiler_runtime_raw [count]
tajs_profiler_runtime_clear
```

The raw view shows all 20 captured phase values. `sim-update` is the most recent phase sample,
while `sim` uses `GameRunner.LatestSimUpdateDuration` when that broad surface is available; this
distinction matters when accelerated simulation runs multiple steps in one worker cycle.

## Deliberate follow-up scope

This phase does not claim callback attribution, automatic before/after spike capture, timeline
JSON export, subsystem-counter correlation, overhead benchmarking, or an in-game overlay. Those
features need their own validated owners and are the later Phase B-F work described by issue #38.
