# Knowledge Atlas App — Experiment-Room Platform

A tool that lets a researcher (or a student with two weeks) **design, generate, and run controlled room experiments** — and collect the data.

Start with [PLAN.md](PLAN.md). Everything hangs off one contract: the **RoomSpec** ([spec/ROOM_SPEC.md](spec/ROOM_SPEC.md)).

> **Current architecture:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Native Unity **HDRP** desktop app; a working HDRP parametric generator lives in [`unity/`](unity/). **Current execution plan:** [docs/WINDOWS_FIRST_ROADMAP.md](docs/WINDOWS_FIRST_ROADMAP.md) — stabilize the complete Windows experience first, then widen delivery. Older staffing and Mac/VR plans are historical context unless this roadmap says otherwise.

## The restart — what changed, what didn't

This project is a restart of the COGS 160 Track 3 work (`../cogs160track3v2`). **The goal is unchanged; the angle is new.**

**The goal (same as ever):** produce controlled, single-variable room stimuli for environmental-neuroscience experiments — a control room and a treatment room that differ in *exactly one* declared variable (ceiling height, contour angular↔curved, lighting, wall texture) — and run participants through them.

**The angle (v3):** the *platform* is the product, and the **RoomSpec JSON contract** is its keystone. Rooms are generated + rendered **parametrically in Unity HDRP** (native Windows app; the browser arm is deferred — DL-9). The end goal: a subject in VR while another student edits the room live. **3D-only — no flat 2D/images** (the 2D-evidence question is parked with Kirsh, DL-11).

```
AUTHOR (Unity slider UI, live) → RoomSpec (JSON) → VALIDATE (single-var gate) → GENERATE+RENDER (Unity HDRP: desktop walk / native VR) → COLLECT (responses → CSV + Cloudflare)
```

## Layout

| Path | What |
|---|---|
| [PLAN.md](PLAN.md) | The one-page plan + dated roadmap. Read first. |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | **The architecture of record** (v3): schematic, data flow, decision log DL-1..14, risk register. |
| [docs/TEAM_PLAN.md](docs/TEAM_PLAN.md) | 7-person staffing (two trios + integrator) + the human+AI operating model. |
| [docs/WINDOWS_FIRST_ROADMAP.md](docs/WINDOWS_FIRST_ROADMAP.md) | **Current execution plan:** Windows-first milestones, ownership boundaries, and immediate next actions. |
| [handoffs/](handoffs/) | Six self-contained workstream files — each teammate feeds ONE to their AI. |
| [`unity/`](unity/) | The native Unity 6000.3 + HDRP app: parametric generator, studio scene, Windows build. |
| [spec/ROOM_SPEC.md](spec/ROOM_SPEC.md) · [spec/room_spec.schema.json](spec/room_spec.schema.json) | The RoomSpec contract (frozen v1). |
| [spec/PRESETS.md](spec/PRESETS.md) · [spec/presets/](spec/presets/) | The preset envelope contract + room-type presets. |
| [spec/study.schema.json](spec/study.schema.json) · [spec/response_log.schema.json](spec/response_log.schema.json) · [spec/RESPONSE_LOG.md](spec/RESPONSE_LOG.md) | Study + data-row contracts. |
| [spec/contracts/](spec/contracts/) | ENGINE_SEAM (ISpecChannel) · ROOM_API (Worker REST) · AI_AUTHORING (NL copilot) · schema.sql (D1). |
| [spec/fixtures/](spec/fixtures/) | Golden fixtures: `diff_vectors.json` (generated from the reference validator), seam messages, response rows. |
| [spec/pairs/](spec/pairs/) · [spec/examples/](spec/examples/) | Committed control/treatment pairs + example specs. |
| [tools/validate_pair.py](tools/validate_pair.py) | **The single-variable gate** — the reference implementation every port must match. |
| [docs/PROPOSAL.md](docs/PROPOSAL.md) · [docs/RENDERING_RESEARCH.md](docs/RENDERING_RESEARCH.md) · [docs/VR_LIVE_EDITING.md](docs/VR_LIVE_EDITING.md) · [docs/ROOMSPEC_V1_1.md](docs/ROOMSPEC_V1_1.md) · [docs/CURVED_WALLS_SUBPLAN.md](docs/CURVED_WALLS_SUBPLAN.md) · [docs/ASSET_SOURCING.md](docs/ASSET_SOURCING.md) · [docs/STORAGE.md](docs/STORAGE.md) | Science, rendering physics, VR end-goal, v1.1 batch, assets, storage. |
| [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md) · [docs/TECH_FEASIBILITY.md](docs/TECH_FEASIBILITY.md) | **History / design source** (the WebGL-era plan superseded by v3). |
| [docs/reference/KIRSH_MEETING_NOTES.md](docs/reference/KIRSH_MEETING_NOTES.md) | Distilled requirements from the June 2026 Kirsh meeting. |
| [docs/LEGACY_PROJECT.md](docs/LEGACY_PROJECT.md) | Salvage map of the old repo. |

## Status

- Phase 0 (lock the contract): ✅ `spec/room_spec.schema.json` v1 frozen.
- Phase 1 (prove the pipeline): ✅ ceiling pair + `validate_pair.py` (the gate), tests green.
- Phase 2 (this package, 2026-07-02): ✅ HDRP pivot decided + working generator adopted into `unity/` + contracts/fixtures/handoffs committed.
- **Now: Windows-first execution.** Follow [docs/WINDOWS_FIRST_ROADMAP.md](docs/WINDOWS_FIRST_ROADMAP.md) for current priorities and [team_hub/](team_hub/) for the team-facing status. Older dated roadmaps remain as history.

## Dev setup

```powershell
# Python (contracts + gate) — from the repo root:
python -m pip install -r requirements.txt
python -m pytest tests -q
python tools/validate_pair.py spec/pairs/ceiling_height_study_01/control.spec.json spec/pairs/ceiling_height_study_01/treatment.spec.json

# Unity (Engine trio): Unity Hub → install 6000.3.16f1 (+ Windows Build Support) →
# open unity/ → wait for packages → menu "RoomGen ▸ Bootstrap Project" → open
# Assets/RoomGen/Scenes/RoomStudio.unity → Play.  Build: "RoomGen ▸ Build Windows Application".
```

*(Some machines use `py` instead of `python` — whichever launcher works.)*
