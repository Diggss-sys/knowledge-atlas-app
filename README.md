# Knowledge Atlas App — Experiment-Room Platform

A tool that lets a researcher (or a student with two weeks) **design, generate, and run controlled room experiments** — and collect the data.

Start with [PLAN.md](PLAN.md). Everything hangs off one contract: the **RoomSpec** ([spec/ROOM_SPEC.md](spec/ROOM_SPEC.md)).

## The restart — what changed, what didn't

This project is a restart of the COGS 160 Track 3 work (`../cogs160track3v2`). **The goal is unchanged; the angle is new.**

**The goal (same as ever):** produce controlled, single-variable room stimuli for environmental-neuroscience experiments — a control room and a treatment room that differ in *exactly one* declared variable (ceiling height, contour angular↔curved, lighting, wall texture) — and run participants through them.

**The old angle (v2 repo):** the *generator* was the product. Infinigen/Blender procedural generation, baked renders, a web viewer/wizard, Cloudflare hosting. The science lived at the edge; the rendering pipeline was the center.

**The new angle (this repo):** the *platform* is the product, and the **RoomSpec JSON contract** is its keystone. **Rooms are generated + rendered in Unity, parametrically. The end goal: a subject in VR while another student edits the room live. 3D-only — no flat 2D/images.**

```
AUTHOR (Unity slider UI, live) → RoomSpec (JSON) → VALIDATE (single-var isolation) → GENERATE+RENDER (Unity: web-3D / native VR) → COLLECT (responses → Cloudflare + CSV/JSON)
```

- **Unity is the renderer/generator**, driven entirely by the RoomSpec (`RoomBuilder.BuildFromSpec`). Swap render target (web-3D ↔ VR) without touching the contract. *(The old A-Frame web viewer is dropped.)*
- The validation gate **is** the science: it refuses any control/treatment pair that differs in more than the declared variable.
- The data-collection half (tasks, response logging, export) is what makes this a tool to run experiments, not just a room generator.

> **⚠ Current architecture lives in [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md) and [docs/VR_LIVE_EDITING.md](docs/VR_LIVE_EDITING.md).** Anything below describing "web-first / images / Unity as optional" is superseded — the current plan is Unity-generated 3D + live VR editing.

## Layout

| Path | What |
|---|---|
| [PLAN.md](PLAN.md) | The one-page plan + roadmap. Read first. |
| [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md) | **The current, living detailed plan** (architecture v2, Unity + VR). |
| [docs/VR_LIVE_EDITING.md](docs/VR_LIVE_EDITING.md) | The VR end goal: live operator editing, PCVR/standalone, comfort, perf. |
| [docs/RENDERING_RESEARCH.md](docs/RENDERING_RESEARCH.md) | Unity realism, curved/bowed walls, light physics. |
| [docs/PROPOSAL.md](docs/PROPOSAL.md) | Cited professor proposal (science, tasks, power, modality). |
| [docs/TECH_FEASIBILITY.md](docs/TECH_FEASIBILITY.md) | Pipeline stress-test + risk register. |
| [docs/ROOMSPEC_V1_1.md](docs/ROOMSPEC_V1_1.md) | Proposed v1.1 schema batch (lighting split, curviness/bow, furniture identity). |
| [docs/ASSET_SOURCING.md](docs/ASSET_SOURCING.md) | Pinned CC0 textures/HDRIs/furniture. |
| [docs/STORAGE.md](docs/STORAGE.md) | How rooms persist — spec-not-mesh, local + Cloudflare library. |
| [spec/ROOM_SPEC.md](spec/ROOM_SPEC.md) | The RoomSpec contract, explained. |
| [spec/room_spec.schema.json](spec/room_spec.schema.json) | The schema (frozen v1). |
| [spec/presets/](spec/presets/) | Room-type envelopes (defaults, ranges, catalog, layout slots). |
| [spec/examples/](spec/examples/) | Concrete filled specs (one room each). |
| [spec/pairs/](spec/pairs/) | Control/treatment pairs — one folder per study, validated by the gate. |
| [tools/validate_pair.py](tools/validate_pair.py) | The diff-validator: refuses any pair that differs beyond the declared variable. |
| [docs/CURVED_WALLS_SUBPLAN.md](docs/CURVED_WALLS_SUBPLAN.md) | The contour (angular↔curved) geometry sub-plan. |
| [docs/reference/KIRSH_MEETING_NOTES.md](docs/reference/KIRSH_MEETING_NOTES.md) | Distilled requirements from the June 2026 meeting with Prof. Kirsh. |
| [docs/LEGACY_PROJECT.md](docs/LEGACY_PROJECT.md) | What the old repo has and what's worth pulling over. |

## Status

- Phase 0 (lock the contract): schema drafted ✅ — `spec/room_spec.schema.json` + dining-room preset + example spec.
- Phase 1 (prove the pipeline): ✅ — `spec/pairs/ceiling_height_study_01/` (3.2 m control vs 2.6 m treatment) + `tools/validate_pair.py`, the gate that enforces single-variable isolation. `py -m pytest tests` to run the suite.
- Phase 2 (current, planning): **Unity parametric room generator + live Unity slider editor + native VR live-editing**, plus the Cloudflare room library and data layer. Detailed living plan in [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md). VR is the end goal, not optional.

## Dev setup

```
py -m pip install -r requirements.txt
py -m pytest tests -q
py tools\validate_pair.py spec\pairs\ceiling_height_study_01\control.spec.json spec\pairs\ceiling_height_study_01\treatment.spec.json
```
