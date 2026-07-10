# COORDINATION — how six workstreams become one platform

*The integrator's (Michael's) operating manual + the rules that bind everyone. Team topology + AI model: [docs/TEAM_PLAN.md](../docs/TEAM_PLAN.md). Timeline: [../PLAN.md](../PLAN.md) roadmap (V1 demo ≈ Aug 1; hard stop end of summer).*

## Branching + merging

- `main` — stable snapshots only (Michael tags after each milestone).
- **`Diggss-sys-branch` — the integration branch.** All feature branches PR into it; Michael (or Diego) merges.
- Feature branches: `<name>/<workstream>-<thing>`, e.g. `paco/gen-adapter`, `nk/ui-diff-panel`. Small PRs (one milestone step), reviewed by the workstream's contract counterpart (table below).
- Commits explain the *why*; convention: end with `Co-Authored-By: Claude <model> <noreply@anthropic.com>` when AI-written.

## Review pairs (who reviews whom)

| PR from | Reviewer | Because |
|---|---|---|
| E1 generator | P2 (Diego) | gate/adapter conformance to the contract |
| E2 fidelity/VR | E1 | touches the same Unity project spine |
| E3 runtime | P2 | row-contract conformance |
| P1 UI | E1 | seam usage correctness |
| P3 Cloudflare | P2 | gate parity + schema validation |
| Contract changes (any) | P2 **+ Michael** | the change rule below |

## The contract-change rule (unchanged, now with more surface)

The contracts are: `spec/room_spec.schema.json` · `spec/study.schema.json` · `spec/response_log.schema.json` · `spec/contracts/ENGINE_SEAM.md` · `spec/contracts/ROOM_API.md` · `spec/contracts/AI_AUTHORING.md` · `spec/contracts/schema.sql` · `spec/PRESETS.md` · `spec/RESPONSE_LOG.md` · the golden fixtures in `spec/fixtures/`.

**Any edit to any of them = ONE PR that contains:** the contract edit + regenerated/extended fixtures + updates to every consuming implementation named in the affected handoffs + a version bump (`spec_version` / `seam_version` / schema `$id` as applicable) + a line in the ARCHITECTURE.md decision log if behavior changed. No exceptions, including "tiny" ones — the fixtures are what let six people work alone safely.

`diff_vectors.json` is REGENERATED (never hand-edited) by re-running the reference validator; the `_meta.reference_commit` field must move forward.

## UI surface ownership (P1 vs E3 — decided 2026-07-03)

Two UI surfaces, two audiences, one owner each — they deliberately do NOT share a design philosophy:

| Surface | Owner | Folder | Design rule |
|---|---|---|---|
| **Operator cockpit** (sliders, diff panel, publish, library) | P1 | `unity/Assets/RoomGen/UI/Operator/` | P1's design judgment governs — dense, efficient, for a researcher at a desk |
| **Participant screens** (setup, instructions, task, done) | E3 | `unity/Assets/RoomGen/UI/Runner/` | **Neutral by scientific requirement, not taste**: minimal, unthemed, identical across studies — a styled participant UI is a stimulus confound. No creativity budget here, on purpose. |
| **Shared base styles** (fonts, spacing, base USS) + the mock seam channel | P1 (styles) / E1+E3 (mock) | `unity/Assets/RoomGen/UI/Shared/` + `unity/Assets/RoomGen/Testing/` | Changes by PR reviewed by BOTH consumers; E3 consumes the shared base and strips, never adds, visual personality |

Cross-surface edits (one lane touching the other's folder) go through the other lane's owner as reviewer — same spirit as the contract-change rule.

## Nobody waits on the generator (the mock-first rule)

Lanes depend on E1's **interface** (ENGINE_SEAM.md + fixtures — frozen now), not its implementation. Concretely: P1 and E3 develop against the shared `MockSpecChannel` (built once in `Testing/`, replays fixture successes/errors); the diff panel renders validator output (exists now); E3's row writer tests against the golden rows (exist now); E2 works on the legacy studio scene (renders the dining room on first boot); P3 never touches Unity. The real-generator dependency is concentrated at ONE planned moment — the mock→real swap at the M1 tracer bullet — not spread across everyone's daily work. If a lane finds itself "waiting on E1" before M1, that's a smell: the work should be restated against the mock + fixtures, or the missing fixture should be added (contract-change rule).

## The tracer-bullet checklist (M1 exit — integration proven end-to-end)

Run top to bottom on ONE machine (the strong PC), from a fresh clone:

- [ ] `python -m pytest tests -q` → all green (Python suite incl. fixture tests)
- [ ] `python tools/validate_pair.py spec/pairs/ceiling_height_study_01/{control,treatment}.spec.json` → PASS
- [ ] Unity opens `unity/`, `RoomGen ▸ Bootstrap Project` idempotent (second run changes nothing)
- [ ] EditMode tests green: adapter (G2), PairGate vs `diff_vectors.json` (G3), seam vs `seam_messages.json` (G4)
- [ ] Load the committed ceiling pair through the seam → both rooms build; gate PASS shown; a deliberately confounded copy is REFUSED with `undeclared_change`
- [ ] Walk mode: WASD + mouse-look through the control room, no clipping; `switch_condition(fade)` → treatment
- [ ] `RoomGen ▸ Build Windows Application` → `RoomStudio.exe` runs the same on a machine WITHOUT Unity installed
- [ ] A fake-participant rating session writes a CSV that validates against `response_log.schema.json` in canonical column order

*(M2 adds: operator UI drives the same flow + publish gate blocks the confounded pair + fidelity gate verdict recorded. M4 adds: the C4 curl script green against production Cloudflare. M5 adds: the north-star VR demo per VR_LIVE_EDIT V1.)*

## Status board (Michael updates at each sync; ✅/▶/⏸/❌)

| Workstream | M0 boot | M1 tracer | M2 instrument | M3 demo | M4 library | M5 VR |
|---|---|---|---|---|---|---|
| E1 generator (Paco) | ▶ | | | | | |
| E2 fidelity/VR | ✅ | | ▶ fidelity | | | |
| E3 runtime | ▶ | | | | | |
| P1 operator UI | ▶ | | | | | |
| P2 contracts (Diego) | ▶ | | | | | |
| P3 cloudflare | ▶ | | | | (owner) | |

> **2026-07-06 — Paco's lane absorbed the E2 fidelity half (team was behind).** L0–L4 lighting sprint delivered the M2 fidelity gate: HDRP quality pass, real ambientCG PBR materials (triplanar, SHA-locked), pendant luminaire + sun, and a **verified matched-luminance result (2.4 m vs 3.2 m ceilings read within 4.7% at eye height)**. 38→49 EditMode tests green. Verdict request + renders in [FIDELITY_GATE.md](FIDELITY_GATE.md). Branch `paco/lighting-l0-l4` (unmerged). VR half (V0–V1) still open for E2. Open follow-ups: furniture models + SSGI bounce before M3.

## Escalation + unblocking

- Two failed AI attempts at the same technical problem → escalate to the high-capability planning session (don't grind; TEAM_PLAN.md).
- Blocked > 1 day on another workstream → Michael re-sequences or temporarily merges roles (the collapse order in TEAM_PLAN.md).
- Contract disputes → P2 decides; if it changes behavior, it goes through the change rule, never a side agreement.

## Definition of done (project-wide)

A thing is DONE when: its handoff's DoD checks pass · it's merged to `Diggss-sys-branch` · the status board reflects it · and nothing in another workstream had to be verbally told about it (if they did, a contract or fixture was missing — fix THAT too).
