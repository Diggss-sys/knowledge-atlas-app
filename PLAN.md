# Track 3 — Experiment-Room Platform: Plan & Diagram

*The shared picture of what we're building. One page. Read this first.*

## TL;DR

We are building a **tool/platform** that lets a researcher design, generate, and run **controlled room experiments** — and collect the data. The experiment itself is *not* fixed yet (likely behavioral), so **flexibility is the product.** Everything flows through one contract, the **`RoomSpec` (a JSON file)**: a room is authored once, validated once, shown to participants, and their responses are logged. Delivery is **web-first** (runs on any device, send-a-link); Unity is an optional renderer + the future VR mode.

---

## The platform pipeline (a room becomes data)

```
 ┌────────────┐    ┌────────────┐    ┌────────────┐    ┌────────────┐    ┌────────────┐
 │ 1. AUTHOR  │    │ 2. CONTRACT│    │ 3. VALIDATE│    │ 4. PRESENT │    │ 5. COLLECT │
 │ web form   │──▶ │  RoomSpec  │──▶ │ single-var │──▶ │ rooms as   │──▶ │ task + log │
 │ (+AI later)│    │  (JSON)    │    │ isolation +│    │ images /   │    │ responses, │
 │ researcher │    │ THE PRODUCT│    │ determinism│    │ web-3D     │    │ RT, paths  │
 └────────────┘    └────────────┘    └────────────┘    └─────┬──────┘    └─────┬──────┘
                                                             │                 │
                                          participants get ──┘                 ▼
                                          a link                         ┌────────────┐
                                                                         │ DATA export│
                                                                         │ CSV/JSON → │
                                                                         │ researcher │
                                                                         └────────────┘

   Unity's role = an OPTIONAL offline renderer for step 4, and the FUTURE VR delivery mode (deferred).
```

### How to read it
1. A researcher **authors a room** in a web form (any laptop). The AI agent is an optional convenience we add later — never the only way to make a room.
2. Everything becomes a **`RoomSpec`** — the one JSON contract. Human-made and AI-made specs are identical files.
3. The **validation gate is the science**: it refuses any control/treatment pair that differs in more than the one declared variable.
4. The room is **shown to participants** (images first; interactive web-3D optional; VR later).
5. The tool **collects their responses** and exports the data. *This is the half that makes it a tool to run experiments, not just a room generator.*

---

## The 5 guardrails (why this is a platform, not a demo)

1. **One contract.** The `RoomSpec` schema is the product. Engines/renderers are swappable; the contract stays. *(Drafted + validated: `spec/room_spec.schema.json`.)*
2. **Single-variable isolation is enforced, not promised.** A validator diffs control vs treatment and fails if anything beyond the declared variable changed — plus a documented list of *coupled* variables (raising a ceiling also changes apparent volume, window ratio…).
3. **Modality is a variable, not a view.** Images and VR are different experiments — record the modality, never pool the data.
4. **Determinism.** Seed + store randomness; pin asset-pack + engine version in `provenance`. Same spec → same room, forever.
5. **Generic data layer.** Because the experiment is unknown, the response-collection stays configurable: a few task types (rate / A-vs-B choice / timed task / free-exploration log) + a plain CSV/JSON export.

---

## Roadmap (small steps, in order)

| Phase | When | What | Status |
|---|---|---|---|
| **0 · Lock the contract** | now (finals) | Freeze `RoomSpec` v1. Ask Kirsh the scoping question (below). | schema ✅ |
| **1 · Prove the pipeline** | post-finals, ~1 day | One **control/treatment room pair** differing in exactly one field + the **diff-validator**. | next |
| **2 · Add the data half** | the new core | Minimal **experiment runner**: show rooms → one task (e.g. rating) → log responses → export CSV. | |
| **3 · Make it research-grade** | the real work | Nuisance-variance control (matched luminance, fixed assets, one camera) + determinism hooks + coupled-var docs. | |
| **4 · Scale** | summer | More presets · parameter-sweep tool · web authoring form. | |
| **5 · VR + AI (only if needed)** | later / optional | Unity → VR/EEG arm; AI agent as an optional authoring front-door. | |

---

## Who owns what

| Team (MacBooks) | The one strong Windows PC |
|---|---|
| Schema · web form · experiment runner · diff-validator · data + analysis | Offline image renderer (and, later, the VR build) |

*Matches the hardware: real-time/VR rendering needs the Windows PC; everything else runs anywhere.*

---

## The open question for Prof. Kirsh

**What kinds of experiments should this tool support, and what responses should it capture** (ratings? choices? timing? exploration paths?)? And: **do you expect the target effects to need immersive VR, or is a 2D/web behavioral study a valid primary outcome?** Even a loose answer scopes how generic the data layer must be — and whether VR (Phase 5) is core or a stretch.

---

## Appendix — How the Unity room generator works (the renderer, zoom-in on step 4)

This is the prototype we already built. It's *one* way to do "Present"; the spec doesn't depend on it.

```
  INPUT                 RoomBuilder.cs  (one script, rebuilds live)            OUTPUT
 ┌──────────┐  ┌────────────────────────────────────────────────────┐   ┌────────────┐
 │ recipe   │  │  Build()  — re-runs whenever a parameter changes    │   │ a room =   │
 │ (params, │─▶│                                                      │─▶ │ a tree of  │
 │ a future │  │  1  clear the previous room                          │   │ GameObjects│
 │ RoomSpec)│  │  2  FLOOR + 4 WALLS from cubes, with GAPS for the    │   └─────┬──────┘
 └──────────┘  │     door + windows                                   │         │
               │  3  CONTOUR: curved → rounded corner columns         │         ▼
               │  4  MATERIALS: textures from Resources/RoomTex/       │    Unity camera
               │  5  FURNITURE: .fbx models from Resources/Furniture/  │    renders →
               │  6  LIGHTING: sun + HDRI skybox (Resources/HDRI/)     │    screen or PNG
               └────────────────────────────────────────────────────┘
```

**The core idea:** a room is **not one model** — it's assembled from a *kit of parts* (cubes + loaded textures/models) by code, driven by the recipe. Change a number → `Build()` re-runs → the room re-assembles instantly. That live, per-parameter control is exactly what Infinigen could not do, and it's why this is the right shape for controlled stimuli.

**Today vs. next:** today the recipe is the Inspector sliders. Next, the recipe is a `RoomSpec` JSON read by a `BuildFromSpec(spec)` method — *same `Build()`, just fed from the contract instead of by hand.* That single change makes the human form, the AI agent, and Unity all interchangeable producers/consumers of one spec.
