# Taj's COI - Captain of Industry Mods

TajsCOI is a monorepo for a suite of mods by TajemnikTV, made for Captain of Industry. TajsCOI is split into smaller mods with clear responsibilities, plus a shared library for code that is genuinely reusable across the suite. 

## Architecture

Target layout:

```text
TajsCOI/
├─ src/
│  ├─ Libraries/
│  │  └─ TajsCOI.Common/
│  │
│  ├─ Mods/
│  │  ├─ TajsCore/
│  │  ├─ TajsTweaks/
│  │  ├─ TajsProfiler/
│  │  └─ TajsPerformance/      # future, when there are proven fixes
│  │
│  └─ Tests/
│     ├─ TajsCOI.Common.Tests/
│     ├─ TajsTweaks.Tests/
│     └─ TajsProfiler.Tests/
│
├─ build.ps1
├─ build-and-run.ps1
├─ run-game.ps1
├─ tail-log.ps1
└─ TajsCOI.slnx
```

The intended dependency direction is:

```text
                 TajsCOI.Common
                       ↑
                    TajsCore
                       ↑
          ┌────────────┼────────────┐
          │            │            │
     TajsTweaks   TajsMod1   TajsWhateverMod2
```

Mods should not depend on each other by default. They share infrastructure through `TajsCore` and `TajsCOI.Common` instead.

## Projects

### TajsCOI.Common

`TajsCOI.Common` is a normal .NET library and **not** a Captain of Industry mod.

It contains boring, reusable building blocks that make sense in more than one TajsCOI mod, for example:

- small collection and utility types;
- generic timing and metric primitives;
- bounded histories and histograms;
- formatting helpers;
- version/signature description types;
- generic reflection helpers;
- small Harmony helper abstractions;
- shared result/error types.

Common must not own gameplay features, mod lifecycle, global mutable game state, or Captain of Industry feature-specific knowledge.

A useful rule is: **if only one mod uses it, it stays in that mod.** Do not promote code into Common merely because it could theoretically be reusable someday.

### TajsCore

`TajsCore` is a separate installable Captain of Industry mod and the runtime foundation of the TajsCOI suite.

It exists once per game process and provides suite-level infrastructure such as:

- mod/runtime lifecycle coordination;
- shared compatibility and game-version checks;
- Harmony runtime/patch coordination;
- logging infrastructure;
- console-command infrastructure;
- TajsCOI mod discovery/registration;
- capability detection for private MaFi APIs;
- shared diagnostics/service registries where a true cross-mod service is needed.

**Core is infrastructure only.** It must not become a home for camera tweaks, truck fixes, balancing changes, dumping optimizers, UI features, or other gameplay behavior.

Installing Core by itself should make essentially no gameplay-visible difference.

`TajsCOI.Common` is the library layer below Core. The intended packaging model is that Core owns the runtime copy of the Common assembly while other TajsCOI mods depend on Core. The exact Captain of Industry loader/dependency behavior for shared assemblies must be validated before this packaging contract is treated as final.

### TajsTweaks

`TajsTweaks` contains user-facing quality-of-life and gameplay tweaks.

Examples include:

- camera/UI improvements;
- configurable numeric changes;
- isolated logistics or simulation tweaks;
- small Harmony patches that can be independently enabled or disabled.

Features should be organized as **vertical slices**:

```text
TajsTweaks/
└─ Features/
   └─ ExampleFeature/
      ├─ ExampleFeature.cs
      ├─ ExampleFeatureConfig.cs
      ├─ ExampleFeaturePatches.cs
      └─ ExampleFeatureBindings.cs
```

Keep everything needed to understand one feature together. Avoid global `Managers/`, `Services/`, or `Patches/` dumping grounds containing unrelated features.

### TajsProfiler

`TajsProfiler` is for **observation and diagnostics**. It should not change gameplay behavior in order to make the game faster.

Its internal split is deliberately simple:

```text
TajsProfiler/
├─ Core/       # how profiling works
└─ Probes/     # what is being profiled
```

Profiler Core may contain:

- timed capture sessions;
- warmups;
- named snapshots;
- bounded history;
- profile comparison;
- timing accumulators;
- latency histograms;
- spike/event recording;
- report formatting;
- generic metric aggregation.

Profiler probes contain Captain of Industry subsystem knowledge, for example:

```text
Probes/
├─ Dumping/
├─ Simulation/
├─ Vehicles/
├─ Logistics/
├─ PathFinding/
├─ Terrain/
├─ GC/
└─ Frame/
```

The dumping/pathfinding diagnostics originally developed inside `TajsTweaks` are the prototype for this architecture: generic capture/reporting plumbing belongs in `TajsProfiler/Core`, while dumping-specific Harmony hooks, paths, counters, and snapshots belong in `TajsProfiler/Probes/Dumping`.

Profiler instrumentation should be behavior-neutral and cheap when disabled. Deep probes should be opt-in where practical.

### TajsPerformance

`TajsPerformance` is reserved for **proven performance fixes**.

Its contract is different from the profiler:

```text
TajsProfiler    observes, measures, explains
TajsPerformance changes implementation/behavior to make the game faster
```

