# Room generation research — making random rooms that look real (no slop)

*Question (Diego, 2026-06-27): how do we randomly generate rooms that look real and plausible — generate a whole house and scrap the rooms, or generate the rooms directly? They must be plausible, nice, no slop. Room types: office, bedroom, living room, bathroom, dining room, kitchen. Researched + cited. Fits our Unity runtime, parametric, live-edit architecture.*

## Verdict (short)

- **Generate rooms DIRECTLY, per room type — do NOT generate a whole house and scrap rooms.**
- **"Random" must be CONSTRAINED, not free.** Plausible rooms come from a **per-room-type template (the preset envelope) + parametric variation + rule-driven, constraint-based furniture placement** — never arbitrary randomness. This is exactly how the good systems (ProcTHOR, constraint-DSL solvers) do it, and it maps onto our existing preset + slot + footprint + seed system.
- This also fits our architecture: template+constraint sampling is **fast and runtime** (no offline bake), seeded (reproducible), and gives the **tight control** single-variable experiments need.

## Why NOT "generate a house, scrap the rooms"

1. **We never need the house.** Our stimuli are single controlled rooms, not navigable buildings. Generating a whole house = mostly wasted work, then you throw it away.
2. **Whole-layout generation is globally incoherent.** Wave Function Collapse and similar whole-floor-plan methods are "locally fine but globally a mess — the algorithm lacks a global overview" ([WFC tips, BorisTheBrave](https://www.boristhebrave.com/2020/02/08/wave-function-collapse-tips-and-tricks/), [WFC building gen thesis](https://reposit.haw-hamburg.de/bitstream/20.500.12738/15709/1/BA_Procedural%20Generation%20of%20Buildings_geschw%C3%A4rzt.pdf)). WFC works best **filling detail inside a fixed boundary**, not authoring a coherent house.
3. **Loss of control.** A room carved from a random house inherits random window/door positions, proportions, and adjacencies — the opposite of the reproducible, single-variable control our experiments require.

## Why NOT pure random

Plausible interior scenes are **not** sampled freely — they come from **constraints + design rules**. The research consensus: indoor scene synthesis "incorporates interior-design guidelines, ergonomic objectives, and spatial priors to optimize plausible arrangements"; the strong systems use "a domain-specific language for constraints + a solver that maximally satisfies them" or "learn arrangement priors (object co-occurrence, support relations, spatial distributions) from example scenes" ([ProcTHOR / scene synthesis survey](https://arxiv.org/html/2512.11234), [constraint-based arrangement](https://www.researchgate.net/publication/220507604_Procedural_Arrangement_of_Furniture_for_Real-Time_Walkthroughs)). Pure random = slop; constrained sampling = plausible.

## Our approach — template + parametric + constrained layout

This is the same shape as the systems above, and it's already half-built in our preset/slot design:

1. **Per-room-type template (= our preset envelope).** A hand-designed envelope per type: allowed size/ceiling ranges, material palettes, furniture catalog, and **named layout slots** (anchors). The "random" room is a **seeded sample inside this envelope**, never arbitrary. (We already do this for `dining_room`.)
2. **Constrained furniture placement.** Slots anchor pieces to walls/zones; `footprint_m` prevents overlap; add **clearance + functional-zone rules** so nothing blocks doors or walkways. Real design clearances to bake in ([Decorilla](https://www.decorilla.com/online-decorating/placing-furniture/), [JD Elite spacing](https://jdelite.decoratingden.com/furniture-spacing-guidelines-every-room/)):
   - walkways **30–36 in**; main route door→seating keep **30 in** clear;
   - sofa↔coffee table **14–18 in**; facing seats **6–8 ft**;
   - bed↔dresser **24 in** (drawer clearance); dining chair **~45 cm egress + ~90 cm behind**.
3. **Anchor + zone logic per type** (functional grammar): big pieces against walls, seating around a focal point, circulation along the sides. Rectangular rooms anchor seating centrally with side circulation; square rooms favor symmetry ([IDI](https://www.theinteriordesigninstitute.com/us/en/blog-mastering-furniture-arrangement-for-optimal-space)).
4. **Reject-and-resample.** A layout that violates a hard constraint (overlap, blocked door, walkway < min) is rejected and re-sampled — so bad gens never ship.
5. **Seeded determinism.** seed → the same room (our guardrail). Random rooms are reproducible samples.
6. **Curated, pinned assets.** Slop also comes from bad meshes/materials — avoided by pinned CC0 PBR sets + curated furniture (see ASSET_SOURCING.md), not procedural-junk geometry.

## The six room types — template sketch (required anchors + rules + key clearances)

| Type | Anchors against walls | Focal / zone logic | Key clearances |
|---|---|---|---|
| **office** | desk (facing wall or window), shelving/cabinets on walls | desk = focal; chair circulation behind | chair pull-out ~90 cm; walkway 30–36 in |
| **bedroom** | bed centered on one wall, nightstands flanking, dresser/wardrobe on another wall | bed = focal | bed↔dresser 24 in; path both sides of bed ~24 in |
| **living room** | sofa(s), media unit / fireplace on a wall | seating ring around focal (TV/window); rug zones | sofa↔coffee 14–18 in; facing seats 6–8 ft; walkway 30–36 in |
| **bathroom** | fixtures (sink/toilet/tub) on a plumbing wall | compact, fixed fixture cluster | in-front-of-fixture clearance ~21–30 in |
| **dining room** | table centered; sideboard on a wall | table = focal; chairs ringed | chair egress ~45 cm + ~90 cm behind (our dining preset already) |
| **kitchen** | counters/cabinets/appliances along walls | **work triangle** sink–stove–fridge; optional island | aisle ≥ 36–42 in; appliance door swing clear |

Each becomes a `*.preset.json` (mirror the dining preset): defaults, ranges, furniture catalog with footprints, named slots, **plus** new clearance/zone constraints the layout sampler enforces. Aspirational/decor knobs (clutter, artwork) stay non-geometry approximations.

## Why this beats the alternatives for us

- **vs whole-house scrap:** no wasted generation, full per-room control, no global-incoherence problem.
- **vs Infinigen (MB's path):** Infinigen Indoors is photoreal but **offline, 3–4 h/seed, and the furniture comes out as broken instancers/flat-white on export** (their own known issues). Our template+constraint sampler is **runtime-fast, seeded, controllable, live-editable** — the whole point of our architecture.
- **vs pure random / WFC:** constrained templates give plausible, no-slop rooms; WFC/free-random give global mess.

## Anti-slop checklist (acceptance bar)
- No overlapping furniture (footprint test); nothing blocks a door or window.
- All walkways ≥ 30 in; required clearances per table above met.
- Every piece anchored to a slot/zone (no floating-in-space furniture).
- Materials from pinned CC0 PBR sets at real-world scale; no flat-white/junk meshes.
- Seeded + reproducible; passes a layout-validity check before it's saveable.
- Judge against the realism gate (presence pilot) before participant use.

## Open questions for the next round
- Layout sampler: hand-rule constraints first (fast, controllable) vs a learned/solver approach later? Recommend **rules first**.
- How much variation per type is enough for the experiment library (seed/variant count)?
- Decor realism (rugs, plants, artwork) — how far before it's slop or a confound?

## Sources
- ProcTHOR / constraint scene synthesis — [RoomPilot survey](https://arxiv.org/html/2512.11234) · [procedural furniture arrangement](https://www.researchgate.net/publication/220507604_Procedural_Arrangement_of_Furniture_for_Real-Time_Walkthroughs) · [Infinigen Indoors](https://arxiv.org/html/2406.11824)
- WFC / whole-layout limits — [BorisTheBrave WFC](https://www.boristhebrave.com/2020/02/08/wave-function-collapse-tips-and-tricks/) · [WFC plan adjacencies](https://www.samuelaston.com/wave-function-collapse-plan-adjacencies/) · [building layout review](https://arxiv.org/pdf/2504.09694)
- Design clearances/zones — [Decorilla](https://www.decorilla.com/online-decorating/placing-furniture/) · [JD Elite spacing](https://jdelite.decoratingden.com/furniture-spacing-guidelines-every-room/) · [IDI arrangement](https://www.theinteriordesigninstitute.com/us/en/blog-mastering-furniture-arrangement-for-optimal-space)
