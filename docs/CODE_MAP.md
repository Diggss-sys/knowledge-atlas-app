# CODE MAP — what runs, what does what, what should die

*Written 2026-07-28 because the project had grown three overlapping room-editing UIs and four
auto-spawners, and every session was re-deriving "which file is the real one". Read this before
adding any UI or entry point. If you add or retire a surface, update this file in the same commit.*

---

## 1. What happens when you press Play

Four scenes exist. **Build Settings order** decides what a plain Play/build lands on:

| # | Scene | What it is | Status |
|---|---|---|---|
| 1 | `Scenes/RoomStudioUI.unity` | **UI Toolkit Room Studio — the current default** | ✅ current |
| 2 | `Scenes/RoomStudio.unity` | Legacy IMGUI studio | ⚠️ retiring |
| — | `Scenes/OperatorStudio.unity` | Paco's operator studio (spec-channel + publish) | ✅ current |
| — | `Scenes/ParticipantRunner.unity` | Participant flow (id → instructions → walk → rate → CSV) | ✅ current, distinct purpose |

**Four things auto-run.** This is the part that caused a day of confusion — a scene can be
"correct" and still show the wrong UI because something spawned itself on top of it:

| Spawner | Trigger | Owns |
|---|---|---|
| `Runtime/Studio/RoomStudioBootstrap` | `RuntimeInitializeOnLoad(AfterSceneLoad)` | Legacy IMGUI studio — **opt-IN to the `RoomStudio` scene only** |
| `UI/Studio/RoomStudioUiBootstrap` | same, **editor-only** | New UI Toolkit studio in every *other* scene (so an empty/Untitled scene gives you the current UI) |
| `Editor/RoomGenProjectBootstrap` | `[InitializeOnLoad]` | XR/package setup. **Was hard-assigning `EditorBuildSettings.scenes` on every editor load**, silently reverting scene-order changes. Now additive. |
| `Runtime/Lighting/QualityRig` | on room build | HDRP volume overrides (RT gated on `SystemInfo.supportsRayTracing`) |

**Historic trap:** `RoomStudioBootstrap` used to be opt-**out** (spawn the legacy studio everywhere
except a hardcoded name list). Any new scene therefore got the *old* IMGUI panel drawing a
full-screen `GUI.Box` over whatever else was there. Inverted 2026-07-28.

---

## 2. The pipeline (this part is healthy — don't churn it)

```
RoomSpec JSON ──► RoomSpecValidator ──► RoomGenerator ──► ShellGenerator / OpeningGenerator
                        │                                  FurnitureLayoutResolver
                        │                                  LightingSystem + QualityRig
                  PairValidator (single-variable gate)
                        │
                  ISpecChannel / LocalChannel (Seam/SeamContract.cs) ──► RoomRuntime
```

One geometry path, one lighting path, one validator, one seam. **All duplication is in the UI layer
above this line**, not in the engine.

Key single-sources-of-truth (reuse, never re-derive):
- `RoomSpecValidator` — legal envelope: `MinWidthM`/`MaxWidthM`, `MinCeilingHeightM`/`MaxCeilingHeightM`,
  `MaxCornerRadiusM(geometry)`. **UI sliders must read these**, not keep their own constants.
- `LightingSystem.SunTargetLuxFactor`, `CalibratedSunEuler`, `SunDaylightKelvin` — the sun's calibration.
- `FootprintPath.Build(geometry)` — the room's true footprint ring (use for any containment test).
- `LightingCalibrator.MatchTargetLux` — the matched-luminance mechanism the study depends on.

---

## 3. The actual problem: three UIs for one job

~2,700 lines across three surfaces that all "edit a room pair with live previews":

| Surface | Lines | Engine access | Unique value |
|---|---|---|---|
| `Runtime/Studio/RoomStudioController` (IMGUI) | 702 | generator directly | sun/bow/floor realism controls, VR entry, export |
| `UI/Studio/*` (RoomStudioPanel + ViewModel + Controller) | 680 | generator directly | labelled sections, live throttle, validator-clamped sliders, disabled-with-reason |
| `UI/Operator/*` | ~1,100 | **`ISpecChannel`** ← target architecture | `StudyPublisher` (publish→consume), `PerfHud` |

Correctly shared already: `PreviewRenderer`, `SliderFill`, `Shared/base.uss`.
**Duplicated:** the view-model + binder layer (`RoomStudioViewModel` ≈ `OperatorPanelViewModel`).

