# CLAUDE.md — agent map for this repo

Read this first, every session. It tells you what's current, where things live, and what to ignore.

## What this is

A native Unity **HDRP Windows app** that generates controlled, single-variable 3D room pairs for a
perception experiment (UCSD COGS 160, Prof. Kirsh; demo ≈ Aug 1 2026). A student authors a room pair
with sliders, a validator gate enforces exactly one manipulated variable, a participant walks the
rooms and rates them, responses land in a CSV. Everything is **parametric** and flows through one
contract: the **RoomSpec JSON**.

```
AUTHOR (sliders/AI) → RoomSpec JSON → VALIDATE (single-var gate) → GENERATE+RENDER (HDRP) → COLLECT (CSV)
```

## Read order

1. **This file.**
2. **[docs/CODE_MAP.md](docs/CODE_MAP.md)** — what boots when you press Play, which UI surface owns
   which job, what is being retired, and the rules that stop another one appearing.
   **Read this before adding any UI or entry point.**
3. [PLAN.md](PLAN.md) — one-page picture + pipeline diagram.
4. [docs/WORKING_AGREEMENT.md](docs/WORKING_AGREEMENT.md) — who owns which decisions, and the rules
   that land work. **Read this before planning anything other people will build against.**
5. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — architecture of record (v3, HDRP-native) + decision log DL-1..15.
6. Your lane's doc (below), then code.

## Where things live

| Path | What |
|---|---|
| `spec/` | **The product**: room_spec + response_log schemas, contracts (ENGINE_SEAM, AI_AUTHORING), golden fixtures, presets, reference validators (`study.schema.json` lives in `unity/Assets/RoomGen/Resources/RoomGen/`) |
| `unity/Assets/RoomGen/Runtime/Generation/` | Parametric geometry: ShellGenerator, WallBand, FootprintPath, OpeningGenerator, furniture |
| `unity/Assets/RoomGen/Runtime/Lighting/` | Physical lighting, QualityRig (RT GI/reflections/AO), CameraRealism |
| `unity/Assets/RoomGen/Runtime/Validation/` + `Gate/` | RoomSpec validation + single-variable gate |
| `unity/Assets/RoomGen/Runtime/Seam/` | `ISpecChannel` / `LocalChannel` — **the spine**: every UI drives the engine only through this |
| `unity/Assets/RoomGen/Runtime/Studio/` | Legacy IMGUI Room Studio (Diego's lane) — see "two studios" below |
| `unity/Assets/RoomGen/Runtime/Runner/` + `Metrics/` | Experiment runtime: TrialSequencer, StudyRunner, ResponseCsv, PerfMonitor |
| `unity/Assets/RoomGen/UI/` | UI Toolkit layer (PR #3): `Operator/` panel + view-model, `Runner/` participant flow, `Shared/base.uss` |
| `unity/Assets/RoomGen/Tests/` | EditMode (94+) + PlayMode (3+) — **suite must stay green to merge** |
| `unity/Assets/RoomGen/Editor/` | Scene setup, build scripts, LiveSmoke, UiCapture |
| `unity/docs/REALISM_PASS_PLAN.md` | Diego's living realism plan (RT landed; textures/assets next) |
| `handoffs/` | Per-lane execution docs + sprint records |
| `team_hub/` | Static team website (GSAP via CDN — never vendor libs into the repo) |

## Doc status — trust this table over folder position

**LIVE (act on these):**
`PLAN.md` · `docs/CODE_MAP.md` · `docs/WORKING_AGREEMENT.md` · `docs/ARCHITECTURE.md` ·
`handoffs/COORDINATION.md` · `handoffs/UI_LANE_HANDOFF.md` · `unity/docs/REALISM_PASS_PLAN.md` ·
`docs/GETTING_STARTED.md` · `docs/PERFORMANCE.md` · `docs/TEAM_RUN.md` · `unity/ASSETS.md` ·
`docs/ROOMSPEC_V1_1.md` (proposal, not yet locked)

**HISTORY (context only — do not execute from these):**
- `handoffs/archive/` — delivered workstreams and superseded plans, including `REPLAN_JULY.md`
  (the July lane collapse; its decision-ownership half now lives in `docs/WORKING_AGREEMENT.md`)
  and the six original role handoffs (`UNITY_GENERATOR`, `UNITY_UI`, `EXPERIMENT_RUNTIME`, …),
  which remain useful as specs of each subsystem. See `handoffs/README.md` for live vs archived.
- `docs/history/HANDOFF.md` — 2026-06-10 restart doc; superseded, banner inside says so.
- `docs/PHASE2_PLAN.md` — WebGL/URP architecture v2; superseded by ARCHITECTURE.md v3.
- `docs/TEAM_PLAN.md` — the 7-person/6-lane staffing model; superseded by `docs/WORKING_AGREEMENT.md`.
- Research docs (`RENDERING_RESEARCH`, `ROOM_GENERATION_RESEARCH`, `CLOUD_RENDERING_RESEARCH`,
  `TECH_FEASIBILITY`, `VR_LIVE_EDITING`, `ASSET_SOURCING`, `STORAGE`, `LEGACY_PROJECT`,
  `CURVED_WALLS_SUBPLAN`) — cited groundwork; conclusions absorbed into ARCHITECTURE.md.
- Sprint records (`handoffs/LIGHTING_SPRINT`, `OVERNIGHT_BUILD`, `FABLE_REVIEW`, `FIDELITY_GATE`,
  `A1_A2_REVIEW`, `SOFTWARE_FOUNDATION`) — completed work logs.
- `docs/PROPOSAL.md` — the course proposal; keep intact, it's a deliverable.

## THREE studios (known duplication — see [docs/CODE_MAP.md](docs/CODE_MAP.md) §3 for the retirement order)

There is now a third surface: `UI/Studio/` (UI Toolkit, the current default on Play). **Do not add a
fourth.** Extend `UI/Studio`; any parallel build must ship with the commit that deletes what it
replaces. The section below predates it and is kept for context.

## Two studios (known duplication — converging, not both forever)

- **Legacy IMGUI studio** (`Runtime/Studio/RoomStudioController`) — Diego's realism lane drives the
  generator directly. Keeps working until convergence is agreed.
- **Operator Studio** (`UI/Operator/`, scene `OperatorStudio.unity`) — UI Toolkit panel that drives
  the engine **through `ISpecChannel`**. This is the target architecture: every producer of rooms
  (sliders, AI copilot, VR, web) emits RoomSpecs through the seam.

New room-editing features go on the spec-channel path unless Diego says otherwise.

## Conventions & gotchas

- **Branches:** Diego on `Diggss-sys-branch`; feature branches → PR to `main`. Nothing merges with a
  red suite. Push only when Diego says push.
- **EditMode test asmdef cannot see Newtonsoft.** Keep `JObject` inside Runtime; hand tests plain
  C# types (this shaped OperatorPanelViewModel — preserve the pattern).
- **Determinism:** `System.Random` seeded from the spec, never `UnityEngine.Random`. Store the
  recipe (spec), not the cake (mesh).
- **Engine errors surface verbatim** in UIs — never re-worded (locked decision).
- **Slider streams debounce ~150 ms before the channel** (ENGINE_SEAM).
- **Exposure stays fixed** in the realism pass — fix light physics, don't mask with exposure.
- Unity project: `unity/` (Unity 6, HDRP + ray tracing). Tests run via Unity Test Runner, EditMode.
- No vendored JS libraries, no `__MACOSX`/`.DS_Store` — the hub loads CDN.
