# Sub-plan — Curved Walls (the contour manipulation)

*The one big geometry gap, and Kirsh's likely headline variable (angular vs curved). This plan tackles it.*

## Decision: Method A (procedural), scoped

Two ways to get curved rooms:
- **(a) Procedural curved-wall mode** — generate the wall as a curved *mesh* from a "curviness" parameter.
- **(b) Hand-built curved kit** — model or buy fixed curved rooms.

**We pick (a).** It's more engineering than placing cubes, but it's the only path that stays **parametric** (a curviness *slider* = a clean controlled variable) and is **code-driven** (buildable without an artist). (b) breaks the one-generator model and needs modeling skill/assets. We keep (a) tractable by **scoping the curve type** (below) instead of allowing arbitrary shapes.

## The concept: one "curviness" slider

The footprint is a **rounded rectangle** whose **corner radius** is the variable:

```
  TOP-DOWN FOOTPRINT  —  one "curviness" slider  (= corner radius)

   curviness 0 (ANGULAR)        curviness 0.5            curviness 1 (CURVED)
   ┌───────────┐                ,───────────,            (───────────)
   │           │                |           |            |           |
   │   room    │                |   room    |            |   room    |
   │           │                |           |            |           |
   └───────────┘                `───────────`            (───────────)
   sharp 90° corners            rounded corners          fully rounded ends (stadium)

        sharp box  ──────────  corner radius grows  ──────────▶  rounded room
```

One parameter sweeps angular → curved. That *is* the contour manipulation — no arbitrary splines needed.

## Plan diagram (the steps)

```
 ① CURVE MODEL     ② MESH GEN        ③ OPENINGS v1     ④ TEXTURE UV     ⑤ ROOMSPEC        ⑥ THE PAYOFF
 rounded-rect   →  build curved   →  door/windows   →  map along arc →  add 'curviness' → angular(0) vs
 + 1 "curviness"   wall mesh from     on STRAIGHT       length so it     to schema +       curved(1) PAIR
 slider (corner    the footprint      sections only     doesn't stretch  diff-validator    = the contour
 radius 0..max)    (extrude up)       (curves = later)                                      study, DONE
```

## Steps

1. **Curve model.** Rounded-rectangle footprint + one `curviness` (corner-radius) parameter. Defer arbitrary splines/ellipses.
2. **Curved-wall mesh generator.** Build the footprint as a path (straight runs joined by quarter-arc corners), then extrude it up to ceiling height → one wall mesh. Floor + ceiling follow the same footprint outline.
3. **Openings v1 — on the straight bits.** Keep door + windows on the flat wall sections; don't place them on the curved corners yet (that's the hard part). Still delivers a real curved room.
4. **Texture UVs.** Map textures along the wall's *arc length* so they don't stretch around the curve.
5. **RoomSpec + validator.** Expose `curviness` (0–1). Update `room_spec.schema.json` and the single-variable diff-check.
6. **The payoff.** Generate an **angular (curviness 0)** and **curved (curviness 1)** room that differ in *only* that one field → the control/treatment stimulus for the contour study. Kirsh's manipulation, done.

## Known limits (phased on purpose)

- 🔴 **Openings on curved surfaces** + **arched / curved-top windows** — deferred to v2 (the genuinely hard sub-problem). v1 keeps openings on straight sections.
- 🟡 **Furniture near curves** — define slots relative to the footprint so pieces don't clip the curved wall.
- ⚙️ **It's custom mesh code** (build vertices/triangles directly, or use Unity Splines / ProBuilder's runtime API) — more involved than primitives, but bounded and one-time.

## Where it fits

Slots into the main plan as a **Phase-2/3 geometry feature**, owned by the Unity/Windows-PC side (it's renderer mesh code) with a small shared schema change. It directly produces the **contour control/treatment pair** — the manipulation most likely at the center of Kirsh's study.
