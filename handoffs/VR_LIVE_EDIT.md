# HANDOFF — VR_LIVE_EDIT (role E2: fidelity now, the VR north star next)

*Self-contained. You (+ your AI) need ONLY this file and the contracts it names. Repo `https://github.com/Diggss-sys/knowledge-atlas-app`, branch `Diggss-sys-branch`; feature branch → PR ([COORDINATION.md](COORDINATION.md)). This workstream has two halves: FIDELITY (starts now, no headset needed) and the VR ARM (starts when hardware arrives — M5).*

## Context (3 paragraphs)

The platform's north star ([docs/VR_LIVE_EDITING.md](../docs/VR_LIVE_EDITING.md)): a **subject wears a VR headset inside a generated room while an operator edits the room around them live** from the same PC. The 2026-07-02 pivot (DL-8) made the whole app native Unity **HDRP**, which is both your opportunity (physical light units, area lights, screen-space GI, volumetrics — browser constraints gone) and your responsibility: **Kirsh's realism bar** — "realistic enough that it gives you a reliable experience of being in the room" — is judged on your work at the M2 fidelity gate. The precedent to beat: the Max Planck rooms Kirsh cites were built in Unity HDRP; the paradigm is validated, the ceiling is high enough.

The generator you build on (`unity/Assets/RoomGen/`, E1's workstream) already has a physically-calibrated HDRP light rig: four recessed spots in lumen→candela with `LightingCalibrator.MatchTargetLux` calibrating to a target lux, and a fixed-exposure volume. That calibrator is the platform's **matched-luminance mechanism** — the answer to the coupled-variable problem that a 3.2 m ceiling room is physically dimmer than a 2.6 m one (inverse-square + surface area; the numbers are in [docs/RENDERING_RESEARCH.md](../docs/RENDERING_RESEARCH.md) §2, which remains normative physics even though its WebGL constraints are retired).

Your VR half rides the seam: `ISpecChannel` has a `LocalChannel` (v1, in-process). PCVR live-editing = the operator UI and the VR camera in ONE process — no networking. The comfort rules are pre-decided and non-negotiable: **geometry changes reach an immersed subject only via fade-swap or teleport-between-variants; only lighting/material/furniture may morph live.**

## Contracts you implement/consume

- `spec/room_spec.schema.json` `lighting.*` + `surfaces.*` — what your rig renders (v1.0 single-channel; the v1.1 split into `natural`/`artificial`/`bounce` is planned — [docs/ROOMSPEC_V1_1.md](../docs/ROOMSPEC_V1_1.md) Change 1; the Kelvin law `K = 6500 − 3800 × warmth` is normative).
- `spec/contracts/ENGINE_SEAM.md` — `switch_condition` transitions (`fade`, `teleport`) are yours to implement well; the VR phase adds no new message kinds.
- [docs/ASSET_SOURCING.md](../docs/ASSET_SOURCING.md) — the pinned CC0 texture/HDRI/furniture lists + the realism ladder (greybox → CC0 → paid). Extend `unity/ASSETS.md` with URL+hash pins for everything you import (determinism guardrail).
- [docs/RENDERING_RESEARCH.md](../docs/RENDERING_RESEARCH.md) §2 (light physics), §4 (curved/bow mesh recipe — keep curved walls MATTE; concave+glossy = caustic confound), §5 (texture playbook: real-world texel density, roughness-over-albedo, channel-packing).

## Scope / NOT scope

**Yours (fidelity half, M2):** HDRP quality settings pass (the bootstrap's auto-created HDRP asset is bare-bones — shadows, SSAO/SSGI, exposure, ACES need a deliberate pass) · real PBR material sets replacing the flat builtins (per ASSET_SOURCING pins; world-scale tiling) · window treatment (HDRI skybox visible through openings, sun + sky, HDRP **area lights** in window openings — the real version of the old "window fill" hack) · furniture fidelity ladder (greybox → CC0 models) · the **fidelity gate** run + evidence (side-by-side vs a reference photo, screenshots via the seam) · keeping `MatchTargetLux` truthful as materials change (albedo affects bounce).
**Yours (VR half, M5):** PCVR bring-up on the existing OpenXR scaffolding (`VrExplorationMode` exists and initializes the loader; make it production-grade) · comfort implementation (fade-swap ~0.2 s, teleport-between-variants, no locomotion during operator edits by default) · 90 fps budget on the 5070 Ti (HDRP: single-pass instanced stereo, cap SSGI/volumetrics per VR quality profile — `RenderQualityProfiles.ApplyVr()` exists as the hook) · headset procurement input (recommendation on record: Quest 3 + Link).
**NOT yours:** generator geometry/adapter (E1) · task flow (E3) · UI panels (P1) · schema changes (route lighting-split needs through P2's v1.1 batch).

## Build steps (in order)

1. **F0 — Boot + audit.** Boot per [UNITY_GENERATOR.md](archive/UNITY_GENERATOR.md) G0. Audit the auto-created HDRP asset: enable SSAO, SSGI (quality Medium), soft shadows, ACES tonemapping, physically-based exposure; document every setting you change in `unity/ASSETS.md`. *DoD: before/after screenshots of the dining room committed with the PR.*
2. **F1 — Materials.** Import the ASSET_SOURCING-pinned CC0 sets for the schema's 9 material enums; world-scale tiling (meters, per the texture's declared physical size); wire into `SurfaceResolver` (E1 coordinates the mapping table); `tint_hex` multiplies base color. *DoD: `wood`, `plaster`, `carpet`, `tile` read as real materials at 1080p from eye height.*
3. **F2 — Daylight.** HDRI sky (pinned) visible through window/door openings; HDRP directional sun + area light per window opening driven by `lighting.preset`/`warmth`/`intensity`; glass non-shadow-casting. *DoD: `neutral_daylight` vs `warm_evening` vs `dim` produce obviously distinct, plausible moods on the same room.*
4. **F3 — Fidelity gate (M2 exit).** Render the committed ceiling pair; side-by-side against a real dining-room photo at 1080p; team judges against Kirsh's bar; iterate F1/F2 until pass or escalate (TEAM_PLAN.md escalation rule). *DoD: the gate verdict + evidence screenshots recorded in the PR and the COORDINATION.md status board.*
5. **V0 — PCVR bring-up (M5, hardware-gated).** Headset via Link: `VrExplorationMode` → production `pcvr` camera path behind the same seam; VR quality profile capped for 90 fps; comfort vignette on locomotion. *DoD: a wearer walks the dining room at stable 90 fps (`SteamVR`/OVR metrics screenshot).*
6. **V1 — Live-edit comfort (M5).** Operator drags lighting sliders → subject sees smooth live change; operator changes ceiling height → subject gets fade-swap (screen fade 0.2 s, rebuild, fade in) or teleport-between-variants; `switch_condition("teleport")` = blink transition. *DoD: the north-star demo — subject in headset, operator editing live — runs for a full pair session without comfort complaints; every geometry change went through fade/teleport (assert via the seam log).*

## Environment gotchas

- HDRP quality assets: change the project's HDRP asset (under `Assets/RoomGen/Settings/`), never Default-HDRP in Packages.
- The generator rebuilds rooms wholesale — your probe/GI settings must survive rebuilds (put volumes on a persistent root, not under the generated room).
- Textures: import as sRGB only for BaseColor; normal maps flagged as Normal; channel-packed masks Linear.
- VR perf: HDRP VR wants single-pass instanced (XR settings), and SSGI in VR is expensive — profile before enabling; 90 fps > pretty (comfort is a launch gate, DL-1 realism notwithstanding).
- Never let a lighting change silently alter the *other* condition of a pair — shared presets apply to both or neither (the gate catches spec-level drift, not scene-side hacks; don't hack scene-side).

## Your integration role

M2's fidelity gate is the project's single most-watched checkpoint (it decides "does this look real enough for Kirsh"). M5 is the north-star demo. You coordinate with E1 on `SurfaceResolver`/lighting-table touchpoints and with P2 when the v1.1 lighting split lands.
