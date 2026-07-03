# ARCHITECTURE — master schematic + decision log (v3, HDRP-native)

*Written 2026-07-02. This is the architecture of record. It supersedes the Unity-WebGL/URP architecture v2 in [PHASE2_PLAN.md](PHASE2_PLAN.md) (kept as history + design source) per decisions DL-8..DL-14 below. One-page overview: [../PLAN.md](../PLAN.md). Team + workflow: [TEAM_PLAN.md](TEAM_PLAN.md). Execution: [../handoffs/](../handoffs/).*

## 1. What changed and why (context for the pivot)

Architecture v2 (2026-06-11) planned a Unity **WebGL/URP** build in the browser, with a JS↔Unity bridge, because "send a link" reach was assumed necessary and no Unity implementation existed yet. Two facts changed:

1. **A working native generator exists.** Paco built, in the old repo, a native Unity **6000.3 + HDRP** parametric room generator (`unity/` — now landed in THIS repo): procedural shell/openings/furniture from a spec, **curved-wall geometry already working** (arc-tessellated rounded corners), physically-based HDRP lighting in real units (lumen→candela, fixed exposure) **calibrated to a target lux** — which is architecture v2's planned "luxmeter"/matched-luminance tool (RENDERING_RESEARCH.md §6 step 6), already implemented. A control-vs-treatment studio UI, a VR exploration mode (OpenXR), and a one-click Windows build ship with it.
2. **The realism bar moved from "viable" to "validated."** The Max Planck rooms Kirsh cites as the bar (Nour Tawil, Center for Environmental Neuroscience, MPI Berlin) were built in **Unity HDRP** for a tethered headset — the exact stack the existing code uses. The entire WebGL constraint chain (RENDERING_RESEARCH.md §1: no baked GI, no deferred, ~2 GB heap, 50–100 MB budget, probe-approximation workarounds) exists **only because of the browser**. Going native dissolves it.

## 2. The platform (one diagram)

```
                    ┌──────────────────  ONE NATIVE UNITY APP (HDRP, Windows)  ──────────────────┐
                    │                                                                             │
  STUDENT/OPERATOR →│  OPERATOR UI (UI Toolkit)          EXPERIMENT RUNTIME                       │← PARTICIPANT
                    │  preset browser · sliders           id entry → instructions → explore       │  (same PC now;
                    │  live diff panel · publish gate     rooms → task (rating / A-vs-B) →        │   headset later)
                    │        │                            responses logged per-trial              │
                    │        │ RoomSpec JSON                        │ RoomSpec JSON               │
                    │        ▼                                      ▼                             │
                    │  ┌────────────── ENGINE SEAM (ISpecChannel) ──────────────┐                 │
                    │  │  LocalChannel (v1: in-process)  ·  NetworkChannel (VR)  │                │
                    │  └──────────────────────────┬──────────────────────────────┘                │
                    │                             ▼                                               │
                    │  ┌── C# PAIR GATE (port of validate_pair.py; diff_vectors-conformant) ──┐   │
                    │  │   blocks confounded pairs at load AND at publish                      │  │
                    │  └──────────────────────────┬────────────────────────────────────────────┘  │
                    │                             ▼                                               │
                    │  ROOM GENERATOR (RoomBuilder.BuildFromSpec — adapted from Paco's RoomGen)   │
                    │  shell · curved contour · openings · furniture (PRESETS.md) · HDRP light    │
                    │  rig calibrated to target lux · walk/orbit/fixed_eye cameras                │
                    └───────────────┬─────────────────────────────────────┬───────────────────────┘
                                    │ ROOM_API.md (REST, when online)     │ POST /responses
                                    ▼                                     ▼
                    ┌────────── CLOUDFLARE WORKER ─────────┐      ┌───────────────┐
                    │ rooms · pairs (server-side JS gate)   │      │ D1: studies,  │→ researcher CSV
                    │ studies · R2 thumbnails               │      │ responses     │  (authed GET)
                    └───────────────────────────────────────┘      └───────────────┘

  CONTRACTS (the only coupling): room_spec.schema.json (frozen v1) · study.schema.json ·
  response_log.schema.json · ENGINE_SEAM.md · ROOM_API.md · PRESETS.md · golden fixtures
```

**Storage boundary unchanged** ([STORAGE.md](STORAGE.md)): a room is a RoomSpec JSON (the recipe, not the mesh); local-first (`persistentDataPath`), Cloudflare = library/backup/response sink, **never in the live loop**. SDSC remains an optional offline accelerator only.

## 3. Data-flow walkthrough (a room becomes data)

