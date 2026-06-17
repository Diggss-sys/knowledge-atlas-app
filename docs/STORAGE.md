# Storage — saving and reloading rooms

*Locked 2026-06-11. How rooms persist so a student can come back, reload, share, and rerun them. Companion to [PHASE2_PLAN.md](PHASE2_PLAN.md) (the `ROOM_API.md` + `schema.sql` contracts) and the determinism guardrail.*

## The key idea — store the recipe, not the cake

A "room" = a **RoomSpec JSON** (a few KB), **not** a 3D mesh or render. Unity regenerates the room from the spec via `RoomBuilder.BuildFromSpec`. Because of the determinism guardrail (same spec + pinned assets/engine version → same room, forever), the spec **is** the room. So persistence = storing tiny JSON, not gigabytes of geometry.

**Why not store baked meshes/renders:** huge, brittle, version-locked, and it throws away the parametric point. A spec is tiny, diffable, shareable, and reproducible.

## Hybrid: local-first + Cloudflare library

| Layer | Job | Tech |
|---|---|---|
| **Local-first** | instant save/load, offline, dev | Unity writes RoomSpec `.json` to `Application.persistentDataPath` |
| **Cloud library** | persist across machines, browse "my rooms", share pairs, backup | **Cloudflare Worker + D1** (specs) + **R2** (optional thumbnails) |
| **Sync** | local = working copy; cloud = library/backup/share | push to cloud when online |

Flow: edit room → save locally (instant) → push spec to the Worker → D1. Browse/reload: GET list → pick → GET spec → `BuildFromSpec` rebuilds it.

## What a stored room record holds (D1)

Per the drafted `schema.sql`:
- `id` — **content hash (sha256 of the spec)**; immutable, content-addressed.
- `name` — friendly label (a pointer to a hash; renaming never mutates the room).
- `spec_json` — the RoomSpec (the room itself).
- `room_type`, `author`, `created_at`.
- `pair_id` + `condition` — if part of a study pair.
- `validation_json` — the single-variable gate stamp (for pairs).
- optional `thumbnail` key → **R2** (Unity screenshots the room on save for a browse-gallery preview).

**Content-addressed + immutable + keep-every-version:** specs are tiny, so storing every version is free and gives perfect reproducibility (a published study always points at an exact spec hash). A "save" that changes a room writes a new hash; the name pointer moves.

## Two stores — don't confuse them

- **`spec/pairs/` in git** = canonical/seed rooms (ours, version-controlled, used by tests).
- **Cloudflare D1** = user-generated rooms (runtime writes from the Unity app — no git commit needed).

## Boundary (important)

Cloudflare is **storage + the room library + the response sink** — it is **never in the live render or live-edit loop** (that's local Unity; see [VR_LIVE_EDITING.md](VR_LIVE_EDITING.md) compute topology). SDSC (if available) only batch-bakes the offline high-fidelity variant library into this same store. Live editing reads a spec once, then runs locally.
