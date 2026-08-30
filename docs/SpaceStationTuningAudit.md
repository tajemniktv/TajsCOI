# Space-station tuning audit

`SpaceStationTuningFeature` targets the single `Mafi.Core.SpaceProgram.SpaceStationProto`
registered by Captain of Industry 0.8.7b. The gameplay-scene owner captures each value once
through `IBaseValueOverride<double>` and derives configured values from that capture. A
failed member conversion or setter disables only that member's descriptor and
leaves the native space-program state machine in control.

## Field classification

| Native member | Classification | Reason |
| --- | --- | --- |
| `m_constructionCostFirstTier`, `m_constructionCostPerTier` | Future upgrade only | `OrbitManager` and `SpaceStation.StartUpgrade` read `GetDataFor` when a new construction/upgrade is requested. An existing `Ongoing...Cost` is never rewritten. |
| `m_maintenancePerMonthPerTier`, `m_crewSuppliesPerMemberPerMonth` | Save/game reload required | `StationTierData` caches derived rates and buffer capacities for the current station. |
| `m_researchPointsProvidedPerMonthPerTier`, `m_researchSuppliesConsumedPerMonthPerTier` | Save/game reload required | Daily generation and supply consumption read cached `StationTierData`; reload rebuilds it without replacing stored points or product buffers. |
| `m_unityBonusFirstTier`, `m_unityBonusPerTier`, `m_researchEfficiencyBonusFirstTier`, `m_researchEfficiencyBonusPerTier` | Save/game reload required | Unity and efficiency values are derived into cached tier data and applied by the normal station tick. |
| `m_crewRequiredPerTier` | Save/game reload required | Crew capacity is derived when tier data is built; normal assignment/ejection remains native. |
| `CREW_ROTATION_DURATION` | Live/future tick | `NextCrewRefresh` and the daily rotation check read this static value directly. |
| `CREW_ROTATION_REQUEST_TIME`, `DEGRADES_AT` | Live/future tick | Native station checks read the static thresholds directly; they are registered and reported independently, with no extra user setting. |
| `MAINTENANCE_PARTS_BUFFER_RESERVE`, `MAINTENANCE_LEVEL_LASTS_FOR`, `CREW_SUPPLIES_BUFFER_RESERVE`, `RESEARCH_SUPPLIES_BUFFER_RESERVE`, `RESEARCH_POINTS_BUFFER_CAPACITY`, `MIN_MAINTENANCE_PARTS_BUFFER_CAP` | Future station/upgrade calculations | These constants are consumed while constructing future `StationTierData` and buffers. Existing buffers are not resized or truncated. |
| `ADVANCED_PARTS_TIER_FROM`, `RESEARCH_TIER_FROM`, `CREW_REQUIRED_FROM`, `ASTEROIDS_SUPPORT_FROM` | Future progression calculations | These are registered for compatibility visibility but intentionally have no writable setting; changing tier gates would alter the vanilla progression contract. |

The user-facing controls are bounded multipliers. The exact 0.8.7b base values
are included in each setting description, and the shared dashboard numeric
editor displays the accepted value plus its reload/live state. Fixed-point
values are converted through their logical value (`Fix32` uses 10 fractional
bits; `Percent` uses five decimal fraction digits) and clamped before writing.

Changing a construction multiplier cannot reprice a station upgrade that has
already been paid for: `OngoingUpgradeCost` and delivered parts are native
runtime state and are not touched by this feature. Reset clears the registrations
through typed registrations, restoring each captured native field exactly.
