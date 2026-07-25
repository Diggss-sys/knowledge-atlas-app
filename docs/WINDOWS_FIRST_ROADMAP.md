# Windows-first roadmap

*Current planning reference — updated 2026-07-24. This document replaces the old deadline-driven staffing plan for day-to-day decisions. It does not change the RoomSpec, fixtures, or study contracts.*

## The product we are finishing

Knowledge Atlas is a Unity HDRP room-study instrument. A researcher must be able to:

1. customize a room and place furniture;
2. create a valid control/treatment pair that differs only in the declared variable;
3. publish that pair;
4. run a participant through it; and
5. save usable response data.

The immediate delivery path is a polished, functional **Windows** app. Native Mac optimization and formal piloting are deferred, not cancelled. Once the Windows experience is stable, we will test Mac access to the Windows GPU host; an hourly cloud GPU is only a fallback.

## Current state

- The Windows engine realism work and the author-to-participant-to-CSV flow are integrated on `main`.
- Diego is actively building the furniture-placement system.
- PR #6 (`paco/pair-contract-parity`) makes the runtime pair gate match the editor and Python reference validator. Diego owns the final contract review before merge.
- The native Mac package/performance work is parked. Do not start new Mac release or optimization work during the Windows-first phase.

## Delivery path

```text
Room-building loop
    -> Study loop
        -> Windows demo verification
            -> Wider delivery access
```

### Milestone 1 — room-building loop

**Goal:** A user can make and preserve a believable room layout.

Done means the user can choose room options; add, move, rotate, and remove furniture; receive clear invalid-placement feedback; and save/reload the same layout.

**Current critical path:** Diego completes the placement engine and publishes the UI-facing interface. The UI must use that confirmed interface rather than guessing engine methods or data shapes.

### Milestone 2 — study loop

**Goal:** The complete research workflow works with a furniture-enabled room.

Done means an author can make control/treatment rooms, validate the declared difference, publish, run a participant through the assigned condition, collect ratings, and produce complete CSV rows.

The runtime gate must remain in parity with `tools/validate_pair.py`, editor validation, schemas, and committed fixtures. Any schema, fixture, or declaration-rule change follows the contract-change process and requires Diego's review.

### Milestone 3 — Windows demo-ready

**Goal:** The full loop works reliably on the actual Windows demo PC.

Done means the team has run author -> validate -> publish -> participant -> CSV repeatedly; recorded demo-PC performance; resolved use-blocking bugs; and rehearsed a short operator/demo script. The visual target should be judged through real screenshots/video and actual display testing, not only the Unity editor.

Graphics and fidelity improvements belong here, after the furniture system and study loop are connected. The bar is a reliable, understandable experience, not perfect visual polish.

### Milestone 4 — delivery expansion

**Goal:** Give Mac-heavy team members practical access without derailing Windows development.

First test a Mac controlling or streaming from the Windows GPU host on the same network: video clarity, input responsiveness, setup simplicity, and the full study loop. Consider an hourly cloud GPU only if self-hosted Windows access is insufficient. Revisit native Mac builds after the Windows product and delivery path are stable.

## Who owns what

| Area | Owner | Boundary |
|---|---|---|
| Windows engine, geometry, HDRP/lighting, furniture placement, experiment contracts | Diego | Final owner of engine interfaces and contract decisions. |
| UI flow, integration, author-to-CSV verification, Windows demo readiness | Paco + Claude | Build against confirmed engine interfaces; do not independently change contract rules. |
| Project planning, PR review, study-method review, priority and scope control | Paco + Codex | Keep one decision record and identify conflicts before implementation. |
| Study/fidelity review | Any available teammate | Review hypotheses, participant wording, room screenshots/video, and possible confounds. No Unity work required. |
| Test and demo review | Any available teammate | Follow repeatable test scripts; report exact steps, machine, and screenshots/results. |
| Mac access/streaming review | Any available teammate | After Windows smoke is stable, test Mac-to-Windows GPU-host access. |

Team membership is intentionally a task pool rather than fixed assignments: availability is unknown. People claim a bounded task when available.

## Immediate next actions

1. **Diego:** finish the furniture-placement engine and give the UI a confirmed interface/acceptance checklist.
2. **Diego:** review PR #6 before merge; decide any future group-variable or array-declaration policy through the contract process.
3. **Paco + Claude:** prepare the furniture UI integration plan, then connect it only after the engine interface is confirmed.
4. **Paco + Claude:** run and document the full Windows author-to-CSV smoke once furniture placement is integrated.
5. **Team:** use the Team Hub for the Windows-first status; claim a study review, fidelity review, test, or future access task when available.

## Working rules

- One owner of record per decision: Diego for engine/contracts; Paco for UI/integration; the team reviews through PRs and documented acceptance checks.
- Keep work isolated on branches. Do not edit the same checkout at the same time.
- A feature is not done until it has an acceptance check on the real Windows build or demo PC where appropriate.
- Do not restart native Mac work, formal piloting, cloud deployment, VR work, or broad library work until the current milestone makes it relevant.
- If a proposed feature depends on an engine capability that does not exist, stop and confirm the interface with Diego before building UI around it.

## Related references

- [Team Hub](../team_hub/) — current at-a-glance status and task pool.
- [RoomSpec](../spec/ROOM_SPEC.md) — the room contract.
- [Engine seam contract](../spec/contracts/ENGINE_SEAM.md) — integration boundary.
- [Pair validator](../tools/validate_pair.py) — reference behavior for controlled pairs.
- [Legacy team plan](TEAM_PLAN.md) and [July replan](../handoffs/REPLAN_JULY.md) — historical context; not the current execution plan.
