# Response log — the data contract

*Companion to [response_log.schema.json](response_log.schema.json). One participant response = one row. Produced by the experiment runtime (Unity), consumed by the Cloudflare Worker (`POST /responses`), the local CSV export, and analysis.*

## Rules

1. **One row per response, written immediately.** The runner persists each row when it is produced (local file append + queued POST), never only at session end — a crash loses at most the in-flight trial.
2. **Rows are self-describing.** `pair_id`, `manipulated_variables`, `modality`, `execution_path` are copied into every row so a CSV is analyzable standing alone, without joining back to the study.
3. **Modality is a variable** (guardrail #3). Never pool rows across `modality` values. The enum is 3D-only by design (`desktop_3d`, `pcvr`, `standalone_vr`) — there is deliberately no 2D/image modality; adding one is a contract change requiring a Kirsh-level decision (see ARCHITECTURE.md decision log DL-11).
4. **Determinism.** `presentation_order_seed` + `trial_index` fully reconstruct what was shown when. The runner's shuffle MUST be a seeded PRNG (C# `System.Random(seed)`; any JS port uses mulberry32) — never `UnityEngine.Random` without a recorded seed.
5. **Timing honesty.** `rt_ms` is optional and accuracy-first: native desktop timing is trustworthy; if a browser arm ever exists, its RT carries ~80–100 ms platform lag ([PROPOSAL.md](../docs/PROPOSAL.md) §4) and must not be compared against native rows.
6. **Validation at the sink.** The Worker schema-validates every posted row and rejects the batch member that fails (`422`, error envelope per [contracts/ROOM_API.md](contracts/ROOM_API.md)); the runner also validates before writing the local CSV. Fixture rows: [fixtures/response_rows.json](fixtures/response_rows.json).

## Canonical CSV column order

The CSV export (runner-local and `GET /studies/:id/responses.csv`) uses exactly this order:

```
schema_version, study_id, pair_id, participant_id, session_id, trial_index,
task_type, condition, manipulated_variables, modality, execution_path,
response_json, rt_ms, timestamp_utc, presentation_order_seed, runner_version, spec_sha256
```

- `manipulated_variables` is serialized as a `;`-joined list (e.g. `shell.ceiling_height_m`).
- `response_json` is the full `response` object as a JSON string (task-specific fields stay lossless).
- Empty optional fields are empty strings, never `"null"`.
- Header row always present; UTF-8, LF line endings, RFC-4180 quoting.

## Per-task `response` payloads (v1 registry)

| task_type | payload | notes |
|---|---|---|
| `rating` | `{scale_min, scale_max, value, prompt?}` | single-stimulus scale rating; `value` within `[scale_min, scale_max]` is runner-enforced |
| `choice` | `{chosen: left\|right, left_condition, right_condition, prompt?}` | A-vs-B; side assignment counterbalanced by the session seed, so `chosen` + side conditions recover the preferred condition |

Extending the registry (e.g. `proofreading`, `pointing`) = a coordinated contract change: bump this doc + the schema + the runner + fixtures together (COORDINATION.md rule). The v2 candidates and their measures are listed in [docs/PROPOSAL.md](../docs/PROPOSAL.md) §4.
