# HANDOFF — EXPERIMENT_RUNTIME (role E3: the half that makes it science)

*Self-contained. You (+ your AI) need ONLY this file and the contracts it names. Repo `https://github.com/Diggss-sys/knowledge-atlas-app`, branch `Diggss-sys-branch`; feature branch → PR ([COORDINATION.md](COORDINATION.md)).*

## Context (3 paragraphs)

This platform authors controlled room pairs (one declared variable difference) and — YOUR workstream — **runs participants through them and collects the data**. Without you it's a room generator; with you it's an experiment instrument. Prof. Kirsh's experiment types (all behavioral): concentration tasks in-room, memory tests administered after/outside the room, navigation/pointing, and adaptive A-vs-B preference. V1 ships two task types — `rating` and `choice` — behind a registry designed to extend.

You consume a **study document** (`spec/study.schema.json`: a validated pair + task config + modality) and drive the renderer through the **engine seam** exactly like the operator UI does: `load_pair`, `switch_condition` (use `fade` or `teleport` — Kirsh's own VRChat-teleport idea is the comfort-safe default), `set_camera_mode("walk")`. The participant walks the room desktop-first (WASD + mouse-look; E1 builds that mode), does the task, and your runner writes **one schema-valid response row per response**.

Data honesty is the whole game: rows are written immediately (local append + queued POST), self-describing, seeded for reproducibility, and modality-stamped. The row contract (`spec/response_log.schema.json` + `spec/RESPONSE_LOG.md`) is frozen; the golden rows in `spec/fixtures/response_rows.json` are your acceptance tests.

## Contracts you implement/consume

- `spec/study.schema.json` — your input. Refuse to run a study whose `validation.ok` is false or whose `status` isn't `published` (honesty rule).
- `spec/response_log.schema.json` + `spec/RESPONSE_LOG.md` — your output: row schema, canonical CSV column order, per-task payloads, determinism rules.
- `spec/fixtures/response_rows.json` — valid rows you must be able to produce; invalid rows your writer must refuse.
- `spec/contracts/ENGINE_SEAM.md` — how you drive rooms (`load_pair`, `switch_condition`, `set_camera_mode`).
- `spec/contracts/ROOM_API.md` — `POST /responses` (batch, retry, 422 semantics). OPTIONAL in v1: local CSV is the requirement; the POST path activates when P3's Worker is live (feature flag).

## Scope / NOT scope

**Yours:** the participant flow (participant-id entry → instructions → room exploration → task screens → done) · seeded trial ordering (`System.Random(seed)`; the seed goes in every row) · per-trial row writing (local CSV in canonical column order + queued POST with retry + `UNIQUE(session_id, trial_index)` idempotency) · session provenance log · the Windows build stays green (`RoomGen ▸ Build Windows Application`).
**NOT yours:** authoring/validation UI (P1) · rendering (E1) · the Worker itself (P3) · new task types without a contract change (COORDINATION.md rule).

**Design rule for YOUR screens (non-negotiable):** the participant screens are part of the stimulus environment, so they are **neutral by scientific requirement** — minimal, unthemed, no animations or visual personality, identical across every study. Anything visually loud is a confound. Consume P1's base styles from `unity/Assets/RoomGen/UI/Shared/`; your screens live in `UI/Runner/`; there is deliberately no creativity budget here (see COORDINATION.md "UI surface ownership").

## The v1 participant flow (build exactly this)

1. **Setup screen** (operator-facing): pick a study JSON (file dialog or library), enter/generate `participant_id`, auto-generate `session_id` (GUID) + `presentation_order_seed`.
2. **Instructions screen**: study task prompt, plain language, "press any key".
3. **Exploration trials** (order from the seeded shuffle per `counterbalance.order_strategy`): for each condition — `switch_condition(fade)` → free walk, min-dwell timer (config, default 60 s) → task screen.
   - `rating` task: the prompt + a 1–7 (config) scale; write one row per rated condition (`condition: control|treatment`).
   - `choice` task: side-by-side presentation is v2; v1 shows conditions sequentially then asks the A/B question (`condition: "both"`, sides recorded per the seed's counterbalancing).
4. **Done screen**: rows written count, CSV path, upload status. Never show raw data to the participant.

## Build steps (in order)

1. **R0 — Boot + row writer.** Boot per [UNITY_GENERATOR.md](UNITY_GENERATOR.md) G0. Build the row writer as pure C#: construct rows, validate structurally against the contract, append CSV in the canonical order, JSONL mirror. *DoD: EditMode tests reproduce the valid fixture rows byte-compatibly (modulo timestamps/ids) and refuse each invalid fixture row for its stated reason.*
2. **R1 — Flow shell.** The four screens over a `MockSpecChannel` (same mock as P1's U0 — share it). Seeded ordering. *DoD: a scripted session produces a valid CSV with correctly seeded order.*
3. **R2 — Real rooms.** Swap in E1's `LocalChannel`: `load_pair` from a study document, walk mode, fade switches, dwell timers. *DoD: a human runs a full fake session in the built `.exe` for the committed ceiling pair; the CSV validates.*
4. **R3 — Upload path.** Queued `POST /responses` with retry/backoff + offline catch-up batch; feature-flagged. *DoD: against P3's `wrangler dev`, a session lands every row in D1 exactly once (retry a killed request to prove idempotency).*
5. **R4 — Second pair + polish.** Run the curved-wall pair (M3); dwell/task config from study JSON fully honored; `runner_version` stamped from a single constant.

## Environment gotchas

- Time: `timestamp_utc` = `DateTime.UtcNow` ISO-8601 with `Z`; `rt_ms` from `Time.realtimeSinceStartupAsDouble` deltas (never `Time.time` — it's scaled).
- Ordering: `System.Random(seed)` Fisher-Yates; NEVER `UnityEngine.Random` (unseeded state leaks).
- CSV: RFC-4180 quoting, UTF-8, LF; `manipulated_variables` joined with `;`; `response_json` is the JSON-serialized payload.
- Participant privacy: `participant_id` is an assigned code — the runner must not collect names/emails (no IRB for PII; see PLAN.md guardrails).

## Your integration role

M1 tracer bullet ends with YOUR fake-participant session producing a valid CSV. M3's Kirsh demo is your flow start-to-finish. You share the seam mock with P1 (build it once, in a shared `unity/Assets/RoomGen/Testing/` assembly).
