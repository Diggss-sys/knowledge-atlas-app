# HANDOFF — CLOUDFLARE_DATA (role P3: the library + the data sink)

*Self-contained. You (+ your AI) need ONLY this file and the contracts it names. Repo `https://github.com/Diggss-sys/knowledge-atlas-app`, branch `Diggss-sys-branch`; feature branch → PR ([COORDINATION.md](COORDINATION.md)). No Unity required for this workstream.*

## Context (3 paragraphs)

The platform generates controlled room stimuli in a native Unity app and logs participant responses. Rooms are stored as **RoomSpec JSON** (a few KB — the recipe, not the mesh; [docs/STORAGE.md](../docs/STORAGE.md)); your workstream is the cloud half: a **Cloudflare Worker + D1 + R2** that is the room library, the study registry, and the response sink. Hard boundary: Cloudflare is **never in the live render/edit loop** — the app reads a spec once, then runs locally; you are storage and sync, not runtime.

Important scope note: **the V1 Kirsh demo does not depend on you** (local CSV is the fallback per decision DL-5), so you can build against `wrangler dev` on your own machine without ever blocking the Unity trios. Your workstream turns "a demo" into "a shared library + global data collection" (milestone M4).

The predecessor repo (`1michaelbongiorno/cogs160track3v2`, reachable read-only as a sibling checkout) contains a **proven Worker pattern** to mine: `cloudflare/worker.js` + `cloudflare/upload_room.py` (API-key auth, content-addressed R2 keys, deploy flow). Mine the *patterns* (auth header handling, hashing, CORS), not the code wholesale — that Worker stored heavy GLB assets; yours stores tiny JSON.

## Contracts you implement

- `spec/contracts/ROOM_API.md` — YOUR surface: routes, auth, status vocabulary, the error envelope (`{"ok":false,"violations":[{code,path,message}]}`), content-addressing rule.
- `spec/contracts/schema.sql` — the D1 schema verbatim (including the `UNIQUE(session_id, trial_index)` idempotency constraint).
- `spec/room_spec.schema.json`, `spec/study.schema.json`, `spec/response_log.schema.json` — server-side validation of everything that enters the store.
- `spec/fixtures/diff_vectors.json` — **your JS pair gate must reproduce every case** (this is the server-side single-variable gate on `PUT /pairs` and `PUT /studies`).
- `spec/fixtures/response_rows.json` — your row validation must accept/reject as marked.
- `spec/RESPONSE_LOG.md` — the canonical CSV column order for the export route.

## Scope / NOT scope

**Yours:** the Worker (routes per ROOM_API.md) · D1 migrations from schema.sql · R2 thumbnail storage · `web/shared/pair_gate.js` (the JS port of `tools/validate_pair.py` — flatten, exemptions, coverage, the seven codes, coupled notes) · JSON-Schema validation in the Worker (use `ajv` bundled at build time) · the CSV export · deploy runbook + `wrangler dev` test script.
**NOT yours:** anything in-Unity · the gate's SEMANTICS (P2/Diego owns rule questions; you match the fixtures bit-for-bit) · auth beyond the per-cohort `X-API-Key` (v1 keeps it simple).

## Build steps (in order)

1. **C0 — Port the gate.** `web/shared/pair_gate.js`: `validatePair(specA, specB, schema) → {ok, violations, diff, notes}` mirroring `tools/validate_pair.py` exactly (read it — it's 200 lines). Test with node against `diff_vectors.json`. *DoD: all 8 cases reproduce `expected` exactly (ok, sorted codes, diff paths, notes_count).*
2. **C1 — Worker skeleton.** New Worker `atlas-room-api`; D1 database `atlas_rooms`; R2 bucket `atlas-room-assets`. `GET /healthz`; error envelope middleware; `X-API-Key` check (Worker secret) on mutating routes; CORS allowing the app origin (native app → `*` is acceptable v1, note it). Apply `schema.sql` as the initial migration. *DoD: `wrangler dev` + a curl script: healthz 200; missing key → 401 envelope.*
3. **C2 — Rooms + pairs.** `GET/PUT /rooms`, `GET/PUT /pairs` per ROOM_API.md: canonical-JSON sha256 ids (sorted keys, no whitespace — match the Unity side's hashing; put ONE canonicalization function in `web/shared/`), schema validation (422 + violations), the pair gate on `PUT /pairs` (422 with the validator's codes verbatim for confounded pairs), thumbnails to R2. *DoD: the committed ceiling pair PUTs successfully; the confounded fixture pair returns 422 with `undeclared_change`.*
4. **C3 — Studies + responses.** `GET/PUT /studies` (schema + `validation.ok===true` + re-run the gate on embedded specs), `POST /responses` (batch all-or-nothing, per-row violations, idempotent upsert on the UNIQUE constraint), `GET /studies/:id/responses.csv` (canonical order, authed). *DoD: fixture rows batch-POST → inserted; each invalid fixture row 422s with its reason; re-POSTing the same batch inserts nothing new; CSV column order matches RESPONSE_LOG.md exactly.*
5. **C4 — Deploy + runbook.** `npx wrangler login` (the only human-OAuth step) → deploy → smoke the curl script against production → write `web/RUNBOOK.md` (resource names, secrets, deploy, rollback, quota notes: D1 free tier is fine for KB-scale specs; thumbnails capped ~100 KB).

## Environment gotchas

- Node + `npx wrangler` (no global install needed); Windows: quote paths, prefer PowerShell-safe scripts since teammates run them.
- D1 is SQLite-flavored: no `RETURNING` on older compat dates; use `ON CONFLICT (session_id, trial_index) DO NOTHING` for idempotency.
- Bundle `ajv` + compiled schemas at build time (Workers have no runtime `require`); keep the Worker < 1 MB.
- Never log API keys or full response rows in `console.log` (Workers logs are retained).
- `.dev.vars` / `wrangler.toml` with secrets stay untracked (repo `.gitignore` covers them — verify before first commit).

## Your integration role

M4 milestone owner. E3's R3 step tests against your `wrangler dev`; P1's publish flow feature-flags onto your `PUT /studies`. P2 (Diego) reviews your pair gate's fixture conformance. Until C4, everything upstream of you works offline by design.
