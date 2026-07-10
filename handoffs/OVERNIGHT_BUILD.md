# Overnight build — engine lanes (2026-07-06/07)

*One autonomous session (Fable planned, Opus executed) working through the engine lanes, followed on 2026-07-07 by real Poly Haven furniture models and an adversarial **Fable review** ([FABLE_REVIEW.md](FABLE_REVIEW.md) — 7 defects found + fixed, verdict: M2-gate-ready). Branch `paco/lighting-l0-l4`, **unmerged / uncommitted** (all on disk). **69/69 EditMode tests green** (from 38); `RoomStudio.exe` rebuild verified via the game DLL. Evidence renders in [`docs/fidelity/`](../docs/fidelity/).*

## What landed

### E2 — fidelity (the M2 gate)
The "Lighting Sprint" L0–L4 plus follow-ups — full log in [FIDELITY_GATE.md](FIDELITY_GATE.md) and [LIGHTING_SPRINT.md](LIGHTING_SPRINT.md).
- **L0** HDRP quality pass (ACES + SSAO + bloom via `QualityRig`; asset support flags + 4096 shadow atlas in bootstrap code).
- **L1** Real ambientCG PBR materials — `AssetFetcher` (SHA-locked in [`unity/ASSETS.md`](../unity/ASSETS.md)), HDRP mask packing, triplanar world mapping.
- **L2** Pendant is a real luminaire (emissive + point light, flux-conserved) + directional sun.
- **L3** Calibrator honesty — models the pendant; measured harness proves the 2.4 m vs 3.2 m pair reads within **4.7 %** at eye height.
- **Furniture materials** — table/chairs/handles now textured (walnut/fabric/metal).
- **SSGI bounce** proven to work headlessly (per-camera frame setting); ready to wire onto runtime cameras.

### E3 — experiment runtime (the "makes it science" half), R0 + R1
Pure-C# core in `unity/Assets/RoomGen/Runtime/Runner/` — no UI/rooms/network, fully tested. Status note atop [EXPERIMENT_RUNTIME.md](EXPERIMENT_RUNTIME.md).
- **R0** row writer: `ResponseRow`, `ResponseCsv` (canonical 17-col RFC-4180), `ResponseWriter` (validate→CSV+JSONL), `ResponseLogValidator` (reuses `JsonSchemaLite`). Reproduces the golden valid rows, refuses the invalid ones.
- **R1** seeded ordering: `TrialSequencer` (fixed/seeded_shuffle/latin_square, balanced, counterbalanced choice sides), `StudyGate` (published+validated only), `ScriptedSession` + `StudyRunner` (consume a study doc → validated CSV; both rating & choice).
- Sample study: `Resources/RoomGen/Examples/ceiling-study.json`.

### E1 — generator: two new experiments
- **Curved-wall pair** (`curved-wall-pair.json`) — Kirsh's "make that wall curved", single variable `geometry.corner_radius_m`, rendered.
- **Lighting-warmth pair** (`warmth-pair.json`) — `lighting.color_temperature_k`, matched luminance, rendered.
- The library is now three validated experiments (ceiling height, curved walls, warmth) across geometry + lighting.

## To commit (source only — exclude bootstrap/texture/capture artifacts)
New: `Runtime/Runner/*.cs` (11), `Runtime/Lighting/QualityRig.cs`, `Editor/{AssetFetcher,HdrpQualityConfigurator,SceneCapture}.cs`, 7 test files, 3 example specs, `ceiling-study.json`, `response_log.schema.json` + `response_rows.json` in Resources, `unity/ASSETS.md`, the handoffs docs, `docs/fidelity/`.
Modified: `RoomGenProjectBootstrap.cs`, `LightingSystem.cs`, `LightingCalibrator.cs`, `GenerationUtil.cs`, `Tests…asmdef`, `.gitignore`, `COORDINATION.md`.
Do NOT commit: `unity/Assets/RoomGen/{Settings,Scenes}/`, `Resources/RoomGen/Materials/` (generated), `.cache/`, `captures/`, bootstrap `ProjectSettings` churn.

## Next (not headless-buildable — need interactive/networked work)
E3 R2 (participant screens + drive real rooms via the seam + walk/fade/dwell), R3 (queued POST to P3's Worker); wire SSGI onto the walk/preview cameras; real CC0 furniture *models*; canonical (Diego-schema) versions of the two new pairs for the gate path.
