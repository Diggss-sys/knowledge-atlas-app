# LIGHTING SPRINT — fidelity lane acceleration (L0–L4)

*Execution plan authored by Fable 2026-07-06 for Opus to execute. Context: the team is behind schedule, so Paco's lane is absorbing the **fidelity half** of E2 (handoffs/VR_LIVE_EDIT.md steps F0–F3). The VR half (V0–V1) stays with E2 untouched. Work on a new branch off `Diggss-sys-branch` (suggested: `paco/lighting-l0-l4`), PR when green. Rules of the house: nothing merges unless the room builds and the full EditMode suite is green (currently 38/38 — every gate below adds tests, never breaks them). Escalate to Fable after 2 failed attempts on the same problem.*

## Ground truth (verified 2026-07-06 — read before coding)

- `RoomGenProjectBootstrap.EnsurePipeline` creates a **default-constructed** `HDRenderPipelineAsset` — no SSGI, no SSAO tuning, nothing. **Bootstrap-generated assets are NOT committed** (regenerated on open), therefore **every HDRP/quality setting must be applied in bootstrap code**, not by hand-editing the asset. Hand-edited settings will silently vanish on a fresh clone.
- `SurfaceResolver.Resolve(stableId)` loads `Resources/RoomGen/Materials/<stableId with '.'→'-'>`; on miss it builds a flat-color fallback. So shipping real materials = **drop correctly-named HDRP/Lit materials into Resources** — zero E1 code changes for the basic wire-up.
- Shell mesh UVs: `GenerationUtil.BuildFootprintPrism` emits **world-meter UVs on the side faces** (`uv.Add(p.x, p.y)`) but the caps use a fan with center `(0.5, 0.5)` mixed with world-coordinate rim UVs — the floor/ceiling caps will smear textures. Wall blocks: audit `AddWallBlock`'s mesh (unverified). **L1 includes a UV audit/fix: all shell surfaces must carry world-meter UVs.** Then world-scale tiling = material tiling `1 / physical_size_m` from the ambientCG declared size.
- `LightingSystem.Build`: 4 recessed spots, lumen→candela via `LightUnitUtils`, color temperature from spec, fixed-exposure global volume, `LightingCalibrator.MatchTargetLux` is **analytic** (Utilization 0.58 / UniformFraction 0.82 constants — it never measures the scene). Adding SSGI + real albedos WILL make actual lux drift from the analytic estimate (L3 handles this).
- `CC0AssetPipeline.cs` exists but is a prototype: one hardcoded Poly Haven wood floor, `async void` + modal dialogs (batchmode-hostile), no hash pinning. L1 replaces it.
- Pendant fixture is a placeholder mesh with **no light emission** — the room's declared luminaire doesn't emit; all light comes from invisible recessed spots.
- Pinned asset lists live in `docs/ASSET_SOURCING.md` (ambientCG 2K per material enum, Poly Haven HDRIs per lighting preset). RENDERING_RESEARCH.md §2 is normative light physics, §5 the texture playbook.
- Headless driving gotcha: **PowerShell does not wait for Unity.exe** (GUI-subsystem binary) — use `Start-Process -Wait -PassThru` and read `.ExitCode`, then parse the `-testResults` XML. Never trust `$LASTEXITCODE`.

## Non-negotiable guardrails