1. **AUTHOR** — operator opens the app, picks a preset (dining room), drags sliders in the UI Toolkit panel. Each change (debounced ~150 ms) pushes the full RoomSpec through `ISpecChannel.Apply` → the room rebuilds live (<100 ms target for our simple geometry).
2. **PAIR** — "Save as control" freezes a spec; "duplicate → edit" produces the treatment. The **live diff panel** renders `validate_pair`'s result continuously: green when the diff == the declared variable, red with the confound list otherwise.
3. **VALIDATE + PUBLISH** — the publish gate refuses any pair with violations (client-side C# gate; the Worker re-runs the same gate server-side on `PUT /pairs` — both must pass `diff_vectors.json`). Published study = `study.schema.json` document with embedded spec snapshots + the validation stamp.
4. **RUN** — the experiment runtime loads a study (by id from Cloudflare, or a local file), assigns `participant_id`/`session_id`, seeds the trial order, and runs: instructions → walk the room(s) (`walk` camera, eye 1.65 m; conditions switched by `fade`/`teleport`) → task (rating or A-vs-B) → per-trial response rows.
5. **COLLECT** — every row is appended locally (CSV per RESPONSE_LOG.md) and POSTed to the Worker when online; researcher pulls `responses.csv` with the API key. Modality recorded on every row; never pooled.

## 4. Decision log

Diego's seven grill decisions (2026-06-10/11) are preserved as DL-1..DL-7 with their current status; DL-8+ are the 2026-07-02 pivots (owner: Paco, planning session with Fable).

| # | Decision | Status |
|---|---|---|
| DL-1 | First consumer = real participants soon; realism on the critical path | **Unchanged** |
| DL-2 | Renderer = Unity (not the old A-Frame viewer); native app kept as an option | **Superseded by DL-8** — the "later option" is now the primary |
| DL-3 | Unity development on the strong Windows PC, in this repo's `unity/` folder | **Unchanged** (the folder now contains the working generator) |
| DL-4 | Storage = Cloudflare Worker + R2 + D1, adapted from the old repo's pattern | **Unchanged** ([ROOM_API.md](../spec/contracts/ROOM_API.md), [schema.sql](../spec/contracts/schema.sql)) |
| DL-5 | Responses POST to the Worker → D1; client-side CSV kept as fallback | **Unchanged** ([response_log.schema.json](../spec/response_log.schema.json)) |
| DL-6 | Free editing + live diff panel + publish gate | **Unchanged** ([UNITY_UI handoff](../handoffs/UNITY_UI.md)) |
| DL-7 | Integration = tracer-bullet milestone | **Unchanged, retargeted** — see [COORDINATION.md](../handoffs/COORDINATION.md) checklist |

**DL-8 — Native HDRP desktop app is the runtime; WebGL/URP is superseded.** *(2026-07-02)*
Rationale: (a) **Fidelity**: HDRP gives physical light units, real area lights for windows, screen-space GI, volumetrics, ACES — no probe-approximation stack needed; Kirsh's bar ("reliable experience of being in the room") is the binding constraint and DL-1 puts realism on the critical path. (b) **Precedent**: the Max Planck rooms Kirsh cites were built in Unity HDRP for a tethered headset — we adopt a validated paradigm rather than pioneering browser realism. (c) **Working code**: the generator, curved walls, calibrated lighting, VR mode, and Windows build already exist in HDRP; a URP/WebGL port would *add* weeks to reach a *lower* ceiling. (d) **VR end goal**: the north-star (operator edits around an immersed subject, VR_LIVE_EDITING.md) was always native — one pipeline from tracer bullet to VR, no mid-project engine swap. (e) **Hardware**: the lab PC (RTX 5070 Ti) is HDRP-class.
Consequences: RENDERING_RESEARCH.md's WebGL constraint chain (§1) and Brotli/DPR delivery work (§6) no longer bind; its **physics** (§2), **texture playbook** (§5), and **curved-mesh recipe** (§4) remain normative. TECH_FEASIBILITY risks #2 (WebGL GI), #3 (heap/size), #4 (JS bridge), #5 (HiDPI) are retired; new risk register in §5 below.

**DL-9 — The web arm is DEFERRED, not dropped.** Distribution v1 = a standalone Windows build (folder/zip; double-click). This trades "send a link" for fidelity + schedule. The RoomSpec contract and the seam are engine-surface-agnostic, so a URP/WebGL consumer can be added post-V1 if reach demands it (the old architecture v2 IS that plan, on the shelf). Revisit after the Kirsh demo. *(Kirsh note: "making vs running are distinct" — running a built app is the lighter half; participants don't install Unity.)*

**DL-10 — Adopt and reconcile Paco's generator; do not rebuild.** The KA **RoomSpec schema stays the one contract** (frozen v1.0 + the [ROOMSPEC_V1_1.md](ROOMSPEC_V1_1.md) batch); the generator's internal types adapt TO it via a `RoomSpecAdapter` (field mapping in [UNITY_GENERATOR handoff](../handoffs/UNITY_GENERATOR.md) §3). The generator's `ConditionPairSpec`/`PairValidator` are replaced by the schema's `experiment` block + a C# gate that must reproduce [diff_vectors.json](../spec/fixtures/diff_vectors.json). Its `wall_thickness_m` becomes the 0.15 m convention constant (not spec-exposed). Its lux-calibrated `LightingSystem` is adopted as the **matched-luminance mechanism** (Phase-3 nuisance control, previously only planned).

**DL-11 — 3D-only stands; the 2D-image evidence goes to Kirsh as a question.** Published evidence from the Max Planck group (Tawil et al.): the curvature effect was **strong in 2D image studies but weak in immersive free-exploration VR**. This does not reopen the 3D-only decision here — it is recorded as PROPOSAL.md open question #6 (should a controlled-stills arm exist for effect detection?) for Kirsh to rule on. A stills export would be cheap (the seam already has `capture_screenshot` + `fixed_eye`), but adding a 2D modality is a Kirsh-level scope decision, not an engineering one.

**DL-12 — Desktop-first; VR architected-for from day one.** No headset exists in the team yet, so v1 runs on desktop (`walk` camera, WASD + mouse-look, eye 1.65 m — the one genuinely missing piece of the adopted generator). The `ISpecChannel` seam and OpenXR scaffolding stay live so PCVR is an added channel + camera rig, not a redesign ([VR_LIVE_EDIT handoff](../handoffs/VR_LIVE_EDIT.md)). Hardware recommendation unchanged: Quest 3 + Link (PCVR when tethered, standalone later).

**DL-13 — Timeline: V1 demo ≈ Aug 1, 2026; hard stop end of summer.** Dated milestones in [../PLAN.md](../PLAN.md) roadmap + [COORDINATION.md](../handoffs/COORDINATION.md) status board.

**DL-14 — Team topology 3–3–1 + AI operating model.** Seven people: Engine team (3), Platform team (3), Michael floating as integrator/QA/contract referee. Each workstream = one human + their AI, fed exactly one handoff file. Planning/architecture and the hardest Unity work run at high capability ("Fable"), routine implementation at standard capability ("Opus") — see [TEAM_PLAN.md](TEAM_PLAN.md).

## 5. Risk register (v3 — replaces TECH_FEASIBILITY.md's WebGL-era table)

| Risk | Severity | When known | Mitigation / fallback |
|---|---|---|---|
| **Fidelity gate fails** — greybox+HDRP still doesn't read as "a real room" | High | M2 fidelity pass (mid-July) | Iterate materials/HDRI/furniture (ASSET_SOURCING.md ladder: greybox → CC0 → paid pack ~$50); HDRP headroom is large (SSGI, area lights); Tawil precedent says the ceiling is high enough. This gate is judged against a reference photo, per TECH_FEASIBILITY's protocol. |
| **RoomSpec reconciliation friction** — adapter reveals contract gaps (e.g. lighting mapping, contour semantics) | Medium | M1 tracer bullet (week 2) | Gaps route into the v1.1 batch (ROOMSPEC_V1_1.md) under the contract-change rule; the adapter isolates churn from the rest of the generator. |
| **No send-a-link reach** — recruiting desktop participants needs a lab machine | Medium | Accepted (DL-9) | Lab sessions on the strong PC for v1 (N≈24 within-subjects is Kirsh's own scale); web arm on the shelf if remote reach becomes required. |
| **Unity onboarding** — most of the team has zero Unity experience | Medium | Week 1 | The project self-bootstraps (menu: `RoomGen ▸ Bootstrap Project`); handoffs assume no Unity knowledge; heavy Unity problems escalate per TEAM_PLAN.md §AI model. |
| **Headless Unity CI is fiddly** (licenses, batchmode) | Low | M2+ | Pure-C# logic (adapter, gate, slot resolver, segmenter) lives in EditMode-testable assemblies; CI runs the Python/fixture suite from day one; Unity EditMode in CI is a stretch goal, not a gate. |
| **VR comfort when geometry changes around a subject** | Medium (VR phase) | M5 | Pre-decided rules: geometry via fade/teleport only; live-morph only light/material/furniture (VR_LIVE_EDITING.md). |
| **Cloudflare quota/limits** (D1/R2 free tier) | Low | M3+ | Specs are KB-scale; thumbnails capped; the entire data layer is optional for the V1 demo (local CSV fallback per DL-5). |

## 6. What carries over from architecture v2 unchanged

The five guardrails (PLAN.md) · the frozen RoomSpec v1.0 schema + `validate_pair.py` as the reference gate · Paco's validator conventions (structured violation codes, exit 0/1/2, dual CLI+library) · the honesty labels (`live|cached|regen|preview_only`) · determinism (seed + pinned assets + content hashes) · STORAGE.md's recipe-not-cake model and Cloudflare boundary · PRESETS envelope model · the v1.1 batch plan (lighting split, `instance_id`, curviness/bow) · the literature grounding + open questions in PROPOSAL.md · the comfort rules and compute-topology corrections in VR_LIVE_EDITING.md.
