# Dumping diagnostics consolidation plan

## Boundary at the start of this migration

`DumpSearchDiagnosticsService` already lives in `TajsProfiler/Probes/Dumping` and owns the
dumping/pathfinding Harmony hooks. It currently collects search-path, caller, candidate, cache,
latency, pathfinding-enqueue, worst-call, and timed-profile data. Its hot-path state is a set of
atomic counters plus thread-local nested-call contexts; its timed profile history is separately
locked and reported through `tajs_dump_profile*` commands.

`GameLoopTimingDiagnosticsService` owns the flight recorder, frame history, spike windows, deep
tracing, markers, and Chrome trace export. It samples only a fixed six-field
`RuntimeSubsystemCounterSnapshot` from the dumping service. The exporter exposes four of those
fields, so dumping is visible in the runtime timeline but remains a parallel reporting system.

The dump probe must remain the owner of MaFi-specific bindings and detailed search breakdowns.
Core should own only the generic publication, bounded frame snapshots, correlation markers, and
report composition. No constructor should resolve an additional gameplay dependency.

## Migration steps

1. Add a bounded `RuntimeTelemetry` core store. Probes register named counter handles during
   initialization, then publish with atomic add/increment operations. Frame capture copies deltas
   into preallocated value-only slots; names and formatting are resolved only in reports/exports.
2. Replace the dumping probe's timeline bridge with registered handles for dumping calls/results,
   elapsed ticks, pathfinding enqueues/search time, and future terrain events. Preserve its detailed
   `tajs_dump_search_stats` output initially as a compatibility view over probe-owned state.
3. Add generic telemetry to runtime summaries, spike records, and trace counter events. Add a
   bounded dumping subsystem view and correlation markers for export/profile enable/disable and
   large dumping/pathfinding bursts.
4. Retire only the duplicate timed dump-profile history/reporting after the shared runtime capture
   can represent the same interval and comparison data. Keep old commands as explicit compatibility
   aliases or return a migration message; do not silently change their semantics.

## Constraints and proof

Registration may allocate and use a setup lock. Capture-time publication must be allocation-free,
lock-free, and string-free. Dump and pathfinding hooks remain fail-open and behavior-neutral.
Tests will cover counter deltas, multi-threaded publication, bounded frame snapshots, event/marker
correlation, and reset/wrap behavior. Validation will include Debug and Release builds plus the
existing test suite against the configured COI 0.8.7b references.
