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
discovers the running game root (using Doorstop's `DOORSTOP_PROCESS_PATH` when supplied), honors
the install manifest's explicit disabled state, prefers the `0Harmony.dll` beside the Doorstop
target, and initializes the same API. The entrypoint is fail-open: an unavailable payload,
unreadable/foreign install manifest, or incompatible Harmony assembly is reported through
`BootstrapApi.Status` without preventing vanilla startup. The installer supplies the x64
UnityDoorstop `winhttp.dll` proxy and a `doorstop_config.ini` targeting the currently installed
`TajsBootstrap.dll` from the TajsCore mod directory. Doorstop 4 resolves the configured value as a
path but does not expand `%APPDATA%`, so the installer writes the absolute path discovered from the
loaded mod assembly. The root `TajsCOI/Bootstrap` copies remain installer-owned
compatibility/repair payloads; they are not the normal update target. An existing `version.dll` is
never used or modified.

The loader deliberately does not guess a Steam installation path. `InitializeFromGameRoot` checks
the target assembly's sibling first, then the bounded root candidates
(`0Harmony.dll`, `Captain of Industry_Data/Managed/0Harmony.dll`, `Mods/TajsCore/0Harmony.dll`,
and `TajsCOI/Bootstrap/0Harmony.dll`) for an explicit feasibility probe. `BootstrapInstaller` is the
explicit installer/repair boundary: it discovers a root from the running executable (or accepts a
caller-supplied root), copies the Tajs bootstrap payload, the bundled x64 Doorstop proxy, and its
configuration, and records owned files and SHA-256 hashes in
`TajsCOI/TajsBootstrap.install.json`. It supports verify, repair, disable, and uninstall. Repair
and uninstall refuse drifted files. An existing root `version.dll`, unknown `winhttp.dll`, or
unknown `doorstop_config.ini` is never overwritten or removed; the installer refuses rather than
claiming ownership of foreign files.

If bootstrap prevents the game from starting, run `scripts/disable-bootstrap.ps1 -GameRoot
<Captain-of-Industry-root>` after closing the game. The script refuses a running process,
foreign/malformed manifests, and unknown payload records, then atomically changes only the
Tajs-owned manifest's `Enabled` flag. It does not remove the payload or touch `version.dll`, so the
next launch falls back to the ordinary no-bootstrap installation.

The BCL-only loader and custody operations are covered by unit tests. Successful UnityDoorstop
startup was also exercised on 2026-08-27 with the installed 0.8.7b executable and managed data in
an isolated temporary launch root. The x64 Doorstop 4.5 verbose log recorded the proxy load,
Mono `v4.0.30319` initialization, opening `TajsBootstrap.dll`, and invoking
`Doorstop.Entrypoint.Start`; a probe wrapper recorded `TajsBootstrap Ready` with Harmony 2.4.2.0
and SHA-256 `77e6901ecc606aec66c2a972782a3779e4f50c037d2d165eb7ececdd4d8f794d`. The live Steam
installation and its existing external `version.dll` were not changed. The packaged proxy is the
x64 `winhttp.dll` from UnityDoorstop 4.5.0; its LGPL-2.1 text is shipped beside the mod asset.
Fresh-game UI acceptance and Steam-launch behavior remain external checks; repository builds do
not launch Captain of Industry.
