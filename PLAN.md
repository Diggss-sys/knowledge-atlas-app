# Track 3 — Experiment-Room Platform: Plan & Diagram

*The shared picture of what we're building. One page. Read this first; the architecture of record is [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); who does what is [docs/TEAM_PLAN.md](docs/TEAM_PLAN.md); execution lives in [handoffs/](handoffs/).*

## TL;DR

We are building a **native Unity HDRP platform that generates controlled, single-variable 3D rooms, lets an operator edit a room live, runs a behavioral task with a participant walking the room, and collects the data.** Everything flows through one contract, the **`RoomSpec` (a JSON file)**: a room is authored (Unity slider UI), validated (single-variable gate), generated + rendered **in Unity HDRP**, presented to a participant (desktop walkthrough now, native VR next), and their responses logged. **3D-only** — desktop real-time 3D and native VR; no flat 2D images (the 2D-image evidence question is parked with Kirsh — [ARCHITECTURE.md](docs/ARCHITECTURE.md) DL-11).

**What changed 2026-07-02 (v3):** the runtime is a **native HDRP Windows app**, not Unity WebGL in the browser — because a working HDRP generator already exists (adopted into `unity/`), HDRP matches the Max Planck realism precedent Kirsh cites, and the WebGL constraint stack existed only for the browser. Distribution = a double-click `.exe`; the web arm is deferred, not dropped (DL-8, DL-9).

## The end goal (north star — unchanged)

A **subject wears a VR headset inside a generated room**; a **second student edits the room around them live** via a slider UI on the same strong PC; fully **parametric**. Single Unity PCVR app, no networking (the `ISpecChannel` seam makes networked/standalone an add-on later). Details: [docs/VR_LIVE_EDITING.md](docs/VR_LIVE_EDITING.md).

## The platform pipeline (a room becomes data)

```
 ┌────────────┐    ┌────────────┐    ┌────────────┐    ┌────────────┐    ┌────────────┐
 │ 1. AUTHOR  │    │ 2. CONTRACT│    │ 3. VALIDATE│    │ 4. GENERATE│    │ 5. COLLECT │
 │ Unity UI   │──▶ │  RoomSpec  │──▶ │ single-var │──▶ │ + RENDER   │──▶ │ task + log │
 │ sliders    │    │  (JSON)    │    │ gate (C# + │    │ UNITY HDRP │    │ rows → CSV │
 │ (live)     │    │ THE PRODUCT│    │ py + JS)   │    │ desktop/VR │    │ + Worker   │
 └────────────┘    └────────────┘    └─────┬──────┘    └─────┬──────┘    └─────┬──────┘
                                           │                 │                 ▼
                          every gate implementation          │          ┌────────────┐
                          must reproduce                     │          │ Cloudflare │
                          spec/fixtures/diff_vectors.json ───┘          │ D1 + R2    │
                                                                        └────────────┘
   Rooms are stored as RoomSpec JSON (the recipe, not the mesh) — docs/STORAGE.md.
   All in-app parts talk through the ENGINE SEAM (spec/contracts/ENGINE_SEAM.md).
```

## The 5 guardrails (unchanged — why this is a platform, not a demo)

1. **One contract.** The `RoomSpec` schema is the product; Unity is a consumer. *(Frozen v1.0: `spec/room_spec.schema.json`; batched v1.1: [docs/ROOMSPEC_V1_1.md](docs/ROOMSPEC_V1_1.md).)*
2. **Single-variable isolation is enforced, not promised.** The gate diffs control vs treatment and fails on any undeclared change; coupled variables print as notes. Reference implementation: `tools/validate_pair.py`; every port must pass the golden vectors.
3. **Modality is a variable, not a view.** `desktop_3d` / `pcvr` / `standalone_vr` recorded on every row; never pooled.
4. **Determinism.** Seed + pinned assets + engine version; same spec → same room, forever; content-hash ids.
5. **Generic data layer.** Task registry (rating, A-vs-B; extensible) → schema-validated rows → CSV + Cloudflare.

## Roadmap (dated — V1 demo ≈ Aug 1; hard stop end of summer)

| Phase | What | Status |
|---|---|---|
| **0 · Lock the contract** | RoomSpec v1 frozen | ✅ |
| **1 · Prove the pipeline** | Ceiling pair + `validate_pair.py` (11 tests) | ✅ |
| **2 · This package** | HDRP pivot, working generator adopted into `unity/`, contracts + fixtures + handoffs | ✅ (this PR) |
| **M0 · Land + boot** (Jul 2–8) | Team boots Unity; fixture suite green everywhere | ▶ next |
| **M1 · Tracer bullet** (Jul 9–15) | Committed ceiling pair → adapter → walkable desktop rooms → gate-enforced → `.exe` | |
| **M2 · Instrument** (Jul 16–22) | Operator UI + live diff + publish gate; rating task → valid rows; fidelity pass 1 | |
| **M3 · V1 demo** (Jul 23–Aug 1) | Author→validate→publish→run→CSV end-to-end; curved-wall pair; **Kirsh demo** | |
| **M4 · Research-grade** (Aug) | Matched luminance (the calibrated light rig), v1.1 batch, Cloudflare library | |
| **M5 · VR arm** (Aug–Sep 15) | PCVR live-edit via `NetworkChannel`-ready seam; comfort rules | |

## Who owns what

Two trios + an integrator; every role has exactly one handoff file — [docs/TEAM_PLAN.md](docs/TEAM_PLAN.md). Engine: generator (Paco), lighting/fidelity, experiment runtime. Platform: operator UI, contracts/validation (Diego), data/library. Michael integrates, merges, and referees contract changes.

## Storage

Unchanged ([docs/STORAGE.md](docs/STORAGE.md)): local-first `persistentDataPath`, Cloudflare Worker + D1 (specs, studies, responses) + R2 (thumbnails), content-addressed and immutable; never in the live loop.

## The questions for Prof. Kirsh

[docs/PROPOSAL.md](docs/PROPOSAL.md) §8 — effect sizes, primary modality, realism threshold (IPQ), flagship study, the MPI rooms — **plus #6 (new):** given the MPI group's own finding that curvature effects surfaced in 2D images but not free-exploration VR, does he want a controlled-stills arm for effect detection, or strictly 3D?
