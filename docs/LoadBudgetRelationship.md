# Load-budget relationship: #10, #18, and #20

The load-time candidates are intentionally ordered and independently switchable:

1. `LazyResourceVisualization` (#18) is the first opt-in candidate. It defers only the hidden
   resource-bar build and invokes the exact vanilla initialization before any overlay activation.
   Direct renderer activation is covered as well, and a failed deferred attempt immediately
   replays the eager vanilla path.
2. `PathabilityInitialization` (#20) remains a separate opt-in candidate. It defers the ship
   blocking pass only during load, runs the same vanilla methods before the first query, and marks
   a provider terminally failed after an exception so it cannot spin on retries.
3. Scene cleanup (#10) remains measurement-only. The profiler wraps the existing compacting
   collection/finalizer calls and records their cost without changing collection policy.

Both performance candidates default to disabled. When a persisted process opt-in is already true,
the data-only performance mod installs its process patches before dependency resolution; the scene
host then reports the existing owner without registering duplicates. Missing, malformed, or
unsupported settings remain fail-closed. The current evidence is static compatibility and
unit/build proof; it is not a claim of a fresh-game A/B improvement. Runtime validation still needs
large-map cold-load timings, first-use latency, overlay/pathfinding correctness, save reload, and
memory measurements before either candidate can be enabled by default or promoted to a behavior
change.
