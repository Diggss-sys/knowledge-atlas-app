# Phase 2 replan — architecture v2 + AI-readable workstream handoff package

*Status: **LIVING PLANNING DOC — not yet executed.** Workflow: this plan gets iterated and pushed in rounds; when it's locked, we execute it (build the package, then the workstreams start). It supersedes HANDOFF.md §9's original Phase 2 sketch. Written 2026-06-10 after a /grill-me session on the original draft.*

## Context

Phase 1 is **verified done** (`Diggss-sys-branch` @ `e6781cb`, clean tree): `py -m pytest tests -q` → 11 passed; `validate_pair.py` → PASS on the ceiling pair.

A grilling session on the original Phase 2 draft produced a **major architecture pivot** and a **role pivot**:

**Role pivot (the deliverable):** Diego + the planning session are the *planners*, not the sole builders. Partners each take a workstream by feeding a self-contained handoff file to their own AI. So the next executable output is a **planning package committed to this repo** — architecture doc with schematics, locked interface contracts, golden fixtures, and one handoff file per workstream — NOT the application code itself.

**Locked decisions from the grill session:**

| # | Decision | Answer |
|---|---|---|
| 1 | First consumer | **Real participants soon** — realism path is on the critical path; schematic-SVG-only stimuli rejected |
| 2 | Renderer & display | **Unity**, not the old A-Frame web viewer. Unity **WebGL in the browser** now; native Unity app kept as a later option (same RoomSpec feeds both) |
| 3 | Unity development | **On Diego's PC, in this repo** (`unity/` folder). Diego installs the editor (Hub already present); AI writes the C# |
| 4 | Storage | **Cloudflare** — Worker + R2 + D1, new resources adapted from the old repo's proven `cloudflare/v2` pattern (worker code, API-key auth, content-addressed R2, deploy runbook all exist as prior art). Room library = presets + user-saved rooms; users browse/fork/edit/save; every save goes to Cloudflare for recall |
| 5 | Data return | **Responses POST to the Worker → D1** ("data is global on Cloudflare for now"); client-side CSV download kept as offline fallback |
| 6 | Editor pair workflow | **Free editing on both rooms** + a **live diff panel** (green when diff == declared variable, red confound list otherwise) + **publish gate** — a confounded pair can be edited but never saved as a study |
| 7 | Integration shape (recommendation, pending confirmation) | Parallel workstreams meeting at a **tracer-bullet milestone**: one preset room → ceiling slider drives WebGL viewer live → save pair → rating task → response row lands in D1 |

**Unchanged foundations:** RoomSpec v1 stays frozen; `validate_pair.py` remains the reference implementation of the single-variable gate; the 5 guardrails and Paco's conventions (violation codes, exit 0/1/2, dual CLI+library) bind all new code.

**Architecture v2 (goes in the docs as the master schematic):**

```
            ┌────────────────────────  WEB FRONT END (Cloudflare-hosted)  ───────────────────────┐
            │                                                                                     │
  STUDENT → │  EDITOR UI (HTML/JS)            PARTICIPANT RUNNER (HTML/JS)                        │ ← PARTICIPANT
            │  preset browser · controls       id entry → instructions → trials → done            │   (send-a-link)
            │  live diff panel · publish gate  task UI (rating, A-vs-B) · RT capture              │
            │        │            ▲                  │                                            │
            │        ▼ RoomSpec   │ events           ▼ RoomSpec                                   │
            │  ┌──────────── UNITY WebGL BUILD (BuildFromSpec) ────────────┐                      │
            │  │  the renderer/viewer — JS↔Unity bridge (VIEWER_BRIDGE.md) │                      │
            │  └───────────────────────────────────────────────────────────┘                      │
            └───────────┬──────────────────────────────────────┬─────────────────────────────────┘
                        │ ROOM_API.md (REST)                   │ POST /responses
                        ▼                                      ▼
            ┌──────────── CLOUDFLARE WORKER ───────────┐   ┌─────────────┐
            │ rooms · pairs (server-side diff gate)     │   │ D1: studies, │ → researcher exports
            │ studies · R2 assets (WebGL build, HDRIs)  │   │ responses    │   CSV via authed GET
            └───────────────────────────────────────────┘   └─────────────┘

  CONTRACTS = the only coupling: room_spec.schema.json (frozen) · study.schema.json ·
  response_log.schema.json · VIEWER_BRIDGE.md · ROOM_API.md · golden fixtures
```

