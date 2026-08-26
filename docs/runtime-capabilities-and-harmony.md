# Runtime capabilities and Harmony diagnostics

`TajsCore` owns the read-only runtime registry exposed through `ITajsRuntime`. The registry has
two value-only surfaces:

- capabilities identify optional semantic integrations and report `Available`, `Degraded`, or
  `Unavailable` state;
- components identify the owning mod/component, lifetime, expected seam, Harmony owner IDs, and
  required/optional capability IDs.

Registrations are idempotent. A descriptor with the same stable identity may refresh its mutable
status/details, while a different owner or incompatible component metadata is rejected and
published through the existing `TajsCore/RuntimeRegistry` compatibility report. Snapshots are
sorted and contain no resolver, `IMod`, or other gameplay object references. Gameplay-scoped
registrations are recreated with the gameplay runtime; no CLR static registry is used.

Core also owns a stateless Harmony inspector. It is called by the profiler command, the dashboard
Compatibility page, Core status, and the profiler trace export path—not from a frame callback. It
enumerates methods patched by at least one `TajsCOI.*` owner and records the target signature,
patch kind, owner, priority, before/after constraints, and whether each patch is Tajs-owned.
Targets shared with other owners are retained so collision context is visible.

Risk is deliberately advisory:

- `High` marks duplicate Tajs registrations or Tajs/non-Tajs transpilers on one target;
- `Medium` marks multiple prefixes involving a bool-returning prefix, missing ordering owners, or
  ordering cycles;
- `Informational` marks a shared target without one of those heuristic hazards;
- `None` means no inspected heuristic was triggered.

The inspector does not remove patches, rewrite priorities, or install a suite-wide patch manager.
The existing `tajs_runtime_harmony_audit` command remains the compatibility entry point and now
formats this shared snapshot with the richer metadata.
