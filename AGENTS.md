# Taj's COI - Agent Guidelines

This file defines repository architecture, implementation constraints, validation rules, and agent-execution discipline
for TajsCOI.

## Documentation boundary

`README.md` is public, user-facing documentation. Keep it short and practical: describe available mods, user-visible
features, and how they are used in-game. Do not put installation instructions there until the supported distribution
flow is ready.

Keep implementation architecture, project layout, dependency rules, private API details, build/test commands,
compatibility assumptions, profiling internals, contributor workflow, and agent-operating rules in this file or relevant
`docs/` documents.

---

## Agent operating contract

For every repository task:

1. Establish the requested outcome, scope, acceptance criteria, exclusions, and canonical sources before broad
   investigation.
2. Investigate only questions whose answers can change the implementation, validation result, or verdict.
3. Reuse evidence already gathered during the task.
4. Prefer `search -> bounded source range -> edit` over whole-file reads.
5. Once enough evidence exists to act, act. Do not continue searching merely for confidence.
6. Keep investigation, implementation, validation, and audit as distinct phases.
7. Validate with the narrowest decisive check first.
8. Broaden validation only when repository policy, acceptance criteria, or remaining uncertainty requires it.
9. Do not reopen resolved questions without contradictory evidence.
10. Stop when the requested outcome and required evidence level are satisfied.

Exploration is not progress by itself.

Every read, search, probe, build, test, or inspection must do at least one of:

- resolve an open question;
- locate code needed for an imminent edit;
- validate a concrete hypothesis;
- verify changed behavior;
- provide required completion evidence.

If it does none of those, do not run it.

Optimize for evidence gained and work completed, not raw tool-call count.

---

## Long-task state and compaction recovery

For long or investigation-heavy tasks, maintain temporary `.codex/TASK_STATE.md`.

Use it when the task spans multiple phases, substantial reference research has already been completed, context
compaction is likely, or multiple acceptance criteria must be tracked.

Update it only after a material conclusion, implementation milestone, scope change, or decisive validation result. Do
not update it after ordinary reads or individual edits.

Recommended structure:

```md
# Task state

## Goal
<exact requested outcome>

## Acceptance
- ...

## Completed
- ...

## Confirmed facts
- ...

## Decisions
- ...

## Rejected / disproven
- ...

## Already researched
- `path/file.cs`: <relevant conclusion>
- Reference X: <relevant conclusion>

## Changed files
- ...

## Validation
- Debug: ...
- Tests: ...
- Release: ...

## Open questions
- ...

## Next
1. ...

## Stop condition
Task is complete when ...

## Do not repeat
- repository discovery
- resolved API searches
- completed reference audits
```

After automatic or manual context compaction:

1. Do not restart repository discovery.
2. Read `.codex/TASK_STATE.md` first when it exists.
3. Inspect `git status --short`.
4. Inspect the focused current diff if needed.
5. Reconcile the checkpoint with the actual working tree.
6. Resume from `Next`.
7. Do not reread `Already researched` material without a new concrete question.
8. Do not regenerate an already-valid plan.

The repository and working tree are authoritative execution state. A compacted conversation summary is supporting
context.

Delete or reset `TASK_STATE.md` when the task is complete.

---

## Source-reading discipline

Use the smallest read that can answer the current question.

Default escalation:

1. `rg -n "<symbol|term>" <specific-path>`
2. Read roughly 40-120 lines around relevant matches.
3. Expand nearby ranges only when required.
4. Read the complete file only when overall structure/lifecycle materially matters.

For files >500 lines, a single investigation step should normally inspect
no more than 150-200 total source lines from that file, also:

- search first;
- prefer bounded ranges;
- avoid reading more than ~200 contiguous lines without a concrete reason;
- do not repeatedly reopen the same file when one search can locate all relevant symbols.

Do not dump large files merely to become familiar with them.

### Good vs bad

Bad:

```text
read 1800-line manager
search all refs for generic terms
read several related classes completely
search again with broader terms
```

Good:

```text
rg exact/native symbol
read bounded range
inspect one direct caller if ownership remains unclear
record conclusion
continue
```

---

### Tool-output budget

