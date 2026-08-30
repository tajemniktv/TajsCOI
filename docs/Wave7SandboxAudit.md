# Wave 7 sandbox and tuning audit

This note records the 0.8.7b compatibility decisions behind issues #72, #73,
#80, #106, and #107.  It intentionally names public/native seams only; the
private reference tree remains the source for signature verification.

## #72 settlement needs, waste, pollution, and focus

| Category | 0.8.7b classification | Behavior |
| --- | --- | --- |
| Settlement goods needs | Native property modifier: `SettlementConsumptionMultiplier` | Independent, immediate toggle. |
| Food need | Native property modifier: `FoodConsumptionMultiplier` | Independent, immediate toggle. |
| Disease effects | Native property modifier: `DiseaseEffectsMultiplier` | Independent, immediate toggle; existing disease state is not rewritten. |
| Air/water pollution effects | Native property modifiers: `AirPollutionMultiplier`, `WaterPollutionMultiplier` | Independent, immediate toggles. |
| Ship/vehicle/train pollution | Native emission multipliers | Independent, immediate toggles at the authoritative emission calculation. |
| Solid waste and biowaste | Narrow `Settlement.TransformProductIntoWaste` accumulator seams | Independent Harmony owners restore only the selected accumulator. Recycling and the other waste branch remain native. This is used because the source-product cache can contain multiple output classes; a broad pre-multiplier would incorrectly suppress unrelated outputs. |
| Air/water pollution production | No safe global production property | Disabled/fail-open; no recipe or product deletion. |
| Electricity/computing needs | No narrow global settlement demand seam | Disabled/fail-open. |
| Clean water/wastewater | One native water-demand property owns both behavior | Disabled/fail-open so the two controls cannot silently change each other. |
| Infinite focus | No supported infinite-capacity seam | Implemented as a bounded 1000x native multiplier, with separate infinite and multiplier owners. |

All modifiers are removed when their setting is disabled.  The profiler remains
observational and consumes the resulting effective values.

## #73 progression and construction

- Research and construction cost use native difficulty/property multipliers.
- Design mode observes `IConstructionManager.EntityConstructionStateChanged`
  after normal command processing and finalizes through
  `MarkConstructed`/`MarkDeconstructed`.  It has a persistent HUD indicator and
  no saveable callback.
- Cargo-ship turnaround is prototype-cached in 0.8.7b and has no validated
  runtime command setter, so the toggle is disabled/fail-open.
- Ore-sorting speed uses the coherent #106 prototype adapter (buffers and
  throughput together, reload-scoped).
- Instant storage empty is an explicit `tajs_storage_empty <id> CONFIRM`
  input command.  It revalidates the setting, entity, and confirmation at
  execution and invokes the native storage cheat-clear seam; nothing is
  cleared implicitly.

## #80 bulldoze

The Harmony patch targets only the soft `ClearingChecker` pre-eligibility
result.  Execution still revalidates through native entity-removal validators.
Hard-invariant classes are never whitelisted, and the whitelist is exact type
name/full-name matching.  Ports, pathability, manager cleanup, and reload
behavior therefore remain native.

## #106 base-value tuning

`BaseValueOverrideService` captures each prototype/property base once and derives
every effective value from that immutable base.  Registration is idempotent,
reset restores the exact base, bounds are enforced, and conversion/setter
failures leave the native value unchanged.  Shipyard cargo, truck pickup
duration, ore sorting buffers/throughput, shaft throughput, and thermal storage
capacity are independent reload-scoped adapters.  Thermal reductions preserve
already stored heat as temporary over-capacity and reject new charging until the
stored amount falls below the reduced capacity.

## #107 disease scaling

The vanilla ordered distance thresholds are captured once during game
configuration.  `Vanilla`, `MapScaled`, and `Custom` policies are bounded and
strictly ordered; invalid custom input falls back to vanilla.  Existing unlocked
disease state is untouched.  The current Harmony seam reproduces the native
top-three candidate selection against the effective thresholds, so map-scaled
policies can unlock lower tiers without fabricating or cloning disease
prototypes.  Custom-trigger diseases and already-unlocked state are untouched.
The setting remains reload-scoped and is intentionally not registered as a new
native disease prototype.  Integration into the native difficulty UI/backend is
deferred to issue #169, which is still open; until that backend change lands the
Tajs setting is the single reload-scoped policy surface.
