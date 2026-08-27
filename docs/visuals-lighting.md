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
direction, `RenderSettings` ambient values, and `QualitySettings` shadow values once
per scene/light recreation, then restores those exact values at reset/termination.
