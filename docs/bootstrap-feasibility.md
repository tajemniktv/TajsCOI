# TajsBootstrap feasibility boundary

`src/Bootstrap/TajsBootstrap` is intentionally isolated from the installable mods. It targets
.NET Framework 4.8 and references only the BCL, so an early UnityDoorstop entry point cannot pull
MaFi, Unity, or Harmony through compile-time dependencies.

`BootstrapApi.Initialize(path)` accepts the canonical packaged `0Harmony.dll` path supplied by the
installer/runtime. It records the file path, assembly version, and SHA-256, installs an
`AssemblyResolve` handler only for the `0Harmony` simple name, and refuses to replace an already
loaded assembly with a different version or location. `BootstrapApi.Disable()` removes the
resolver and leaves the normal no-bootstrap mod installation untouched.

The assembly also exposes UnityDoorstop's required `Doorstop.Entrypoint.Start()` method. It
discovers the running game root, looks for the installer-owned
`TajsCOI/Bootstrap/0Harmony.dll`, and initializes the same API. The entrypoint is fail-open: an
unavailable payload or incompatible Harmony assembly is reported through `BootstrapApi.Status`
without preventing vanilla startup. A Doorstop configuration must target
`TajsCOI/Bootstrap/TajsBootstrap.dll`; the external Doorstop proxy and its configuration remain
operator-owned.

The loader deliberately does not guess a Steam installation path. `InitializeFromGameRoot` only
checks the bounded root candidates (`0Harmony.dll`, `Captain of Industry_Data/Managed/0Harmony.dll`,
and `Mods/TajsCore/0Harmony.dll`) for an explicit feasibility probe. `BootstrapInstaller` is the
explicit installer/repair boundary: it discovers a root from the running executable (or accepts a
caller-supplied root), copies only the Tajs bootstrap payload, and records owned files and SHA-256
hashes in `TajsCOI/TajsBootstrap.install.json`. It supports verify, repair, disable, and uninstall.
Repair and uninstall refuse drifted files. Root `winhttp.dll` and other external UnityDoorstop
files are never copied, replaced, elevated, or removed; an operator must manage those files
separately.

The BCL-only loader and custody operations are covered by unit tests. Successful UnityDoorstop
startup on the supported 0.8.7b distribution remains an external acceptance step; no game process
is launched by the repository build.
