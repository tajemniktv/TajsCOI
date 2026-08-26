# Taj's COI - Agent Guidelines

This file defines implementation rules for agents working in the TajsCOI repository.

## Documentation boundary

`README.md` is public, user-facing documentation. Keep it short and practical: describe the
available mods, their user-visible features, and how those features are used in-game. Do not put
installation instructions there until the supported distribution flow is ready.

Keep implementation architecture, project layout, dependency rules, private API details, build and
test commands, compatibility assumptions, profiling internals, and contributor workflow in this
file or in the relevant `docs/` document. Do not duplicate those details in the public README.

## Project intent

Captain of Industry is a highly complex supply chain and factory automation simulator. It operates as a directed acyclic graph (DAG) where nodes are factories with strict input/output throughput specifications, and edges are belts or pipes. It's made in Unity.
TajsCOI is a monorepo containing multiple Captain of Industry mods plus shared libraries. Preserve architectural boundaries even when a shortcut would make one task locally easier.

Target projects:

```text
src/
├─ Libraries/
│  └─ TajsCOI.Common/
│
├─ Mods/
│  ├─ TajsCore/
│  ├─ TajsTweaks/
│  ├─ TajsProfiler/
│  ├─ TajsPerformance/
│  └─ TajsWhateverNextComesToMind/
│
└─ Tests/
```

Compile-time contract dependency:

```text
                 TajsCOI.Common
       ┌─────────┬─────┴─────┬───────────┐
   TajsCore  TajsTweaks  TajsProfiler  TajsPerformance
```

Tweaks, Profiler, and Performance manifest-depend on Core but must not compile-reference `TajsCore.dll`. Runtime services inject Common's `ITajsRuntime` through the CoI dependency resolver.

## Project responsibilities

### TajsCOI.Common

`TajsCOI.Common` is a normal library, not an `IMod`.

Allowed:

- generic utilities;
- collections;
- metric/timing primitives used by multiple mods;
- generic reflection helpers;
- version/signature description types;
- shared result/error abstractions;
- formatting helpers genuinely reused across mods.

Not allowed:

- Captain of Industry gameplay features;
- mod lifecycle ownership;
- feature-specific MaFi private API bindings;
- suite-wide mutable service state;
- code used by only one mod.

Rule: **if only one mod uses it, keep it in that mod.**

The current shared contracts cover compatibility reports, runtime and logger interfaces, typed
settings descriptors/snapshots/change results, and assembly build-provenance metadata. Common has
no MaFi or Harmony dependency.

### TajsCore

`TajsCore` is an installable Captain of Industry mod and the suite runtime foundation.

Allowed responsibilities:

- suite/runtime lifecycle coordination;
- shared logging infrastructure;
- shared console-command infrastructure;
- compatibility/game-version services;
- Harmony runtime/patch coordination;
- TajsCOI mod registration/discovery;
- cross-mod capability detection;
- true cross-mod diagnostics/service registries.

Installing only TajsCore should produce essentially no gameplay-visible change.

`TajsCOI.Common` is a library below Core, not a second mod. Core owns and explicitly loads the runtime copies of Harmony, Common, and Core in dependency order. Dependent mods reference Common with copy-local disabled and receive Core services only through Common interfaces.

The packaged runtime load order is `0Harmony.dll`, `TajsCOI.Common.dll`, then `TajsCore.dll`.

### TajsTweaks

Contains user-facing QoL and gameplay tweaks.

Organize each feature as a vertical slice under `Features/<FeatureName>/`. Keep its config, patches, bindings, runtime state, and helpers together unless a helper is genuinely shared.

Do not turn `Infrastructure/`, `Interop/`, `Services/`, or `Managers/` into catch-all directories.

The current user-facing feature is `UnlockedSpeed`. It bypasses the vanilla 20x requested-speed
validation through a scoped private-MaFi compatibility seam. Its global maximum is configurable
between 20x and 500x; the feature must report a compatibility failure and leave vanilla behavior
available when that seam no longer matches the supported game version.

### TajsProfiler

Diagnostics only. Profiling should observe the game, not fix it.

Split into:

```text
TajsProfiler/
├─ Core/       # generic profiling machinery
└─ Probes/     # subsystem-specific instrumentation
```

Profiler Core may contain sessions, snapshots, histories, histograms, comparison, spike recording, report formatting, and generic aggregation.