## Unity room generation — concrete design (drives UNITY_WORKSTREAM.md + ARCHITECTURE.md)

**Code architecture (`unity/Assets/Scripts/`)** — pure-C# logic separated from MonoBehaviours so it's unit-testable without scenes:

| Class | Kind | Responsibility |
|---|---|---|
| `RoomSpecModel` / `RoomPresetModel` | pure C# (Newtonsoft JSON — JsonUtility can't parse the placement `oneOf`) | typed mirrors of `room_spec.schema.json` + preset JSON |
| `WallSegmenter` | pure C# | wall + openings → list of solid boxes. Per wall: openings (door = floor→2.05 m constant; window = sill_m→head_m) at normalized position × wall length; sort, reject overlaps (violation-style error); emit full-height slabs between openings + header boxes above doors/windows + sill boxes below windows |
| `SlotResolver` | pure C# | named slot → (x, z, rotation°) using preset `layout_slots` + `footprint_m`. Implements the relative-slot semantics that are currently UNDEFINED (gap found in grilling) — to be specified in a new `spec/PRESETS.md` contract: chairs distribute along the named side of the anchor's footprint at even spacing facing the anchor; wall slots center on the wall at `offset_m` from the wall face; `ceiling: true` slots hang from ceiling height |
| `FootprintBuilder` | pure C# | rounded-rect footprint path per CURVED_WALLS_SUBPLAN: straight runs + quarter arcs (16 segments/arc), corner radius = curviness × min(w,l)/2; extrude to wall mesh, triangulate for floor/ceiling, UVs by arc length |
| `RoomBuilder` | MonoBehaviour | `Build(spec, preset)`: clear children → floor/ceiling → walls (segmenter or curved mesh) → materials → furniture (resolver) → lighting. Deterministic: no `Random` in v1; `seed` reserved |
| `Bridge` + `bridge.jslib` | MonoBehaviour + WebGL plugin | VIEWER_BRIDGE.md protocol |
| `CameraRig` | MonoBehaviour | `orbit` (editor) / `fixed_eye` (experiments: 1.6 m, front-wall center, fixed FOV) |

**Conventions (constants table in the handoff):** origin = room center at floor level, +x right, +z front/door wall (matches schema `wall_ref`); dimensions are INTERIOR (walls built outward, thickness 0.15 m); door height 2.05 m; chair clearance 0.05 m.

**Materials & lighting:** URP (WebGL2-safe). The 9 surface-material enum values → PBR materials from CC0 sources (ambientCG/Poly Haven), pinned by URL+hash in `unity/ASSETS.md` (determinism guardrail). Texture tiling scaled to real-world meters so texture density doesn't vary with room size (nuisance control). `tint_hex` multiplies base color. The 5 lighting presets → a concrete value table (sun angle/intensity, color temperature, ambient, HDRI id, exposure); `warmth` 0→warm 2700K, 1→cool 6500K lerp (schema's direction); `intensity` = multiplier. Realism stack: PBR at real scale + HDRI visible through window gaps + URP SSAO + soft shadows + ACES tonemapping. Furniture v1 = greybox primitives matching `footprint_m` (honest placeholder), v2 = real pinned models. **Fidelity gate:** render the ceiling pair, judge against Kirsh's bar before any participant sees it.

**Contour:** angular (boxes) ships first; curved lands as `FootprintBuilder` per the subplan — openings on straight sections only (v1), `contour: "curved"` maps internally to curviness 1.0 so the frozen schema needs no change; exposing the 0..1 slider later = deliberate RoomSpec v1.1 + validator coordination (recorded in ARCHITECTURE.md decision log). Walls can also bend **in and out**: per-wall `wall_bow` (−1 concave .. +1 convex, circular-arc sagitta) joins the v1.1 batch — mesh recipe, light-physics consequences, and the keep-curved-walls-matte confound rule are in [RENDERING_RESEARCH.md](RENDERING_RESEARCH.md) §4 and §2.

