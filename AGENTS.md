# Taj's COI - Agent Guidelines

Repository-specific architecture, compatibility, validation, and contributor rules for TajsCOI.

Global `~/.codex/AGENTS.md` execution discipline also applies. This file overrides it where TajsCOI needs stricter or
more specific behavior.

## Documentation boundary

`README.md` is public/user-facing. Keep it short and practical: available mods, user-visible features, and in-game use.
Do not put installation instructions there until the supported distribution flow is ready.

Keep architecture, project layout, dependency rules, private API details, build/test commands, compatibility
assumptions, profiling internals, contributor workflow, and agent-facing project rules here or in relevant `docs/`.

## Project structure

```text
src/
├─ Libraries/
│  └─ TajsCOI.Common/
├─ Mods/
│  ├─ TajsCore/
│  ├─ TajsTweaks/
│  ├─ TajsProfiler/
│  ├─ TajsPerformance/
│  └─ TajsWhateverNextComesToMind/
└─ Tests/
```

Compile-time contracts:

```text
                 TajsCOI.Common
       ┌─────────┬─────┴─────┬───────────┐
   TajsCore  TajsTweaks  TajsProfiler  TajsPerformance
```

Tweaks, Profiler, and Performance manifest-depend on Core but must not compile-reference `TajsCore.dll`. Runtime
services inject Common's `ITajsRuntime` through the CoI dependency resolver.

Preserve these boundaries even when a shortcut would make one task locally easier.

## Project responsibilities

### TajsCOI.Common

Normal library, not an `IMod`.

Allowed: generic utilities/collections, genuinely shared metric/timing primitives, generic reflection helpers,
version/signature description types, shared result/error abstractions, and formatting reused by multiple mods.

Not allowed: gameplay features, mod lifecycle ownership, feature-specific MaFi bindings, suite-wide mutable service
state, or code used by only one mod.

**If only one mod uses it, keep it in that mod.** Common has no MaFi or Harmony dependency.

### TajsCore

Installable, gameplay-neutral suite runtime foundation.

Owns suite lifecycle coordination, shared logging/console commands, compatibility/game-version services, Harmony runtime
coordination, mod registration/discovery, cross-mod capability detection, and true cross-mod service/diagnostic
registries.

Installing only Core should cause essentially no gameplay-visible change.

Packaged runtime load order:

```text
0Harmony.dll
TajsCOI.Common.dll
TajsCore.dll
```

Dependent mods reference Common with copy-local disabled and receive Core services only through Common interfaces.

### TajsTweaks

User-facing QoL/gameplay tweaks. Organize each feature as a vertical slice under:

```text
Features/<FeatureName>/
```

Keep config, patches, bindings, state, and helpers local unless genuinely shared. Do not turn `Infrastructure/`,
`Interop/`, `Services/`, or `Managers/` into catch-all directories.

Private-MaFi compatibility seams must fail gracefully and leave vanilla behavior available when signatures no longer
match.

### TajsProfiler

Diagnostics only: observe the game, do not fix it.

```text
TajsProfiler/
├─ Core/       # generic profiling machinery
└─ Probes/     # subsystem-specific instrumentation
```

Core may own generic sessions, snapshots, histories, histograms, comparisons, spike recording, formatting, and
aggregation. Subsystem-specific hooks/counters/snapshots belong in probes such as Dumping, Simulation, Vehicles,
Logistics, PathFinding, Terrain, GC, and Frame.

Generic capture/reporting belongs in Core; subsystem knowledge belongs in the owning probe.

### TajsPerformance

Only evidence-backed performance fixes.

A feature needs a measured problem, identified expensive stage, clear behavioral contract, behavior-preserving
implementation, and before/after validation.

Profiling belongs in TajsProfiler. Experimental/default-off candidates require issue-specific in-game A/B evidence
before default enablement; proxy benchmarks alone are insufficient.

Stable product-owner/slot buffers must never be compacted by product-buffer shrinking.

### TajsDebugger

If created, reserve it for invasive developer tooling such as live state mutation, debug overlays, entity dumps, and
path visualization. Do not stretch Profiler into a general debugger.

## Namespaces and identities

Preferred namespaces:

```text
TajsCOI.Common
TajsCOI.Core
TajsCOI.Tweaks
TajsCOI.Profiler
TajsCOI.Performance
```

