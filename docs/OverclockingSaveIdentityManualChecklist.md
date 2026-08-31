# Overclocking save-identity manual checklist

These cases require a live 0.8.7b game because unit tests cannot exercise the
native save window, resolver lifecycle, or real entity IDs:

- Create two saves with the same display name. Set a different overclock policy
  in each, save, reload each, and verify the policies stay isolated.
- Save the same game repeatedly (including a large change in file size), reload,
  and verify its policy/group state remains available.
- Use Save As/copy to create a new save, reuse an entity ID, and verify the new
  save starts without the original policy/group state.
- Rename/move a save without copying its file, then reload and verify its
  verified physical lineage keeps the policy/group state.
- Delete a save, recreate the same name, and verify the recreated save starts
  clean even if entity IDs are reused.
- Place a legacy name-based overclock sidecar in the old location. Verify it is
  reported as untrusted, left unchanged, and not imported automatically.
- Pause and resume with Auto enabled, then test a very low and very high
  simulation speed. Auto decisions should follow simulation time, not paused
  wall-clock time or render cadence.
- Confirm Auto is available for Machines; OreSortingPlant, OfficeBuilding,
  WasteSortingPlant, and Transport remain manual until their native demand/
  timing semantics are separately verified.
