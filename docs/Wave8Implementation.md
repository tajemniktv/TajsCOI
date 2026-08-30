# Wave 8 implementation boundary

This repository now contains the behavior-neutral foundations for issues #109,
#112, #120, #131, #132, #137 and #138.

## Core planning and workflow

- `TajsCOI.Core.Production` normalizes configured blueprint recipes to fixed-point
  per-minute rates, separates pollution, nets internal logistics flows, filters
  virtual/obsolete products, reports missing/fallback recipes, and caches by
  stable blueprint identity/content hash.
- `ProductionCatalog` and `ProductionPlanner` build a prototype-time catalog and
  solve explicit recipe choices. Ambiguous routes, cycles, and node bounds are
  diagnostics; no live simulation state is changed.
- `TajsCOI.Core.Flow.ProductFlowIndex` is a scene-owned value index with one
  explicit bootstrap scan and incremental add/change/remove updates. Explorer
  sessions own temporary highlight/resource-visualization handles and release
  them on clear/dispose.
- `TajsCOI.Core.Blueprints` provides stable sidecar metadata, folder/category
  persistence, soft-delete/restore/purge, operational stats, versioned
  machine-readable export, separate human-readable summaries, atomic sidecar
  load, and missing-prototype previews.
- `BlueprintsLibraryNativeAdapter` is a thin fail-safe bridge for native
  0.8.7b import/export, rename, description, move, and delete operations; it
  never replaces the game's library authority.
- `TajsCOI.Core.Undo.UndoRecorder` groups nested construction work into one
  bounded record, validates the complete inverse before scheduling, and clears
  on scene changes. Records contain stable IDs, prototypes, transforms, and
  value-only configuration snapshots.

## Profiler history

`TajsCOI.Profiler.Core.DailyValueHistory` is the bounded daily ring buffer shared
by throughput and environmental probes. `ThroughputHistoryService` records only
explicitly monitored, actually transferred quantities and can produce bounded
capacity-aware heatmap entries. `EnvironmentalHistoryService` is disabled by
default, records effective attributable emissions and radioactive inventory,
and retains bounded unsupported-attribution diagnostics.

The native 0.8.7b Harmony seams, renderer widgets, and game-specific entity
adapters remain intentionally fail-open integration work. These contracts keep
the simulation read-only and avoid retaining scene objects; they can be wired to
validated native APIs without replacing the game's authorities.

## Management-surface adoption follow-up

- Fleet replacement tasks use the native `VehiclesReplacer` command/task model when a
  caller needs a durable zone, depot, assignee, or unassigned-only scope. The existing
  per-vehicle bulk commands remain available for simple operations; task progress and
  cancellation read native task state rather than a private success counter.
- The world browser keeps a discovered-only snapshot and now exposes the existing pure
  kind/sort query (including distance) directly in the management window. Entity aliases and
  notes are included in the snapshot, search, and row display without retaining world-map
  entity references between refreshes.
- Core's dashboard now exposes the native blueprint library through a scene-owned table. It
  shows folder/blueprint detail summaries, forwards metadata edits through `BlueprintsLibrary`,
  wraps native payloads in a versioned Tajs envelope for portable sharing, and stores explicit
  delete/restore/purge entries as value-only sidecar payloads. Preview parses without mutating
  the native library; import and purge require explicit actions.