Installable IDs may remain `TajsCore`, `TajsTweaks`, `TajsProfiler`, and `TajsPerformance`.

Do not casually rename user-facing mod IDs; they affect manifests, dependencies, deployed directories, and save/mod
identity.

## Harmony

Core owns the packaged Harmony runtime.

Each independent patch set owns a stable scoped Harmony ID, e.g.:

```text
TajsCOI.Tweaks.UnlockedSpeed
TajsCOI.Profiler.Dumping
TajsCOI.Profiler.RuntimePerformance
TajsCOI.Performance.StreamingSaveCompression
```

Do not add a suite-wide patch manager merely because multiple scoped owners exist. Prefer explicit patch installation
over broad `PatchAll()` when practical.

Every patch should establish the vanilla target, why it is needed, observe-vs-modify behavior, failure behavior when the
signature is absent, and unpatch/dispose behavior if required.

Profiler patches must fail open and must not suppress vanilla exceptions, alter search results, reorder jobs, mutate
saves, throttle gameplay, introduce alternative algorithms, or perform expensive reflection/enumeration/stack traces in
hot paths unless explicitly bounded.

Feature patch installation must not leave partial patch state after compatibility failure. Scoped owners roll back their
own Harmony ID when required.

## Private MaFi API / interop

Private APIs are compatibility seams.

> Centralize the mechanism; localize the knowledge.

Generic reflection/signature helpers may live in Common/Core. Specific private knowledge belongs with the owning
feature/probe, e.g.:

```text
TajsProfiler/Probes/Dumping/Interop/
TajsTweaks/Features/Foo/FooBindings.cs
```

Prefer this over a global `MafiPrivateApi.cs`.

Validate member existence, parameter types, return type, static/instance shape, and supported game version. On failure,
disable only the affected feature/probe and log a useful compatibility message.

Avoid per-call reflection in hot paths; resolve delegates/accessors once when practical.

For private MaFi/Harmony behavior, inspect the exact supported game-version reference rather than guessing from older
source or another mod.

## Lifecycle

Keep root `IMod` implementations small. Use simple feature/probe hosts rather than giant unrelated lifecycle lists. Do
not invent frameworks that obscure the game's real lifecycle.

Process-wide state: loaded assemblies, CLR statics, Harmony patch tables.

Gameplay scene/resolver state: `[GlobalDependency]` instances, `DependencyResolver`, managers/renderers, console/UI
objects, and `SimLoopEvents`.

Process-lifetime patches must be idempotent. Process-static state must not strongly retain resolver-scoped objects or
callbacks.

Do not resolve gameplay-scene dependencies while the resolver is still constructing its dependency graph. Defer
resolver-scoped work to the appropriate gameplay lifecycle.

## Configuration

Features/probes own setting descriptors. Shared contracts live in Common; persistence/dashboard live in Core.

```text
feature/probe -> SettingDescriptor through ITajsSettings
TajsCore      -> persistence + runtime changes
```

Do not add `ModJsonConfig` or per-mod `config.json`. Use stable `ModId.key` identifiers. Declare `Immediate`,
`ReloadSave`, or `RestartGame` honestly. Invalid persisted values fall back to descriptor defaults.

Global settings:

```text
%APPDATA%\Captain of Industry\TajsCOI\settings.json
```

Runtime controls:

```text
tajs_dashboard
tajs_settings_list
tajs_settings_set <ModId.key> <value>
```

The dashboard is gameplay-scene-only until a deliberate main-menu bootstrap exists.

## Profiling

Prefer top-down measurement:

1. measure a broad/root scope;
2. identify the expensive subsystem;
3. instrument one level deeper;
4. repeat until isolated.

Do not instrument dozens of internals based on suspicion.

Capture cumulative and worst-event cost where meaningful. Deep probes must be cheap when disabled and bounded when
enabled. Avoid unbounded history, per-call formatting, hot-loop stack traces/reflection, or dictionaries keyed by
transient objects unless a bounded diagnostic explicitly requires them.

Distinguish wall-clock capture duration, simulation/update duration, cumulative nested work, and potentially concurrent
work. Do not present nested cumulative time as exclusive wall time.

## Performance fixes

Before changing game behavior: reproduce, measure, identify the actual expensive stage, understand vanilla semantics
from exact supported references, implement the smallest behavior-preserving fix, compare before/after, verify relevant
save/gameplay semantics, and document compatibility assumptions.

