# Third-party map-editor preservation

`TajsTweaks.map_editor_third_party_mods` is an advanced, restart-scoped opt-in. With the
default `false` value, the ordinary native map-editor transition is untouched. When enabled,
the process-time main-menu hook snapshots only loaded third-party manifest IDs/versions, lets
the native `TryLoadMods` path validate them, and carries only exact compatible entries into the
editor. Missing, changed, or unsupported manifests remain excluded and the callback fails open
to vanilla behavior.

The hook is installed from `TajsTweaksMod` through the ordinary game transition; it does not
depend on the optional early bootstrap. The 0.8.7b method/field shapes are checked before patching,
and context is cleared on the return-to-menu transition, callback failure, and scene teardown.
