# ROOM_API — the Cloudflare Worker REST contract

*Storage + library + response sink ONLY — never in the live render/edit loop ([docs/STORAGE.md](../../docs/STORAGE.md) boundary). Adapted from the old repo's proven `cloudflare/` Worker pattern (API-key auth, content-addressed storage). Implemented by [handoffs/CLOUDFLARE_DATA.md](../../handoffs/CLOUDFLARE_DATA.md); D1 tables in [schema.sql](schema.sql).*

## Conventions

- Base: the Worker origin. All bodies JSON (UTF-8). Timestamps ISO-8601 UTC.
- **Auth:** mutating routes and CSV export require `X-API-Key` (per-cohort key, Worker secret). Reads of the shared library are keyless in v1 (private repo + unlisted origin); tighten later without contract change.
- **Error envelope everywhere** (Paco's validator convention over HTTP):
  `{"ok": false, "violations": [{"code": "...", "path": "...", "message": "..."}]}`
- Status vocabulary: `200` ok · `400` malformed request · `401` bad/missing API key · `404` unknown id · `409` conflict (id exists with different bytes) · `422` validation failed (schema or pair gate) · `500` internal.
- **Content addressing:** a room's `id` = sha256 of its canonical spec JSON (sorted keys, no insignificant whitespace). Immutable: a changed spec is a NEW id; names are pointers ([docs/STORAGE.md](../../docs/STORAGE.md)).

## Routes

### Health
- `GET /healthz` → `200 {"ok": true, "d1": true, "r2": true, "version": "..."}`

### Rooms (RoomSpec library)
- `GET /rooms?room_type=dining_room&author=…` → `200 {"ok":true,"rooms":[{id,name,room_type,author,created_at,pair_id?,condition?,thumbnail_url?}]}` — metadata only, no spec bodies.
- `GET /rooms/:id` → `200 {"ok":true,"room":{…meta,"spec":<RoomSpec>}}`.
- `PUT /rooms` *(auth)* — body `{"name": "...", "spec": <RoomSpec>, "thumbnail_png_base64"?: "..."}`.
  Worker: schema-validate the spec (`422` on failure) → compute sha256 id → insert (idempotent if same bytes; `409` if hash collision with different bytes — practically impossible, checked anyway) → store thumbnail in R2 at `thumbs/<id>.png` if provided → `200 {"ok":true,"id":"<sha256>"}`.

### Pairs (the server-side single-variable gate)
- `GET /pairs/:pair_id` → `200 {"ok":true,"pair":{pair_id, control_id, treatment_id, manipulated_variables, validation}}`.
- `PUT /pairs` *(auth)* — body `{"control": <RoomSpec>, "treatment": <RoomSpec>}`.
  Worker runs the **JS pair gate** (a port of `validate_pair.py` that MUST pass [../fixtures/diff_vectors.json](../fixtures/diff_vectors.json)): on violations → `422` + envelope with the validator's codes verbatim; on pass → store both specs as rooms (content-addressed) + a pair row with the `validation` stamp → `200 {"ok":true,"pair_id":"…","control_id":"…","treatment_id":"…"}`.
  **A confounded pair can never enter the library** — same rule as the client-side publish gate, enforced where it can't be bypassed.

### Studies
- `GET /studies?status=published` → metadata list.
- `GET /studies/:study_id` → the full study document ([../study.schema.json](../study.schema.json)).
- `PUT /studies` *(auth)* — body = a study document. Worker: schema-validate; verify `validation.ok === true` AND re-run the pair gate on the embedded specs (`422` otherwise); verify `pair_id` consistency → upsert by `study_id` (drafts mutable; a `published` study is immutable except `status → closed`).

### Responses (the data sink)
- `POST /responses` *(auth)* — body `{"rows": [<ResponseLogRow>, …]}` (batch; the runner posts per-trial with retry, batching is for offline catch-up).
  Worker validates each row against [../response_log.schema.json](../response_log.schema.json): all-or-nothing per request — any invalid row → `422` with per-row violations `{"code":"row_invalid","path":"rows[3].task_type",…}`; valid batch → insert → `200 {"ok":true,"inserted":N}`.
- `GET /studies/:study_id/responses.csv` *(auth)* → `200 text/csv` in the **canonical column order** ([../RESPONSE_LOG.md](../RESPONSE_LOG.md)).

## Non-goals (v1)

No accounts/OAuth (API key per cohort), no spec mutation/deletion (immutability is the feature), no server rendering, no live transport (the seam is local/Netcode — [ENGINE_SEAM.md](ENGINE_SEAM.md)). D1/R2 resource names + deploy runbook live in the Cloudflare handoff.
