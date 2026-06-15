# Asset sourcing — pinned CC0 assets for the Unity renderer

*Companion to [RENDERING_RESEARCH.md](RENDERING_RESEARCH.md) §5 and the determinism guardrail (provenance pins asset versions). Researched 2026-06-11 against the live ambientCG and Poly Haven APIs. These are **candidate pins** — once the Unity workstream confirms each downloads + imports cleanly, this list moves to `unity/ASSETS.md` with URL+hash locks.*

## Why pinning matters

Guardrail #4 (determinism): *same spec → same room, forever.* That only holds if the textures, HDRIs, and furniture are pinned to exact, immutable asset versions. Every asset below gets: exact source id, URL, license (CC0 only — no attribution burden), resolution, and — for textures — the declared real-world physical size that drives world-scale tiling (RENDERING_RESEARCH.md §5.2). All sources are CC0: ambientCG and Poly Haven both license their full libraries CC0.

## 1. PBR texture sets — one per RoomSpec surface material

Source: **ambientCG** (chosen over Poly Haven because each ambientCG material publishes a real-world physical size — required for the world-scale tiling that doubles as nuisance control). Download the **2K-PNG** or **2K-JPG** PBR zip (Color/Normal(GL)/Roughness/AO/Displacement). IDs verified live against the ambientCG v2 API.

| RoomSpec material | Asset id | URL | Backup id | Notes |
|---|---|---|---|---|
| plaster | `Plaster001` | https://ambientcg.com/view?id=Plaster001 | Plaster003 | smooth interior wall plaster; neutral |
| paint | `PaintedPlaster016` | https://ambientcg.com/view?id=PaintedPlaster016 | PaintedPlaster017 | painted wall finish; takes `tint_hex` well |
| wood (floor) | `WoodFloor043` | https://ambientcg.com/view?id=WoodFloor043 | WoodFloor051 | residential plank flooring |
| brick | `Bricks060` | https://ambientcg.com/view?id=Bricks060 | Bricks059 (red) | light/white interior brick; 059 for exposed-red |
| concrete | `Concrete012` | https://ambientcg.com/view?id=Concrete012 | Concrete034 | smooth interior concrete |
| tile | `Tiles107` | https://ambientcg.com/view?id=Tiles107 | Tiles040 | residential floor/wall tile |
| carpet | `Carpet012` | https://ambientcg.com/view?id=Carpet012 | Carpet016 | low-pile residential carpet |
| marble | `Marble001` | https://ambientcg.com/view?id=Marble001 | Marble016 | polished marble; keep roughness low |
| glass | *(shader-only)* | — | — | no texture: URP transparent material, low roughness, IOR ~1.5, non-shadow-casting (RENDERING_RESEARCH.md §3.7) |

## 2. HDRI environments — one per lighting preset

Source: **Poly Haven** HDRIs (CC0). Use **2K** `.hdr` for image-based lighting; 1K is fine if build size presses. IDs verified live against the Poly Haven API.

| Lighting preset | HDRI id | URL | Mood |
|---|---|---|---|
| neutral_daylight | `kiara_interior` | https://polyhaven.com/a/kiara_interior | bright interior, natural daylight through glass |
| warm_evening | `cayley_interior` | https://polyhaven.com/a/cayley_interior | sunrise-sunset, warm lamp + window glow |
| cool_clinical | `hospital_room_2` | https://polyhaven.com/a/hospital_room_2 | explicitly tagged "clinical"; cool even fluorescent |
| dim | `fireplace` | https://polyhaven.com/a/fireplace | night, low ambient, warm point source |
| bright_office | `blender_institute` | https://polyhaven.com/a/blender_institute | office, even artificial light, low contrast |

**Design caveat (decide in the v1.1 round):** these are *indoor* HDRIs — great for realistic interior ambient, but the wrong thing to see *through a window*. For rooms with windows we likely want an **outdoor sky** HDRI for the through-window view while the sun/fill rig (RENDERING_RESEARCH.md §3) drives interior mood. Outdoor candidates to pin then: `belfast_sunset`, `kloofendal_48d_partly_cloudy` (a predecessor pick — confirmed still on Poly Haven), `spruit_sunrise`, `venice_sunset`. Predecessor's `royal_esplanade` is also still available. Resolution-vs-build-size tradeoff: HDRIs are the heaviest single assets; prefer 1–2K and lean on the runtime bounce probe for indoor light rather than high-res IBL.

## 3. Furniture models — dining room catalog

Honest finding: **Poly Haven's CC0 model library is thin for dining furniture** — it has `Dining Chair 02` (good) and assorted wooden tables, but no seats-6 dining table and only chandeliers for ceiling lights. Usable as a start; not a complete matched set.

| Catalog id | Candidate (Poly Haven, CC0) | URL | Format / polys | Notes |
|---|---|---|---|---|
| dining_chair | `dining_chair_02` | https://polyhaven.com/a/dining_chair_02 | glTF/FBX/blend, ~22K tris | realistic, web-OK budget |
| dining_table_6 | `WoodenTable_02` | https://polyhaven.com/a/WoodenTable_02 | glTF/FBX/blend | not a true 6-seater; closest CC0 match |
| sideboard | `GothicCabinet_01` | https://polyhaven.com/a/GothicCabinet_01 | glTF/FBX/blend | style mismatch; placeholder only |
| pendant_light | `Chandelier_01` | https://polyhaven.com/a/Chandelier_01 | glTF/FBX/blend | chandelier ≠ pendant; emissive + point light |

**Format note:** Unity imports **FBX** natively; glTF needs the *glTFast* package (free, official). Poly Haven offers both — pin FBX to avoid the dependency.

**Fallback ladder (pre-agreed):**
1. **v1 — greybox primitives** matching each preset `footprint_m` (honest placeholder; lets the whole pipeline run before art is sourced).
2. **v2 — CC0 models** above where they fit.
3. **v3 — paid Unity Asset Store interior pack** *if the realism gate (RENDERING_RESEARCH.md §6) demands a matched residential set.* Researched options to evaluate then: "Archviz Interior" / "Apartment Kit" style packs (~$30–80, one-time, royalty-free for runtime builds). Flag licensing before committing — Asset Store license permits shipping in builds but not redistributing raw assets in the public repo, so paid models would live outside git / in R2.

## 4. Caveats & licensing

- ambientCG + Poly Haven: CC0, redistributable — safe to commit to the repo (or cache in R2).
- All texture IDs verified against the **live ambientCG v2 API** 2026-06-11; HDRI + model ids against the **live Poly Haven API** same day. Re-confirm exact download URLs at pin time (the `unity/ASSETS.md` hashes are the real lock).
- Furniture is the weak link — budget for the greybox→paid ladder rather than assuming free models clear Kirsh's realism bar.
- HDRI indoor-vs-outdoor-through-windows is an open design decision (see §2 caveat), not yet locked.
