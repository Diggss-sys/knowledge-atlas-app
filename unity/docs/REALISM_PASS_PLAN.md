# Realism Pass — Plan (living doc)

Goal: kill the "Roblox" look and make the Unity room renders read as real, immersive
spaces, with real light physics. VR is the eventual target (perf matters later — the
current pass targets max desktop fidelity).

Iterated in rounds. Update this doc as items land. Owner: Diego (P2). Builder: this session.

---

## Status legend
- [ ] todo   - [~] in progress   - [x] done   - [!] blocked / needs Diego

## Realism baseline (already shipped)
- [x] Curved/bendable walls (WallBand + EarClip + FootprintPath).
- [x] Transparent refractive glass windows (SurfaceResolver).
- [x] Physically Based Sky + sun disk (QualityRig + LightingSystem, interactsWithSky).
- [x] Screen-space reflections on walk/VR cameras (QualityRig + CameraRealism).
- [x] SSGI bounce, ACES, SSAO, bloom.
- Realism is DEFAULT-ON in Desktop Walk + VR cameras. The two Studio preview
  thumbnails intentionally skip SSGI/SSR (they smear when rebuilt every slider frame).
  **Judge realism in Walk mode, not the preview thumbnails.**

## THE remaining Roblox cause — textures (Diego, one click)
- [!] `RoomGen ▸ Fetch CC0 Materials` — downloads real CC0 maps (oak/walnut/tile/
  carpet/fabric/metal/ceiling/warm-white). Materials currently have EMPTY map slots →
  flat HDRP/Lit color = the Roblox look. No code substitute. AssetFetcher.cs is solid.

---

## Round 1 — Regressions (fix the broken studio first) — IN PROGRESS
A broken studio blocks testing any realism, so these come first.

- [x] Corner-radius turns the treatment room into a "landscape" — root cause: selecting
      the radius variable seeded treatment to CalculateMaxSafeRadius (~3 m for a 6 m
      room), collapsing the footprint toward a circle. Now seeds a modest 0.6 m fillet.
- [x] "Chest" de-spawns in the windowed room — NOT a code bug: realism-test-pair simply
      had no sideboard (only table + chairs), while the dining pair does. Added the
      sideboard against the clear back wall in both conditions.
- [~] Warmth / Brightness-target sliders "don't work" — wiring is intact (DrawSharedSlider
      → apply → Rebuild → LightingSystem). The interior lamps are washed out by sky-ambient
      flood (pre-existing since the gradient sky at exposure 13.5; the PBR sky floods the
      same). This is the Round-3 "rebalance sky vs fixed interior exposure" item and needs
      in-engine eyeballing — deferred, do NOT blind-tweak. Also: color-temp is coupled to
      the sun tint (LightingSystem.AddSun), so warmth should read via the sun even now.
- [!] Wall-bow "all walls" only bows one wall — ARCHITECTURAL, not a quick fix. SetAllBow
      skips any wall carrying a door/window (CanBow gate), because openings on curved walls
      are unsupported in v1 (validator refuses). The windowed room glazes front+right+left,
      leaving only the back wall bowable. True "all walls bow" needs arc-aligned opening
      cuts in WallBand + OpeningGenerator. Its own round — see Round 2b below.

## Round 2b — Wall-bow rework (openings on curved walls) — DONE
- [x] All four walls bow, openings ride the arc. FootprintPath.SubSpan cuts arc sub-spans
      at opening edges (interpolated boundary samples, continuous arc-length UVs);
      ShellGenerator builds bowed walls as segment/sill/header bands (same split as straight
      walls, in wall-coord space); WallBand gained an opt-in bottom cap (header undersides);
      OpeningGenerator builds curved glass + trim as inward-offset thin bands; validator no
      longer refuses OPENING_ON_BOWED_WALL; studio CanBow gate removed — the "all walls"
      slider finally means all walls. Tests updated (acceptance + SubSpan cut test).
- Also fixed: dark blotchy "warping" GI on curved walls — RT GI/SSR/AO quality tier forced
      to High (Medium ran RTGI at half resolution; the upscale was the blotching).

## Round 2 — Sun-angle slider + light physics (Diego's stressed priority)
- [ ] New studio slider: sun azimuth/elevation → moves the PBR-sky sun disk → real
      interior + exterior shadows through the windows. Builds on SunSkySystem.cs.
- [ ] Verify shadow crispness/softness reads real; soft shadows already on.

## Round 2c — Real-time ray tracing (DONE code-side; DX12 + supportRayTracing verified on)
- [x] Ray-traced GI + reflections + AO: QualityRig overrides tracing to RayCastingMode.RayTracing;
      RayTracingSettings with extended camera/shadow culling (off-frustum geometry stays real).
- [x] RayTracing frame setting per walk/VR camera (CameraRealism); previews stay raster.
- [x] Ray-traced sun shadows (LightingSystem: useRayTracedShadows; 0.5° disk → real penumbra).
- [x] Volumetric fog (QualityRig): sun shafts through windows, spot halos (mfp 35 m, aniso 0.4).
- Machine state verified: Graphics API = DX12 first, RoomGenHDRP.asset supportRayTracing: 1, RTX 5070.

## Round 3 — Realism polish (real-life light reference)
- [ ] Exposure balance: PBR sky vs fixed interior exposure (sky.exposure at 0f now).
- [ ] Volumetric fog + light shafts (god-rays) through the windows for max fidelity.
- [ ] Reference real interior daylight (soft fill + warm bounce + crisp sun patch).

## Round 4 — Room types (after realism solid)
- [ ] Presets + per-type furniture/openings: bedroom, bathroom, kitchen, living, dining.

---

## Notes / decisions
- Sky method: Physically Based Sky + sun (Diego's pick; code-only, VR-safe).
- Fidelity target: max desktop now; VR perf later.
- Rules this pass: inline sequential work, no subagents/fleets, small commits to
  Diggss-sys-branch, push OK. Never commit the OB(...) vault.
