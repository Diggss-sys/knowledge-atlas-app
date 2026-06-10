# Knowledge Atlas App — Experiment-Room Platform

A tool that lets a researcher (or a student with two weeks) **design, generate, and run controlled room experiments** — and collect the data.

Start with [PLAN.md](PLAN.md). Everything hangs off one contract: the **RoomSpec** ([spec/ROOM_SPEC.md](spec/ROOM_SPEC.md)).

## The restart — what changed, what didn't

This project is a restart of the COGS 160 Track 3 work (`../cogs160track3v2`). **The goal is unchanged; the angle is new.**

**The goal (same as ever):** produce controlled, single-variable room stimuli for environmental-neuroscience experiments — a control room and a treatment room that differ in *exactly one* declared variable (ceiling height, contour angular↔curved, lighting, wall texture) — and run participants through them.

**The old angle (v2 repo):** the *generator* was the product. Infinigen/Blender procedural generation, baked renders, a web viewer/wizard, Cloudflare hosting. The science lived at the edge; the rendering pipeline was the center.

**The new angle (this repo):** the *platform* is the product, and the **RoomSpec JSON contract** is its keystone.

```
AUTHOR (web form / AI) → RoomSpec (JSON) → VALIDATE (single-var isolation) → PRESENT (web-first) → COLLECT (responses → CSV)
```

- Renderers are swappable consumers of the spec — web viewer first, Unity as an optional offline renderer, VR later. No renderer is load-bearing.
- The validation gate **is** the science: it refuses any control/treatment pair that differs in more than the declared variable.
- The data-collection half (tasks, response logging, export) is what makes this a tool to run experiments, not just a room generator.

## Layout

| Path | What |
|---|---|
| [PLAN.md](PLAN.md) | The one-page plan + roadmap. Read first. |
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
- Next (Phase 2): the data half — minimal experiment runner (show rooms → task → log responses → export CSV).

## Dev setup

```
py -m pip install -r requirements.txt
py -m pytest tests -q
py tools\validate_pair.py spec\pairs\ceiling_height_study_01\control.spec.json spec\pairs\ceiling_height_study_01\treatment.spec.json
```
