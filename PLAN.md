# Track 3 — Experiment-Room Platform: Plan & Diagram

*The shared picture of what we're building. One page. Read this first, then [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md) for the live, detailed plan.*

## TL;DR

We are building a **platform that generates controlled, single-variable 3D rooms in Unity, lets one person edit a room live while another experiences it in VR, runs a behavioral task, and collects the data.** Everything flows through one contract, the **`RoomSpec` (a JSON file)**: a room is authored once (Unity slider UI), validated once (single-variable gate), generated + rendered **in Unity**, presented to a participant, and their responses logged. **The project is 3D-only** — interactive web-3D (non-immersive participants) and **native VR** (the immersive end goal). No flat 2D/images.

## The end goal (north star)

A **subject wears a VR headset inside a generated room**; a **second student edits the room around them live** via a Unity slider UI on a PC (ceiling height, curvature, wall bow, lighting, furniture); fully **parametric**. Best case = a **single Unity PCVR app** (tethered headset + operator at the same PC's monitor, no networking); standalone/two-machine adds a network transport behind the same seam. Details + feasibility: [docs/VR_LIVE_EDITING.md](docs/VR_LIVE_EDITING.md).

## The platform pipeline (a room becomes data)

```
 ┌────────────┐    ┌────────────┐    ┌────────────┐    ┌────────────┐    ┌────────────┐
 │ 1. AUTHOR  │    │ 2. CONTRACT│    │ 3. VALIDATE│    │ 4. GENERATE│    │ 5. COLLECT │
 │ Unity UI   │──▶ │  RoomSpec  │──▶ │ single-var │──▶ │ + RENDER   │──▶ │ task + log │
 │ sliders    │    │  (JSON)    │    │ isolation +│    │ in UNITY   │    │ responses, │
 │ (live)     │    │ THE PRODUCT│    │ determinism│    │ web-3D / VR│    │ RT, paths  │
 └────────────┘    └────────────┘    └────────────┘    └─────┬──────┘    └─────┬──────┘
                                                             │                 │
                                       operator edits LIVE ──┘                 ▼
                                       (slider → rebuild)              ┌────────────┐
                                                                       │ DATA export│
                                                                       │ → Cloudflare│
                                                                       │   + CSV/JSON│
                                                                       └────────────┘

   Rendering is done in Unity (parametric RoomBuilder). VR = native OpenXR (the end goal). Web-3D = non-immersive arm.
   Rooms are stored as RoomSpec JSON (the recipe, not the mesh) — see Storage below.
```

### How to read it
1. A student **authors a room** in a **Unity slider UI** — and can edit it **live** while a subject is in it.
2. Everything becomes a **`RoomSpec`** — the one JSON contract. (An AI authoring path is an optional convenience for later, never the only way.)
3. The **validation gate is the science**: it refuses any control/treatment pair that differs in more than the one declared variable.
4. The room is **generated + rendered in Unity** and shown to the participant (interactive web-3D, or native VR for the immersive arm). **3D-only.**
5. The tool **collects responses** and exports the data (to Cloudflare, with CSV/JSON).

## The 5 guardrails (why this is a platform, not a demo)

1. **One contract.** The `RoomSpec` schema is the product. Unity is the renderer/generator, driven entirely by the spec — swap the render target (web-3D ↔ VR) without touching the contract. *(Frozen: `spec/room_spec.schema.json`; v1.1 batch in [docs/ROOMSPEC_V1_1.md](docs/ROOMSPEC_V1_1.md).)*
2. **Single-variable isolation is enforced, not promised.** A validator diffs control vs treatment and fails if anything beyond the declared variable changed — plus documented *coupled* variables (raising a ceiling also changes apparent volume, window ratio, floor illuminance…).
3. **Modality is a variable, not a view.** Non-immersive web-3D and immersive VR are different experiments — record the modality, never pool the data. *(3D-only — no 2D/images in either arm.)*
4. **Determinism.** Seed + store randomness; pin asset-pack + engine version in `provenance`. Same spec → same room, forever. (This is why we store the spec, not the baked mesh.)
5. **Generic data layer.** A few configurable task types (rate / A-vs-B choice / timed task / exploration log) + export to Cloudflare and CSV/JSON.

## Roadmap (small steps, in order)

| Phase | What | Status |
|---|---|---|
| **0 · Lock the contract** | Freeze `RoomSpec` v1. | ✅ |
| **1 · Prove the pipeline** | One control/treatment pair + the diff-validator. | ✅ |
| **2 · Unity generator + live editor + VR** | Parametric `RoomBuilder.BuildFromSpec`, Unity slider UI, live editing, single-app PCVR. The current core — see [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md). | in progress (planning) |
| **3 · Research-grade** | Nuisance-variance control (matched luminance, fixed camera, pinned assets) + RoomSpec v1.1 (lighting split, curviness/bow, furniture identity) + Cloudflare room library. | |
| **4 · Scale** | More presets · parameter sweeps · standalone-VR network transport · optional AI authoring. | |

## Who owns what

| Team | The strong Windows PC |
|---|---|
| Schema · Unity UI · validator · data layer · Cloudflare worker · analysis | **Unity render + PCVR runtime** (the heavy GPU work; subject's headset tethers here) |

*Real-time 3D/VR rendering needs the strong PC; authoring/validation/data run anywhere.*

## Storage

A "room" = a **RoomSpec JSON** (few KB), not a 3D file — Unity regenerates it via `BuildFromSpec`. Store the recipe: **hybrid local-first (Unity `persistentDataPath`) + Cloudflare library** (Worker + D1 for specs, R2 for optional thumbnails), content-addressed (sha256) + immutable for reproducibility. See [docs/PHASE2_PLAN.md](docs/PHASE2_PLAN.md) §contracts (`ROOM_API.md`, `schema.sql`).

## The question for Prof. Kirsh

Full cited proposal: [docs/PROPOSAL.md](docs/PROPOSAL.md). Open questions: response/task types; whether web-3D is an acceptable primary outcome for cognitive tasks (VR for immersion); realism threshold; flagship study; the Max Planck VR rooms.

## Appendix — how the Unity generator works (the renderer)

```
  RoomSpec  ──►  RoomBuilder.BuildFromSpec(spec)  (one script, rebuilds live)  ──►  a room =
                  1  clear previous room                                             a tree of
                  2  FLOOR + WALLS (parametric mesh; openings as gaps)               GameObjects
                  3  CONTOUR: curviness (rounded corners) + per-wall bow (concave/convex)   │
                  4  MATERIALS: pinned CC0 PBR sets, world-scale tiling               ▼
                  5  FURNITURE: catalog models at preset slots                   Unity renders →
                  6  LIGHTING: sun + sky + window fill + runtime bounce probe     headset / monitor
```

**Core idea:** a room is **not one model** — it's assembled from a *kit of parts* by code, driven by the RoomSpec. Change a slider → `BuildFromSpec` re-runs → room re-assembles instantly. That live, per-parameter control is exactly what Infinigen could not do, and is why this is the right shape for controlled, live-editable stimuli. Rendering details + light physics: [docs/RENDERING_RESEARCH.md](docs/RENDERING_RESEARCH.md).