Default shell commands should request no more than 4,000-6,000 output tokens.

A command expected to exceed ~200 source lines or ~8,000 output tokens must be split or narrowed unless the complete output is specifically required.

Do not use `max_output_tokens` above 12,000 for ordinary source investigation.

Do not batch multiple large source reads into one tool call.

If one tool result reaches its output limit, treat the query as too broad:

- do not repeat it with a larger limit;
- narrow by symbol, path, or line range.

For source investigation, prefer several decision-dependent small reads over one speculative 30,000-token batch.

---

## Evidence reuse and search discipline

Information already obtained remains valid unless:

- the relevant file changed;
- the prior result failed or was truncated;
- a new question needs unseen source lines;
- later evidence contradicts it.

Do not reread unchanged source solely to refresh context or confidence.

Before rereading, identify:

1. the new question;
2. why prior evidence is insufficient;
3. the smallest additional range needed.

Before broad repository/reference/decompiled-source search, identify the unanswered question it should resolve.

Good:

> Need the native owner of machine speed mutation, so search `SetBaseSpeedFactor`.

Bad:

> Search broadly for anything related to overclocking.

Do not run successive broad searches that substantially answer the same question.

After two exploration attempts without material new evidence:

- synthesize what is known;
- narrow the hypothesis;
- edit/test if evidence is sufficient; or
- change strategy.

Do not perform a third equivalent search.

---

## Evidence-delta rule

Repeated operations must produce meaningful new evidence.

Do not repeat:

- `git status` when no file operation occurred;
- the same `rg` query against unchanged sources;
- a successful build before compile-relevant changes;
- a successful test suite before behavior-relevant changes;
- unchanged logs/reports;
- the same reference-source inspection.

If a repeated operation produces no new evidence twice, stop repeating it and change strategy.

---

## Shell-output and batching discipline

Commands should return only information likely to affect the next decision.

Prefer:

- `rg -n`;
- exact paths;
- bounded `Select-Object -First/-Skip`;
- path-specific diffs;
- `git diff --stat`;
- concise test verbosity;
- filtered logs.

Avoid:

- whole large source-file dumps;
- unfiltered recursive listings;
- complete repository diffs when only a few paths matter;
- complete successful build logs;
- broad decompiled-source dumps.

If output is unexpectedly large, narrow the query before doing more exploration.

Batch only independent operations already known to be needed.

Prefer:

```text
search -> synthesize -> next decision
```

over:

```text
search many vaguely related things -> consume output -> decide afterward
```

Retry only failed, missing, or truncated operations.

---

## Phase discipline

### Investigation

Goal: obtain enough evidence to choose an implementation or verdict.

Exit when:

- the owning code path is identified;
- required API/compatibility assumptions are understood;
- important implementation risks are known;
- enough evidence exists to act.

Do not seek exhaustive understanding of adjacent systems.

### Implementation

Make the smallest coherent change satisfying acceptance criteria.

Do not restart broad investigation unless implementation exposes a concrete blocker or contradicts an earlier
assumption.

Prefer coherent edit batches over tiny speculative edits.

### Validation

Use the narrowest decisive check first.

Broaden only when repository completion rules, acceptance criteria, failures, or meaningful uncertainty require it.

### Audit

Perform at most one general correctness audit after required validation succeeds.

A second general audit requires:

- a newly discovered defect;
- failed validation;
- contradictory evidence; or
- explicit user request.

Do not repeatedly restart "final review."

---

## Completion and stopping rule

A code-change task is complete when:

- requested acceptance criteria are satisfied;
- required Debug/Release builds pass;
- required tests pass;
- final diff/repository hygiene passes;
- no concrete unresolved correctness issue remains.

Once this gate is met:

- do not start another speculative audit;
- do not broaden scope;
- do not refactor adjacent code merely because it could be cleaner;
- do not search for hypothetical additional problems;
- do not rerun already-passing evidence without a relevant change.

A theoretical possibility of another issue is not a concrete unresolved correctness issue.

Then stop.

---

## Project intent and structure

Captain of Industry is a complex Unity supply-chain/factory-automation simulator.

TajsCOI is a monorepo containing multiple Captain of Industry mods plus shared libraries. Preserve architectural
boundaries even when shortcuts would make one task locally easier.

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