Probe code owns subsystem-specific details, for example:

```text
Probes/Dumping/
Probes/Simulation/
Probes/Vehicles/
Probes/Logistics/
Probes/PathFinding/
Probes/Terrain/
Probes/GC/
Probes/Frame/
```

The runtime baseline probe exposes bounded named captures for save/load timing, managed and Unity
memory, process CPU, GC collections, product-renderer telemetry, and lifecycle checkpoints. The
current diagnostic commands are:

```text
tajs_runtime_profile_capture <label>
tajs_runtime_profiles
tajs_runtime_profile_show <label>
tajs_runtime_profile_compare <before> <after>
tajs_runtime_profile_clear
tajs_runtime_profile_reset
tajs_runtime_lifecycle_checkpoint <label>
tajs_runtime_lifecycle_checkpoints
tajs_runtime_lifecycle_watches
tajs_runtime_harmony_audit
tajs_runtime_initialization_hotspots
```

Capture before and after a representative operation. Comparisons subtract cumulative counters,
while worst-event values remain explicitly non-subtractive. The dumping/pathfinding split follows
the same boundary: generic capture and reporting belong in `TajsProfiler/Core`, while dumping-specific
hooks, paths, counters, and snapshots belong in `TajsProfiler/Probes/Dumping`.

### TajsPerformance

Contains only evidence-backed performance fixes.

Do not create or populate it merely because code is performance-related. Profiling/diagnostics belong in TajsProfiler. A Performance feature should have a measured problem, a clear behavioral contract, and validation that the fix does not silently break intended simulation semantics.

The current default-off performance candidates are:

- larger buffered save/load reads;
- streaming save compression;
- lower product-texture mip modes;
- lazy resource-visualization building;
- paused-only manual asset trimming; and
- conservative product-buffer shrinking after sustained under-utilization.

Product-buffer shrinking may touch only remappable live/reserve buffers after its utilization gate;
stable product-owner and slot buffers are never compacted.

Proxy benchmarks live under `docs/benchmarks`. A candidate remains experimental until its own
issue-specific in-game A/B acceptance is complete; profiler evidence and a proxy benchmark alone
are not enough to enable it by default.

### TajsDebugger

If created in the future, reserve it for invasive developer tooling such as live state inspection/mutation, debug overlays, entity dumps, or path visualization. Do not stretch TajsProfiler into a general runtime debugger.

## Namespaces and identities

Preferred namespaces:

```text
TajsCOI.Common
TajsCOI.Core
TajsCOI.Tweaks
TajsCOI.Profiler
TajsCOI.Performance
```

Installable mod IDs may remain:

```text
TajsCore
TajsTweaks
TajsProfiler
TajsPerformance
```

Do not rename user-facing mod IDs casually because they are part of manifests, dependency metadata, deployed directories, and save/mod identity.

## Harmony rules

Core owns the packaged Harmony runtime binary. Each independently installed patch set owns a
stable, scoped Harmony ID so it can validate, roll back, and evolve without disturbing unrelated
features or probes, for example:

```text
TajsCOI.Tweaks.UnlockedSpeed
TajsCOI.Profiler.Dumping
TajsCOI.Profiler.RuntimePerformance
TajsCOI.Performance.StreamingSaveCompression
```

Do not introduce a suite-wide patch manager merely because multiple scoped owners exist. Add
orchestration only for a concrete shared lifecycle or coordination requirement.

Prefer explicit patch installation to broad assembly-wide `PatchAll()` when practical.

Every patch should answer:

1. what vanilla method is being patched;
2. why the patch is needed;
3. whether it observes or changes behavior;
4. what happens if the expected method/signature is absent;
5. how it is unpatched/disposed if required.

Profiler patches must be fail-open. They must not:

- suppress vanilla exceptions;
- change search results;
- reorder jobs;
- mutate saves;
- throttle gameplay;
- introduce alternative algorithms;
- perform expensive stack traces/reflection/enumeration in hot paths unless explicitly required for a bounded diagnostic mode.

## Private MaFi API / interop rules

Private APIs are compatibility seams, not random implementation details to scatter through the codebase.

Principle:

> Centralize the mechanism; localize the knowledge.

Generic reflection/signature helpers may live in Common/Core.

Specific knowledge such as a private `TerrainDumpingManager` helper belongs with the owning feature/probe.

