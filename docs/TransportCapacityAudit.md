# Transport capacity tuning audit

The #130 adapters target the 0.8.7b prototype seams and are reload-scoped. They
capture each prototype variant's native value once through the lifecycle-owned
`TypedBaseValueOverrideRegistry` and `IBaseValueOverride<double>` contracts; later settings changes derive from that capture and
never compound a prior override.

| Adapter | Native base | Effective downstream owner | Lifecycle |
| --- | --- | --- | --- |
| Truck | `TruckProto.CapacityBase` | `Truck`'s native capacity multiplier callback | Reload required |
| Excavator | `ExcavatorProto.Capacity` | `Excavator` mining/dump capacity checks | Reload required |
| Train wagon | `CargoWagonProto.m_baseCapacity` | `Capacity` and `SubCarCapacity` (then native train multiplier) | Reload required |
| Cargo ship | `CargoShipProto.CapacityMultiplier` | Cargo module capacity and `ContractsManager` trade estimates | Reload required |
| Cargo depot | `CargoDepotModuleProto.Capacity` | Module storage buffer construction and transfer limits | Reload required |

Each adapter is independent. A missing member or a failed setter disables only
that adapter's variant and leaves the native transport path available. Capacity
reductions do not inspect or rewrite cargo in the prototype adapter. Callers
that have an entity occupancy value may use `CapacityReductionPolicy` to defer
the reduction or explicitly represent temporary over-capacity while cargo
drains; cargo is never truncated.

Supporting infrastructure is an explicit, separate choice: the cargo-depot
adapter has its own setting/key and callers must select that adapter when they
intend to scale it. Vehicle settings never fan out to depots or other support
buildings; every selected value still delegates to the shared #106 base-value
registration and reset path.

The cargo-ship estimate seam already reads `CargoShipProto.CapacityMultiplier`
in the exact 0.8.7b `ContractsManager` path, so changing the authoritative
prototype multiplier keeps contract/trade estimates coherent without a second
estimate patch. The existing belt spacing/stack compensation remains owned by
transport overclocking and does not touch these prototype capacity fields. Its
native spacing and stack maxima are captured once per transport construction,
so later policy changes or prototype writes cannot compound the effective
capacity calculation.

Static builds and unit contracts do not prove live-game reload behavior. A
fresh-save runtime check is still required before treating the settings as
player-validated.