**Rendering deep-dive (2026-06-11):** [RENDERING_RESEARCH.md](RENDERING_RESEARCH.md) settles HOW realism is achieved on the web — the WebGL constraint chain (no baked/realtime GI for runtime rooms → curated approximation stack: sun + sky + window fills + **runtime-captured bounce probe** + SSAO + ACES), the flux-balance physics with the coupled-variable numbers (higher ceiling ≈ dimmer: inverse-square + surface-area effects, quantified), the curved/bowed-wall mesh recipe, the texture-realism playbook (real-world texel density doubles as nuisance control), Cloudflare `_headers` Brotli config, and **pre-agreed native-app fallback criteria** (fidelity gate fail / <30 fps mid laptop / >100 MB build). Verdict: web path is viable; the fidelity gate in M-U5 decides mechanically.

**Unity milestones (each with a DoD acceptance check):** M-U0 scaffold (Unity 6 LTS + URP + WebGL module, Newtonsoft, `unity/` .gitignore) → M-U1 shell from spec (EditMode tests on segmenter trivial case) → M-U2 openings (segmenter tested against the committed pair fixtures) → M-U3 materials + lighting presets → M-U4 furniture via SlotResolver (chairs ring the table from footprints alone) → M-U5 Bridge + WebGL build + harness page (**tracer-bullet dependency** — committed pair's ApplySpec visibly changes ceiling height, screenshot capture works) → M-U6 curved contour. Curves deliberately AFTER the tracer bullet. EditMode tests read fixtures straight from the repo's `spec/` (one source of truth).

## Manipulation coverage — dimensions, items, lighting (audit 2026-06-11)

The platform manipulates more than walls/contour. Audit of what the contract supports today vs. what the next schema iteration must add — grounded in this repo, the old repo's research, and the Kirsh transcript.

### Room dimensions (height, length, width) — ✅ covered
`shell.width_m / length_m / ceiling_height_m` are in the frozen schema with hard ranges, preset ranges narrow them per room type, and the validator already notes their coupled variables (volume, aspect ratio, window-to-wall ratio). The editor just needs three sliders bound to them. No contract change needed.

### Items — which furniture is in the room, and where — ⚠ partial, one real contract gap
- **Covered:** `furniture[]` entries pick items from the preset catalog (`catalog_id`) and place them by named slot or explicit `x_m/z_m/rotation_deg`. Choosing items and placing them is fully authorable today. Editor v1: catalog checklist (add/remove) + slot dropdown or coordinate input; drag-to-place on a top-down plan view is the v2 upgrade.
- **THE GAP (found in this audit):** `validate_pair.py` addresses items by array index (`furniture[0].catalog_id`…). *Moving* one item (same count, same order) diffs cleanly. But *adding/removing* an item shifts every later index → cascade of spurious `undeclared_change` violations; the only escape is declaring the whole `furniture` array as manipulated, which licenses ANY furniture difference — too coarse to be science.
- **Fix (validator v2 + RoomSpec v1.1 candidate):** stable per-item identity — an optional `instance_id` on each furniture entry, plus declared-variable forms like `furniture[id=chair_5]` or `furniture/presence:dining_chair`; alternatively order-insensitive multiset matching on `(catalog_id, placement)`. Decide the mechanism next iteration round.
- **Prior art:** the old kitchen manifest exposed `cabinet_density` and `appliance_visibility` — "how much stuff" as a single scalar knob. For clutter-style studies that is scientifically cleaner than raw add/remove; consider a preset-level density knob alongside per-item control.

### Lighting — indoor, outdoor, bounce — ⚠ partial; the full model is already designed in the old repo
Today's schema is single-channel: one `preset` (5 moods), one `warmth`, one global `intensity`, one `hdri`. No indoor/outdoor separation, no bounce parameter. But the old repo already researched and partly implemented the richer model — salvage, don't invent:

| Concept (user's terms) | Old-repo prior art (grounded) | RoomSpec v1.1 candidate |
|---|---|---|
| **Outdoor / natural light** | `daylight_intensity` — "scalar on the sun/sky contribution through windows" — a distinct manifest knob in all 3 room types; viewer had a rotating sun + time-of-day model and a sunlight-strength slider (`wizard/lighting_control_surface.md` §1; `viewer/index.html`) | `lighting.natural.intensity` (+ `sun_angle` or `time_of_day` — pick ONE exposed knob) |
| **Indoor / artificial light** | `lighting_intensity` — "controls CeilingLightFactory output" — separate from daylight; ties to the `pendant_light` catalog item | `lighting.artificial.intensity` + `lighting.artificial.warmth` |
| **Color temperature** | Exact law already derived: **Kelvin = 6500 − 3800 × warmth** (2700 K warm ↔ 6500 K cool), Tanner-Helland kelvin→RGB (§2) | adopt as the normative warmth law in Unity |
| **Light bounce** ("buoyancy") | Viewer implemented a **one-bounce GI irradiance probe** with adjustable intensity (`giIntensity`, default 0.8, live `KA_GI()` tuning) + GTAO ambient occlusion + warm-sun/cool-skylight split + warm ground-bounce color | `lighting.bounce` (0..1) → Unity: ambient/GI probe intensity |
| **Sky / environment** | Single fixed HDRI today; a multi-HDRI catalog interface was proposed but never built (§3: `hdri_id` → file + thumbnail gallery) | keep `hdri` id; build the catalog in the room library |
| **Windows as light source** | Window topology research exists (clerestory default ×4; window kinds catalog); viewer made glass non-shadow-casting so sun passes through | windows stay geometry (openings); daylight couples to them — add to COUPLED_VARS |

**Why lighting is doubly load-bearing (transcript-verified):** "We alter the windows or the natural lighting in some way" is Kirsh's manipulation #2 verbatim — and lighting quality is *why* Infinigen died ("you're never going to get lighting out of Infinigen… why can't Unity take a 3D model and apply lighting?"). The old repo's honesty rule (lighting previews must be labeled if not baked) dissolves in the new architecture: Unity WebGL renders live, so the preview IS the stimulus — a genuine win to note in ARCHITECTURE.md.

**New coupled-variables to add to the validator:** `lighting.natural.intensity` ↔ shadow contrast/depth; `lighting.bounce` ↔ perceived overall brightness; window count/size (geometry) ↔ daylight level; `lighting.artificial.warmth` ↔ perceived brightness (existing warmth note generalizes).

**Schema policy:** these are **RoomSpec v1.1 candidates, batched** — one coordinated bump (curviness slider + furniture `instance_id` + lighting split + bounce), with validator, editor, preset files, and Unity updated in lockstep under COORDINATION.md's contract-change rule. v1 stays frozen until the batch is locked.

**Bonus salvage find:** the old repo's presets are literature-grounded experiment conditions — `meyers_levy_high/low_ceiling`, `vartanian_cathedral/high`, `ulrich_recovery`, `kaplan_restorative` — exactly the named studies this platform serves. Port them as seed content for the Cloudflare room library.

## Deliverable: files to create (all in this repo, committed + pushed)

### 1. Architecture + plan docs
- **`docs/ARCHITECTURE.md`** — the master schematic above, expanded: component diagram, data-flow walkthrough (author→validate→publish→run→collect), deployment diagram (Pages/Worker/R2/D1), the two-language-validator strategy, decision log (the grill table with rationale, including why the A-Frame viewer was dropped and why WebGL-not-native now).
- **`PLAN.md` rewrite** — stays a one-pager: new pipeline diagram, guardrails (unchanged), new roadmap = workstreams + tracer-bullet milestone + deepening milestones (M2: full editor controls + furniture; M3: research-grade nuisance control; M4: native app option / VR / AI authoring). Old Unity appendix replaced by a pointer to ARCHITECTURE.md.
- **`HANDOFF.md` update** — §3 records the pivot, §5 adds "Phase 2 planning package ✅", §9 becomes "assign workstreams, install Unity, wrangler login" with pointers to `handoffs/`.

### 2. Contracts (`spec/` — the coupling surface between workstreams)
- **`spec/contracts/VIEWER_BRIDGE.md`** — exact JS↔Unity WebGL protocol. Web→Unity via `unityInstance.SendMessage('Bridge', ...)`: `ApplySpec(specJson)` (full-spec push, debounced ~150ms — RoomBuilder rebuilds whole room anyway), `SetCameraMode(mode)` (`orbit` for editor / `fixed_eye` for experiments, eye 1.6 m), `CaptureScreenshot(requestId)`. Unity→Web via a `.jslib` dispatching CustomEvents: `unity:ready {builderVersion}`, `unity:specApplied {ok, errors[], buildMs}`, `unity:screenshot {requestId, pngBase64}`. Versioned (`bridge_version: 1`); unknown-message and malformed-spec error behavior specified; a committed sample message transcript serves as fixture.
- **`spec/contracts/ROOM_API.md`** — Worker REST surface. `GET /rooms?room_type=` (list, metadata only) · `GET /rooms/:id` (spec JSON) · `PUT /rooms` (X-API-Key; server schema-validates; content-hash id like old v2) · `GET /pairs/:pair_id` · `PUT /pairs` (**server-side diff gate**: runs the JS validator, 422 + violations JSON on confound — same codes as validate_pair.py) · `GET/PUT /studies` · `POST /responses` (validates rows against response_log schema; accepts batch) · `GET /studies/:id/responses.csv` (X-API-Key; canonical column order). Error envelope `{ok:false, violations:[{code,path,message}]}` everywhere — Paco's convention over HTTP.
- **`spec/contracts/schema.sql`** — D1: `rooms(id, room_type, spec_json, sha256, created_by, created_at)`, `pairs(pair_id, control_id, treatment_id, manipulated_variables, validation_json, created_at)`, `studies(study_id, pair_id, task_json, status, created_at)`, `responses(id, study_id, participant_id, session_id, trial_index, task_type, condition, rt_ms, row_json, received_at)` (hot fields flat for SQL/CSV, full row preserved as JSON).
- **`spec/study.schema.json`** — study definition: pair ref + full specs snapshot, task config (`rating` | `choice`, extensible discriminator), validation stamp (`ok`, diff, notes, validated_at), modality + execution_path, status (`draft|published|closed`).
- **`spec/response_log.schema.json` + `spec/RESPONSE_LOG.md`** — one row = one response; required fields incl. `participant_id`, `session_id`, `trial_index`, `task_type`, `condition`, `manipulated_variables`, `modality`, `execution_path`, `response` (per-task payload), `rt_ms`, `timestamp_utc`, `presentation_order_seed`, `runner_version`; canonical CSV column order; seeded-shuffle (mulberry32) determinism note.
- **`spec/PRESETS.md`** — the preset contract that's currently implicit: exact slot-resolution semantics (`relative_to`/`side`/`index` distribution math, wall slots via `offset_m`, `ceiling: true` mounting), footprint/clearance rules, range/default precedence, how a renderer must consume preset + spec together. Closes the gap found while grilling (the dining preset uses these fields; no document defines them).

### 3. Golden fixtures (`spec/fixtures/`) — let each workstream test alone
- `diff_vectors.json` — pair-validation cases **generated by the existing Python validate_pair.py** (pass case, each violation code) so the future JS port provably matches the reference.
- `bridge_messages.json` — valid/invalid VIEWER_BRIDGE message samples.
- `response_rows.json` — valid rating + choice rows, plus invalid rows per coherence code.
- `tests/test_fixtures.py` — pytest proving fixtures validate against the schemas and that diff_vectors reproduce exactly through `validate_pair()` (keeps fixtures honest forever; existing 11 tests untouched).

### 4. Workstream handoffs (`handoffs/`) — each 100% self-contained: a partner's AI reads ONLY that file + the contracts it names
Common skeleton per file: project context (3 paragraphs); repo URL/branch/conventions; YOUR scope and NOT-your-scope; the contracts you implement (by path); build steps in dependency order; **definition of done** as runnable acceptance checks against the fixtures; environment gotchas; integration milestone role.
- **`handoffs/UNITY_WORKSTREAM.md`** — the full "Unity room generation — concrete design" section above, expanded to standalone: class table with responsibilities and signatures, geometry conventions/constants, the WallSegmenter algorithm spelled out, SlotResolver semantics (per spec/PRESETS.md), materials/lighting value tables, ASSETS.md pinning rule, contour phasing, milestone ladder M-U0→M-U6 with per-milestone DoD acceptance checks against the committed pair fixtures, Bridge + .jslib implementation guide per VIEWER_BRIDGE.md, WebGL build settings (compression, size budget <60 MB), and the committed harness page under `unity/harness/`.
- **`handoffs/CLOUDFLARE_WORKSTREAM.md`** — start from old repo's `cloudflare/v2/room_api_worker.js` pattern (auth, hashing, MIME — file is reachable read-only at `../cogs160track3v2/cloudflare/v2/`); new resources `atlas-room-api` / `atlas-room-assets` / `atlas_rooms` D1; implement ROOM_API.md + schema.sql; **JS validator module `web/shared/pair_diff.js`** (flatten/diff/coverage port of validate_pair.py) shared by Worker and editor, must pass `diff_vectors.json`; deploy runbook (wrangler login is the only human-OAuth step); DoD: miniflare/`wrangler dev` test script exercising every endpoint incl. 422 on the confounded fixture and a batch POST of fixture response rows.
- **`handoffs/WEBUI_WORKSTREAM.md`** — dependency-free HTML/JS/CSS in `web/`; editor page (preset browser via API → controls bound to RoomSpec fields within preset ranges → debounced `ApplySpec` to the embedded Unity iframe/canvas → **live diff panel** using `pair_diff.js` → save room / save pair / publish study against ROOM_API; publish blocked on violations); runner page (load published study → id entry → instructions → seeded trial loop (mulberry32, seed logged per row) → rating + A-vs-B tasks → POST rows + CSV fallback); both pages must run against a **mock bridge** (a stub `unity:ready`/`specApplied` emitter, committed) so this workstream never waits on Unity; DoD: full editor flow and a complete fake-participant session run with mock bridge + `wrangler dev` worker, rows visible in D1.
- **`handoffs/COORDINATION.md`** — who merges what where (feature branches → PRs to `Diggss-sys-branch`); contract-change rule (any contract edit = PR touching the contract file + both affected workstreams' fixtures + version bump); the tracer-bullet integration checklist; current status board.

### 5. Wiring
- `README.md` — point at ARCHITECTURE.md + handoffs; update status.
- `.gitignore` — Unity (`unity/Library/`, builds except the committed WebGL artifact policy decided in the Unity handoff), `node_modules`, `.wrangler/`, `.dev.vars`.

## Verification (when the package is built)
```powershell
py -m pytest tests -q     # 11 existing + new test_fixtures.py, all green
py tools\validate_pair.py "spec\pairs\ceiling_height_study_01\control.spec.json" "spec\pairs\ceiling_height_study_01\treatment.spec.json"   # still PASS
```
Plus mechanical doc checks: every contract file referenced by ≥1 handoff; every handoff names only files that exist after the package lands; no handoff references conversation context (self-containment check); ARCHITECTURE.md decision log covers all 7 grill decisions.

## What Diego does after the package lands
1. Assign workstreams to partners; send each ONE file from `handoffs/`.
2. Install Unity (Hub present) if keeping the Unity workstream local; ask Paco where the old RoomBuilder.cs prototype lives (may save porting time).
3. `npx wrangler login` when the Cloudflare workstream is ready to deploy (only human-OAuth step).
4. Open question to Kirsh still pending (response types; 2D-web vs VR as primary outcome) — absorbed by the task-registry design either way.

## Open items for the next iteration round
- Confirm decision #7 (tracer-bullet integration milestone) — recommendation stands, user hasn't formally picked.
- Task design specifics: rating scale wording, trial counts, A-vs-B side counterbalancing policy.
- Preset semantics details for `spec/PRESETS.md` (chair spacing math, clearance rules).
- Asset sourcing pass: which CC0 texture/furniture sets, pinned versions.
- Workstream → person assignment.
- **Lock the RoomSpec v1.1 batch** (from the manipulation audit + rendering research): exact lighting block shape (natural/artificial/bounce), furniture identity mechanism (`instance_id` vs multiset matching), curviness slider, **per-wall `wall_bow` (−1..+1)**, new COUPLED_VARS entries (incl. the quantified ceiling-height light couplings and curved-wall interreflection from RENDERING_RESEARCH.md §2) — one coordinated schema+validator+preset+Unity bump.
- Openings-on-bowed-walls policy: forbid vs locally flatten (RENDERING_RESEARCH.md §4).
- Editor UI specifics for item manipulation: catalog checklist + slot/coordinate placement v1; plan-view drag v2; optional density-style scalar knob.
- HDRI catalog contents for the room library (ids, thumbnails, descriptions).
- Port the literature presets (meyers_levy, vartanian, ulrich, kaplan) from the old repo as seed rooms.
