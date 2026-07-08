# SOFTWARE FOUNDATION — the UI/software side, planned by Fable (2026-07-07)

*Context: Diego is taking the engine side; Paco's lane moves to the software/UI side to lay a
foundation P1 can build on. This plan executes the foundational subset of
[UNITY_UI.md](UNITY_UI.md) (P1's handoff) — the parts that are pure architecture and headlessly
testable — and leaves the interactive/visual polish to a human-at-the-machine session. Branch:
`paco/ui-foundation` off `paco/lighting-l0-l4` (depends on the seam + runner from PR #2).*

## Architectural rule (the whole point of this plan)

**Thin views, testable view-models.** Every panel is (a) a pure-C# view-model that talks to the
engine ONLY through `ISpecChannel` and exposes ONLY plain types (the EditMode test assembly cannot
reference Newtonsoft — keep all JSON inside), plus (b) a UXML/USS layout, plus (c) a dumb binder
that wires (a) to (b). The view-model layer is where correctness lives and where tests bite.
UI Toolkit `VisualElement` trees construct fine headlessly in EditMode, so even the bindings are
testable without rendering.

Compatibility with the engine is by construction: the UI never references generator internals —
only `KnowledgeAtlas.Seam` (`ISpecChannel`, `SeamEvent`, `SeamCodes`) and the canonical JSON
contracts. Swapping `MockSpecChannel` → `LocalChannel` is one constructor argument.

## Gates

### S0 — MockSpecChannel + Testing assembly (the mock-first rule made real)
`unity/Assets/RoomGen/Testing/`: `RoomGen.Testing.asmdef` (refs RoomGen.Runtime + Newtonsoft) and
`MockSpecChannel : ISpecChannel`. Behaviour per the contract, from the contract alone:
- Records every call (kind, requestId, payload json) in a public plain-typed list.
- `Apply` → raises `SeamEvent.SpecAppliedOk` by default; a scripted-failure queue lets tests enqueue
  canned `SeamEvent`s (e.g. schema_invalid with paths) that are raised instead, FIFO.
- `LoadPair` → raises `SeamEvent.PairResult` (default ok, diff `shell.ceiling_height_m`, active
  control; scriptable like Apply).
- `SwitchCondition`/`SetCameraMode`/`CaptureScreenshot` → contract events / no-event per SeamCodes.
- Events dispatch synchronously (deterministic tests).
DoD: EditMode tests — calls recorded, default events fire, scripted failures replay in order.

### S1 — OperatorPanelViewModel (the single-room editor, U1's brain)
`unity/Assets/RoomGen/UI/`: `RoomGen.UI.asmdef` (refs RoomGen.Runtime; NOT Testing). In
`UI/Operator/`: `OperatorPanelViewModel` (pure C#, no UnityEngine.UI deps):
- ctor `(ISpecChannel channel, Func<double> now, double debounceSeconds = 0.15)` — clock injected
  so debounce is testable without waiting.
- `LoadPreset(presetJson)` parses `spec/presets/dining_room.preset.json` (copy byte-identical into
  `Resources/RoomGen/` like the schemas; verify sha) → exposes `IReadOnlyList<FieldSpec>` structs
  `{Path, Label, Min, Max, Value, IsManipulable}` from the preset's `ranges` +
  `manipulable_variables`, and holds the full canonical spec JSON internally as the bind model.
- `SetField(path, value)` clamps to range, updates the model, marks dirty.
- `Tick()` — when dirty and `now - lastEdit >= debounce`, sends ONE `channel.Apply(specJson, reqId)`
  (slider streams debounced BEFORE the channel, per the handoff).
- Subscribes to `OnEvent`: `spec_applied` → `Status`, `Errors` (list of plain structs
  {Code, Path, Message} rendered verbatim — never re-worded); `pair_loaded` → `Validation`
  {Ok, ViolationCodes, DiffPaths, ActiveCondition}. `PublishEnabled => Validation.Ok` (DL-6).
- Pair workflow core: `SetAsControl()` snapshots the model; `DeclaredVariable` property;
  `SubmitPair()` → `channel.LoadPair(controlJson, currentJson, reqId)`.
DoD: EditMode tests against MockSpecChannel — debounce (two rapid SetFields → one Apply after the
window), clamping to preset ranges, error surfacing verbatim, PublishEnabled flips with pair_loaded
validation, SetAsControl/SubmitPair sends both JSONs.

### S2 — Operator UXML/USS + binder (the visible shell)
`UI/Shared/base.uss` — the shared base styles E3 will consume (typography scale, spacing vars,
panel/heading/label/status classes; calm dark theme consistent with the IMGUI studio). 
`UI/Operator/OperatorPanel.uxml` — sliders for the preset's shell fields, lighting warmth/intensity,
declared-variable dropdown, diff-panel list container, publish button. 
`OperatorPanelController : MonoBehaviour` — binds UIDocument controls ↔ view-model (register value-
changed callbacks → `SetField`; `Update()` → `Tick()`; event state → labels/classes; publish button
`SetEnabled(vm.PublishEnabled)`).
DoD: EditMode test loads the UXML/USS assets, instantiates the tree headlessly, asserts the
expected controls exist by name and the binder wires a slider change through to a recorded
mock-channel Apply.

### S3 — StudyPublisher (U3's brain, no form yet)
`UI/Operator/StudyPublisher.cs` (pure C#): `(controlJson, treatmentJson, validationState, title,
hypothesis, taskType, taskConfig) → study JSON` valid against `study.schema.json` (validate with
JsonSchemaLite before returning; refuse — don't emit — when `validation.Ok` is false, DL-6), with
embedded spec snapshots + validation stamp + ISO `created_at` injected (no DateTime.Now inside —
caller passes it; determinism + testability).
DoD: tests — published doc passes StudyGate + schema; confounded validation cannot publish;
degenerate task config refused (reuse the semantic checks style from ResponseWriter).

## Explicitly deferred (needs eyes/hands or other owners)
Visual polish + layout iteration (S2 is a skeleton); U2 full diff-panel rendering against all
diff_vectors cases; U4 library/thumbnails; U5 AI copilot; replacing the IMGUI studio (coordinated
PR with E1/Diego — do NOT touch RoomStudioController); wiring the panel to the real LocalChannel
in the studio scene (M1 tracer moment, human-verified).

## Gotchas for the executor (hard-won; do not rediscover)
- Headless Unity: `Start-Process -Wait -PassThru` on Unity.exe; parse `-testResults` XML; never
  trust `$LASTEXITCODE`; one Unity instance at a time.
- The EditMode test asmdef CANNOT reference Newtonsoft: view-model/mock public surfaces are plain
  types only; JSON stays inside Runtime/Testing/UI assemblies. Factories like
  `ResponseWriter.CreateDefault` show the pattern.
- Parse contract JSON with `ResponseJson.Parse` (DateParseHandling.None) — never `JObject.Parse`.
- Copy any spec/* file into Resources byte-identical and sha256-verify the copy.
- asmdef changes require the references to be asmdef NAMES, and the Tests asmdef must add
  RoomGen.Testing + RoomGen.UI to its references for the new tests.
- Suite must stay green: 69 tests before this work; every gate adds tests, breaks none.