1. **Single-variable rule.** Every fidelity improvement is shared infrastructure: it must affect control and treatment **identically**. No scene-side per-condition tweaks, ever. L-gate tests must include a pair regression: adapted KA pair still passes `PairValidator` + `PairGate` and both conditions resolve the same material set (minus declared diffs).
2. **Determinism.** Same spec → same room. Every external asset gets a URL + SHA256 pin in `unity/ASSETS.md` (create it — this sprint moves ASSET_SOURCING's candidate pins to real locks).
3. **Warmth law** `K = 6500 − 3800 × warmth` is normative (label/formula inversion is a known flagged issue — implement the formula, as the adapter already does).
4. **Matte curved walls** (RENDERING_RESEARCH.md §4): concave + glossy = caustic confound. Clamp curved-wall material smoothness.

## Gates

### L0 — HDRP quality pass, code-driven (≈ F0)
Extend `RoomGenProjectBootstrap` (and/or a new `RoomGen.Editor.HdrpQualityConfigurator`) to configure the pipeline asset + a persistent global volume:
- Pipeline asset: soft shadows w/ decent resolution, SSAO on, **SSGI on (quality Medium)**, reflection/refraction defaults sane, volumetrics OFF for now (perf).
- A persistent "RoomGen Quality" global volume (priority below the per-room exposure volume, i.e. < 50): **ACES tonemapping**, SSAO/SSGI overrides, subtle bloom. Must live on a **persistent root, not under a generated room** (rooms are rebuilt wholesale).
- Keep `RenderQualityProfiles.ApplyDesktop/ApplyVr` as the runtime switch; VR profile must be able to cap SSGI later.
- **Tests:** editor test asserting the bootstrap-produced pipeline asset has SSAO/SSGI/shadow settings expected; existing 38 stay green.
- **DoD:** before/after 1080p screenshots of the dining room committed with the PR (capture via the existing studio + `ScreenshotRegression` or computer-use).

### L1 — Asset pipeline + real PBR materials (≈ F1)
- Rewrite `CC0AssetPipeline` into a **batchmode-runnable fetcher** (`RoomGen.Editor.AssetFetcher.FetchAll`, static menu + `-executeMethod` entry): downloads each ambientCG pin (2K-JPG) from ASSET_SOURCING §1, verifies SHA256 against `unity/ASSETS.md`, extracts, sets importer flags (BaseColor sRGB, Normal as NormalMap, mask maps Linear), builds an HDRP/Lit material per RoomSpec material enum **named to match SurfaceResolver's resource ids**, tiling = 1/physical_size, `tint_hex` multiplied into base color (verify the adapter→internal id mapping covers tint).
- **Decision (made): textures are NOT committed to git.** They're gitignored; `unity/ASSETS.md` is the committed lock (URL + SHA256 + physical size + license). Fetch runs as part of bootstrap/CI setup. Rationale: ~100–250 MB of binaries would bloat the repo; the hash lock preserves determinism. (If the team later prefers vendoring, it's CC0 — flip the .gitignore, nothing else changes.)
- **UV audit/fix** (see ground truth): floor/ceiling caps get proper world-meter planar UVs; verify wall blocks; glass stays shader-only (transparent, IOR ~1.5, non-shadow-casting).
- **Tests:** for each of the 9 material enums → a material resolves from Resources with a non-null BaseColorMap (skippable with a clear message when textures aren't fetched, so CI-without-network still passes); importer-settings assertions; pair regression (guardrail 1).
- **DoD:** wood/plaster/carpet/tile read as real materials at 1080p from eye height (screenshots in PR).

### L2 — Luminaires + daylight (≈ F2)
- **Pendant becomes a real luminaire:** emissive shade material + an HDRP point/area light at the fixture, flux taken as a share of `BaseLuminousFluxLm` so total room flux is unchanged (recessed spots give up the pendant's share). Fixture geometry stays E1's.
- **Windows become real:** HDRI sky (Poly Haven pins per lighting preset, ASSET_SOURCING §2 — note its caveat that through-window views eventually want an *outdoor* HDRI; for now pin the listed per-preset HDRIs and log the v1.1 question) visible through openings; HDRP directional sun + one **area light per window opening**, driven by `lighting.preset`/`warmth`/`intensity`; glass non-shadow-casting.
- Warmth drives all light color temperatures through the one normative formula — single code path.
- **Tests:** preset table test (each of the 5 lighting presets yields the expected sun/area/pendant parameter tuple); pair regression again.
- **DoD:** `neutral_daylight` vs `warm_evening` vs `dim` on the same room = obviously distinct, plausible moods (three screenshots in PR).

### L3 — Keep the calibrator honest
The matched-luminance mechanism is the lane's scientific core; don't let realism silently break it.
- Extend `LightingCalibrator` to account for the new contributors (pendant share, window area lights when the preset includes daylight) analytically — same structure, more terms.
- Add a **measured** check: a batchmode/PlayMode harness that renders the room and samples actual luminance at the 4 probe points (RenderTexture readback), compares to `MatchTargetLux`'s prediction, and reports the delta. Gate: measured mean within a documented tolerance band of target (start loose, e.g. ±25%, tighten later; the point is a tripwire + evidence, not perfection).
- **Tests:** calibrator unit tests over the new terms; the measured harness runs on demand (not in the default suite if too slow — wire as a separate `-testFilter` target).
- **DoD:** calibration report (predicted vs measured per probe) for both conditions of the ceiling pair, attached to the PR. The 3.2 m vs 2.6 m rooms must land within tolerance of the SAME target lux — that's the whole point.

### L4 — Fidelity gate evidence (M2 exit, ≈ F3)
- Render the committed ceiling pair at 1080p from eye height (1.62 m), several angles; side-by-side against a real dining-room reference photo; post to the PR + COORDINATION.md status board for the team/Kirsh-bar judgment.
- Full suite green; run the seam walk (Load KA pair → Walk via seam → Tab) to confirm fidelity survives condition switching live.
- **DoD:** gate verdict recorded; if the verdict is "not yet," the iterate loop is L1/L2 param tuning, then escalate per TEAM_PLAN.md.

## Suggested commit granularity
One commit per gate (as G0–G5 were), test counts in the message. Update `unity/ASSETS.md` in the same commit as the fetcher. Do not touch `Runtime/Gate/*`, `spec/*`, or the seam contract — none of this work changes contracts.

## Coordination note
Post to the team (Discord/COORDINATION.md) before starting: "Paco's lane is picking up the E2 fidelity half (F0–F3) as L0–L4 to unblock M2; VR half (V0–V1) remains with E2." Route any schema needs (e.g. per-window light params) through P2's v1.1 batch — do not edit the schema.
