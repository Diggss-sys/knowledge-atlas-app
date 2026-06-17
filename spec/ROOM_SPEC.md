# RoomSpec — the one contract

**The keystone of the platform.** A *RoomSpec* is an engine-agnostic JSON description of a single room. Everything else hangs off it.

> *Note: this explains the frozen v1.0 contract. The v1.1 batch (lighting natural/artificial/bounce split, curviness + per-wall bow, furniture identity) is proposed in [docs/ROOMSPEC_V1_1.md](../docs/ROOMSPEC_V1_1.md). Current architecture: [docs/PHASE2_PLAN.md](../docs/PHASE2_PLAN.md).*

> One spec → one room. Producers: a **Unity slider UI** (and an optional AI path later). Consumer: the **Unity generator** (`RoomBuilder.BuildFromSpec`), rendered as interactive web-3D or native VR. They agree on **this schema**.

```
   Unity slider UI (operator picks values within the preset envelope)   [optional later: AI structured output]
          │
          ▼
   RoomSpec (JSON)  ──validate against room_spec.schema.json──►  ✅ / ✗ retry
          │
          ▼
   Unity  RoomBuilder.BuildFromSpec(spec)   → built 3D room  → web-3D (WebGL) or native VR (OpenXR)
```

## Two artifacts (don't confuse them)

| File | What it is | Who writes it |
|---|---|---|
| `room_spec.schema.json` | The **schema** — the rules every RoomSpec must obey (fields, enums, ranges). | Us, once. The AI's structured-output tool uses it. |
| `presets/<type>.preset.json` | A **room-type preset** — defaults + allowed ranges + furniture catalog + named layout slots for one room family. | Us, one per room type. |
| `examples/<type>.spec.json` | A concrete, filled **RoomSpec** (one room). | A human or the AI, per room. |

The preset is the **envelope**; the spec is a **point inside it**. The AI's whole job is: *pick a preset, choose a point.*

## How each consumer uses it

- **Unity (the generator + renderer)** — deserializes the JSON (Newtonsoft; `JsonUtility` can't handle the placement `oneOf`) into a `RoomSpec` C# class, then `RoomBuilder.BuildFromSpec(spec)` builds it. Same build feeds the operator's Unity UI preview, the web-3D (WebGL) arm, and the native VR (OpenXR) arm.
- **Unity slider UI (operator)** — binds sliders/toggles to RoomSpec fields within the preset envelope; edits apply live via `BuildFromSpec` (see [docs/VR_LIVE_EDITING.md](../docs/VR_LIVE_EDITING.md)).
- **AI authoring (optional, later)** — a natural-language brief + preset → structured output → schema-valid RoomSpec → validate → retry on mismatch. A convenience front-door, never the only producer.

## Field tour (the short version)

- `room_type` → which preset to load.
- `shell` → the box: `width_m`, `length_m`, `ceiling_height_m`, `contour` (angular/curved). *These are the usual independent variables.*
- `surfaces` → `wall` / `floor` / `ceiling`, each a material + optional tint.
- `openings` → `door` (wall + slide position) and `windows[]` (built as gaps).
- `lighting` → a named `preset` plus optional `warmth` / `intensity` / `hdri` overrides.
- `furniture[]` → `catalog_id` + `placement`. **Placement is a named `slot`** (preferred) or explicit `x_m,z_m,rotation_deg`.
- `experiment` → `condition` (control/treatment) + `manipulated_variables` (dotted paths that may differ) + `pair_id`. **This is how we keep variation honest** — only listed vars change; everything else is held constant.
- `provenance` → `generated_by` + `execution_path` (`live`/`cached`/`regen`/`preview_only`, our standing honesty labels).

## Furniture placement — where the real work lives

The AI says *what* and *roughly where* (`dining_chair` → slot `chair_3`). The **generator + preset** own the *exact where*: the preset's `layout_slots` define anchors (e.g. chairs `relative_to` the table), and `footprint_m` lets the generator avoid overlaps. This is deliberate — it's the part that bit us with Infinigen (collisions, out-of-room furniture), so the spatial logic stays in code we control, not in the model.

## Mapping to today's `RoomBuilder.cs`

We're already most of the way there. Current fields → spec paths:

| `RoomBuilder` field | RoomSpec path |
|---|---|
| `width`, `length`, `height` | `shell.width_m`, `shell.length_m`, `shell.ceiling_height_m` |
| `contour` | `shell.contour` |
| `wall`, `floor` | `surfaces.wall.material`, `surfaces.floor.material` |
| `doorPos` | `openings.door.position` |
| `windows` (count) | `openings.windows[]` (explicit list) |
| `warmth`, `intensity` | `lighting.warmth`, `lighting.intensity` |
| `useHDRI` | `lighting.hdri` |
| `PlaceFurniture(...)` | `furniture[]` + preset `layout_slots` |

The refactor: replace the loose fields with one `public RoomSpec spec;` and have `Build()` read from it. That single move makes the human wizard and the AI agent **interchangeable** — both just produce a `RoomSpec`.

## v1 scope vs. later

**In v1 (this draft):** shell, surfaces, openings, lighting, slot-based furniture, experiment + provenance metadata.

**Deferred (note them, don't build yet):**
- Curved *walls/openings* (v1.0 contour = angular/curved enum only). **v1.1 adds a continuous `curviness` slider + per-wall `wall_bow` (concave/convex)** — mesh recipe + light physics in [docs/RENDERING_RESEARCH.md](../docs/RENDERING_RESEARCH.md), schema in [docs/ROOMSPEC_V1_1.md](../docs/ROOMSPEC_V1_1.md).
- Free-form furniture placement beyond named slots.
- Per-surface photoreal PBR sets (v1 = material enum; textures resolved by the consumer).
- Multi-room / floorplans (v1 = one room per spec).

## Next steps after this draft

1. Add presets for `bedroom`, `classroom` (mirror `dining_room.preset.json`).
2. Refactor `RoomBuilder` → `BuildFromSpec(RoomSpec)`.
3. Stand up the AI path: prompt + preset → Claude structured output → validate against `room_spec.schema.json` → write `room_spec.json` → Unity builds it. **(API key server-side only — never in a Unity build.)**