Prefer structures such as:

```text
TajsProfiler/Probes/Dumping/Interop/
TajsTweaks/Features/Foo/FooBindings.cs
```

over a global `MafiPrivateApi.cs`.

Validate private bindings against expected:

- method/field existence;
- parameter types;
- return type;
- static/instance shape where relevant;
- supported game version.

If validation fails, disable the affected feature/probe gracefully and log a useful compatibility message. Do not crash unrelated mods/features.

Avoid per-call reflection in hot paths. Resolve handles/delegates/accessors once when practical.

## Feature/probe lifecycle

Keep root `IMod` implementations small and boring.

Use feature/probe hosts or similarly simple coordination so root mod classes do not become lists of unrelated `Init`, `Patch`, `Start`, and `Dispose` calls.

Do not invent an elaborate framework that obscures Captain of Industry's real lifecycle. Internal lifecycle abstractions should map cleanly onto the game/mod loader lifecycle.

Captain of Industry's process and gameplay-resolver lifetimes are different. Loaded assemblies,
CLR statics, and Harmony patch tables survive process-wide, while `[GlobalDependency]` service
instances, `DependencyResolver`, managers, renderers, console/UI objects, and `SimLoopEvents`
belong to the current gameplay scene/resolver. Process-lifetime patches must be idempotent and
process-scoped settings must be snapshotted; process-static state must not strongly retain a
resolver-scoped object or callback target.

## Configuration

Setting descriptors are owned by the feature or probe that owns the behavior. Shared contracts live
in `TajsCOI.Common`; persistence and the runtime dashboard live in `TajsCore`.

```text
feature/probe -> registers SettingDescriptor through ITajsSettings
TajsCore      -> persists global values and publishes runtime changes
```

Do not add `ModJsonConfig` or per-mod `config.json` files. Use stable ordinal `ModId.key` identifiers,
declare `Immediate`, `ReloadSave`, or `RestartGame` honestly, and keep gameplay-specific descriptor
knowledge in the owning mod. Invalid persisted values must fall back to descriptor defaults.

Global values are stored outside savegames in `%APPDATA%\Captain of Industry\TajsCOI\settings.json`.
Use `tajs_dashboard` during a running game to inspect loaded suite components and edit registered
settings. `tajs_settings_list` and `tajs_settings_set <ModId.key> <value>` remain available as
console fallbacks. Apply modes state whether a value is immediate, requires a save reload, or
requires a game restart.

The runtime dashboard is gameplay-scene-only until a separate main-menu bootstrap is deliberately
implemented. Do not make gameplay-scene services resolve in the main-menu lifetime by accident.

## Profiling design rules

Prefer top-down measurement before deep instrumentation.

When investigating performance:

1. measure a broad/root scope;
2. identify the expensive subsystem;
3. instrument one level deeper;
4. repeat until the expensive stage is isolated.

Do not instrument dozens of internal methods based only on suspicion.

Capture both cumulative cost and worst-event cost where meaningful. A function may be cheap on average and still cause visible spikes, or expensive cumulatively while never causing a hitch.

Deep probes should be cheap when disabled and bounded when enabled. Avoid unbounded history, per-call string formatting, stack traces, or dictionaries keyed by transient objects in hot loops unless a diagnostic explicitly requires them.

When reporting durations, distinguish wall-clock capture duration, simulation/update duration, cumulative nested work, and potentially concurrent work. Do not present cumulative nested time as exclusive wall time.

## Performance-fix rules

Do not promote a profiler observation directly into a workaround without evidence.

Before changing game behavior:

- reproduce the problem;
- measure it;
- identify the actual expensive stage;
- understand vanilla semantics from supported references;
- implement the smallest behavior-preserving fix possible;
- compare before/after measurements;
- verify saves/gameplay behavior;
- document compatibility assumptions.

If the root cause may be another mod, identify the interaction before adding a blanket vanilla workaround.

## Source/reference policy

The main TajsCOI repository must not contain:

- Captain of Industry game binaries;
- decompiled MaFi source;
- any proprietary third-party code or binaries, be it compiled or decompiled;
- private reference repositories/submodules containing those materials.

`TajemnikTV/TajsCOI-Refs` is a separate private research repository. It may be used to understand signatures, call flow, compatibility, and historical behavior, but implementation in TajsCOI must be original and clean. Do not paste large proprietary/decompiled source into comments, docs, tests, or PR descriptions. Avoid mentioning other mods, especially in user or public facing places. 