Compile-time contract dependency:

```text
                 TajsCOI.Common
       ┌─────────┬─────┴─────┬───────────┐
   TajsCore  TajsTweaks  TajsProfiler  TajsPerformance
```

Tweaks, Profiler, and Performance manifest-depend on Core but must not compile-reference `TajsCore.dll`.

Runtime services inject Common's `ITajsRuntime` through the CoI dependency resolver.

---

## Project responsibilities

### TajsCOI.Common

A normal library, not an `IMod`.

Allowed:

- generic utilities/collections;
- shared metric/timing primitives;
- generic reflection helpers;
- version/signature description types;
- shared result/error abstractions;
- genuinely reused formatting helpers.

Not allowed:

- gameplay features;
- mod lifecycle ownership;
- feature-specific MaFi bindings;
- suite-wide mutable service state;
- code used by only one mod.

Rule: **if only one mod uses it, keep it in that mod.**

Common has no MaFi or Harmony dependency.

### TajsCore

Installable runtime foundation.

Owns:

- suite/runtime lifecycle coordination;
- shared logging;
- shared console commands;
- compatibility/game-version services;
- Harmony runtime/patch coordination;
- mod registration/discovery;
- cross-mod capability detection;
- true cross-mod diagnostics/service registries.

Installing only TajsCore should produce essentially no gameplay-visible change.

Core explicitly loads:

```text
0Harmony.dll
TajsCOI.Common.dll
TajsCore.dll
```

Dependent mods reference Common with copy-local disabled and receive Core services only through Common interfaces.

### TajsTweaks

Contains user-facing QoL/gameplay tweaks.

Organize features as vertical slices under:

```text
Features/<FeatureName>/
```

Keep config, patches, bindings, runtime state, and helpers together unless genuinely shared.

Do not turn `Infrastructure/`, `Interop/`, `Services/`, or `Managers/` into catch-all directories.

Private-MaFi compatibility seams must fail gracefully and preserve vanilla behavior when signatures no longer match.

### TajsProfiler

Diagnostics only. Observe the game; do not fix it.

```text
TajsProfiler/
├─ Core/       # generic profiling machinery
└─ Probes/     # subsystem-specific instrumentation
```

Core may own generic sessions, snapshots, histories, histograms, comparisons, spike recording, formatting, and
aggregation.

Subsystem-specific hooks/counters/snapshots belong in probes such as:

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

Generic capture/reporting belongs in Core; subsystem knowledge belongs in the owning probe.

Current runtime diagnostics include bounded captures for save/load timing, memory, CPU, GC, renderer telemetry,
lifecycle checkpoints, Harmony audit, and initialization hotspots.

### TajsPerformance

Contains only evidence-backed performance fixes.

A Performance feature needs:

- a measured problem;
- identified expensive stage;
- clear behavioral contract;
- behavior-preserving implementation;
- before/after validation.

Profiling/diagnostics belong in TajsProfiler.

Experimental/default-off candidates require issue-specific in-game A/B acceptance before default enablement. Proxy
benchmarks alone are insufficient.

Stable product-owner/slot buffers must never be compacted by product-buffer shrinking.

### TajsDebugger

If created, reserve it for invasive developer tooling such as live state mutation, debug overlays, entity dumps, and
path visualization.

Do not stretch TajsProfiler into a general debugger.

---

## Namespaces and identities

Preferred namespaces:

```text
TajsCOI.Common
TajsCOI.Core
TajsCOI.Tweaks
TajsCOI.Profiler
TajsCOI.Performance
```

Installable IDs may remain:

```text
TajsCore
TajsTweaks
TajsProfiler
TajsPerformance
```

Do not rename user-facing mod IDs casually; they affect manifests, dependencies, deployed directories, and save/mod
identity.

---

## Harmony rules

Core owns the packaged Harmony runtime.

Each independent patch set owns a stable scoped Harmony ID, e.g.:

```text
TajsCOI.Tweaks.UnlockedSpeed
TajsCOI.Profiler.Dumping
TajsCOI.Profiler.RuntimePerformance
TajsCOI.Performance.StreamingSaveCompression
```

