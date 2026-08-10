# HANDOFF — UNITY_GENERATOR (role E1)

*Self-contained. You (+ your AI) need ONLY this file and the contracts it names. Everything is in the repo `https://github.com/Diggss-sys/knowledge-atlas-app`, branch `Diggss-sys-branch` (work on a feature branch, PR back — see [COORDINATION.md](../COORDINATION.md)).*

## Context (3 paragraphs)

This platform generates controlled, single-variable room stimuli for environmental-neuroscience experiments (UCSD COGS 160, Prof. Kirsh): a control room and a treatment room that differ in exactly one declared variable (ceiling height, contour, lighting, …), walked by a participant, with responses logged. Everything flows through one JSON contract, the **RoomSpec** (`spec/room_spec.schema.json`, frozen v1.0) — the schema is the product; Unity is its renderer.

The repo's `unity/` folder contains a **working native Unity 6000.3.16f1 + HDRP generator** (adopted 2026-07-02 from the predecessor repo — [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md) DL-8/DL-10): `RoomGen.Generation.RoomGenerator` procedurally builds shell, curved corners (arc-tessellated), openings, furniture placeholders, and a physically-calibrated HDRP light rig (lumen→candela, fixed exposure, matched to a target lux) from its own internal spec types. It self-configures via `RoomGenProjectBootstrap` (menu `RoomGen ▸ Bootstrap Project`) and builds a Windows app via `RoomGen ▸ Build Windows Application`.

**Your workstream is the reconciliation**: make the generator consume the *canonical* KA RoomSpec (not its internal `RoomGen.Contracts.RoomSpec`), add the missing desktop walkthrough, and expose everything behind the engine seam. You are the platform's critical path for milestone M1 (tracer bullet).

## Contracts you implement

- `spec/room_spec.schema.json` — the input (READ ONLY; changes go through the contract-change rule).
- `spec/PRESETS.md` + `spec/presets/dining_room.preset.json` — slot resolution, ranges, catalog, placement validity.
- `spec/contracts/ENGINE_SEAM.md` — `ISpecChannel`/`IRoomRuntime`, message envelopes, error codes, atomic-apply guarantee.
- `spec/fixtures/diff_vectors.json` — your C# pair gate MUST reproduce every case's `expected` block.
- `spec/fixtures/seam_messages.json` — your seam parsing MUST accept/reject as marked.

## Scope / NOT scope

**Yours:** RoomSpecAdapter (KA JSON → generator build calls) · desktop walk mode · C# pair gate (`PairGate.cs`) · seam implementation (`LocalChannel` + `IRoomRuntime`) · placement validity errors · keeping `RoomGen ▸ Build` green.
**NOT yours:** the operator UI panels ([UNITY_UI.md](UNITY_UI.md)) · task/trial/logging flow ([EXPERIMENT_RUNTIME.md](EXPERIMENT_RUNTIME.md)) · HDRP material/fidelity iteration and VR ([VR_LIVE_EDIT.md](../VR_LIVE_EDIT.md)) · anything Cloudflare.

## The field mapping (KA RoomSpec → existing generator types)

The generator's internal types live in `unity/Assets/RoomGen/Runtime/Contracts/RoomSpec.cs`. Build `RoomSpecAdapter` (pure C#, Newtonsoft — already a project dependency) that parses canonical KA JSON and produces the internal spec. Do NOT edit the internal types to match the schema field-for-field; adapt at the boundary so generator internals stay free to evolve.

| KA RoomSpec (canonical) | Generator internal | Rule |
|---|---|---|
| `room_type` | *(new)* | selects the preset file; v1 ships `dining_room`; unknown → seam error `preset_missing` |
| `seed` | `seed` | pass through (no randomized choices in v1 anyway) |
| `shell.width_m` / `length_m` / `ceiling_height_m` | `geometry.width_m` / `length_m` / `ceiling_height_m` | pass through |
| `shell.contour` | `geometry.corner_radius_m` | `angular` → `0`; `curved` → `CalculateMaxSafeRadius(spec)` (the existing helper that respects openings). Forward-compatible: v1.1 `curviness` maps to `curviness × min(w,l)/2`, clamped by the same helper |
| *(absent)* | `geometry.wall_thickness_m` | constant `0.15` (project convention; never spec-exposed) |
| `surfaces.{wall,floor,ceiling}.material` + `tint_hex` | `surfaces.*_material_id` | material enum → HDRP material via the table below; `tint_hex` multiplies base color; `ceiling.visible:false` → skip ceiling mesh |
| `openings.door{wall,position,width_m}` | `OpeningSpec{kind:door}` | `center_m = (position − 0.5) × usable_wall_length` where `usable_wall_length = wall_length − 2×corner_radius`; `bottom=0`, `top=2.05` (convention) |
| `openings.windows[]{wall,position,width_m,sill_m,head_m}` | `OpeningSpec{kind:window}` | same `center_m` formula; `bottom=sill_m`, `top=head_m` |
| `lighting.preset` | `LightingSpec` | preset table below sets `target_lux` + `color_temperature_k` |
| `lighting.warmth` (0..1) | `color_temperature_k` | **normative law:** `K = 6500 − 3800 × warmth`; overrides the preset's K when present |
| `lighting.intensity` (0..2) | `base_luminous_flux_lm` | multiplier on the preset's base flux |
| `furniture[]{catalog_id, placement}` | `FurniturePlacementSpec` | resolve per `spec/PRESETS.md` (slots → x/z/rotation; footprints from the preset catalog); `catalog_id` map: `dining_table_6→builtin.dining-table`, `dining_chair→builtin.dining-chair`, `sideboard→builtin.sideboard`, `pendant_light→(ceiling light fixture)` |
| `experiment.*` / `provenance.*` | *(not rendered)* | carried for the gate + logging; never affects geometry |