A suspected optimization does not belong in `TajsPerformance` until profiling demonstrates a real problem and the fix can be validated against intended behavior.

Keeping observation and optimization separate makes failures easier to reason about and lets users install diagnostics without also installing experimental behavior changes.

### Possible future: TajsDebugger

A future `TajsDebugger` may provide invasive developer tooling such as:

- live entity/state inspection;
- debug overlays;
- path visualizations;
- internal-state dumps;
- controlled runtime mutation;
- developer commands.

That is intentionally separate from `TajsProfiler`, whose primary job is measurement rather than state manipulation.

## Names and namespaces

Installable mod IDs may stay short and user-facing:

```text
TajsCore
TajsTweaks
TajsProfiler
TajsPerformance
```

The preferred code namespaces are suite-oriented:

```text
TajsCOI.Common
TajsCOI.Core
TajsCOI.Tweaks
TajsCOI.Profiler
TajsCOI.Performance
```

This keeps the source tree coherent while preserving simple mod IDs and assembly names.

## Harmony policy

Use one Harmony owner per installable mod, for example:

```text
TajsCOI.Core
TajsCOI.Tweaks
TajsCOI.Profiler
TajsCOI.Performance
```

Use categories for individual features/probes where useful:

```text
TajsCOI.Tweaks.FreeCamera
TajsCOI.Profiler.Dumping
TajsCOI.Profiler.VehiclePathFinding
TajsCOI.Performance.DumpingSearch
```

Prefer explicit patch installation over indiscriminate assembly-wide `PatchAll()` when practical. A mod should be able to explain exactly which patches are active and why. Profiler patches in particular must be fail-open and must not suppress vanilla exceptions, reorder jobs, mutate saves, or silently alter gameplay semantics.

## Private MaFi APIs and interop

Private/reflection-based access is expected, but it needs a boundary. The rule is:

> **Centralize the mechanism; localize the knowledge.**

Generic reflection, Harmony, and signature-validation helpers may live in Common/Core. Knowledge that a specific private method such as a dumping cache helper exists belongs next to the feature or probe that uses it.

For example:

```text
TajsCOI.Common/Reflection/              # generic mechanism
TajsProfiler/Probes/Dumping/Interop/    # dumping-specific private API knowledge
TajsTweaks/Features/Foo/FooBindings.cs  # Foo-specific private API knowledge
```

Private bindings should validate expected methods/fields/signatures and fail gracefully when the running game version is unsupported.

Avoid a single enormous `MafiPrivateApi.cs` containing unrelated internals from the entire game.

## Feature and probe lifecycle

Root mod classes should remain boring.

They should delegate to feature/probe hosts rather than accumulate dozens of direct calls such as `Foo.Init()`, `Bar.Patch()`, and `Baz.Start()`.

Conceptually:

```text
TajsTweaksMod
    ↓
FeatureHost
    ↓
Feature A
Feature B
Feature C
```

and:

```text
TajsProfilerMod
    ↓
ProbeHost
    ↓
DumpingProbe
SimulationProbe
VehicleProbe
```

Lifecycle abstractions should map cleanly onto Captain of Industry's actual mod lifecycle instead of creating a second fictional lifecycle on top of it.

## Configuration

Configuration belongs to the mod that owns the behavior. Use separate config files such as:

```text
TajsTweaks/config.json
TajsProfiler/config.json
TajsPerformance/config.json
```

Do not create one giant suite-wide gameplay config. Prefer typed sections per feature/probe.

## Testing

Unit-test pure logic where tests provide real value, including:

- histogram bucketing;
- timing/metric aggregation;
- profile snapshots and comparisons;
- bounded history behavior;
- config parsing/validation;
- version/signature selection logic;
- formatting helpers.

Harmony patches against Captain of Industry assemblies are primarily integration territory. Build and compatibility checks should validate those hooks without pretending every private-game interaction is a pure unit test.

## Reference repository policy

Game binaries, decompiled MaFi sources, recovered third-party mod binaries, and other proprietary/reference material do **not** belong in this repository. Compatibility research lives separately in the private `TajsCOI-Refs` repository. TajsCOI may use those references for local analysis, but should not copy decompiled proprietary source into the public/main project.

## Current development loop

Build in Rider or run:

```powershell
.\build.ps1
```

Build and launch Captain of Industry:

```powershell
.\build-and-run.ps1
```

Release build and launch:

```powershell
.\build-and-run.ps1 -Configuration Release
```

Follow the newest Captain of Industry log:

```powershell
.\tail-log.ps1
```

Show every line:

```powershell
.\tail-log.ps1 -All
```

Custom filter:

```powershell
.\tail-log.ps1 -Pattern "Tajs|Path|Simulation"
```

Shared CoI build/deploy plumbing currently lives in `src/Mods/Directory.Build.props` and `src/Mods/Directory.Build.targets`.

Each successful mod build can deploy directly to:

```text
%APPDATA%\Captain of Industry\Mods\<ModId>
```

Release builds can additionally produce a versioned ZIP distribution artifact.

The per-mod project properties remain the source of truth for mod identity/version/game compatibility and are used to generate manifests and build metadata.
