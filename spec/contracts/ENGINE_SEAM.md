# ENGINE_SEAM — the runtime contract (ISpecChannel + IRoomRuntime)

*`seam_version: 1`. Replaces the retired `VIEWER_BRIDGE.md` (JS↔WebGL) design — with the HDRP-native pivot ([docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md) DL-8) the operator UI and the renderer live in the SAME Unity process, so the seam is a C# interface pair, not a browser bridge. The seam is designed so the networked VR case ([handoffs/VR_LIVE_EDIT.md](../../handoffs/VR_LIVE_EDIT.md)) is a second implementation, not a redesign. Message fixtures: [../fixtures/seam_messages.json](../fixtures/seam_messages.json).*

## Why a seam at all (in one process)

Three consumers drive one renderer: the operator UI (live sliders), the experiment runtime (scripted trials), and — later — a networked operator editing around a headset subject. They all speak **RoomSpec JSON + the messages below**, never touch generator internals. That keeps: UI ↔ generator independently testable, the network transport additive (`NetworkChannel`), and the message log a complete provenance record of everything a subject ever saw.

## The interfaces (C#, namespace `KnowledgeAtlas.Seam`)

```csharp
public interface ISpecChannel {
    // Fire-and-forget submission; results arrive as events. specJson = a FULL RoomSpec.
    void Apply(string specJson, string requestId);
    void LoadPair(string controlJson, string treatmentJson, string requestId);
    void SwitchCondition(string condition, string transition, string requestId); // condition: control|treatment; transition: cut|fade|teleport
    void SetCameraMode(string mode, string requestId);                           // walk|orbit|fixed_eye
    void CaptureScreenshot(string requestId);
    event Action<SeamEvent> OnEvent;
}

public interface IRoomRuntime {                       // implemented by the generator side
    SeamEvent Apply(RoomSpec spec);                   // synchronous core the channel wraps
    // ... mirrors the channel methods; LocalChannel is a thin passthrough.
}
```

`LocalChannel` (v1) = same-process direct calls, events dispatched on the main thread, zero latency. `NetworkChannel` (VR phase) = the same byte-level messages over Netcode/WebSocket; operator side sends, headset side applies. **UI code and generator code MUST NOT know which channel is active.**

## Messages (the wire/log format)

Every call and event serializes to JSON (this is what `NetworkChannel` transmits and what the provenance log records). Envelope:

```jsonc
{ "seam_version": 1, "kind": "apply_spec", "request_id": "r-0042", "payload": { /* per-kind */ } }
```

### Calls (operator/runner → runtime)

| kind | payload | notes |
|---|---|---|
| `apply_spec` | `{ "spec": <RoomSpec> }` | full-spec push; rebuild is wholesale; UI debounces sliders ~150 ms |
| `load_pair` | `{ "control": <RoomSpec>, "treatment": <RoomSpec> }` | runtime validates the pair via the C# gate before building |
| `switch_condition` | `{ "condition": "control"\|"treatment", "transition": "cut"\|"fade"\|"teleport" }` | geometry-safe transitions; VR comfort rules pick fade/teleport |
| `set_camera_mode` | `{ "mode": "walk"\|"orbit"\|"fixed_eye" }` | walk = first-person 1.65 m eye; orbit = editor; fixed_eye = fixed viewpoint for matched stimuli |
| `capture_screenshot` | `{ "width": 1920, "height": 1080 }` | thumbnails + fidelity-gate evidence |

### Events (runtime → operator/runner)

| kind | payload | notes |
|---|---|---|
| `runtime_ready` | `{ "builder_version": "1.0.0", "engine": "unity-6000.3", "pipeline": "hdrp" }` | emitted once on scene ready |
| `spec_applied` | `{ "ok": true, "errors": [], "build_ms": 41, "spec_sha256": "…" }` | after every apply; `errors[]` uses violation-style objects `{code, path?, message}` |
| `spec_applied` (failure) | `{ "ok": false, "errors": [{ "code": "schema_invalid", … }], "build_ms": 3 }` | room stays on the LAST GOOD spec — a failed apply never leaves a half-built room |
| `pair_loaded` | `{ "ok": …, "validation": <validate_pair result>, "active_condition": "control" }` | includes the gate's full result — the UI's diff panel renders straight from this |
| `condition_switched` | `{ "condition": "treatment", "transition": "fade", "ms": 220 }` | |
| `screenshot_captured` | `{ "request_id": "…", "png_base64": "…" }` | |
| `runtime_error` | `{ "code": "…", "message": "…" }` | never a blank/silent failure |

### Error codes (frozen vocabulary)

`schema_invalid` · `unknown_catalog_id` · `furniture_out_of_bounds` · `furniture_overlap` · `furniture_blocks_door` · `preset_missing` · `unsupported_seam_version` · `bad_request` (malformed envelope). Pair-gate codes come from the validator vocabulary and pass through `pair_loaded.validation` untouched.

## Guarantees

1. **Atomic apply**: a failed `apply_spec` leaves the previous room intact and visible.
2. **Ordering**: events for a `request_id` arrive after its call, in order; a newer `apply_spec` supersedes an in-flight one (last-write-wins; superseded requests still emit `spec_applied` with `"superseded": true`).
3. **Determinism**: same spec (+ pinned assets/engine) → same `spec_sha256` room. The runtime never randomizes without the spec's `seed`.
4. **Honesty**: the runtime renders exactly what the spec says or reports failure — no silent approximations (`execution_path` conventions apply).
5. **Provenance**: with logging enabled, every message is appended to a session log (JSONL) — the complete record of what an operator did and a subject saw, timestamped.

## Version rule

`seam_version` bumps on ANY change to kinds, payload fields, or error codes; both sides reject mismatched versions with `unsupported_seam_version`. Changing this file follows the COORDINATION.md contract-change rule (fixtures + both consuming workstreams updated in the same PR).
