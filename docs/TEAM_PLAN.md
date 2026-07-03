# TEAM_PLAN — who does what, and how humans + AIs work together

*Written 2026-07-02 (DL-14). Seven people: an Engine trio, a Platform trio, and Michael floating between them as integrator. Suggested assignments below are proposals — Diego (repo owner) confirms/reassigns names; the ROLES are fixed by the architecture. Each role maps to exactly one handoff file in [../handoffs/](../handoffs/).*

## Principles (why it's shaped this way)

1. **One workstream = one human + their AI + one handoff file.** Diego's established model: a partner reads ONLY their handoff + the contracts it names, and can execute without this conversation's context. Nobody needs to hold the whole system in their head — the contracts are the coupling.
2. **The two trios meet only at contracts.** Engine (in-Unity) and Platform (contracts/data/UI) can progress in parallel all week; integration happens at the tracer-bullet checklist, not in daily coordination.
3. **Fixtures make solo work honest.** Every workstream's definition-of-done runs against committed golden fixtures (`spec/fixtures/`) — you can be DONE without waiting on anyone.
4. **Nobody is "the one who does most of the work."** The plan deliberately breaks the solo-hero pattern: the critical path (generator reconciliation) is one workstream among six, and the integrator role exists precisely to keep progress distributed.

## The teams

### Engine trio — inside the Unity app (the strong Windows PC)

| Role | Workstream (handoff) | What they own | Suggested |
|---|---|---|---|
| **E1 · Generator steward** | [UNITY_GENERATOR.md](../handoffs/UNITY_GENERATOR.md) | RoomSpecAdapter (KA schema → generator), desktop walk mode, shell/openings/furniture per PRESETS.md, the C# pair gate | **Paco** (it's his code) |
| **E2 · Lighting & fidelity** | [VR_LIVE_EDIT.md](../handoffs/VR_LIVE_EDIT.md) §fidelity + VR phase | HDRP light rig from `lighting.*`, materials/texture playbook, the fidelity gate, matched-luminance calibration; later: the PCVR arm + comfort | engine-curious teammate |
| **E3 · Experiment runtime** | [EXPERIMENT_RUNTIME.md](../handoffs/EXPERIMENT_RUNTIME.md) | Participant flow (id → instructions → rooms → task), seeded ordering, response rows, CSV + POST, the Windows build | teammate comfortable with app logic |

### Platform trio — contracts, UI, data

| Role | Workstream (handoff) | What they own | Suggested |
|---|---|---|---|
| **P1 · Operator UI** | [UNITY_UI.md](../handoffs/UNITY_UI.md) | UI Toolkit editor panel (sliders within preset ranges), live diff panel, publish gate, save/load/library browse | design/UX-leaning teammate |
| **P2 · Contracts & validation** | fixtures + schemas + gate parity (spread across handoffs; referee duties in [COORDINATION.md](../handoffs/COORDINATION.md)) | The schemas, `validate_pair.py` stewardship, golden fixtures, the v1.1 batch, C#/JS gate conformance review | **Diego** (owns the contract) |
| **P3 · Data & library** | [CLOUDFLARE_DATA.md](../handoffs/CLOUDFLARE_DATA.md) | Worker + D1 + R2 per ROOM_API.md, the JS pair gate, CSV export, deploy runbook | teammate with any web/JS exposure |

### The floater

| Role | What they own | Suggested |
|---|---|---|
| **M · Integrator / QA / referee** | Merges PRs into `Diggss-sys-branch`, runs the tracer-bullet checklist, enforces the contract-change rule, owns the Kirsh demo script, unblocks whichever trio is behind | **Michael** (already bridges both halves) |

**If someone finishes early or a role is unstaffed:** collapse in this order — E2's fidelity work folds into E1; P3 defers to local-CSV-only (the demo doesn't need Cloudflare, DL-5); P1's library browse defers to file-open. The tracer bullet only truly requires E1 + E3 + P2.

## The AI operating model

Every workstream runs as **human + AI pair**, and capability is allocated by difficulty, not politeness:

| Layer | Who | Used for |
|---|---|---|
| **Planning & architecture** | Fable (high-capability session) | This package, contract changes, replans, integration disputes |
| **Implementation** | Opus (each teammate's own session) | Executing a handoff: writing the C#/JS/Python it specifies, tests-first against fixtures |
| **Escalation** | Fable | Anything an implementation session thrashes on — HDRP internals, mesh generation edge cases, seam concurrency. Rule of thumb: two failed attempts at the same problem → escalate, don't grind. |

**How a teammate starts (the onboarding script, zero Unity experience assumed):**
1. Clone the repo, check out `Diggss-sys-branch`, read `PLAN.md` (5 min).
2. Open YOUR handoff file. Feed it + the contracts it names to your AI session verbatim ("implement this handoff; work milestone by milestone; test against the fixtures it names").
3. Engine-trio only: install Unity Hub → Unity **6000.3.16f1** → open `unity/` → wait for packages → run menu `RoomGen ▸ Bootstrap Project` once → press Play.
4. Work on a feature branch; PR to `Diggss-sys-branch`; Michael merges (COORDINATION.md).

**Human review is not optional:** the AI writes, the teammate runs the acceptance checks in their handoff's definition-of-done and eyeballs the result before PRing. A green fixture suite is the floor, not the ceiling.

## Cadence (light on purpose)

- **Two syncs/week** (~20 min): each role reports against their handoff's milestone ladder — done / blocked / next. Michael keeps the [COORDINATION.md](../handoffs/COORDINATION.md) status board current.
- **Contract changes never happen in a sync.** They follow the written rule: one PR touching the contract + both affected workstreams' fixtures + a version bump, reviewed by P2 (Diego).
- **Escalations go to the group chat the day they happen**, not the next sync.

## Timeline (DL-13; details in PLAN.md roadmap)

| Week | Milestone | The team-level bar |
|---|---|---|
| Jul 2–8 | **M0 Land + boot** | Package merged; every Engine member has Unity open and the dining room rendering; every Platform member has the fixture suite green locally |
| Jul 9–15 | **M1 Tracer bullet** | The committed ceiling pair, loaded from `spec/pairs/`, walkable on desktop, gate-enforced, `.exe` built |
| Jul 16–22 | **M2 Instrument** | Operator UI live-edits + diff panel + publish gate; rating task writes valid rows; fidelity pass 1 judged |
| Jul 23–Aug 1 | **M3 V1 demo** | End-to-end: author → validate → publish → run → CSV; curved-wall second pair; **Kirsh demo** |
| August | **M4 Research-grade + library** | Matched luminance, v1.1 batch, Cloudflare library live |
| Aug–Sep 15 | **M5 VR arm** | PCVR live-edit (headset-dependent); end-of-summer close-out |
