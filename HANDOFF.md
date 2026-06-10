# HANDOFF — Knowledge Atlas App (master context for the next session)

*Written 2026-06-10 at the end of the restart session. Everything below is verified state, not aspiration. If you are an assistant reading this: start by running `git status` and the test suite (commands in §6), then pick up at §9.*

---

## 1. What this project is

A **tool/platform for controlled room experiments** in environmental neuroscience (UCSD COGS 160, Track 3, Prof. David Kirsh). A researcher — or an undergrad with two weeks — designs a control/treatment room pair that differs in **exactly one variable** (ceiling height, angular↔curved contour, lighting, wall texture), shows the rooms to participants, runs a behavioral task, and exports the response data.

Everything flows through one contract: the **RoomSpec** — an engine-agnostic JSON description of one room. Producers (web wizard, AI agent) and consumers (web viewer, Unity, future VR) only agree on the schema; nothing talks to anything else directly.

```
AUTHOR (form/AI) → RoomSpec JSON → VALIDATE (single-var gate) → PRESENT (web-first) → COLLECT (task + responses → CSV)
```

## 2. Where everything lives

| Thing | Location |
|---|---|
| **This project** | `C:\Users\jimen\OneDrive\Documents\Claude\Projects\knowledge atlas\knowledge atlas app` |
| **GitHub remote** | `https://github.com/Diggss-sys/knowledge-atlas-app` — **private**, owner `Diggss-sys` (the user, Diego; local git identity `diggsss` / jimenezdiego423@gmail.com) |
| **Branches** | `main` (root commit `82bcaae`, the restart skeleton) and `Diggss-sys-branch` (`475ce7a`, **Diego's workspace — work here**, tracks origin) |
| **Teammate** | Paco Yan — GitHub `payan-cell`, payan@ucsd.edu. Collaborator invite (push access) sent 2026-06-10, may still be pending. He authored `validator.py` in the old repo — keep his conventions (see §8). |
| **Old project (reference only)** | Sibling folder `..\cogs160track3v2` — the shared class repo (`1michaelbongiorno/cogs160track3v2`), branch `Diggss-sys-branch`. **Do not touch:** it has uncommitted changes (modified `viewer/index.html` +135/−5, untracked `cloudflare/v2/delete_hero.sh` and `spec/`). Its untracked `spec/` is a byte-identical duplicate of this repo's `spec/` and can eventually be deleted, but only with the user's say-so. |

## 3. The restart story (angle changed, goal didn't)

- **Goal (unchanged):** controlled single-variable room stimuli + the experiments around them.
- **Old angle (cogs160track3v2):** the *generator* was the product — Infinigen/Blender procedural pipeline, baked renders, web viewer/wizard, Cloudflare hosting. Retired because Infinigen's doors/windows were unfixably bad, parameters were unreachable without tearing it apart, and server compression wrecked textures.
- **New angle (this repo):** the *platform* is the product and the **RoomSpec contract is the keystone**. Renderers are swappable consumers. The validation gate **is** the science. The data-collection half is what makes it a tool, not a demo.

The five guardrails (from [PLAN.md](PLAN.md)): one contract · single-variable isolation **enforced** · modality is a variable, never pool image/VR data · determinism (seed + pinned assets) · generic data layer.

## 4. Requirements from Prof. Kirsh (June 2026 meeting)

Full notes: [docs/reference/KIRSH_MEETING_NOTES.md](docs/reference/KIRSH_MEETING_NOTES.md). The load-bearing points:

- Students get **two weeks** to build stimuli; everything starts from control vs. treatment; he'll curate ~25–50 candidate experiment ideas.
- Manipulation difficulty, his ranking: ceiling height (easiest) → windows/lighting → wall texture → curved wall (hard; see [docs/CURVED_WALLS_SUBPLAN.md](docs/CURVED_WALLS_SUBPLAN.md)).
- Experiment types are behavioral: concentration tasks in-room (proofreading), memory tests administered *after/outside* the room, navigation/pointing (angular error as measure), adaptive preference (A-vs-B, ~20 stimuli in ~8 smart comparisons).
- **Realism bar:** "realistic enough that it gives you a reliable experience of being in the room" — low-poly/GitHub-Pages-VR fidelity explicitly fails.
- **Engine-agnostic:** he doesn't care about Unity vs anything, only that a non-engine user can say "make the ceiling high." The front end is the product.
- His recurring correction: too many simultaneous changes = not an experiment. Single-variable discipline above all.

## 5. Current state — what is DONE and verified

**Phase 0 — contract locked ✅**
- `spec/room_spec.schema.json` — RoomSpec v1 (JSON Schema 2020-12). Covers shell (width/length/ceiling/contour), surfaces (material+tint per wall/floor/ceiling), openings (door + windows as wall gaps), lighting (5 presets + warmth/intensity/hdri overrides), furniture (catalog_id + slot or x/z placement), experiment (condition/manipulated_variables/pair_id), provenance (generated_by + execution_path honesty labels `live|cached|regen|preview_only`).
- `spec/presets/dining_room.preset.json` — the envelope: defaults, ranges, furniture catalog with footprints, named layout slots. The AI/student only picks points inside the envelope; spatial logic stays in code.
- `spec/examples/dining_room.spec.json` — validated example.
- Docs: `spec/ROOM_SPEC.md` (contract explainer), `PLAN.md` (the one-pager + roadmap), `README.md`.

**Phase 1 — pipeline proven ✅** (commit `475ce7a` on `Diggss-sys-branch`)
- `spec/pairs/ceiling_height_study_01/control.spec.json` (3.2 m) + `treatment.spec.json` (2.6 m) — byte-identical except `shell.ceiling_height_m`, for the proofreading/concentration study.
- `tools/validate_pair.py` — **the diff-validator / single-variable isolation gate.** Schema-validates both specs; checks pair coherence (shared `pair_id`, exactly one control + one treatment, identical `manipulated_variables`); flattens both specs to dotted paths and rejects on any undeclared stimulus difference AND on any declared-but-unchanged variable. `experiment.*` and `provenance.*` are exempt from the stimulus diff. Coupled variables (e.g. ceiling height → volume, window-to-wall ratio) print as notes, not violations. Violation codes: `schema_invalid, missing_experiment, pair_id_mismatch, condition_invalid, declared_mismatch, undeclared_change, declared_unchanged`. Exit 0 pass / 1 violations / 2 IO error. Usable as library: `from validate_pair import validate_pair`.
- `tests/test_validate_pair.py` — 11 tests, **all passing** as of handoff. `conftest.py` puts `tools/` on `sys.path`.
- `requirements.txt` — jsonschema, pytest.

**Not yet started:** everything in §9.

## 6. Environment — commands and gotchas (Windows 11)

```powershell
# from the repo root:
py -m pip install -r requirements.txt
py -m pytest tests -q                      # expect: 11 passed
py tools\validate_pair.py spec\pairs\ceiling_height_study_01\control.spec.json spec\pairs\ceiling_height_study_01\treatment.spec.json
# expect: PASS — pair differs only in: shell.ceiling_height_m  (+ a coupled-variable note)
```

- **Python = `py` launcher** (3.14 at `...\Programs\Python\Python314`). Bare `python`/`pip` are broken (MS Store stub / not on PATH in bash). Always `py -m pip`.
- **`gh` CLI is NOT installed** (winget is available). GitHub operations were done via the REST API using the credential Git Credential Manager already stores for github.com (retrievable non-interactively with `git credential fill`; the account is `Diggss-sys`, token scoped for repo operations). Repo creation and the collaborator invite were both done this way and worked.
- Paths contain spaces (OneDrive) — quote everything.
- `pdftoppm` unavailable; `pypdf` is installed if PDFs need extracting.
- Line endings: files were written LF; Windows git warns it will commit CRLF translations — harmless, ignore.

## 7. Repo layout

```
knowledge atlas app/
├── HANDOFF.md                  ← this file
├── README.md                   ← project framing + status + dev setup
├── PLAN.md                     ← the one-page plan & roadmap (read first)
├── requirements.txt  conftest.py  .gitignore
├── spec/
│   ├── ROOM_SPEC.md            ← contract explainer
│   ├── room_spec.schema.json   ← RoomSpec v1 (frozen)
│   ├── presets/dining_room.preset.json
│   ├── examples/dining_room.spec.json
│   └── pairs/ceiling_height_study_01/{control,treatment}.spec.json
├── tools/validate_pair.py      ← the single-variable gate
├── tests/test_validate_pair.py
└── docs/
    ├── CURVED_WALLS_SUBPLAN.md ← contour manipulation plan (phase 2/3 geometry)
    ├── LEGACY_PROJECT.md       ← salvage map of the old repo
    └── reference/
        ├── KIRSH_MEETING_NOTES.md
        └── kirsh_meeting_transcript.pdf
```

## 8. Conventions to keep

- **Validator style (Paco's, from old `validator.py`):** structured violation objects with short `code` strings, CLI exit codes 0/1/2, dual CLI+library use.
- **Honesty labels:** `provenance.execution_path` ∈ `live | cached | regen | preview_only` — label how a room was actually realized, always.
- **Single-variable discipline:** any new pair goes through `validate_pair.py` before it's considered real. Never hand-wave it.
- **Pair file convention:** `spec/pairs/<pair_id>/control.spec.json` + `treatment.spec.json`, folder name == `experiment.pair_id`.
- Commit messages explain the *why*; commits to date end with `Co-Authored-By: Claude <model> <noreply@anthropic.com>`.

## 9. What's NEXT (Phase 2 — "the data half", per PLAN.md)

> **⚠ SUPERSEDED 2026-06-10 — read [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md) instead.** A grill session replanned Phase 2: Unity WebGL is the renderer/display (the old web-viewer route is dropped), Cloudflare Worker + R2 + D1 is the room library and response store, and the next deliverable is an AI-readable workstream handoff package, not code. The sketch below is kept only as history. PHASE2_PLAN.md is a living doc — iterated and pushed in rounds before execution.

A minimal **experiment runner**: show the two rooms of a pair → run one task type → log responses → export CSV/JSON. This is the half that makes it a tool to run experiments, not a room generator. Suggested first slice:

1. Decide presentation v1: static images per room (cheapest honest option; the old repo's `viewer/` realism work is the eventual upgrade path) — remember **modality is recorded as a variable**.
2. A simple web page (plain HTML/JS, hostable anywhere): loads a pair, shows rooms in randomized order, runs **one task** (start with rating, then A-vs-B choice), captures responses + reaction time + modality + spec provenance.
3. Export: flat CSV one-row-per-response with pair_id, condition, manipulated variable, task type, response, RT, timestamp, participant id.
4. Tests around the data layer (schema of the log rows).

After that (Phase 3+): nuisance-variance control (matched luminance, fixed camera), determinism hooks, more presets (`bedroom`, `classroom` — mirror the dining preset), the curved-walls geometry, web authoring form (salvage old `wizard/` — note: in the old repo, rendering auto-synced viewer→wizard but new UI controls had to be hand-ported), and the optional AI authoring path (NL brief + preset → structured output → schema-validate → retry; API key server-side only).

**Open question pending with Kirsh** (from PLAN.md): what experiment/response types must the tool support, and is 2D/web a valid primary outcome or is VR core? Scopes how generic the data layer must be.

## 10. Old-repo salvage map (short version — full version in docs/LEGACY_PROJECT.md)

- `validation_gate.py` / `validator.py` / tests → already mined for conventions in Phase 1.
- `wizard/` → Phase 4 authoring form. `viewer/` (rotating sun, time-of-day mood, shadow contrast work) → the realism upgrade for "Present".
- `server.py`, `cloudflare/` → hosting patterns. `presets/` (kitchen, living_room) → source material for new presets.
- Left behind on purpose: the whole Infinigen/Blender bake pipeline.