### Target: ONE studio
Keep the `UI/` UI Toolkit surface. Merge so that it has:
- the **layout, labelling, live throttle and validator-clamped sliders** from `UI/Studio`,
- the **`ISpecChannel` path + StudyPublisher** from `UI/Operator` (per CLAUDE.md: every producer of
  rooms drives the engine through the seam),
- the **realism controls** (sun hour, wall bow, floor) ported off the legacy IMGUI panel.

Then delete `Runtime/Studio/RoomStudioController` + `RoomStudioBootstrap` + `Scenes/RoomStudio.unity`.

**Retire in this order** (each step independently verifiable, suite green throughout):
1. Port the legacy panel's remaining realism controls (wall bow, corner radius) into `UI/Studio`.
2. Move `UI/Studio` onto `ISpecChannel` instead of driving `RoomGenerator` directly.
3. Fold `UI/Operator`'s publisher into it; collapse the two view-models into one.
4. Delete the legacy studio, its bootstrap, and its scene. Drop `RoomStudio.unity` from Build Settings.

Nothing is deleted before its replacement is proven — but **do not add a fourth surface.**

### Amendment 2026-08-10 — step 3 is deferred; two surfaces, declared scopes

Steps 1, 2 and 4 stand. **Step 3 (folding `UI/Operator` into `UI/Studio`) is deferred until after
the study has run.** Not cancelled — scheduled.

Why: `UI/Operator` is the head of the publish → participant → CSV chain. That chain is the only path
in this repo that has produced research data, it is released on Windows and macOS, and the
participant app built on it is what the team tests with. Re-plumbing it onto `UI/Studio` before the
study is real risk against a fixed deadline for nothing a participant would perceive. The
duplication §3 identifies is real and the cost of carrying it is understood; we are choosing to
carry it for one release cycle.

Until step 3 happens, the two surfaces have **written, non-overlapping scopes**:

| Surface | Owns | Does not own |
|---|---|---|
| `UI/Studio` | Room authoring and realism: geometry, lighting, materials, furniture, the walk/VR entries | Publishing a study, the participant flow, canonical-spec gating |
| `UI/Operator` | The study instrument: pair gate → `StudyPublisher` → participant runner → CSV, `PerfHud` | Realism/authoring controls |

If a change needs both columns, that is the signal step 3 has become due — raise it, don't quietly
build the missing half into the other panel. **Neither surface grows a copy of the other's job.**

Two things `UI/Operator` should adopt from `UI/Studio` without waiting for step 3, because they are
hygiene rather than architecture: sliders reading their ranges from `RoomSpecValidator`, and
disabled controls stating the reason they are disabled.

---

## 4. Rules that stop this recurring

1. **No new UI surface.** There are exactly two, with the scopes declared in §3: extend `UI/Studio`
   for authoring/realism, `UI/Operator` for the study instrument. A third is not allowed, and
   neither of the two may grow a copy of the other's job. If a parallel build seems necessary, it
   must come with the commit that deletes what it replaces.
2. **New entry point ⇒ update §1 of this file in the same commit.** The bootstrap tables above are
   the only reliable answer to "why am I seeing the wrong panel".
3. **UI reads limits from the validator.** Never copy a range into a panel.
4. **Verify by looking.** Tests passing is not the gate for anything visual — render the panel
   (`unity/captures/`) or open the editor. Several "done" claims here were wrong because only the
   suite was checked.
5. **Batchmode is not the editor.** Headless has no DXR and SSGI never converges, so ~82% of the
   lighting model is absent. Judge lighting in the editor, and never from a brightened screenshot.

---

## 5. Known open items (not yet fixed)

- **Free furniture placement is blocked**: `RoomSpecAdapter` stamps every free-coordinate item with
  slot `"explicit"`, and `RoomSpecValidator` rejects duplicate slot ids → two free items fail. Needs
  `instance_id` + optional `slot_id`. See `memory/furniture-placement-contract.md`.
- **Containment is not concave-aware**: furniture is tested against a rectangle + rounded corners, so
  a `wall_bow < 0` wall can pass through furniture that validated clean. Fix with point-in-polygon
  against `FootprintPath.Build`.
- **Walls go black at the top** as the ceiling rises: the four spots point straight down and raster GI
  is weak, so there is no wall-wash. This is the next lighting pass.
- **L3 calibration reads FAIL in batchmode** — pre-existing and an artifact of headless GI, not a
  regression. Must be re-measured in the editor before it means anything.
