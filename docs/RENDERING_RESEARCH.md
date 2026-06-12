# Rendering research — realism, curved walls, and light physics in Unity WebGL

*Companion to [PHASE2_PLAN.md](PHASE2_PLAN.md). Researched 2026-06-11 (web sources + old-repo prior art). Claims are labeled: **[verified]** = documentation/source-backed, **[physics]** = derived from standard radiometry, **[validate]** = must be confirmed empirically in milestone M-U5/M-U6.*

---

## 1. The constraint chain — what WebGL allows, and the one hard problem

Our rooms are **generated at runtime** (that's the whole product), which collides with how Unity normally achieves realism:

| Technique | Available to us? | Why |
|---|---|---|
| HDRP (high-end pipeline) | ❌ | Not supported on web — **URP is the pipeline** [verified] |
| URP Deferred rendering | ❌ | Not supported on WebGL2; **Forward/Forward+ only** [verified] |
| Baked lightmaps | ❌ | Baking is editor-time and needs UV2 unwrapping (editor-only API) — impossible for rooms built at runtime [verified] |
| Enlighten realtime GI | ❌ | Not supported on WebGL [verified] |
| Adaptive Probe Volumes | ❌ | Probes are baked offline — same conflict with runtime geometry |
| URP SSAO renderer feature | ✅ | Works on WebGL2 (depth+normals prepass) [verified] |
| Realtime directional light + soft shadows | ✅ | Forward path, standard |
| Runtime ReflectionProbe.RenderProbe() | ✅ | Scriptable at runtime [verified] |
| RenderSettings.ambientProbe (SphericalHarmonicsL2) | ✅ | Settable at runtime from script [verified] |
| WebGPU backend | ⚠ future | Unity 6.1+ experimental/public-access; compute shaders & better lighting in-browser, but not all participant browsers — an **upgrade path, not a v1 dependency** [verified] |

**The one hard problem:** indirect light (bounce/GI) cannot be baked or simulated by Unity's built-in GI for runtime rooms. It must be **approximated by a curated stack** — and the old repo's viewer already proved the right trick (§3).

## 2. The light physics we are simulating (and the equations the platform should know)

**Direct sun [physics].** The sun is effectively a directional source (≈0.53° angular size → parallel rays; no inverse-square falloff indoors). Illuminance on a surface follows Lambert's cosine law, E = E₀·cos θ. Outdoors: direct sun ≈ 100,000 lux; overcast sky ≈ 10,000 lux of *diffuse* light. Indoors, a window admits (a) a sharp **sun patch** that moves with sun angle and (b) soft **skylight** proportional to the solid angle of sky visible from each point in the room.

**Sun color [physics].** Low sun travels a longer atmospheric path → Rayleigh scattering removes blue → CCT slides from ≈5800 K (noon) toward 2000–3500 K (horizon). A single `time_of_day` knob therefore couples sun *angle* and sun *color* — the old viewer's `applyDaylight()` implemented exactly this coupling and we keep it.

**Interreflection — the bounce ("light buoyancy") [physics, verified source].** Once light enters, it bounces between surfaces. The steady-state indirect component follows the flux-balance / integrating-sphere model used by daylighting engineering (EnergyPlus split-flux method):

```
E_indirect ≈ Φ_in · ρ̄ / ( A · (1 − ρ̄) )
```

where Φ_in = flux entering the room, **A = total interior surface area**, ρ̄ = area-weighted average surface reflectance. The 1/(1−ρ̄) term is the geometric series of bounces — *this* is why bright-walled rooms glow and dark rooms eat light. Validity caveat [verified]: the model assumes near-cubical matte rooms; rooms deeper than ~3× ceiling height break it.

**What geometry changes do to light — the coupled-variable equations:**

- **Higher ceiling** [physics]: (1) A grows → E_indirect *drops* (≈ proportionally, via the equation above) — a 3.2 m room is ambiently dimmer than a 2.6 m room with identical windows/lights. (2) A ceiling-mounted fixture obeys inverse-square to the floor: E = I/d², so 3.2 m vs 2.6 m pendant height → (2.6/3.2)² ≈ **0.66 — the floor receives ~34% less direct lamp light**. The validator's coupled-vars note for ceiling height should carry these two numbers; Phase 3's "matched luminance" means compensating `lighting.artificial.intensity` until measured floor lux matches across the pair (§6 step 6 gives the measuring tool).
- **Bowed-in (concave) walls** [physics]: matte-surface interreflection is governed by view factors — a concave wall "sees" more of itself and of opposing surfaces → higher mutual view factors → locally **elevated bounce** (a brighter mid-wall wash, softer luminance gradients). If the surface is glossy, a concave wall acts as a cylindrical focusing mirror and can produce caustic stripes — a **confound risk: keep curved walls matte** (low-gloss roughness floor) unless focusing is itself the stimulus.
- **Bowed-out (convex) walls** [physics]: spread reflected flux → flatter, slightly dimmer local bounce.
- **Rounded corners (curviness)** [physics]: sharp corners are the darkest regions of a room (maximal mutual occlusion — what SSAO renders). Rounding removes that dark gradient and raises minimum luminance — likely part of *why* curvature feels different psychologically, and our stack reproduces it for free because both SSAO and the bounce probe operate on the **actual generated geometry** (§3).

## 3. The bounce-light approximation stack (Unity translation of the proven trick)

The old A-Frame viewer solved runtime GI with a **captured irradiance probe** — render the finished room to a small cubemap, convert to spherical harmonics, feed it back as colored ambient (one-bounce GI: warm floor lighting the ceiling, wall color bleeding into the room). Unity has first-class APIs for the identical trick [verified: `ReflectionProbe.RenderProbe()`, `SphericalHarmonicsL2`, `RenderSettings.ambientProbe`]:

1. **Sun**: one realtime directional light — angle/intensity/Kelvin from `lighting.natural` (Kelvin law from the old repo: K = 6500 − 3800 × warmth; kelvin→RGB via Tanner-Helland).
2. **Sky**: HDRI or gradient skybox visible through window gaps; contributes the cool skylight against the warm sun (the single biggest indoor-realism cue, per the old viewer's notes).
3. **Window fill**: URP has **no realtime area lights** — approximate each window with a broad, shadowless spot/point light scaled by window area × daylight intensity (the old viewer did exactly this; sun handles the sharp patch, fill handles the soft spill) [validate: tune in M-U3].
4. **THE BOUNCE**: 1–2 frames after every rebuild, `RenderProbe()` a small cubemap (64–128 px) from room center → project to SH-L2 → `RenderSettings.ambientProbe = probe × lighting.bounce`. Geometry-aware by construction: taller rooms capture dimmer ambience, curved walls capture their own altered interreflection — **the physics in §2 emerges from the capture instead of being hand-tuned**. Re-capture on every spec change (debounced with the rebuild).
5. **Specular**: the same runtime reflection probe assigned to the room's renderers (metal/glass/marble need it to read as material at all).
6. **Contact darkening**: URP SSAO renderer feature (the GTAO equivalent) — corners, under-furniture, wall-ceiling junctions.
7. **Image finish**: linear color space, ACES tonemapping, exposure ≈1.0–1.2, subtle bloom. Window glass: transparent, **non-shadow-casting** so sun passes through (old-viewer trick).
8. **Artificial light**: pendant fixture = emissive mesh + realtime point light; `lighting.artificial.intensity/warmth` drive it (CeilingLightFactory semantics from the old manifests).

## 4. Curved and bowed walls — the mesh recipe

Extends the CURVED_WALLS_SUBPLAN with the user's requirement that walls bend **in and out**, not just rounded corners. Two shape parameters (both RoomSpec v1.1 batch candidates):

- `shell.curviness` (0..1) — corner radius = curviness × min(width, length)/2. 0 = angular, 1 = stadium. (Already planned.)
- `shell.wall_bow.{front,back,left,right}` (−1..+1) — **NEW**: per-wall bow as circular-arc sagitta; negative = concave (bows into the room), positive = convex (bows outward). Sagitta = bow × bow_max (e.g. 0.6 m), wall span solved as the circular arc through the two corners with that sagitta.

**Mesh generation [standard technique, verified approach]:** the footprint becomes a closed analytic path — four edges (straight or arc per bow) joined by quarter-arc corners (per curviness). Sample the path (≈16 segments per arc) → ring of 2D points → extrude up to `ceiling_height_m` for the wall band; ear-clip-triangulate the footprint polygon for floor/ceiling (bowed-in walls make it non-convex — ear clipping handles that, convex fans do not). Hand-rolled `Mesh` code (vertices/triangles/normals/UVs) — no ProBuilder/Splines dependency; our paths are lines+arcs, so direct math is simpler and deterministic.

- **Normals**: shared vertices + true analytic normals along arcs (smooth curve shading); split vertices at angular corners (hard edges). `RecalculateTangents()` after manual normals so normal maps work.
- **UVs**: U = cumulative **arc length** ÷ texture physical size, V = height — constant texel density around curves, no stretch (subplan step ④, now concrete).
- **Openings**: v1 keeps door/windows on straight segments only (subplan's explicit scoping); a bowed wall either forbids openings or locally flattens the opening's span — decide in the v1.1 round.
- **Collision**: MeshCollider from the same mesh (needed later for raycast furniture placement).

## 5. Texture realism playbook (what actually makes surfaces look real)

1. **Full PBR sets** — BaseColor/Normal/Roughness/AO(/Height) per material, from CC0 libraries (ambientCG, Poly Haven), pinned by URL+hash in `unity/ASSETS.md`.
2. **Real-world scale, locked once** [verified guidance]: consistent texel density (~512–1024 px/m for walls/floors); UV tiling = world meters ÷ the texture's *declared physical size* (ambientCG publishes physical dimensions per texture). Never rescale per object — and because our UVs are in meters (§4), density is automatically constant across all room sizes: realism technique and nuisance control in one.
3. **Roughness variation over albedo detail** — believable materials break up specular highlights (roughness maps) more than they vary color; avoid high-contrast albedo that screams "tiling".
4. **Anti-tiling**: pick low-repetition textures; if repetition shows at room scale, add a subtle macro-variation overlay (4-way rotation mix or low-frequency tint noise in Shader Graph) [validate: probably unnecessary at ≤12 m rooms with 2K textures].
5. **Layered occlusion**: texture AO map (micro) × SSAO (meso) × bounce probe (color) — three depth cues at three scales.
6. **Filtering**: trilinear + 4–8× anisotropic (floors at grazing angles go blurry without aniso).
7. **Budget**: channel-pack (roughness+AO+height into one RGBA) → ~9 materials ≈ 18 × 2K textures; DXT texture subtarget for desktop web [verified]. Keep total build ≤60 MB Brotli (§6).

## 6. UI + web delivery — verdict: the web path is viable; native is a codified fallback

**Verdict:** Unity WebGL (URP, Forward+, the §3 stack) can plausibly meet "reliable experience of being in the room" on a mid laptop. It is NOT automatic — the fidelity gate (below) decides, and the native fallback is pre-planned, not a scramble.

- **UI**: Unity renders **only the room viewport** (a canvas); every control is plain HTML/JS overlaying/flanking it, talking through VIEWER_BRIDGE.md (`SendMessage` in, CustomEvents out). DOM UI = crisp text, native inputs, accessibility, zero Unity-UI build weight. This was already the locked architecture; research confirms it's also the *quality* play.
- **Sharpness** [verified]: set `devicePixelRatio` in the Unity instance config (cap at 2 for GPU cost); match canvas pixel size to CSS size × DPR — otherwise Retina/HiDPI renders blurry. Expose a quality toggle (DPR 1 vs native) for weak machines.
- **Hosting on Cloudflare** [verified]: precompress the build as Brotli (`.wasm.br`, `.data.br`) and serve with a Pages `_headers` file:
  ```
  /Build/*.wasm.br
    Content-Encoding: br
    Content-Type: application/wasm
  /Build/*.data.br
    Content-Encoding: br
    Content-Type: application/octet-stream
  /Build/*.js.br
    Content-Encoding: br
    Content-Type: application/javascript
  ```
  Brotli needs HTTPS (Cloudflare gives it). Keep Unity's *decompression fallback* ON during bring-up, OFF once headers verified (smaller, faster). HTML loading screen with a progress bar over the ~30–60 MB first load; R2/Pages cache makes repeat loads near-instant.
- **WebGPU upgrade path** [verified]: Unity 6.1+ ships experimental WebGPU (compute, better lighting features in-browser). Same codebase, flip the graphics API later for participants on modern browsers. Not a v1 dependency.
- **Native fallback — explicit trigger criteria** (pre-agreed so the decision is mechanical):
  1. fidelity gate fails after material/lighting iteration (room doesn't read as real on a 1080p laptop), or
  2. <30 fps on a mid-tier laptop / MacBook Air at DPR 1, or
  3. build cannot get under ~100 MB Brotli.
  Then: native Unity desktop app, same RoomSpec + same generation code, UI Toolkit in-app panels replacing the DOM controls; Cloudflare keeps storing specs/responses. Decision #2 already reserved this path — the contracts make it a swap, not a rewrite.

## 7. The concrete rendering pipeline (build order)

1. **Project**: Unity 6 LTS, URP, Linear color, WebGL target; URP asset: Forward+, SSAO on, soft shadows, 4× aniso, ACES.
2. **Geometry core**: FootprintBuilder (lines + arcs + bow sagitta) → wall ring extrusion + ear-clip floor/ceiling; WallSegmenter for openings on straight segments; manual normals/tangents; arc-length UVs in meters. *(EditMode-tested against the committed pair specs.)*
3. **Materials**: 9-material PBR library, channel-packed, world-scale tiling from declared physical sizes, `tint_hex` multiply.
4. **Light rig from spec**: sun (angle/Kelvin/intensity ← `lighting.natural`), skybox ambient, per-window shadowless fill, pendant emissive+point (← `lighting.artificial`).
5. **Bounce**: post-rebuild RenderProbe → SH-L2 → ambientProbe × `lighting.bounce`; same probe for specular. Debounced with rebuilds.
6. **Physics QA tool**: a script "luxmeter" sampling light at floor/wall points — verifies the §2 predictions in-engine (ceiling 2.6 vs 3.2 → measurable indirect drop; curved vs angular → corner-gradient difference) and later powers Phase-3 **matched-luminance auto-compensation**.
7. **Web build**: Brotli + DXT + devicePixelRatio config → Cloudflare Pages with `_headers` → measure fps + size on a mid laptop and a MacBook Air.
8. **Fidelity gate**: render the ceiling pair; side-by-side judgment against Kirsh's realism bar; iterate materials/lighting; escalate per §6 criteria if it cannot pass.

## Sources

- [Unity Manual — WebGPU (experimental)](https://docs.unity3d.com/6000.3/Documentation/Manual/WebGPU.html) · [Unity 6.1 WebGPU public access](https://discussions.unity.com/t/public-access-to-webgpu-experimental-in-unity-6-1/1572462)
- [Unity Manual — Realtime GI using Enlighten](https://docs.unity3d.com/Manual/realtime-gi-using-enlighten.html) (WebGL unsupported) · [Unity discussions — URP Deferred vs WebGL2](https://discussions.unity.com/t/why-are-the-urp-deferred-requirements-high-and-above-webgl-2/1581575) · [URP Forward+ on WebGL](https://forum.unity.com/threads/does-webgl-support-the-forward-renderer-in-urp.1397578/)
- [ReflectionProbe.RenderProbe](https://docs.unity3d.com/ScriptReference/ReflectionProbe.RenderProbe.html) · [RenderSettings.ambientProbe](https://docs.unity3d.com/ScriptReference/RenderSettings-ambientProbe.html) · [SphericalHarmonicsL2](https://docs.unity3d.com/ScriptReference/Rendering.SphericalHarmonicsL2.html)
- [EnergyPlus Engineering Reference — Daylight Factor Calculation](https://bigladdersoftware.com/epx/docs/24-1/engineering-reference/daylight-factor-calculation.html) (flux balance, split-flux validity)
- [Texel density guide (RebusFarm)](https://rebusfarm.net/blog/texel-density-basics-every-artist-should-know) · [Beyond Extent — texel density deep dive](https://www.beyondextent.com/deep-dives/deepdive-texeldensity) · [PBR realism guide (GarageFarm)](https://garagefarm.net/blog/physically-based-rendering-pbr-realism-in-digital-materials)
- [Unity Manual — Deploying compressed Web builds](https://docs.unity3d.com/Manual/webgl-deploying.html) · [Unity WebGL compression done right](https://miltoncandelero.github.io/unity-webgl-compression) · [Unity Manual — Web canvas size / DPR](https://docs.unity3d.com/Manual/webgl-canvas-size.html) · [Unity Manual — Web texture compression](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-texture-compression.html)
- [Procedural mesh normals & tangents (code-spot)](https://www.code-spot.co.za/2020/11/25/procedural-meshes-in-unity-normals-and-tangents/) · [Procedural mesh UV mapping (Nobel-Jørgensen)](https://blog.nobel-joergensen.com/2011/04/05/procedural-generated-mesh-in-unity-part-2-with-uv-mapping/)
- Old-repo prior art: `../cogs160track3v2/wizard/lighting_control_surface.md`, `viewer/index.html` (GI probe, sun/time-of-day model, window fill, glass shadows)