**Material map (v1):** `wood→builtin.oak` (floor) / `builtin.walnut` (furniture accent) · `plaster|paint→builtin.warm-white` · `tile|marble|concrete|brick|carpet→nearest builtin now, real PBR sets in M2 (E2's ladder)` · `glass→builtin.glass`. Unknown → seam error, never a silent fallback (honesty rule).

**Lighting preset table (v1 targets):** `neutral_daylight` 500 lx / 5500 K · `bright_office` 750 lx / 5000 K · `warm_evening` 200 lx / 2900 K · `cool_clinical` 500 lx / 6200 K · `dim` 80 lx / 2700 K. The existing `LightingCalibrator.MatchTargetLux` does the calibration — that's the platform's matched-luminance mechanism; keep it intact.

## Build steps (in order; each is PR-able)

1. **G0 — Boot.** Unity Hub → install **6000.3.16f1** (+ Windows Build Support) → open `unity/` → let packages import → menu `RoomGen ▸ Bootstrap Project` → open `Assets/RoomGen/Scenes/RoomStudio.unity` → Play. Fix anything that errors (expect the bootstrap to need one manual run after first import). *DoD: the dining pair renders in the studio; `RoomGen ▸ Build Windows Application` produces `Builds/Windows/RoomStudio.exe`.*
2. **G1 — Desktop walk mode.** New `DesktopWalkMode` (pattern-match `unity/Assets/RoomGen/Runtime/VR/VrExplorationMode.cs`, which requires an XR loader and is useless without a headset): mouse-look + WASD, eye height 1.65 m, `CharacterController` capsule collision (radius 0.25), Esc exits, cursor locked while walking. Register it as the seam's `walk` camera mode alongside existing orbit/fixed. *DoD: you can walk the dining room without a headset and cannot pass through walls or furniture.*
3. **G2 — RoomSpecAdapter.** Pure C# per the mapping table, in a new `RoomGen.Adapter` namespace, EditMode-testable without a scene. Parse errors and placement-validity failures (`furniture_out_of_bounds`, `furniture_overlap`, `furniture_blocks_door`, `unknown_catalog_id` — rules in PRESETS.md) surface as structured errors. *DoD: EditMode tests load `spec/pairs/ceiling_height_study_01/{control,treatment}.spec.json` (read from the repo — one source of truth) and produce two internal specs differing only in ceiling height.*
4. **G3 — PairGate.cs.** Port `tools/validate_pair.py` semantics: flatten to dotted paths, `experiment`/`provenance` exempt, coverage rule (`path == var` or nested under it), the seven violation codes, coupled-variable notes. *DoD: an EditMode test loads `spec/fixtures/diff_vectors.json` and reproduces every case's `expected` block exactly (ok, sorted codes, diff paths).* Schema validation inside Unity: validate structurally (required fields, enums, ranges) in the adapter; full JSON-Schema conformance stays the Python/Worker gate's job — document this split in code comments.
5. **G4 — Seam.** `IRoomRuntime` over the generator + `LocalChannel` per `ENGINE_SEAM.md`: atomic apply (failed apply keeps last good room), `spec_applied`/`pair_loaded` events with `build_ms` + `spec_sha256`, message-envelope parsing accepted/rejected per `seam_messages.json`, JSONL session logging. *DoD: EditMode test drives `apply_spec` → `spec_applied{ok:true}`; the malformed fixtures produce their named error codes.*
6. **G5 — Tracer-bullet support.** Wire `load_pair` + `switch_condition` (cut + fade) so the runner (E3) and UI (P1) can build on you. *DoD: the [COORDINATION.md](../COORDINATION.md) tracer-bullet checklist rows G1–G5 check off.*

## Environment gotchas

- Unity version is pinned by `unity/ProjectSettings/ProjectVersion.txt` = **6000.3.16f1**; do not upgrade.
- First open takes minutes (HDRP shader import); the bootstrap's XR warning on first run is expected — run `RoomGen ▸ Bootstrap Project` again after import completes.
- `unity/Library/`, `Builds/` are gitignored; commit `Assets/`, `Packages/`, `ProjectSettings/` only (with `.meta` files).
- Windows: paths with spaces (quote everything); Python for the reference validator is plain `python` on Paco's machine, `py` on some others.
- IMGUI studio (`RoomStudioController`) is legacy-but-working: leave it functional until P1's UI Toolkit panel replaces it (M2), then it's deleted in a coordinated PR.

## Your integration role

M1 tracer bullet depends on G1–G5. E3 (runner) consumes your seam + walk mode; P1 (UI) consumes your seam + gate events; P2 (Diego) reviews your PairGate against the fixtures. Escalation rule (TEAM_PLAN.md): two failed attempts at an HDRP/mesh problem → escalate to the high-capability session, don't grind.
