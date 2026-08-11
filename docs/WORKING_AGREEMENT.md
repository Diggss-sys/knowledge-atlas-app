# Working agreement — who decides what, and how work lands

*How this team avoids two people building two futures for the same code. This is governance, not a
roadmap: the plan is [PLAN.md](../PLAN.md), the architecture is [ARCHITECTURE.md](ARCHITECTURE.md),
and what boots is [CODE_MAP.md](CODE_MAP.md).*

## Ownership

Membership is a **task pool, not fixed assignments** — availability varies week to week, so people
claim a bounded task when they have time. What is fixed is who owns each *decision*.

| Area | Owner | Boundary |
|---|---|---|
| Engine, geometry, HDRP/lighting, furniture placement, experiment contracts | **Diego** | Final owner of engine interfaces and contract decisions. Schema, fixture, and declaration-rule changes go through him. |
| UI flow, integration, author→CSV verification, demo readiness | **Paco** | Builds against *confirmed* engine interfaces. Does not independently change contract rules. The operator UI lane is currently delegated — see [handoffs/UI_LANE_HANDOFF.md](../handoffs/UI_LANE_HANDOFF.md). |
| Planning, PR review, scope and priority | **Paco** | Keeps one decision record and surfaces conflicts *before* implementation starts. |
| Study and fidelity review | Any available teammate | Hypotheses, participant wording, room screenshots/video, possible confounds. **No Unity required.** |
| Test and demo review | Any available teammate | Follow repeatable test scripts; report exact steps, machine, and results. |

## Working rules

1. **One owner of record per decision.** Engine and contracts: Diego. UI and integration: Paco.
   Everything else is reviewed through PRs with documented acceptance checks.

2. **Planning documents live on `main`.** A decision recorded on an unmerged branch is a decision
   nobody made. In August 2026 an architecture doc sat on a branch for two weeks while someone
   planned six sprints against the surface it scheduled for retirement — both people were working in
   good faith and neither could see the other. If a document governs what other people build, merge
   it the day you write it.

3. **If a feature depends on an engine capability that does not exist, stop.** Confirm the interface
   with its owner before building UI around it. Guessing at method names and data shapes produces
   work that has to be thrown away.

4. **A feature is not done until it has an acceptance check on a real build.** Green tests are not
   the gate for anything visual — UI Toolkit renders black in batchmode, and headless has no
   ray tracing. Judge visual output in the editor or from a real build, never from the suite alone
   (see [CODE_MAP.md](CODE_MAP.md) §4).

5. **Keep work isolated on branches, and don't edit the same checkout from two places at once.**

6. **The single-variable gate stays in parity across all three ports.**
   [`tools/validate_pair.py`](../tools/validate_pair.py) is the reference; `PairGate` (runtime) and
   `PairValidator` (editor) must reproduce it against the committed golden vectors. A change to one
   is a change to all three plus the fixtures, and it is a contract change requiring Diego's review.
