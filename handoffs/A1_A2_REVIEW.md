# A1 + A2 review brief — for Paco, then Fable

*Track A of REPLAN_JULY.md is done: the instrument is complete for the demo (author → validate →
publish → run → data). Both items are on `paco/ui-foundation` (A1 = `0bdd136`, A2 = `26e7984`),
pushed. Tests: **94 EditMode + 3 PlayMode green**. All additive — no engine/contract/preset edits.*

## What shipped

- **A1 — interactive Operator Studio** (`Scenes/OperatorStudio.unity`). The UXML/USS panel + view-model
  drive the **real** engine: a slider rebuilds two live preview rooms, "Validate pair" runs the real
  single-variable gate (confounded → red + publish locked), "Walk this room" enters DesktopWalkMode.
- **A2 — participant runner** (`Scenes/ParticipantRunner.unity`). Four **neutral** screens (id →
  instructions → walk+rate per trial → done) drive the real response pipeline to a CSV that validates
  against `response_log.schema.json` in canonical column order.

## How to launch (Paco, on a machine with Unity)

1. Open `unity/` in Unity 6000.3.16f1.
2. Menus (already run once, but here for regeneration): **RoomGen ▸ Setup Operator Studio Scene** and
   **RoomGen ▸ Setup Participant Runner Scene**.
3. Open either scene under `Assets/RoomGen/Scenes/` and press **Play**. `RoomStudio.unity` (the legacy
   IMGUI studio) is untouched and still works.

Proof captures: `unity/captures/capture-ui-operator-studio.png`, `capture-ui-participant.png`.

## Where to look hardest (Fable — ruthless review targets)

1. **`StudioSpecChannel` (A1's crux).** The panel edits only shell/surfaces/lighting, so the view-model
   emits a *partial* spec — no `spec_version`/`room_type` (schema-required) and no `experiment` block
   (gate-required). The mock-only EditMode tests never surfaced this. The decorator completes the spec
   against a base template and, on `load_pair`, stamps `experiment`
   (`condition` by arg order, `manipulated_variables=[declared]`, shared `pair_id`). **Is the
   composition layer the right home, and does the stamped experiment faithfully match `PairGate`?**
   Edge to probe: a declared variable with no slider (contour / wall material / warmth) → the gate
   returns `declared_unchanged` (correct, but is that the UX we want?).
2. **`RoomStudioBootstrap` scene guard.** The one file touched in Diego's `Studio/` area: it now skips
   auto-spawning the IMGUI studio in the two additive scenes. Non-destructive (RoomStudio behaves
   identically) but **Diego should sign off** — it's his lane.
3. **A1 layers / walk target.** Previews on 8/9, the seam room (RoomRuntime, validation + atomic-apply
   only, no camera) on 10; Walk enters the treatment **preview** room. Confirm no phantom-collision or
   double-build surprises.
4. **A2 pipeline fidelity.** `ParticipantSession` mirrors `StudyRunner` but with real ratings. Confirm
   the rows are byte-identical in shape to the fake-responder rows (same columns, same validation),
   and that participant-id sanitization + the stable per-id seed are sound.
5. **Neutrality.** `UI/Runner/runner.uss` must stay grayscale/brand-free (stimulus-confound rule).

## Known follow-ups (not blockers)

- **Lighting warmth/intensity sliders are inert** in the operator panel — they're in the UXML but not
  in the preset `ranges`, so the binder never wires them. Wiring needs a preset (contract) change →
  Diego's call, deferred. Geometry (the flagship ceiling variable) is fully wired.
- **Narrow-width furniture:** shrinking room width far enough makes the base furniture invalid and the
  seam correctly refuses the apply (surfaced verbatim in the panel). Correct behavior; note for the
  demo script (drive ceiling height, which is always safe).
- **`Scenes/` was untracked** before this — `RoomStudio.unity` was never committed (the runtime
  bootstrap made it optional). A1/A2 add the first tracked scenes; if we want the demo scene in git too,
  that's a separate small commit (Michael/Diego's call).
- **`rt_ms`** is recorded from the rating-screen dwell; it's 0 in the synchronous tests, positive in a
  real session.

## Not in scope (per REPLAN)

VR (parked until after Aug 1), Cloudflare Worker (local CSV is the demo path), and A1's "Publish study"
button currently logs + is gated by the verdict — wiring it to `StudyPublisher` to emit a study JSON is
the natural next small step if we want the operator to author the very study A2 consumes.
