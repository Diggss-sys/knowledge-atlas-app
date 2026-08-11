# REPLAN — July 2026: from six lanes to one instrument and a study

> **ARCHIVED 2026-08-10 — superseded, kept for provenance.** This was the execution plan for July;
> the lane collapse it describes did happen, and it is why the repo looks the way it does. It is not
> current instruction: its dates, task assignments, and open questions have all been overtaken.
> Decision ownership and the rules that land work now live in
> [docs/WORKING_AGREEMENT.md](../../docs/WORKING_AGREEMENT.md); what boots and which UI surface owns
> which job is [docs/CODE_MAP.md](../../docs/CODE_MAP.md).

*Fable, 2026-07-09, at Paco's request. The end goal is unchanged (Kirsh demo ≈ Aug 1: a student
authors a one-variable room pair, walks it, runs a participant, gets clean data). What changes is
the shape of the work. This invokes TEAM_PLAN.md's own collapse mechanism — it is the plan working,
not the plan failing.*

## The honest diagnosis

The 6-lane structure assumed seven people building in parallel. Reality after two weeks: the
Paco+Claude lane built the generator, the gate, the seam, the fidelity package, the runtime core,
and the operator-UI foundation (38 → 92 automated checks); Diego merged PR #1 and has been quiet
since; nobody else has opened the repo. The lanes didn't fail because the docs were bad — they
failed because most of the team builds *studies*, not *software*. So: stop splitting engineering
that one lane can finish, and give everyone else the work the project scientifically needs anyway.

## New structure: two tracks

**Track A — finish the instrument (Paco + Claude; Diego reviews).** Two build items remain, specced
below so an Opus session can execute either without replanning.

**Track B — run the science (everyone).** The moment Track A ships, the team's job is: design the
demo study, pilot it on each other, judge the evidence, and prepare the demo. No Unity knowledge
required for any of it.

## Phases to Aug 1

### Phase 1 — unblock (this week)
- **Diego:** merge PR #2 ("Create a merge commit", not squash). *Fallback the team should agree to
  at the next meeting: if the PR sits idle 5 more days, Michael or Paco merges it — a demo deadline
  outranks review ceremony on an already-tested branch.* Also: the two standing contract calls
  (warmth label/formula; coupled-vars notes).
- **Michael:** the two reality checks — (1) fresh-clone test per docs/GETTING_STARTED.md,
  (2) the M2 Mac smoke test: build `RoomGen ▸ Build macOS Application` on a lab MacBook, launch,
  walk the pair, report. This unlocks Macs for the whole team.

### Phase 2 — the frontend for PR #2 (Track A, ~1 week)
Build item **A1 — Operator Studio scene (interactive)**: a scene wiring the existing
OperatorPanel (UXML/USS/view-model) to the real engine — `LocalChannel` + two `PreviewRenderer`s —
so dragging a slider rebuilds the previewed rooms live, SubmitPair drives the real gate, and a
"Walk" button enters DesktopWalkMode. Ships beside the legacy IMGUI studio (additive scene, no
replacement until Diego agrees). Acceptance: slider→room updates < 0.5 s; confounded pair shows
red verdict + publish stays locked; walk works from the panel. *(All components exist and are
tested; this is composition, not invention.)*

Build item **A2 — minimal participant flow (R2-lite)**: the four neutral screens (id entry →
instructions → walk control/treatment with dwell timer → rating screen ×2 → done) driving
StudyRunner's real pipeline to a CSV. Neutral styling per COORDINATION's rule. Acceptance: a human
completes a session in the built exe; the CSV validates against the fixtures.

With A1+A2 the M1 tracer bullet is fully closed and the instrument is *complete* for the demo's
purposes: author → validate → publish → run → data.

### Phase 3 — the pilot study (Track B, ~1 week, overlaps A2)
- **Study design (2 teammates, no code):** pick the demo experiment (ceiling pair is the flagship;
  curved-wall as backup), write the rating prompt + trials config, define the hypothesis. Deliver:
  one study JSON authored *through the operator UI* — which doubles as our usability test.
- **Piloting (everyone):** each teammate runs one session as operator and sits as participant in
  another's. Target: 5+ clean CSVs. File issues by describing what confused you — that's the
  feedback we can't generate ourselves.
- **Evidence & verdicts (1 teammate):** the M2 fidelity-gate verdict is still open — review
  docs/fidelity/ and call it. Then own the demo-day evidence pack (renders, calibration report,
  a sample CSV).

### Phase 4 — demo prep (final week)
Demo script (author → confound blocked → fix → publish → participant → CSV, in 10 minutes),
presentation/report writing (Track B), rehearsal on the lab PC + one Mac, bug-fix buffer (Track A).
The team hub carries the countdown; Anti syncs it from `team_hub/data/`.

## Who does what (one line each)
- **Paco + Claude** — A1, A2, bug fixes, demo rehearsal. (Model economy: Opus executes A1/A2 from
  this spec; Fable only for review gates and replans.)
- **Diego** — merge PR #2 now; contract calls; engine changes only via PRs so Track A stays stable.
- **Michael** — fresh-clone check, Mac smoke test, then "release manager": every build that goes to
  a teammate passes through his hands.
- **Everyone else** — Phase 3 roles above: study design, piloting, evidence verdicts, writing. Claim
  one at the next meeting; none require touching Unity.

## What we explicitly stop doing
- Pretending six parallel engineering lanes exist (VR stays parked until after Aug 1; Cloudflare
  Worker only if someone actively wants it — local CSV is the demo path, per DL-5).
- Waiting on lane owners: unclaimed engineering folds into Track A by default.
