# Team-hub changelog — append-only narrative log

*Claude appends one entry per work checkpoint (newest first). Antigravity reads this for the
"what happened lately" narrative; `status.json` carries the structured state. Neither is ever
rewritten retroactively — append only.*

## 2026-07-22 — PR #4 closeout: status honesty pass (GPT-reviewed)
- A closeout audit (first review from the new GPT-planner loop, Opus-executed) found the hub was
  overclaiming Mac readiness: `mac_status: "live"` implied a verified-good state, but we only ever
  had a PRE-FIX session (5.7 fps) — no post-fix Mac data exists yet. Corrected to
  `"launch_verified_perf_pending"` with an explicit meaning field, everywhere the hub/docs state it.
- Swept every doc for the same staleness pattern: `status.json`, `index.html`, and
  `docs/GETTING_STARTED.md` all still pointed at PR #3 as the open PR (it merged Jul 14), still
  carried Diego's now-done "M2 Mac smoke test" ask, and GETTING_STARTED's Path C still described the
  Mac app as "waiting on one test" — all replaced with PR #4 (open, awaiting review) and the honest
  Mac state. Test counts bumped 106→108 EditMode (109→111 total) to match the real suite.
- 108 EditMode + 3 PlayMode still green after the doc-only changes (no code touched in this pass).
- **The one remaining external item, unchanged by this pass:** a post-fix Mac performance re-test.
  Nothing here can substitute for that — it needs a human on real Mac hardware.

## 2026-07-20 — first Mac test back (Raven) → Mac perf tier + fixes → Mac is live
- **Raven ran the macOS build on an M3 Air (macOS 14.6)** — full session, all rows valid. The perf
  sidecar did its job and caught the real problem: the app ran at ~5.7 fps because it used the
  full-desktop quality profile at retina resolution. Added a Mac performance tier (lower resolution +
  cheaper shadows on Apple Silicon; Windows unchanged), fixed the "Open results folder" button on
  macOS (path-escaping bug), and rewrote the Mac launch steps (System Settings → "Open Anyway" first,
  Terminal `xattr` fallback — right-click → Open is unreliable on macOS 14+).
- Rebuilt the Mac app, swapped the release asset, `mac_status` is now **live**. 108 EditMode + 3
  PlayMode green. Re-tests welcome — each one logs its own frame rate so we can confirm the tier holds.

## 2026-07-10 (later) — Team Run app RELEASED · author→run loop closed · Mac build
- **Team Run app is downloadable.** Published a private prerelease (`team-run-2026-07-10`) with the
  double-clickable participant study app. **Windows** build verified rendering (RTX 5070 Ti) and
  released; **Mac** build added — universal (Apple Silicon + Intel), packaged Mac-launchable, attached
  and *pending a first-launch check on a real Mac* before we tell the team. Download links + unsigned
  first-launch steps are in docs/TEAM_RUN.md; the hub's Team Run section can now show real downloads
  (see `data/status.json` → `team_run`).
- **Author → run loop closed.** The operator's Publish button now emits a real study document (gated —
  a confounded pair is refused) that the participant app runs, instead of only the bundled fixture.
- **Perf sidecar now stamps the machine** (GPU/OS/model) so the cross-machine team-run numbers are
  attributable; the participant done-screen gained an "Open results folder" button.
- 109 automated checks green (106 EditMode + 3 PlayMode). A render-path note for Diego was drafted
  (RT stays an optional demo-PC tier; the Macs and the study run on raster).

## 2026-07-10 — PR #2 merged · the instrument shipped (A1+A2, PR #3) · Team Run prep
- **Diego merged PR #2** (merge commit `284a152`) — the engine/fidelity/runtime package is on `main`.
  He's now building an engine realism pass (curved walls, time-of-day sun, SSGI) on his branch.
- **A1 + A2 shipped and PR #3 opened**: the interactive Operator Studio (live previews, real
  single-variable gate, publish lock, walk button) + the Participant Runner (neutral screens →
  walk each room → rate → validated CSV). M1 tracer closed end-to-end; both scenes verified on a
  real display; a Fable review found and fixed 5 defects (verdict table in handoffs/archive/A1_A2_REVIEW.md);
  108 automated checks green. Operator UI restyled to the team-hub design system.
- **Performance gate defined** (docs/PERFORMANCE.md): 60 fps demo PC / 45–30 M2 Macs / conditions
  within 10% of each other. Operator studio shows a live fps readout; participant sessions silently
  log per-room frame rate + machine to a `.perf.csv` sidecar next to the response CSV.
- **Team Run staged** (docs/TEAM_RUN.md + hub section): everyone runs one participant session on
  their own machine and sends back 3 files — our cross-machine baseline BEFORE Diego's rendering
  changes land. Windows participant app is built but download stays blocked until it's verified to
  render (lesson of the week: builds that pass tests can still show a black screen).
- Serviced Anti's REQUESTS.md (status.json/CHANGELOG were stale on the PR #2 merge — Anti caught it
  and correctly filed a request instead of inventing content: the sync contract working).

## 2026-07-09 — the July replan: two tracks
- Honest reset (handoffs/REPLAN_JULY.md): the 6-lane structure assumed seven engineers; reality is
  one build lane + a reviewer. Track A (Paco+Claude) finishes the instrument — two build items left
  (A1 interactive operator studio, A2 minimal participant flow). Track B (everyone) runs the
  science: study design, piloting on each other, gate verdicts, demo writing — no Unity required.
- This week's unblocks: Diego merges PR #2 (5-day fallback agreed?); Michael runs the fresh-clone
  check + the M2 Mac smoke test.
- VR and Cloudflare are parked until after the Aug 1 demo; local CSV is the demo data path.

## 2026-07-08 (later) — Anti's hub design lands; content corrected
- Antigravity shipped the hub design in `team_hub/` (GSAP loader + cascade, cream/forest palette,
  side-nav layout) — design approved by Paco and kept as-is.
- Claude corrected the page CONTENT (it had been authored, not synced): Mac install now matches
  GETTING_STARTED.md (Unity from source — not "Python only"), branch guidance fixed, progress
  table owners/states made truthful, PR #2 top-ask + gate-verdict + Mac-volunteer asks added,
  source-of-truth callout + Updated pill added.
- Contract v2: data moved to `team_hub/data/` (old `teamhub/` removed); ANTI_README.md rewritten —
  the rule is now "sync, don't author". Pages deploy workflow switched to MANUAL only (privacy:
  public internet exposure needs a team decision). Reduced-motion guard added to the page.

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
- Lighting sprint planned (handoffs/archive/LIGHTING_SPRINT.md) and executed through the fidelity gates.
