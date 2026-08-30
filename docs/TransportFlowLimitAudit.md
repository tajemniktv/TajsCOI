# Per-transport flow limits (#136)

The first implementation is deliberately scoped to ordinary item transports already present in
the game. Sandbox source/sink buildings are not part of the adapter whitelist and remain vanilla.

## Native seam

The supported 0.8.7b transport exposes the narrow transfer acceptance seam through
`IEntityWithPorts.ReceiveAsMuchAsFromPort(ProductQuantity, IoPortToken)`. `Transport` implements it
explicitly. `TransportFlowLimitFeature` resolves that interface mapping and patches only the
`Transport` target, leaving all other endpoint implementations untouched.

The native method returns the unaccepted remainder. The prefix limits the input quantity to the
integer portion of the available bucket, and the postfix refunds that remainder while restoring
any request that was not offered to native code. When no whole token is available, the native call
is skipped and the original request is returned unchanged. Native product compatibility, FIFO
ordering, product spacing, connectivity, and backpressure therefore remain authoritative; no cargo
is truncated or discarded.

## Policy and runtime state

`TransportFlowLimitState` persists only positive `EntityId -> units per simulation second` policy
values in the save-scoped TajsTweaks metadata file. Runtime state is intentionally transient and
contains only the fractional token balance and the last simulation step. A one-second burst cap
is applied, with a global fixed-point-safe limit of 1,000,000 units/second. Missing, zero, or
cleared policy exits the hot path immediately.

The clock is `ISimLoopEvents.CurrentStep`, obtained from the transport's native cached simulation
events field. Clock rewinds do not mint tokens. Reload and policy re-registration clear buckets,
so a rate change cannot compound a prior runtime balance.

## Integration contracts

`ITransportFlowLimitReader` is a Common-only, read-only contract for diagnostics such as #137.
Blueprint/copy uses the value-only configuration registry and the namespaced
`TajsTweaks.TransportFlowLimit` key; no entity or callback is persisted in a configuration payload.
The inspector/editor layer can use `TransportFlowLimitFeature.TryGetConfiguredLimit`,
`TrySetConfiguredLimit`, and `ClearConfiguredLimit` without owning transfer state.
