# TajsVisuals lighting seam

The 0.8.7b renderer's authoritative sun is `Mafi.Unity.Camera.LightController`'s
private `m_light` directional `UnityEngine.Light`. `LightController.GetState()` captures
the vanilla state and `SetLightIntensity()` / `SetState()` update the water renderer's
specular intensity, so the visuals backend uses those methods and only falls back to
direct `Light` writes when the seam is unavailable.

`WeatherController` remains the source of weather-driven light intensity/color and
`FogController` remains the owner of fog. TajsVisuals does not mutate either service
or the simulation calendar; its presentation policy is applied after the vanilla
weather render callback. Its scene-owned backend captures `LightController.State`,
the directional light, and its direction once per scene/light recreation, then
restores those exact values at reset/termination. Ambient and global quality-shadow
settings remain vanilla-owned and are not captured or rewritten by this backend.

`LightingBackend` is the sole writer for the controller-owned directional-light values. Its
`VanillaSnapshot` is captured once per scene/light,
`BaseLightingPolicy` (#64) and `TimeOfDayPresentation` (#81) are composed into an immutable
`EffectiveState`, and `RestoreVanilla()` uses the same backend path to return to the captured
values. Restore happens only when an active policy is disabled or the scene ends, so repeated
render updates cannot overwrite weather changes with a stale snapshot. The policies are
visual-only; simulation time and weather/fog ownership remain vanilla.
