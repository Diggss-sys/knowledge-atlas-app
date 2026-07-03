-- D1 schema — the Cloudflare data layer (contracts/ROOM_API.md).
-- Storage + library + response sink only; never in the live loop (docs/STORAGE.md).
-- Hot analysis fields are flat columns; every record keeps its full JSON.

CREATE TABLE IF NOT EXISTS rooms (
  id           TEXT PRIMARY KEY,          -- sha256 of canonical spec JSON (content-addressed, immutable)
  name         TEXT NOT NULL,             -- friendly pointer; renaming never mutates the room
  room_type    TEXT NOT NULL,
  spec_json    TEXT NOT NULL,             -- the RoomSpec (the room itself)
  author       TEXT,                      -- cohort/student handle (no auth system in v1)
  pair_id      TEXT,                      -- set when the room is half of a validated pair
  condition    TEXT CHECK (condition IN ('control','treatment') OR condition IS NULL),
  thumbnail_r2 TEXT,                      -- R2 key of the browse thumbnail, e.g. 'thumbs/<id>.png'
  created_at   TEXT NOT NULL              -- ISO-8601 UTC
);
CREATE INDEX IF NOT EXISTS idx_rooms_type    ON rooms(room_type);
CREATE INDEX IF NOT EXISTS idx_rooms_pair    ON rooms(pair_id);

CREATE TABLE IF NOT EXISTS pairs (
  pair_id               TEXT PRIMARY KEY,  -- == experiment.pair_id in both specs
  control_id            TEXT NOT NULL REFERENCES rooms(id),
  treatment_id          TEXT NOT NULL REFERENCES rooms(id),
  manipulated_variables TEXT NOT NULL,     -- JSON array, verbatim from the specs
  validation_json       TEXT NOT NULL,     -- full validate_pair result (the gate stamp); ok MUST be true to exist here
  created_at            TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS studies (
  study_id   TEXT PRIMARY KEY,
  pair_id    TEXT NOT NULL REFERENCES pairs(pair_id),
  title      TEXT NOT NULL,
  modality   TEXT NOT NULL CHECK (modality IN ('desktop_3d','pcvr','standalone_vr')),
  task_type  TEXT NOT NULL CHECK (task_type IN ('rating','choice')),
  status     TEXT NOT NULL CHECK (status IN ('draft','published','closed')),
  study_json TEXT NOT NULL,               -- the full study document (spec/study.schema.json)
  created_by TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT
);
CREATE INDEX IF NOT EXISTS idx_studies_status ON studies(status);

CREATE TABLE IF NOT EXISTS responses (
  id                      INTEGER PRIMARY KEY AUTOINCREMENT,
  study_id                TEXT NOT NULL REFERENCES studies(study_id),
  pair_id                 TEXT NOT NULL,
  participant_id          TEXT NOT NULL,
  session_id              TEXT NOT NULL,
  trial_index             INTEGER NOT NULL,
  task_type               TEXT NOT NULL,
  condition               TEXT NOT NULL,
  modality                TEXT NOT NULL,
  execution_path          TEXT NOT NULL,
  rt_ms                   REAL,            -- nullable: accuracy-first tasks may omit
  presentation_order_seed INTEGER NOT NULL,
  timestamp_utc           TEXT NOT NULL,
  runner_version          TEXT NOT NULL,
  row_json                TEXT NOT NULL,   -- the full ResponseLogRow (lossless; response payload lives here)
  received_at             TEXT NOT NULL,
  UNIQUE (session_id, trial_index)         -- per-trial retry idempotency: a resent row upserts, never duplicates
);
CREATE INDEX IF NOT EXISTS idx_responses_study ON responses(study_id);
