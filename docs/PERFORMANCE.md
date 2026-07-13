# What "runnable" means — the performance gate

*A study instrument isn't "runnable" just because it launches. For a perception study, frame rate is
not polish — it's a research variable: a stuttery walkthrough changes how a room *feels*, and feeling
is our dependent variable. Worse, our conditions differ by geometry (a 3.2 m room shows more surface
and different light bounce than a 2.4 m one), so if one condition renders slower than the other, frame
rate becomes a **confound correlated with the manipulation**. This doc defines the gate and how we
measure it.*

## The three layers of "runnable"

1. **Builds & launches** — fresh clone, tests green, the exe runs on a machine without Unity. (M1
   checklist; Michael's fresh-clone + Mac smoke.)
2. **Authoring is responsive** — operator slider → room rebuild < 0.5 s. (A1 acceptance; tested.)
3. **Presentation holds frame rate** — the layer this doc adds. Numbers below.

## Targets (advisory — they colour the readout and classify a session; they don't fail a build)

| Context | Target | Why |
|---|---|---|
| Lab demo PC, walk @ 1080p | **sustained 60 fps**, no hitch > 100 ms | vsync ceiling; hitches are what people notice |
| **M2 MacBook** (participant machines) | **≥ 45 fps average, never below 30** over a 60 s walk of *both* conditions | the binding constraint — if it fails, drop the HDRP quality tier for Macs, don't change the study |
| Control vs treatment | **average fps within ~10 % of each other** | the confound check — the important one |
| VR (parked until after Aug 1) | 72 / 90 fps hard floor | headset comfort |

**Measure in the BUILT app, not the editor** — editor overhead skews the numbers low. And a beefy
gaming PC's fps tells you nothing about the Macs, which are the machines that decide this.

## How we measure it

- **Operator studio:** a live fps readout, top-right, colour-coded (green ≥60 / amber ≥45 / red below).
  Always on — including during a walk, which is when it matters. It's the operator's own gauge.
- **Participant runner:** **no on-screen counter** — that would be a distraction/confound. Instead each
  room's walk is measured silently and written to a **perf sidecar** next to the response CSV:
  `response-<id>-<stamp>.perf.csv`, one row per trial:

  ```
  session_id,participant_id,study_id,trial_index,condition,avg_fps,min_fps,hitch_count,frame_count,duration_s
  ```

  It joins onto the response rows on `session_id` + `trial_index`. This is what turns Michael's Mac
  smoke test from "walk around, felt okay?" into "read three numbers off the log."

## The per-condition confound check (do this on every pilot)

Group the perf rows by `condition` and compare average `avg_fps`:

- Within 10 % → fine, frame rate isn't tracking the manipulation.
- Diverging (e.g. treatment consistently slower) → **flag it**: the taller/curved condition is costing
  frames, and that cost rides along with your independent variable. Fix the rendering cost before
  collecting real data, don't just note it.

`min_fps` (worst single frame) and `hitch_count` catch stutters that a healthy average hides.

## Thresholds live in code

`RoomGen.Metrics.PerfTargets` (`DemoPcFps` 60, `LaptopAvgFps` 45, `LaptopFloorFps` 30,
`MaxConditionDeltaFraction` 0.10). Change the numbers there if the team revises the gate — the readout
and any future automated check read from that one place.