Do not add a suite-wide patch manager merely because multiple scoped owners exist.

Prefer explicit patch installation over broad `PatchAll()` when practical.

Every patch should establish:

1. patched vanilla method;
2. reason;
3. observe vs modify;
4. behavior if method/signature is absent;
5. unpatch/dispose behavior if required.

Profiler patches must fail open and must not:

- suppress vanilla exceptions;
- change search results;
- reorder jobs;
- mutate saves;
- throttle gameplay;
- introduce alternative algorithms;
- perform expensive reflection/enumeration/stack traces in hot paths unless explicitly bounded.

---

## Private MaFi API / interop

Private APIs are compatibility seams.

Principle:

> Centralize the mechanism; localize the knowledge.

Generic reflection/signature helpers may live in Common/Core.

Feature-specific private knowledge belongs with the owning feature/probe, e.g.:

```text
TajsProfiler/Probes/Dumping/Interop/
TajsTweaks/Features/Foo/FooBindings.cs
```

Prefer that over a global `MafiPrivateApi.cs`.

Validate expected:

- member existence;
- parameter types;
- return type;
- static/instance shape;
- supported game version.

On failure, disable the affected feature/probe gracefully and log a useful compatibility message. Do not crash unrelated
functionality.

Avoid per-call reflection in hot paths; resolve handles/delegates/accessors once when practical.

---

## Feature/probe lifecycle

Keep root `IMod` implementations small and boring.

Use simple feature/probe hosts rather than giant root lists of unrelated lifecycle calls.

Do not invent lifecycle frameworks that obscure the game/mod-loader lifecycle.

Process-wide state includes:

- loaded assemblies;
- CLR statics;
- Harmony patch tables.

Gameplay scene/resolver state includes:

- `[GlobalDependency]` service instances;
- `DependencyResolver`;
- managers/renderers;
- console/UI objects;
- `SimLoopEvents`.

Process-lifetime patches must be idempotent.

Process-static state must not strongly retain resolver-scoped objects or callbacks.

---

## Configuration

Features/probes own their setting descriptors.

Shared contracts live in `TajsCOI.Common`; persistence/dashboard live in `TajsCore`.

```text
feature/probe -> SettingDescriptor through ITajsSettings
TajsCore      -> persistence + runtime changes
```

Do not add `ModJsonConfig` or per-mod `config.json`.

Use stable `ModId.key` identifiers.

Declare `Immediate`, `ReloadSave`, or `RestartGame` honestly.

Invalid persisted values fall back to descriptor defaults.

Global values:

```text
%APPDATA%\Captain of Industry\TajsCOI\settings.json
```

Runtime controls:

```text
tajs_dashboard
tajs_settings_list
tajs_settings_set <ModId.key> <value>
```

The dashboard is gameplay-scene-only until a deliberate main-menu bootstrap exists. Do not accidentally resolve
gameplay-scene services in main-menu lifetime.

---

## Profiling design rules

Prefer top-down measurement before deep instrumentation:

1. measure a broad/root scope;
2. identify the expensive subsystem;
3. instrument one level deeper;
4. repeat until isolated.

Do not instrument dozens of internals based only on suspicion.

Capture cumulative and worst-event cost where meaningful.

Deep probes should be cheap when disabled and bounded when enabled.

Avoid unbounded history, per-call formatting, hot-loop stack traces/reflection, or transient-object dictionaries unless
a bounded diagnostic explicitly requires them.

Distinguish wall-clock duration, simulation/update duration, cumulative nested work, and concurrent work. Do not present
nested cumulative time as exclusive wall time.

---

## Performance-fix rules

Before changing game behavior:

- reproduce;
- measure;
- identify the actual expensive stage;
- understand vanilla semantics from supported references;
- implement the smallest behavior-preserving fix;
- compare before/after;
- verify saves/gameplay behavior;
- document compatibility assumptions.

Do not promote profiler observations directly into workarounds.

If another mod may be the root cause, identify the interaction before adding a blanket vanilla workaround.

---

## Source/reference policy

The main repository must not contain:

- Captain of Industry binaries;
- decompiled MaFi source;
- proprietary third-party compiled/decompiled code;
- private reference repositories/submodules containing those materials.