## Build and repository hygiene

The repository is Rider-first but builds must remain compatible with normal `dotnet`/MSBuild workflows where the current project supports them.

Before declaring a code change complete:

- build Debug;
- build Release;
- use the exact configured Captain of Industry references;
- run available tests;
- inspect the final diff for generated files, binaries, local paths, or accidental refs;
- confirm the working tree contains no unintended build output.

Do not commit local game installation paths, Rider user settings, build output, game DLLs, or recovered artifacts.

Keep `src/Mods/Directory.Build.props` and `Directory.Build.targets` generic. Mod-specific hacks belong in the owning project unless they are truly required by every CoI mod in the repository.

The normal local development commands are:

```powershell
.\build.ps1
.\build-and-run.ps1
.\build-and-run.ps1 -Configuration Release
.\tail-log.ps1
.\tail-log.ps1 -All
.\tail-log.ps1 -Pattern "Tajs|Path|Simulation"
```

Successful mod builds can deploy to `%APPDATA%\Captain of Industry\Mods\<ModId>` and Release builds
can produce a versioned ZIP. Test project references must explicitly disable deployment, Unity
copying, and release packaging so ordinary test builds do not touch those destinations.

The per-mod project properties are the source of truth for mod identity, version, game compatibility,
generated manifests, and build metadata.

## Tests

Unit-test pure logic, especially:

- metric aggregation;
- histograms;
- snapshot/history behavior;
- comparisons;
- config parsing and validation;
- version/signature selection;
- generic formatting.

Treat Harmony/game-runtime behavior as integration work. Do not create fake unit tests that merely assert arguments were forwarded while missing the real semantic interaction being fixed.

## GitHub Repo

### Pull request discipline

Keep PRs conceptually coherent.

A refactor PR should not silently add gameplay changes. A profiler PR should not quietly add an optimization. A performance-fix PR should include evidence explaining what was slow and how the change was validated.

When a PR's investigation disproves its original hypothesis, updating the PR scope/title/body is acceptable, but keep the final change internally coherent.

Before addressing review comments:

- verify each comment against the current head;
- distinguish stale/outdated findings from valid current issues;
- fix correctness issues rather than blindly satisfying wording;
- reply/resolve threads only after the code actually addresses them.

End long work at a coherent commit or handoff boundary with the current diff, completed behavior, canonical owners, remaining acceptance criteria, known failures, validation, and excluded scope. A PR description SHOULD state what changed, root cause, impact, ownership decisions, validation run, local-only authored changes, and missing proof. After big changes, such as finishing implementing a feature, please commit it to the repository. If there are subsequent changes to said feature, commit them too. Try not to commit everything as one big commit, unless it's all related to one system/feature.

## Architectural anti-patterns

Do not introduce:

- a god-like `TajsCore` containing features;
- a `Common` dumping ground for one-off helpers;
- lateral mod dependencies without a strong reason;
- feature-specific private MaFi knowledge in Common;
- giant global `Managers`, `Services`, `Patches`, or `Interop` buckets.

When uncertain, prefer the smallest local implementation with a clean boundary. Extract only after real reuse or a real cross-mod runtime need appears.

## Efficient reasoning and tool use

Optimize for evidence gained, not outer tool-call count.

1. Freeze scope, canonical sources, required work, allowed fixes, and exclusions.
2. Build a minimal map of entry points and direct dependencies.
3. Read targeted files and ranges.
4. Batch only independent operations already known to be needed.
5. Synthesize before another exploration batch.
6. Apply one coherent edit batch.
7. Compile once per compile-relevant batch.
8. Run the narrowest production-path evaluation.
9. Run broader regression once at the final gate when justified.
10. Retry only failed, missing, or truncated operations.
11. Stop when the requested invariant and evidence level are satisfied.

Also:

- reuse gathered results;
- do not reread unchanged files without reason;
- prefer targeted `rg`, exact paths, selected ranges, path-specific diffs, and summarized logs;
- do not use invalid Windows wildcard paths;
- do not repeatedly read skills, memory, status, logs, builds, or reports when nothing changed;
- do not equate a large `Promise.allSettled` batch with efficiency;
- preserve unrelated dirty-worktree changes.
