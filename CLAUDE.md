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
2. [PLAN.md](PLAN.md) — one-page picture + pipeline diagram.
3. [handoffs/REPLAN_JULY.md](handoffs/REPLAN_JULY.md) — **the current execution plan** (2026-07-09).
4. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — architecture of record (v3, HDRP-native) + decision log DL-1..15.
5. Your lane's doc (below), then code.

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
`PLAN.md` · `docs/ARCHITECTURE.md` · `handoffs/REPLAN_JULY.md` · `handoffs/COORDINATION.md` ·
`unity/docs/REALISM_PASS_PLAN.md` · `docs/GETTING_STARTED.md` · `docs/PERFORMANCE.md` ·
`docs/TEAM_RUN.md` · `unity/ASSETS.md` · `docs/ROOMSPEC_V1_1.md` (proposal, not yet locked)

**HISTORY (context only — do not execute from these):**
- `HANDOFF.md` (root) — 2026-06-10 restart doc; superseded, banners inside say so.
- `docs/PHASE2_PLAN.md` — WebGL/URP architecture v2; superseded by ARCHITECTURE.md v3.
- `docs/TEAM_PLAN.md` + the six role handoffs (`UNITY_GENERATOR`, `UNITY_UI`, `EXPERIMENT_RUNTIME`,
  `VR_LIVE_EDIT`, `CLOUDFLARE_DATA`) — the 6-lane structure collapsed into REPLAN_JULY's two tracks;
  still useful as specs of each subsystem.
- Research docs (`RENDERING_RESEARCH`, `ROOM_GENERATION_RESEARCH`, `CLOUD_RENDERING_RESEARCH`,
  `TECH_FEASIBILITY`, `VR_LIVE_EDITING`, `ASSET_SOURCING`, `STORAGE`, `LEGACY_PROJECT`,
  `CURVED_WALLS_SUBPLAN`) — cited groundwork; conclusions absorbed into ARCHITECTURE.md.
- Sprint records (`handoffs/LIGHTING_SPRINT`, `OVERNIGHT_BUILD`, `FABLE_REVIEW`, `FIDELITY_GATE`,
  `A1_A2_REVIEW`, `SOFTWARE_FOUNDATION`) — completed work logs.
- `docs/PROPOSAL.md` — the course proposal; keep intact, it's a deliverable.

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
