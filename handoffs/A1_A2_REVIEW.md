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

---

## Fable review verdict (2026-07-10) — PASS after fixes

*Method: verified the gate semantics, light-layer masking, adapter tolerances and panel styling against
the actual code, then hunted specifically in what the tests structurally cannot see (screen rendering,
input-driven walk, test isolation). Four findings survived verification; all four fixed in the review
commit; suites re-run green (97 EditMode + 3 PlayMode).*

| # | Severity | Finding | Fix |
|---|---|---|---|
| F1 | **Critical (demo-blocking)** | Both production `PanelSettings` assets had `clearColor=true` + an opaque clear value (cargo-culted from the RT capture tests, where it's correct). A screen-space panel clears the whole backbuffer every frame — even with its root `display:none` — so **"Walk this room" / "Enter room" showed a solid cream screen instead of the room.** Sat exactly in the one acceptance criterion no automated test covers (walk needs input devices). | `m_ClearColor: 0` in both assets + both scene-setup generators; new `ScenePanelSettingsTests` EditMode gate so it can't regress. UI look unchanged (both UI roots are opaque + full-bleed). |
| F2 | Moderate (UX honesty) | Warmth/intensity sliders drag freely but are wired to nothing (no preset range) — a silent no-op control mid-demo misleads the operator. | Binder now disables any slider it can't wire; test added. Re-enabling = add ranges to the preset (contract change, Diego's call). |
| F3 | Minor (provenance honesty) | Every plain apply carried the **fixture's** experiment block (`pair_id: ceiling_height_study_01`) into the session provenance JSONL — the log that exists to be the faithful record of operator actions. | `StudioSpecChannel.BuildBase` strips `experiment`; `LoadPair` stamps the real declaration as before. |
| F4 | Minor (test isolation) | `RoomStudioBootstrap` auto-spawned the full legacy IMGUI studio inside every PlayMode test (`InitTestScene…` doesn't match the scene guard), building its bundled pair on layers 8/9 at the origin — superimposed on the tests' own preview rooms. Pre-existing, benign today, but the preview assertions were less isolated than they looked. | Guard now also skips `InitTestScene*`. |

| F5 | **Critical (found live, 2026-07-10)** | The batchmode scene generator serialized the UIDocument's `m_PanelSettings` as `{fileID: 0}` in BOTH scenes (the `visualTreeAsset` assignment survived; `panelSettings` didn't). The panel bound to a **detached root** — booted fine, previews spawned, every test green, and the screen showed nothing. Compounding it: with the panel absent and both preview cameras targeting RenderTextures, the display had **no camera at all** ("No cameras rendering"). | Three layers: (1) scene YAMLs fixed to reference the PanelSettings assets; (2) generators now write the reference through `SerializedObject` (guaranteed to serialize); (3) editor-only self-heal in `Start()` + a hard error if still null. Plus a **backdrop camera** per scene (clears to the panel colour, draws nothing, steps aside during walks) so the display always has a camera. New gates: scene-YAML must carry the reference; some camera must render to the display after Boot. |

**Live verification (driven end-to-end in the editor, 2026-07-10):** panel renders on Play (warmth/
intensity correctly disabled, publish locked) → **Walk this room** puts you inside the treatment room →
**Esc** returns to the panel. Screenshots in the session log. This was the first time the built scenes
ran on a real display — the headless suite structurally cannot see this class of bug, which is exactly
where F1 and F5 both lived.

**A2 live verification (full session on a real display, 2026-07-10):** launched via
`RoomGen ▸ Live Smoke ▸ Play Participant Runner` and driven as a real participant: typed id `P42` →
instructions → per room (Enter room → walked → Esc → rated) × 4 → "Session complete" with the CSV path
shown. The scene loaded with a correct PanelSettings straight from the fixed YAML (no self-heal
warning). Resulting CSV: 4 validated rows, canonical columns, session GUID, seeded order
control/control/treatment/treatment for seed 584989 (a different order than participant P01 — the
per-participant seeding demonstrably works), the exact ratings clicked (6/3/7/2), and REAL rt_ms
values (47 s on the first slow trial, ~2.5 s on the fast ones). No condition label ever visible to
the participant. The `LiveSmoke` editor menu (RoomGen ▸ Live Smoke) is committed for repeating these
real-display checks — one click per scene.

**Verified sound (no action):** the stamped experiment block matches `PairGate` exactly (`experiment`/
`provenance` are non-stimulus prefixes; `IsCovered` is exact-or-prefix); lights are layer-masked
(`LightingSystem` sets `cullingMask = 1 << layer`) so the overlapping-rooms design has no cross-lighting;
`ParticipantSession` rows are shape-identical to `StudyRunner`'s; id sanitization and the stable
per-participant seed are correct; `runner.uss` is genuinely neutral.

**Noted, deliberately not fixed (decisions, not defects):**
- **Dwell timer** (REPLAN's A2 sketch mentions one): a participant can Esc after 2 seconds and rate.
  The right minimum-dwell is a study-design call → Track B decides the threshold, then it's a ~10-line
  change gating the rating buttons.
- **`Camera.Render()` in `PreviewRenderer` is a no-op under HDRP** (SRP doesn't support manual render).
  Previews work because RT cameras render every frame. Consequences: don't ever "optimize" by disabling
  those cameras, and two 720×460 HDRP renders run per frame (three during walk) — fine on the lab PC,
  watch it on the M2 Macs.
- `OperatorStudio.Update` refreshes the diff list every frame (allocation churn) — same pattern as the
  pre-existing controller; harmless at this scale.
- A2 hardcodes the fixture study; the publish→consume loop (A1 `StudyPublisher` → A2 reads it) is the
  natural next step, as noted above.