Do not promote a profiler observation directly into a workaround. If another mod may be the root cause, identify the
interaction first.

## Source/reference policy

The main repository must not contain Captain of Industry binaries, decompiled MaFi source, proprietary third-party
compiled/decompiled code, or private reference repositories/submodules containing those materials.

`TajemnikTV/TajsCOI-Refs` is the private research repository. Use it for signatures, call flow, compatibility, and
historical behavior while keeping TajsCOI implementation original and clean.

Do not paste large proprietary/decompiled source into comments, docs, tests, issues, or PR descriptions. Avoid
mentioning other mods in public/user-facing/PRs/issues/commits material.

Record important reference conclusions so they do not need to be repeatedly rediscovered.

### Versioned refs

When private behavior matters:

- use exact target refs under `TajsCOI-Refs/refs/<version>`;
- use that version's Managed references for build/decompilation when required;
- do not infer current signatures solely from older decompiled source;
- treat older compatibility notes as stale when the seam changed.

## Build and repository hygiene

Rider-first, but normal `dotnet`/MSBuild workflows must remain supported where applicable.

Before completion:

- build Debug;
- run relevant Debug tests;
- build Release;
- run relevant Release tests;
- inspect focused final diff/hygiene;

Do not commit local game paths, Rider user settings, build output, game DLLs, or recovered/decompiled artifacts.

Keep `src/Mods/Directory.Build.props` and `Directory.Build.targets` generic. Mod-specific hacks stay in the owning
project unless truly required by every CoI mod.

Normal helpers:

```powershell
.\build.ps1
.\build-and-run.ps1
.\build-and-run.ps1 -Configuration Release
.\tail-log.ps1
.\tail-log.ps1 -All
.\tail-log.ps1 -Pattern "Tajs|Path|Simulation"
```

`$env:COI_ROOT` is always set to latest version of the game.

Test project references must disable deployment, Unity copying, and release packaging. Per-mod project properties are
authoritative for identity, version, compatibility, manifests, and build metadata.

Do not launch/deploy the game unless requested or required by acceptance criteria. Report build/test proof separately
from in-game/runtime proof.

## Tests

Unit-test pure logic, especially metrics/histories/comparisons, config parsing/validation, version/signature selection,
and generic formatting.

Treat Harmony/game-runtime behavior as integration work. Do not create fake unit tests that merely assert forwarding
while missing the semantic interaction.

Use targeted tests during implementation; run required broader Debug/Release gates after the relevant change batch.

Passing tests do not prove UI layout, patch installation, runtime lifecycle behavior, Unity/driver counter availability,
or performance improvement unless they actually exercise those behaviors.

## GitHub / PR discipline

Keep PRs conceptually coherent. Refactors should not silently add gameplay changes; profiler PRs should not quietly add
optimizations; performance-fix PRs should explain evidence and validation.

If investigation disproves the original hypothesis, update scope/title/body while keeping the final change coherent.

Before addressing review comments: verify each against current head, distinguish stale findings from valid ones, fix
correctness rather than wording, and resolve only after the code actually addresses the issue.

End work at a coherent commit/handoff boundary with current diff, completed behavior, canonical owners, remaining
acceptance criteria, known failures, validation, and excluded scope.

After a large coherent feature implementation, commit it. Substantial follow-up fixes should normally be separate
commits when practical.

Do not let issue closure or adjacent work replace an explicitly requested PR/deliverable.

## Architectural anti-patterns

Do not introduce:

- gameplay features into Core;
- a Common dumping ground;
- lateral mod dependencies without strong reason;
- feature-specific MaFi knowledge in Common;
- giant global `Managers`, `Services`, `Patches`, or `Interop` buckets;
- speculative frameworks without concrete ownership/lifecycle need.

## Completion gate

A TajsCOI code-change task is complete when:

1. requested scope/behavior is satisfied;
2. exact-version compatibility assumptions are checked where relevant;
3. Debug build and relevant Debug tests pass;
4. Release build and relevant Release tests pass;
5. focused final diff/hygiene is clean and intentional;
6. no local game paths, binaries, decompiled artifacts, generated junk, or accidental refs were introduced;
7. architecture/ownership boundaries remain intact;
8. no concrete unresolved correctness issue remains.
