# RoomSpec v1.1 — proposed schema batch

*Status: PROPOSAL for the next iteration round. Batches every contract change surfaced by the manipulation audit ([PHASE2_PLAN.md](PHASE2_PLAN.md)) and the rendering research ([RENDERING_RESEARCH.md](RENDERING_RESEARCH.md)) into ONE coordinated bump, so schema + validator + presets + Unity move in lockstep (COORDINATION.md rule). v1.0 stays frozen until this is locked.*

## Why one batch, not four bumps

Each new manipulation (richer lighting, item identity, bowed walls, curviness slider) touches the same four places: `room_spec.schema.json`, `validate_pair.py`, the preset files, and the Unity `RoomSpecModel`. Shipping them separately = four rounds of cross-component churn. Batch = one migration, one `spec_version` change `1.0 -> 1.1`.

**Compatibility rule:** v1.1 is **additive + backward-compatible**. Every v1.0 spec is a valid v1.1 spec. New fields are optional with defaults that reproduce v1.0 behavior. `spec_version` bumps to `"1.1"`; validators accept both.

---

## Change 1 — lighting block: indoor / outdoor / bounce split

**Problem:** v1.0 lighting is single-channel (`preset`, one `warmth`, one `intensity`). Kirsh manipulation #2 is "windows / natural lighting"; the platform must separate **natural (sun through windows)** from **artificial (fixtures)** and expose **bounce** ("light buoyancy"). The old project already proved the model (daylight_intensity vs lighting_intensity; Kelvin law).

**Design:** keep `preset` as the base mood; keep flat `warmth`/`intensity` as DEPRECATED-but-accepted aliases (back-compat); add nested `natural`, `artificial`, `bounce`. Nested values override the preset.

```jsonc
"lighting": {
  "type": "object",
  "required": ["preset"],
  "additionalProperties": false,
  "properties": {
    "preset":    { "enum": ["neutral_daylight","warm_evening","cool_clinical","dim","bright_office"] },
    "warmth":    { "type": "number", "minimum": 0, "maximum": 1, "description": "DEPRECATED v1.0 alias -> artificial.warmth. 0=warm,1=cool." },
    "intensity": { "type": "number", "minimum": 0, "maximum": 2, "description": "DEPRECATED v1.0 alias -> overall multiplier." },
    "hdri":      { "type": "string", "default": "none" },

    "natural": {                                  // OUTDOOR / sun through windows
      "type": "object", "additionalProperties": false,
      "properties": {
        "intensity":    { "type": "number", "minimum": 0, "maximum": 2, "description": "Sun/sky contribution through windows. =old daylight_intensity." },
        "time_of_day":  { "type": "number", "minimum": 0, "maximum": 1, "description": "0=dawn .. 0.5=noon .. 1=dusk. Drives sun ANGLE and sun COLOR together (couples elevation+CCT)." }
      }
    },
    "artificial": {                               // INDOOR / fixtures
      "type": "object", "additionalProperties": false,
      "properties": {
        "intensity": { "type": "number", "minimum": 0, "maximum": 2, "description": "Ceiling/lamp fixture output. =old lighting_intensity." },
        "warmth":    { "type": "number", "minimum": 0, "maximum": 1, "description": "0=warm .. 1=cool. Kelvin = 6500 - 3800*warmth (old-project law)." }
      }
    },
    "bounce": { "type": "number", "minimum": 0, "maximum": 1, "default": 0.8, "description": "Indirect-light strength = runtime GI probe intensity (RENDERING_RESEARCH.md §3.4). 0=direct only, 1=full bounce. Default 0.8 matches old viewer." }
  }
}
```

**Normative warmth law** (Unity must implement): `Kelvin = 6500 - 3800 * warmth`, then Tanner-Helland kelvin->RGB. Single source of truth for both `natural` (via time_of_day) and `artificial.warmth`.

**Single-variable use:** a windows/lighting study declares e.g. `["lighting.natural.intensity"]` and changes only that. Clean dotted path -> validator already covers it (`_is_covered` handles nested).

---

## Change 2 — furniture item identity (fixes the index-shift bug)

**Problem:** validator flattens furniture as `furniture[0]`, `furniture[1]`... Add/remove an item -> every later index shifts -> cascade of false `undeclared_change`. Can't cleanly run a "with vs without sideboard" study.

**Design:** optional `instance_id` per furniture entry (stable string handle). Validator matches items **by instance_id**, not array position.

```jsonc
"furniture": {
  "type": "array", "maxItems": 40,
  "items": {
    "properties": {
      "instance_id": { "type": "string", "pattern": "^[a-z0-9_]{1,40}$",
                       "description": "Stable per-item handle. Required when furniture is a manipulated variable; lets the validator diff items by identity, not list order." },
      "catalog_id":  { "type": "string" },
      "placement":   { /* unchanged */ },
      "material_override": { /* unchanged */ }
    }
  }
}
```

**Validator change** (`validate_pair.py`): when diffing furniture, key items by `instance_id` (fall back to array index if absent, = v1.0 behavior). New declared-variable forms:
- `furniture[id=sideboard_1]` — that one item may differ (move/recolor).
- `furniture[id=sideboard_1].presence` — that item may be absent in one condition (presence study).

