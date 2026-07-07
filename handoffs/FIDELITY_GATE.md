# FIDELITY GATE (M2) — evidence + verdict

*Lighting/fidelity lane (E2 fidelity half, executed as the L0–L4 "Lighting Sprint" — see [LIGHTING_SPRINT.md](LIGHTING_SPRINT.md)). This is the M2 fidelity-gate submission for the team + Kirsh's realism bar: "realistic enough that it gives you a reliable experience of being in the room." Branch `paco/lighting-l0-l4`. Full EditMode suite: **69/69 green** (post Fable review — see [FABLE_REVIEW.md](FABLE_REVIEW.md)).*

## What shipped (L0–L4)

| Gate | Delivered | Verify |
|---|---|---|
| **L0** HDRP quality pass | ACES tonemapping + SSAO + bloom (runtime `QualityRig`); asset support flags (SSGI/SSAO/SSR/shadow-mask) + 4096 shadow atlas set in bootstrap code (`HdrpQualityConfigurator`) so they survive a fresh clone | `QualityRigTests` |
| **L1** Real PBR materials | `AssetFetcher` downloads ambientCG CC0 zips (SHA-256 locked in [`unity/ASSETS.md`](../unity/ASSETS.md)), packs HDRP MaskMap, builds **triplanar world-mapped** materials over the flat builtins (wood floor, plaster walls/ceiling; +carpet/tile for future rooms) | `AssetPipelineTests` |
| **L2** Luminaires | Pendant is now a **real luminaire** (emissive shade + point light carrying 40% of the flux, spots give up their share so total flux is conserved) + a directional **sun** per room | `LightingLuminaireTests` |
| **L3** Calibration honesty | `LightingCalibrator` extended to model the pendant (shared flux split); **measured** matched-luminance harness renders both conditions and checks a reference card at the calibration plane | `CalibrationTests` + `l3-calibration-report.txt` |
| **L4** Gate evidence | This document + the renders in [`docs/fidelity/`](../docs/fidelity/) | — |

## Evidence (in `docs/fidelity/`)

- **The ceiling pair, full fidelity** — `capture-l4-control.jpg` (2.4 m) vs `capture-l4-treatment.jpg` (3.2 m). Same materials, furniture, luminaire, and matched lighting; the **only** visible difference is the declared variable (ceiling height). This is the single-variable rule and the realism bar in one shot.
- **Mood range** — `capture-l2-neutral-daylight.jpg`, `capture-l2-warm-evening.jpg`, `capture-l2-dim.jpg`: one room, three lighting presets, distinct plausible moods driven by colour temperature + target lux at a fixed exposure.
- **Quality pass before/after** — `capture-l0-before-flat.jpg` vs `capture-l0-after-flat.jpg` (SSAO contact shadows + ACES; greybox materials, so the delta is subtle by design).
- **Matched-luminance report** — `l3-calibration-report.txt`: the 2.4 m and 3.2 m rooms read within **4.7%** at the eye-level calibration plane (tripwire ≤ 25%). Proof that an 0.8 m ceiling difference does **not** leak a brightness confound.

## Self-assessment vs Kirsh's bar

**Passes for the surfaces + light:** wood floor reads as real planks at eye height, plaster walls/ceiling have genuine surface variation, the pendant reads as an actual lamp (glows + pools light), and the whole frame is filmic (ACES) with grounded contact shadows (SSAO). The scientific control is visibly intact and numerically luminance-matched.

**Not yet at the bar (honest gaps, next rungs):**
1. **Furniture models LANDED (2026-07-07)** — table/chairs/sideboard are now real Poly Haven CC0 models (`dining_chair_02`, `wooden_table_02`, `GothicCabinet_01`) built into prefabs by `FurnitureModelBuilder` (axis-corrected, fit-scaled to the slot footprints, roughness-masked, collider-equipped) and loaded through FurnitureLayoutResolver's existing prefab path. Remaining furniture niggles: the table is non-uniformly scaled to the 2.15×1.05 slot footprint (mild grain stretch), the sideboard is a style-mismatched Gothic cabinet (ASSET_SOURCING already flags it placeholder-grade), and the plant is still a greybox box.
2. **Daylight through windows is a flat default sky.** A mood-driven `GradientSky` was attempted and reverted — without SSGI bounce the room relies on the default sky for ambient fill, and a custom sky went near-black in a single headless frame. A real mood sky needs either the SSGI camera frame-setting enabled (support flag is already on) or a baked ambient probe, plus an exposure rebalance. Tracked for a follow-up.
3. **SSGI bounce is proven to work** (`docs/fidelity/capture-ssgi-off.jpg` vs `-on.jpg`) via a per-camera frame-setting override (`HDAdditionalCameraData` + `FrameSettingsField.SSGI`; the support flag is already on from L0 and the `GlobalIllumination` volume override from QualityRig). The remaining step is to enable that frame setting on the runtime walk/preview cameras (a small studio-side change) — or globally via GraphicsSettings' default camera frame settings. The effect is subtle in this bright, low-saturation dining scene (~2% mean brightness, some added ceiling/corner fill); it will read more in dimmer, more colourful rooms.

**Verdict request:** surfaces + luminaires + calibration are gate-ready; recommend conditional pass with furniture models + SSGI bounce as the fast-follow before the Kirsh demo (M3).

## Reproduce

```
# build materials (needs network once; then cached + SHA-verified)
Unity.exe -batchmode -quit -projectPath unity -executeMethod RoomGen.Editor.AssetFetcher.FetchAll -logFile fetch.log
# render the gate evidence
Unity.exe -batchmode -quit -projectPath unity -executeMethod RoomGen.Editor.SceneCapture.CapturePairEvidence -logFile pair.log
Unity.exe -batchmode -quit -projectPath unity -executeMethod RoomGen.Editor.SceneCapture.CaptureMoods -logFile moods.log
Unity.exe -batchmode -quit -projectPath unity -executeMethod RoomGen.Editor.SceneCapture.MeasurePairLuminance -logFile calib.log
```
