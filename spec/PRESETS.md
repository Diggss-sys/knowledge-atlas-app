# Preset contract — how a renderer consumes a RoomPreset + RoomSpec

*Makes the semantics that [presets/dining_room.preset.json](presets/dining_room.preset.json) uses implicitly into a normative contract. The Unity generator ([handoffs/UNITY_GENERATOR.md](../handoffs/UNITY_GENERATOR.md)) implements this; new presets (bedroom, classroom …) must follow it. Closes the gap flagged in [docs/PHASE2_PLAN.md](../docs/PHASE2_PLAN.md) (SlotResolver semantics "currently UNDEFINED").*

## What a preset is

A **RoomPreset** is the *envelope* for one `room_type`: defaults, allowed ranges, the furniture catalog, and named layout slots. A **RoomSpec** picks points inside that envelope. The spatial logic (footprints, spacing, clearance) lives HERE and in generator code — never in the authoring UI or an AI prompt.

## Precedence and ranges

1. **Spec value wins** over preset default; a preset default fills any field the spec omits.
2. If `ranges` declares a span for a field, a spec value outside it is an **authoring-UI clamp + validator warning**, not a hard schema failure (the schema's own min/max remain the hard bounds). The operator UI MUST constrain its sliders to the preset range.
3. `manipulable_variables` lists the fields the operator UI exposes as *independent-variable candidates* for this room type. It is a UI affordance list, not a security boundary — the validator remains the gate.

## Furniture catalog

Each catalog entry: `id`, `label`, and EITHER `footprint_m: [width_x, depth_z]` (floor-standing) OR `ceiling_mounted: true` (hangs from the ceiling).

- `footprint_m` is the item's axis-aligned floor footprint at `rotation_deg = 0`, in meters.
- A spec may only reference `catalog_id`s present in its room_type's catalog (generator error `unknown_catalog_id`).

## Slot resolution (normative)

A slot resolves to `(x_m, z_m, rotation_deg, mount)`. Coordinate frame: origin = room center at floor level, `+x` = right, `+z` = front/door wall (matches `wall_ref` in the RoomSpec schema). Constants: `CLEARANCE = 0.05 m`, door height `2.05 m`, wall thickness `0.15 m` (interior dimensions; walls built outward).

### 1. Absolute slots — `{x_m, z_m, rotation_deg?}`
Place at exactly those coordinates. `ceiling: true` additionally sets `mount = ceiling`: the item hangs with its top at `ceiling_height_m` (e.g. `above_table` pendant).

### 2. Relative slots — `{relative_to, side, index?}`
Distribute items along one side of an anchor item's footprint, facing the anchor:

- Let the anchor's resolved position be `(ax, az)` and its footprint `(aw, ad)`; the placed item's footprint `(iw, id)`.
- **Offset from the anchor edge:** the item's center sits `(anchor_half_extent + CLEARANCE + item_half_extent)` from the anchor center, along the side's axis: `side=front → z = az + ad/2 + CLEARANCE + id/2` (mirror for `back`; use `x`/widths for `left`/`right`).
- **Distribution along the side:** all slots sharing the same `(relative_to, side)` form an ordered group by `index` (missing `index` = 0). With `n` items along a side of usable length `L` (the anchor footprint's extent on that side), item `k` (0-based) centers at fraction `(k + 1) / (n + 1)` of `L`, measured from the side's negative end — i.e. evenly spaced, symmetric, never on the corners.
- **Rotation:** the item faces the anchor: `front → 180°`, `back → 0°`, `left → 90°`, `right → −90°` (rotation_deg is clockwise about +y, 0 = facing +z).

### 3. Wall slots — `{wall, offset_m}`
Center the item on the named wall, its back face `offset_m` from the interior wall face, rotated to face into the room. E.g. `long_wall_back = {wall: back, offset_m: 0.3}` → `z = −(length/2) + 0.3 + id/2`, rotation `0°`.

## Placement validity (generator-enforced)

After resolving all placements the generator MUST check, and report as structured errors (not silently fix):

- `furniture_out_of_bounds` — any footprint corner outside the interior footprint (including curved-corner insets).
- `furniture_overlap` — two footprints intersect (axis-aligned check at resolved rotation, expanded by `CLEARANCE`).
- `furniture_blocks_door` — a footprint intersects the door's swing-clear zone (door width × 1.0 m into the room).

These are *generator* errors surfaced to the operator UI (`SpecApplied.errors`, see [contracts/ENGINE_SEAM.md](contracts/ENGINE_SEAM.md)) — the pair validator does not know about geometry.

## Curved shells

When the shell has rounded corners (contour `curved` / v1.1 `curviness > 0`), the usable interior for bounds-checking is the rounded-rect footprint, and wall slots remain valid only on the straight span of their wall (slots that would land on a corner arc are `furniture_out_of_bounds`).

## Authoring new presets

Copy `dining_room.preset.json`'s shape; every preset MUST provide: `room_type` matching the schema enum, `defaults` sufficient to render with an empty spec body, `ranges` for every `manipulable_variables` entry that is numeric, a catalog where every slot-referenced item exists, and slots that resolve without validity errors on the defaults. A preset that violates this fails `tests/test_fixtures.py::test_presets_resolve` (added with the second preset).
