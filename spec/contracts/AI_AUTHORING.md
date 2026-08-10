# AI_AUTHORING — the natural-language authoring contract

*Decided 2026-07-03 (ARCHITECTURE.md DL-15; a Kirsh requirement). Phase A = **edit copilot**, minimal version ships with the demo (M2–M3, feature-flagged). Phase B = **experiment-design assistant** (M4). Implemented inside the operator UI workstream ([handoffs/UNITY_UI.md](../../handoffs/archive/UNITY_UI.md) step U5).*

## The invariant (read this first)

**The AI is just another producer of RoomSpecs.** It has zero special powers: its output lands in the same sliders, renders through the same seam, and faces the same single-variable gate as a human author. Nothing downstream knows or cares whether a spec came from a slider drag or a sentence. This is why the feature is cheap and safe here, where the old Blender-MCP plan needed an agent loop with watchdogs — in a parametric architecture, "AI editing" is one structured-output API call that writes a small JSON file.

## Phase A — edit copilot

### Interface (C#, `KnowledgeAtlas.AiAuthor`)

```csharp
public sealed record AiProposal(
    bool Ok,                    // false = honest refusal
    string? SpecJson,           // the FULL proposed RoomSpec (null on refusal)
    string Rationale,           // one-to-three sentences, shown to the operator
    string? RefusalReason);     // why the request can't be done (null on success)

public interface IAiAuthor {
    Task<AiProposal> ProposeEdit(string currentSpecJson, string presetJson, string request);
}
```

### Wire protocol

Direct REST to the Anthropic Messages API (`POST https://api.anthropic.com/v1/messages`) via `HttpClient`/`UnityWebRequest` — no SDK dependency inside Unity. Headers: `x-api-key`, `anthropic-version: 2023-06-01`, `content-type: application/json`.

| Field | Value |
|---|---|
| `model` | **`claude-sonnet-5`** (config key `ai_author.model`; pinned, changing it is a config change not a code change) |
| `max_tokens` | `4096` |
| `output_config` | `{"format": {"type": "json_schema", "schema": <AI_PROPOSAL_SCHEMA>}, "effort": "low"}` |
| `system` | Stable-first for prompt caching: (1) role + honesty rules, (2) the RoomSpec JSON schema, (3) the preset envelope (defaults, ranges, catalog, layout slots), then (4) the current spec |
| `messages` | one user turn: the operator's request verbatim |

`AI_PROPOSAL_SCHEMA` (the structured-output shape — mirrors `AiProposal`):

```jsonc
{
  "type": "object",
  "additionalProperties": false,
  "required": ["ok", "rationale"],
  "properties": {
    "ok":             { "type": "boolean" },
    "spec":           { "type": "object", "description": "FULL RoomSpec (schema-valid, within preset ranges). Omit when ok=false." },
    "rationale":      { "type": "string", "description": "What was changed and why, or why not, in plain language." },
    "refusal_reason": { "type": "string", "description": "Present when ok=false: what was asked that is outside the envelope, and what IS possible instead." }
  }
}
```

> **Known API limitation:** structured outputs guarantee the *shape* but do **not** enforce numeric `minimum`/`maximum`. Range enforcement is therefore CLIENT-side, always: validate + clamp against the preset ranges and the RoomSpec schema after parsing (guardrail 1). This was already our design; the limitation just makes it non-optional.

### Guardrails (all mandatory)

1. **Validate → clamp → retry → honest fail.** Parse the proposal; validate the spec structurally against `spec/room_spec.schema.json` rules and clamp to preset `ranges`. On violations, retry the API call at most **2** times with the violation list appended to the conversation. Still bad → surface an honest failure ("the assistant couldn't produce a valid change"), room unchanged.
2. **The operator stays the author of record.** A proposal is never silently applied: it lands as visible slider/field changes (through the same debounced `apply_spec` path) with the rationale shown; the live diff panel judges it like any manual edit; undo restores the pre-proposal spec.
3. **The gate is unreachable by the AI.** Publish still requires the pair gate to pass; the AI cannot publish, save, or bypass anything. A confounded AI-proposed pair is blocked exactly like a confounded manual one.
4. **Honesty on refusal.** Requests outside the envelope ("add a fireplace" when no fireplace is in the catalog) return `ok=false` with `refusal_reason` naming what *is* possible — never an approximation presented as the requested change (the project's `execution_path` honesty culture applies).
5. **Key handling.** `ANTHROPIC_API_KEY` read from the environment (lab/professor key — DL-15), optional settings-screen override stored in `persistentDataPath` (plaintext local file is acceptable for lab machines, never in the repo). The key is never embedded in builds, never logged, and **masked in seam/session JSONL logs**.
6. **Feature flag.** `ai_author.enabled` (default off until M2). The platform is fully usable without the AI — sliders are always the ground truth (PLAN.md: the AI path is a convenience, never the only way).
7. **Provenance.** AI-proposed specs set `provenance.generated_by = "ai_agent"` (the frozen schema already carries this enum) so every stimulus records its authorship.

### Cost envelope

Sonnet 5: $3/MTok in, $15/MTok out (intro $2/$10 through 2026-08-31). A typical edit call ≈ 3–5K input tokens (schema + preset + spec) + ≤1K output ≈ **$0.01–0.03**; a heavy authoring session stays well under $1. System-block ordering above is cache-friendly (schema/preset stable across calls), which cuts repeat-call input cost ~90%.

### Acceptance (definition of done, Phase A)

- "Make it a warm evening room and raise the ceiling to 3.2 m" → `lighting.preset` and `shell.ceiling_height_m` sliders visibly move; diff panel updates; rationale displayed.
- "Add a fireplace" → honest refusal naming the actual catalog options.
- A canned malformed API reply (mock transport) → retry path exercised → honest failure, room unchanged.
- Flag off → zero API calls, UI fully functional.
- EditMode tests run against a **mock `IAiAuthor`** — CI never needs a key.

## Phase B — experiment-design assistant (M4, contract to be detailed then)

Input adds the researcher's hypothesis ("does ceiling height affect creative thinking?"). Output adds: a suggested control/treatment **pair** (two specs), the declared `manipulated_variables`, a suggested task type, and a **citation** drawn from the literature preset registry (the old repo's `meyers_levy_high/low_ceiling`, `vartanian_*`, `ulrich_*`, `kaplan_*` presets, ported as seed content). Every guardrail above applies; the pair still faces the gate. The detailed schema lands with the RoomSpec v1.1 batch under the COORDINATION.md contract-change rule.
