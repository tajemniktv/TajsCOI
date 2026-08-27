# TajsBootstrap feasibility boundary

`src/Bootstrap/TajsBootstrap` is intentionally isolated from the installable mods. It targets
.NET Framework 4.8 and references only the BCL, so an early UnityDoorstop entry point cannot pull
MaFi, Unity, or Harmony through compile-time dependencies.

`BootstrapApi.Initialize(path)` accepts the canonical packaged `0Harmony.dll` path supplied by the
installer/runtime. It records the file path, assembly version, and SHA-256, installs an
`AssemblyResolve` handler only for the `0Harmony` simple name, and refuses to replace an already
loaded assembly with a different version or location. `BootstrapApi.Disable()` removes the
resolver and leaves the normal no-bootstrap mod installation untouched.

The loader deliberately does not guess a Steam installation path. `InitializeFromGameRoot` only
checks the bounded root candidates (`0Harmony.dll`, `Captain of Industry_Data/Managed/0Harmony.dll`,
and `Mods/TajsCore/0Harmony.dll`) for an explicit feasibility probe. A real deployment still needs
the installer/repair PR to discover the game root and pass the recorded canonical path.

This PR proves the BCL-only loader contract and closed failure behavior in unit tests. Successful
UnityDoorstop startup on the supported 0.8.7b distribution remains an external acceptance step;
no game process is launched by the repository build.
