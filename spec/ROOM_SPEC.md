# RoomSpec — the one contract

**The keystone of the AI-agent room generator.** A *RoomSpec* is an engine-agnostic JSON description of a single room. Everything else hangs off it.

> One spec → one room. Many producers (human wizard, AI agent), many consumers (Unity generator, web viewer). They never talk to each other directly — they agree on **this schema**.

```
  "a cozy dining room, low ceiling, warm light, seats 6"
          │
          ▼
   AI agent (Claude/GPT, structured output)
          │  picks a room_type preset, fills a RoomSpec WITHIN its envelope
          ▼
   RoomSpec (JSON)  ──validate against room_spec.schema.json──►  ✅ / ✗ retry
          │
          ├──────────────►  Unity  RoomBuilder.BuildFromSpec(spec)   → built room (lab VR / renders)
          └──────────────►  Web    viewer.loadSpec(spec)             → built room (browser, behavioral)
```

## Two artifacts (don't confuse them)

| File | What it is | Who writes it |
|---|---|---|
| `room_spec.schema.json` | The **schema** — the rules every RoomSpec must obey (fields, enums, ranges). | Us, once. The AI's structured-output tool uses it. |
| `presets/<type>.preset.json` | A **room-type preset** — defaults + allowed ranges + furniture catalog + named layout slots for one room family. | Us, one per room type. |
| `examples/<type>.spec.json` | A concrete, filled **RoomSpec** (one room). | A human or the AI, per room. |

The preset is the **envelope**; the spec is a **point inside it**. The AI's whole job is: *pick a preset, choose a point.*

## How each consumer uses it

- **AI agent** — receives a natural-language brief + the relevant preset. Emits a RoomSpec via tool use / structured output, so the model is *forced* to produce schema-valid JSON. We validate; on mismatch the model retries. The AI never invents geometry — it only sets knobs and names catalog items.
- **Unity** — deserializes the JSON (`JsonUtility` or Newtonsoft) into a `RoomSpec` C# class, then `RoomBuilder.BuildFromSpec(spec)` builds it. This is a small refactor of today's `RoomBuilder` (move the `[Range]` fields into a serializable spec object).
- **Web viewer** — same JSON, parsed in JS, drives the existing wizard/viewer. The wizard's "create a room" form already produces almost this shape.

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
- Curved *walls/openings* (v1 curvature = rounded corners only — same limit as our prototype; Tawil hand-built separate kits).
- Free-form furniture placement beyond named slots.
- Per-surface photoreal PBR sets (v1 = material enum; textures resolved by the consumer).
- Multi-room / floorplans (v1 = one room per spec).

## Next steps after this draft

1. Add presets for `bedroom`, `classroom` (mirror `dining_room.preset.json`).
2. Refactor `RoomBuilder` → `BuildFromSpec(RoomSpec)`.
3. Stand up the AI path: prompt + preset → Claude structured output → validate against `room_spec.schema.json` → write `room_spec.json` → Unity builds it. **(API key server-side only — never in a Unity build.)**
