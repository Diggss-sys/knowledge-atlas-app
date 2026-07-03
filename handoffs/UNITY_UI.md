# HANDOFF — UNITY_UI (role P1: the operator's instrument panel)

*Self-contained. You (+ your AI) need ONLY this file and the contracts it names. Repo `https://github.com/Diggss-sys/knowledge-atlas-app`, branch `Diggss-sys-branch`; work on a feature branch, PR back ([COORDINATION.md](COORDINATION.md)).*

## Context (3 paragraphs)

This platform lets a student author a control/treatment room pair that differs in exactly one variable, walk it, run a task, and collect data. The product surface for the *operator* (the student designing the experiment) is YOUR workstream: a **Unity UI Toolkit panel** inside the native HDRP app ([docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md) — the operator UI box). Prof. Kirsh's requirement verbatim: a non-engine user must be able to say "make the ceiling high" — the front end is the product.

You never touch the generator. You speak to it only through the **engine seam** (`ISpecChannel` — in-process `LocalChannel` in v1): push a full RoomSpec, receive `spec_applied`/`pair_loaded` events. The generator (E1's workstream) guarantees atomic apply — a bad spec never leaves a half-built room — and returns structured errors you render verbatim.

The scientific centerpiece of your UI is the **live diff panel + publish gate**: while the operator edits, the pair's validation state (from the C# pair gate, delivered in `pair_loaded.validation` and re-computable via `PairGate`) renders continuously — green when the diff equals the declared variable, red with the confound list otherwise — and **publish is disabled unless validation passes**. A confounded pair can be *edited* freely but never *saved as a study* (locked decision DL-6).

## Contracts you implement/consume

- `spec/room_spec.schema.json` — the value objects your controls bind to (READ ONLY).
- `spec/presets/dining_room.preset.json` + `spec/PRESETS.md` — sliders MUST be constrained to preset `ranges`; `manipulable_variables` drives which fields get "declare as independent variable" affordances.
- `spec/contracts/ENGINE_SEAM.md` — your only channel to the renderer; debounce slider streams ~150 ms.
- `spec/study.schema.json` — what publish produces (embedded snapshots + validation stamp + task config).
- `spec/fixtures/diff_vectors.json` — your diff-panel rendering logic is tested against these cases (green/red + which codes show).
- `spec/contracts/ROOM_API.md` — the library browse/save calls (OPTIONAL in v1; local save/load is the requirement, Cloudflare is P3's endpoint to wire when ready).
- `spec/contracts/AI_AUTHORING.md` — the NL edit copilot you host (step U5): the `IAiAuthor` interface, wire protocol, guardrails, and acceptance checks.

## Scope / NOT scope

**Yours:** the UI Toolkit panel (sliders/dropdowns/toggles bound to RoomSpec fields within preset ranges) · control/treatment pair workflow (save control → duplicate → edit treatment) · live diff panel · publish gate + study creation (task type + config form) · local save/load (specs + studies as JSON in `Application.persistentDataPath`) · camera-mode switcher (walk/orbit) for preview · the **AI edit copilot** text box (U5, per AI_AUTHORING.md — feature-flagged; the AI is just a second producer of specs, your sliders stay ground truth).
**NOT yours:** rendering/generation (E1) · running participants (E3 — you produce the study document, the runner consumes it) · the Worker (P3) · gate SEMANTICS (P2 owns the validator; you render its output, never re-implement the rules).

## Build steps (in order)

1. **U0 — Boot + mock channel.** Boot Unity per [UNITY_GENERATOR.md](UNITY_GENERATOR.md) G0. Then build `MockSpecChannel` implementing `ISpecChannel` from the contract alone (echoes `spec_applied{ok:true}`, replays fixture errors) so you never block on E1. *DoD: your panel drives the mock; `seam_messages.json` valid entries round-trip.*
2. **U1 — Single-room editor.** Panel binding the dining preset's fields: shell sliders (width/length/ceiling within `ranges`), contour toggle, surface material dropdowns + tint, lighting preset dropdown + warmth/intensity sliders, furniture list (add/remove from catalog, slot dropdown per PRESETS.md). Debounced `apply_spec` on change; `spec_applied.errors` rendered as a toast/panel, verbatim codes + messages. *DoD: every `manipulable_variables` field is editable and clamped to its range.*
3. **U2 — Pair workflow + live diff.** "Set as control" freezes a copy; editing continues on the treatment; a declared-variable picker (from `manipulable_variables`); the diff panel renders `validation` live: PASS state (green, "differs only in: shell.ceiling_height_m", coupled-variable notes listed) and FAIL state (red, per-violation rows with code + path + message). *DoD: reproducing each diff_vectors case in the UI shows exactly its expected codes.*
4. **U3 — Publish gate.** "Publish study" form (title, hypothesis, task type + config per `study.schema.json`) enabled ONLY when validation.ok; emits a schema-valid study JSON with embedded spec snapshots + the validation stamp; saves locally; POSTs to `PUT /studies` when P3's Worker exists (feature-flag it). *DoD: a published study validates against `study.schema.json`; publish is provably impossible for the confounded fixture pair.*
5. **U4 — Library + polish.** Local library browse (saved rooms/pairs/studies with thumbnails via `capture_screenshot`); replace the legacy IMGUI studio in a coordinated PR with E1. *(M2 milestone.)*
6. **U5 — AI edit copilot (minimal).** Implement `spec/contracts/AI_AUTHORING.md` Phase A: a text box in the panel → `IAiAuthor.ProposeEdit` (direct REST, `claude-sonnet-5`, structured output per the contract) → validate + clamp client-side → apply through your normal debounced path so the sliders visibly move and the diff panel judges it → rationale shown; refusals rendered honestly. Feature flag `ai_author.enabled`; `ANTHROPIC_API_KEY` from env (settings override); EditMode tests against a mock `IAiAuthor` (CI needs no key). *DoD: the contract's four acceptance checks pass, including the canned-malformed-reply retry path.* *(M2–M3; demo includes it.)*

## Environment gotchas

- UI Toolkit (not IMGUI, not uGUI) — runtime `UIDocument` + USS; keep all layout in `.uxml`/`.uss` under `unity/Assets/RoomGen/UI/`.
- Bind to a plain C# RoomSpec model (Newtonsoft), serialize on change — do NOT scatter state across controls.
- Slider streams must be debounced BEFORE the channel (~150 ms) and must not emit while dragging at frame rate.
- The panel lives on the PC monitor; in the VR phase it stays desktop-side while the subject wears the headset — never assume the panel and the room camera share a display.

## Your integration role

Tracer bullet (M1): U1 drives E1's real `LocalChannel` for the ceiling pair. M2: U2+U3 make the platform an instrument — this is what gets demoed to Kirsh as "a student changes one thing, the system keeps them honest." P2 (Diego) reviews your diff-panel rendering against the fixtures.
