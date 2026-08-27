# TajsCOI main-menu bridge

Core uses the exact 0.8.7b `Mafi.Unity.MainMenu.MainMenuController` constructor as a narrow
Harmony seam. The postfix reads only the native menu and its resolver, then adds one optional
`TajsCOI Control Center` button. The opened window resolves `ISettingsProfileService`,
`ITajsSettings`, and `ITajsRuntime` only: it shows global settings, profile-safe preview/apply
operations, and read-only diagnostics. It does not resolve entity metadata, save repair, or other
gameplay-scene services.

The patch is process-idempotent, fail-open when the private constructor changes, and owns the
`TajsCOI.Core.MainMenu` Harmony ID. `TajsCoreMod` installs it through the ordinary no-bootstrap
mod path, so disabling the optional early bootstrap does not remove the normal mod installation.

The exact constructor shape is covered by `MainMenuBridgeContractTests`. A fresh-game visual
acceptance check remains external; repository builds do not launch Captain of Industry.
