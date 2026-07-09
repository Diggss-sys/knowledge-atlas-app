# Team-hub changelog — append-only narrative log

*Claude appends one entry per work checkpoint (newest first). Antigravity reads this for the
"what happened lately" narrative; `status.json` carries the structured state. Neither is ever
rewritten retroactively — append only.*

## 2026-07-08 — UI polish, Mac path, hub handoff
- Slider redesign in the operator panel (teal fill, round thumb, quiet read-outs) + preview camera
  now level so the ceiling-height manipulation is visible in the panes. 92/92 + play-mode capture.
- macOS build target added (`RoomGen ▸ Build macOS Application`); `docs/GETTING_STARTED.md`
  committed (Mac-from-source works today; .app awaits one M2 smoke test).
- Hardware verdicts recorded: M2 MacBooks can run the desktop arm; supercomputer not needed for
  runtime (GPU nodes only if batch generation ever revives); tripwire = M2 holds 60fps @1080p.
- **Team-hub ownership moves to Antigravity** (scope-limited to `teamhub/`). Contract: render only
  from `teamhub/status.json` + this changelog; never invent status.

## 2026-07-07 — engine lanes complete, UI foundation, PR #2
- PR #2 opened (fidelity gate L0–L4 + furniture models, E3 runtime core R0/R1, 3 experiment pairs,
  adversarial review with 7 fixes). 38 → 92 tests over the run.
- Walk-mode input bug root-caused (activeInputHandler=0) and fixed in bootstrap; exe rebuilt.
- Studio demo sliders upgraded (3 manipulable variables, shared room/lighting sliders, more floors).
- Software/UI foundation S0–S3 built (MockSpecChannel, OperatorPanelViewModel, panel + binder,
  StudyPublisher), then the panel got the lab theme + live control/treatment room previews.
- Decisions: P1 unowned → Paco carries it; Unity-native UI confirmed over web; AI copilot will use
  the professor's API key; Cloudflare = save/load store only.

## 2026-07-06 — recovery + fidelity sprint start
- Local Unity source had vanished; re-cloned canonical copy, 38/38 green on fresh clone; PR #1
  confirmed merged by Diego.
- Lighting sprint planned (handoffs/LIGHTING_SPRINT.md) and executed through the fidelity gates.
