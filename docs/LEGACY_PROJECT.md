# Legacy project map — what to salvage from `cogs160track3v2`

The previous repo is the shared class repo [`1michaelbongiorno/cogs160track3v2`](https://github.com/1michaelbongiorno/cogs160track3v2) (branch work on `Diggss-sys-branch`). It stays untouched as reference. This is the map of what's there and what's worth pulling into the new platform when each phase needs it.

## Directly reusable (pull when the phase calls for it)

| Old asset | Where it fits in the new plan |
|---|---|
| `validation_gate.py`, `validator.py`, `test_validation_gate.py` | **Phase 1 — diff-validator.** Closest existing code to the single-variable isolation gate. Rework to diff two RoomSpecs against `experiment.manipulated_variables`. |
| `wizard/` | **Reference only** for RoomSpec field shape. The live editor is a **Unity slider UI** (UI Toolkit), NOT a web form — do not port the wizard as the editor. |
| `viewer/` (lighting/realism work: rotating sun, time-of-day, GI/irradiance probe, shadow contrast) | **Prior art for Unity lighting**, not a renderer. The captured-irradiance-probe GI trick is reimplemented in Unity ([docs/RENDERING_RESEARCH.md](RENDERING_RESEARCH.md) §3); the A-Frame viewer itself is dropped. |
| `spec/` (untracked in old repo) | Already migrated here — it's byte-identical to this repo's `spec/`. The old copy can be deleted eventually. |
| `presets/` (dining_room, kitchen, living_room) | Source material for additional room-type presets (Phase 4). |
| `server.py`, `cloudflare/` | Cloudflare Worker + R2 + D1 patterns for the **room library + response sink** (storage only — never the live render loop; see [docs/STORAGE.md](STORAGE.md)). |
| `spec_v2/` (gap analysis, contracts, verification tests, e2e plan) | Process documents — mine for test ideas and contract language. |

## Background / context (read, don't port)

- `PIPELINE.md`, `RENDER_PIPELINE.md`, `DEVELOPMENT_LOG.md`, `PROGRESS.md` — how the old pipeline worked and why it grew complicated.
- `hssd_divergence.md`, `coverage_survey.md`, `parameter_source_mapping.md` — the Infinigen-era parameter research.
- `ATTRIBUTE_CONTRACT.md` — the old contract attempt; superseded by RoomSpec but useful for naming.

## Deliberately left behind

- The Infinigen/Blender bake pipeline (`bake_variant.py`, `bpy_lighting_inject.py`, `infinigen_wrapper.py`, manifests, baked galleries) — retired per the June meeting: unfixable doors/windows, compression artifacts, parameters unreachable. The new plan treats any generator as a swappable consumer of RoomSpec.
- The sprint/handoff docs (SPRINT*, OVERNIGHT_BRIEF, SYNC_*) — process scaffolding for the old codebase.

## One convention worth keeping

The old repo's **honesty labels** (`live` / `cached` / `regen` / `preview_only`) survive in the new schema as `provenance.execution_path`. Keep using them.