`TajemnikTV/TajsCOI-Refs` is a separate private research repository.

Use it to understand signatures, call flow, compatibility, and historical behavior, while keeping TajsCOI implementation
original and clean.

Do not paste large proprietary/decompiled source into comments, docs, tests, or PR descriptions.

Avoid mentioning other mods, especially in public/user-facing material.

Record important reference conclusions so they do not need to be repeatedly rediscovered.

---

## Build and repository hygiene

The repository is Rider-first but must remain compatible with normal `dotnet`/MSBuild workflows where supported.

Before declaring a code change complete:

- build Debug;
- build Release;
- use exact configured CoI references;
- run available relevant tests;
- inspect final diff for generated files, binaries, local paths, or accidental refs;
- confirm no unintended build output remains.

Do not commit local game paths, Rider user settings, build output, game DLLs, or recovered artifacts.

Keep `src/Mods/Directory.Build.props` and `.targets` generic.

Normal local commands:

```powershell
.\build.ps1
.\build-and-run.ps1
.\build-and-run.ps1 -Configuration Release
.\tail-log.ps1
.\tail-log.ps1 -All
.\tail-log.ps1 -Pattern "Tajs|Path|Simulation"
```

Test project references must disable deployment, Unity copying, and release packaging.

Per-mod project properties are authoritative for identity, version, game compatibility, manifests, and build metadata.

---

## Tests

Unit-test pure logic, especially metrics, histories, comparisons, config parsing, version/signature selection, and
formatting.

Treat Harmony/game-runtime behavior as integration work.

Do not create fake tests that merely assert argument forwarding while missing the actual semantic interaction.

Use targeted tests during implementation; run broader required gates after relevant changes are complete unless a broad
run is needed to diagnose a failure.

---

## GitHub / PR discipline

Keep PRs conceptually coherent.

A refactor PR should not silently add gameplay changes.

A profiler PR should not quietly add an optimization.

A performance-fix PR should explain what was slow and how the change was validated.

If investigation disproves the original hypothesis, update scope/title/body as needed while keeping the final change
coherent.

Before addressing review comments:

- verify each against current head;
- distinguish stale findings from valid current issues;
- fix correctness rather than blindly satisfying wording;
- resolve threads only after code truly addresses them.

End long work at a coherent commit/handoff boundary with:

- current diff;
- completed behavior;
- canonical owners;
- remaining acceptance criteria;
- known failures;
- validation;
- excluded scope.

After a large coherent feature implementation, commit it. Substantial follow-up fixes should normally be separate
commits when practical.

---

## Architectural anti-patterns

Do not introduce:

- god-like `TajsCore` features;
- a Common dumping ground;
- lateral mod dependencies without strong reason;
- feature-specific MaFi knowledge in Common;
- giant global `Managers`, `Services`, `Patches`, or `Interop` buckets;
- speculative frameworks without concrete ownership/lifecycle need.

Prefer the smallest local implementation with a clean boundary. Extract only after real reuse or cross-mod need appears.

---

## Practical recovery examples

### After compaction

Bad:

```text
rerun repository discovery
reread references
regenerate full plan
repeat broad searches
```

Good:

```text
read TASK_STATE
git status --short
inspect focused diff if needed
resume Next
revisit prior evidence only if contradicted
```

### After validation

Bad:

```text
Debug passes
tests pass
Release passes
start another speculative audit
reread giant implementation files
```

Good:

```text
Debug passes
tests pass
Release passes
focused diff hygiene
no concrete unresolved defect
stop
```

---

## Final execution checklist

Before finishing repository work, verify:

1. Requested scope is satisfied.
2. Acceptance criteria are satisfied or explicitly reported incomplete.
3. No concrete correctness issue is being hidden.
4. Required Debug build is green.
5. Required Release build is green.
6. Relevant tests are green.
7. Final diff contains only intended changes.
8. No local paths, binaries, generated artifacts, or accidental refs were introduced.
9. Architectural ownership remains intact.
10. Temporary task state is cleaned up or intentionally retained for handoff.
11. No further non-speculative investigation is required.

Then stop.
