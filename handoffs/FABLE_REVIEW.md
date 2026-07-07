# Fable review — adversarial pass over the overnight engine build (2026-07-07)

*Scope: everything on `paco/lighting-l0-l4` — the L0–L4 fidelity sprint, E3 R0/R1 runtime core, the
three experiment pairs, furniture models. Method: hostile re-read of every new/modified file, claim
verification against renders/logs (not trust), and edge-case hunting. Result: **7 defects found and
fixed** (suite now **69/69**, player rebuilt), plus documented accepted risks. Verdict at the end.*

## Defects found → fixed

1. **Silent data mutation in the row writer (worst finding — data honesty).** `RatingResponse`
   silently *clamped* out-of-range values into the scale. At the data sink that is fabrication: a
   participant "12" on a 1–7 scale must never become a quiet "7", and clamping can mask task-screen
   bugs. The schema cannot express the range (it's "runner-enforced"), so enforcement lived nowhere
   honest. **Fix:** the writer now REFUSES semantically invalid rows — out-of-range rating values,
   degenerate rating scales (max<min), and choice rows with `left_condition == right_condition`
   (schema-legal but counterbalancing-broken). `ClampRating` survives as an input-layer helper only.
   New test: `Writer_refuses_semantically_invalid_rows_instead_of_repairing`.
2. **Wrong asset pins.** The furniture table in `unity/ASSETS.md` mixed 8k-entry md5s with the 2k
   files actually downloaded — a broken determinism lock. **Fix:** exact per-file 2k md5s (verified
   at download) now recorded.
3. **Stale evidence.** `docs/fidelity/` still carried pre-furniture-model renders while the text
   claimed textured-greybox; FIDELITY_GATE's self-assessment said "furniture is greybox" after
   models landed. **Fix:** re-rendered and restaged all pair evidence; gate doc updated.
4. **Crash on degenerate study config.** `StudyRunner` computed `index % (scaleMax−scaleMin+1)` —
   a study with `scale_max < scale_min` (schema doesn't forbid it) crashed with modulo-by-≤0.
   **Fix:** refuse with a reason.
5. **Crash on malformed study JSON.** `StudyGate` cast `validation.ok` to `bool?` — a non-boolean
   value threw instead of gating. **Fix:** type-checked read; non-boolean ⇒ refuse.
6. **Downloaded-but-unused roughness maps.** Furniture materials shipped with one flat smoothness
   while the per-texel roughness JPGs sat on disk. **Fix:** packed into HDRP MaskMaps at prefab
   build (shared `PackMaskTexel` layout).
7. **Fallen-plant illusion.** The greybox plant's 25° Z-tilt read as a knocked-over object next to
   real furniture. **Fix:** subtle 6° lean.

## Verification traps caught during review (methodology notes)

- **`RoomStudio.exe` mtime is a lie.** The launcher stub's bytes are identical across builds, so
  Unity may not rewrite it; verify builds via `RoomStudio_Data/Managed/RoomGen.Runtime.dll` mtime.
- **The EditMode test asmdef cannot reference Newtonsoft** even though it lists
  `Unity.Newtonsoft.Json`. Any test that *names* a `JObject` (including passing `LoadSchema()`
  results) breaks compilation. Pattern: Runtime-side factories/fixture-runners returning plain
  types (`ResponseWriter.CreateDefault`, `SampleOutOfRangeRatingRow`, …).
- **`JObject.Parse` corrupts ISO timestamps** (auto-coerces to Date tokens → schema `type: string`
  fails). All contract JSON goes through `ResponseJson.Parse` (DateParseHandling.None).

## Accepted risks (documented, deliberately not "fixed")

- **The analytic calibrator does not model the sun.** The matched-luminance guarantee rests on the
  *measured* harness (4.7% control-vs-treatment at the eye-plane card), which is the scientifically
  relevant comparison; both conditions share identical windows and an identical sun. Absolute lux
  (300 nominal) is therefore calibrated pre-sun and NOT verified absolutely — flagged for the E2
  owner if absolute photometry ever matters.
- **Sun tint is coupled to `lighting.color_temperature_k`** (lerped toward 6500K). Deterministic
  function of the declared variable, same formula both conditions — a coupled effect in the
  pendant-Y class. Comment added at the code site; belongs in analysis notes.
- **`JsonSchemaLite` ignores `format: date-time`** — a garbage timestamp string passes local
  validation (the Worker re-validates server-side). Cost/benefit of a regex check left to P2.
- **`latin_square` order strategy keys on seed parity** (2-condition square = {AB, BA}). Correct
  only if operators assign alternating seeds across participants; fine for v1, worth an operator-UI
  affordance later.
- **Table model is non-uniformly scaled** to the 2.15×1.05 slot footprint (mild grain stretch);
  **sideboard is a style-mismatched Gothic cabinet** (ASSET_SOURCING already rates it placeholder);
  **plant remains greybox**. All cosmetic-tier vs the demo bar.
- **`ScriptedSession`/`StudyRunner` stamp constant timestamps and a fixed participant id** — they
  are the *fake responder* harness by design; R2's human path must use `DateTime.UtcNow` (already
  the documented rule) and real ids. `ResponseWriter` does not create parent directories — R2
  owner should pick the session folder deliberately.

## What survived the review untouched (checked, no findings)

CSV canonical order + RFC-4180 quoting (asserted against RESPONSE_LOG.md); golden-fixture parity
(valid rows byte-deep-equal via builder, all 5 invalid rows refused for their stated reasons);
seeded-order determinism; pair single-variable validation for all three experiment pairs; the
triplanar material wiring; prefab axis/scale math (after the batchmode `renderer.bounds` staleness
fix); pendant flux conservation (spots surrender the pendant's share); per-camera SSGI wiring;
`.gitignore` hygiene for fetched binaries.

## Verdict

**Up to par, with the fixes applied: yes — M2-gate-ready and honestly evidenced.** The scientific
spine (single-variable gate → matched luminance → schema-valid data rows) is now enforced at every
layer including the sink, and every claim in the gate doc is backed by an artifact in
`docs/fidelity/` or a test. The debt that remains is intentional and named: R2's interactive
participant flow, R3's upload path, SSGI on runtime cameras, and the three cosmetic furniture items.
Suite: **69/69**. Player build: verified via fresh `RoomGen.Runtime.dll`.