New violation codes: `furniture_id_missing` (item differs but no instance_id to track it), `furniture_id_duplicate` (same instance_id twice in one spec).

**Alternative considered + rejected:** order-insensitive multiset match on `(catalog_id, placement)`. Rejected — can't express "the SAME chair moved" vs "a different chair"; identity is cleaner and explicit.

**Density knob — DEFERRED to v1.2** (decision locked). For clutter studies, a preset scalar like `cabinet_density` (old kitchen manifest pattern) would expand into N items. Needs generator expansion logic (scalar -> N placed items) the Unity side isn't built for yet. `instance_id` (Change 2) already covers explicit add/remove presence studies, so v1.1 ships without density; revisit in v1.2 once the generator can place procedurally.

---

## Change 3 — geometry: curviness + per-wall bow

**Problem:** v1.0 contour is enum `angular|curved` only. Need a continuous curviness slider AND walls that bow in/out (RENDERING_RESEARCH.md §4).

```jsonc
"shell": {
  "properties": {
    "contour": { "enum": ["angular","curved"], "description": "DEPRECATED-soft: angular=curviness 0, curved=curviness 1. Kept for v1.0 specs." },
    "curviness": { "type": "number", "minimum": 0, "maximum": 1, "default": 0,
                   "description": "Corner radius = curviness * min(width,length)/2. 0=sharp box, 1=stadium. Overrides contour when present." },
    "wall_bow": {                                 // NEW: bend walls in/out
      "type": "object", "additionalProperties": false,
      "properties": {
        "front": { "type": "number", "minimum": -1, "maximum": 1 },
        "back":  { "type": "number", "minimum": -1, "maximum": 1 },
        "left":  { "type": "number", "minimum": -1, "maximum": 1 },
        "right": { "type": "number", "minimum": -1, "maximum": 1 }
      },
      "description": "Per-wall circular-arc bow. -1=concave (into room), 0=flat, +1=convex (outward). Sagitta = bow * preset.bow_max (per-preset, room-appropriate)."
    }
  }
}
```

**Generator note (Unity):** keep curved-wall surfaces **matte** (roughness floor) — concave + glossy = focusing caustics = confound (RENDERING_RESEARCH.md §2). Openings stay on straight sections / flat walls in v1.1; openings-on-bowed-walls deferred.

---

## Change 4 — new coupled variables (validator notes)

Add to `COUPLED_VARS` in `validate_pair.py` (printed as notes, not violations — researcher must account for them):

| Manipulated var | Coupled note |
|---|---|
| `shell.ceiling_height_m` | *(extend existing)* also changes floor illuminance: pendant inverse-square (3.2 vs 2.6 m -> ~34% less floor light) + larger surface area -> dimmer bounce (RENDERING_RESEARCH.md §2) |
| `shell.curviness` | rounds corners -> removes corner shadow gradient -> raises minimum luminance |
| `shell.wall_bow.*` | concave raises local bounce (view factors); convex flattens it; changes floor area + wall arc length |
| `lighting.natural.intensity` | shadow depth/contrast shift with daylight level |
| `lighting.natural.time_of_day` | sun ANGLE + sun COLOR (CCT) move together — never an isolated single variable |
| `lighting.bounce` | raises perceived overall brightness (adds indirect on top of direct) |
| window count/size (openings) | changes daylight admitted -> couples to `lighting.natural` |

---

## Migration checklist (when locked)

1. `room_spec.schema.json`: bump `spec_version` const accept `["1.0","1.1"]`; add fields above; keep `additionalProperties:false`.
2. `validate_pair.py`: furniture-by-instance_id diff; `furniture[id=X]` + `.presence` declared forms; new violation codes; extend `COUPLED_VARS`.
3. `tests/test_validate_pair.py`: add cases — furniture presence study passes; index-shift no longer false-positives; new codes fire.
4. Preset files: add `curviness`/`wall_bow`/lighting ranges to `manipulable_variables` + `ranges`; add per-preset `bow_max` (meters, room-appropriate max sagitta).
5. Unity `RoomSpecModel`: parse new fields; implement Kelvin law, bounce probe scaling, bow mesh.
6. Bump `examples/` + the ceiling pair? No — they stay valid v1.0; add NEW example pairs (windows study, furniture-presence study, contour study) to exercise v1.1.

## Decisions — LOCKED 2026-06-11
- **Sun control: coupled `time_of_day`** (one knob, angle+CCT together). Physically honest, confound-proof, matches old project. No decoupled angle/CCT knobs.
- **Flat `warmth`/`intensity`: keep as aliases forever.** Old v1.0 specs never break; determinism guardrail holds. No v2 removal planned.
- **`bow_max`: per-preset** (meters). Each room type sets a geometry-safe max sagitta (small rooms small, halls large). Added to preset migration step 4.
- **Density knob: deferred to v1.2.** Keeps v1.1 batch tight; `instance_id` already covers add/remove presence studies. Revisit once generator does procedural placement.
