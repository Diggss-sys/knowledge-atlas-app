using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KnowledgeAtlas.Seam
{
    /// <summary>
    /// The runtime seam (seam_version 1) per spec/contracts/ENGINE_SEAM.md. Three consumers — the
    /// operator UI, the experiment runtime, and (later) a networked VR operator — drive one renderer
    /// through RoomSpec JSON + the messages here, never touching generator internals. LocalChannel is
    /// the v1 in-process implementation; a NetworkChannel would send the identical envelopes.
    ///
    /// Note vs the contract's sketch: our internal RoomSpec (RoomGen.Contracts) is NOT the canonical
    /// KA schema, so the seam boundary is JSON (as the wire format intends) and IRoomRuntime.Apply
    /// takes canonical spec JSON — the runtime schema-validates, adapts, and builds.
    /// </summary>
    public static class SeamCodes
    {
        public const int Version = 1;

        // Frozen error vocabulary (ENGINE_SEAM.md).
        public const string SchemaInvalid = "schema_invalid";
        public const string UnknownCatalogId = "unknown_catalog_id";
        public const string FurnitureOutOfBounds = "furniture_out_of_bounds";
        public const string FurnitureOverlap = "furniture_overlap";
        public const string FurnitureBlocksDoor = "furniture_blocks_door";
        public const string PresetMissing = "preset_missing";
        public const string UnsupportedSeamVersion = "unsupported_seam_version";
        public const string BadRequest = "bad_request";

        // Call kinds (operator/runner -> runtime).
        public const string ApplySpec = "apply_spec";
        public const string LoadPair = "load_pair";
        public const string SwitchCondition = "switch_condition";
        public const string SetCameraMode = "set_camera_mode";
        public const string CaptureScreenshot = "capture_screenshot";

        // Event kinds (runtime -> operator/runner).
        public const string RuntimeReady = "runtime_ready";
        public const string SpecApplied = "spec_applied";
        public const string PairLoaded = "pair_loaded";
        public const string ConditionSwitched = "condition_switched";
        public const string ScreenshotCaptured = "screenshot_captured";
        public const string RuntimeError = "runtime_error";

        public static readonly string[] CallKinds =
            { ApplySpec, LoadPair, SwitchCondition, SetCameraMode, CaptureScreenshot };
        public static readonly string[] EventKinds =
            { RuntimeReady, SpecApplied, PairLoaded, ConditionSwitched, ScreenshotCaptured, RuntimeError };

        public static readonly string[] Transitions = { "cut", "fade", "teleport" };
        public static readonly string[] CameraModes = { "walk", "orbit", "fixed_eye" };
        public static readonly string[] Conditions = { "control", "treatment" };

        public static bool IsRejection(string code) =>
            code == UnsupportedSeamVersion || code == BadRequest;
    }

    public sealed class SeamError
    {
        public string Code;
        public string Path;
        public string Message;
        public SeamError(string code, string path, string message) { Code = code; Path = path; Message = message; }
    }

    /// <summary>
    /// One seam event. Fat but plain (no Newtonsoft in the public surface) so any assembly can read
    /// it; ToJson() emits the wire/log envelope. Only the fields relevant to a given Kind are set.
    /// </summary>
    public sealed class SeamEvent
    {
        public string Kind;
        public string RequestId;

        // spec_applied / pair_loaded
        public bool Ok;
        public readonly List<SeamError> Errors = new List<SeamError>();
        public long BuildMs;
        public string SpecSha256;
        public bool Superseded;

        // pair_loaded
        public bool PairOk;
        public readonly List<string> PairDiffPaths = new List<string>();
        public readonly List<string> PairViolationCodes = new List<string>();
        public readonly List<SeamError> PairViolations = new List<SeamError>();
        public readonly List<string> PairNotes = new List<string>();
        public string ActiveCondition;

        // condition_switched
        public string Condition;
        public string Transition;
        public long Ms;

        // runtime_error
        public string Code;
        public string Message;

        // runtime_ready
        public string BuilderVersion;
        public string Engine;
        public string Pipeline;

        public List<string> ErrorCodes => Errors.Select(e => e.Code).ToList();

        // ---- factories ----
        public static SeamEvent SpecAppliedOk(string requestId, long buildMs, string sha, bool superseded = false) =>
            new SeamEvent { Kind = SeamCodes.SpecApplied, RequestId = requestId, Ok = true, BuildMs = buildMs, SpecSha256 = sha, Superseded = superseded };

        public static SeamEvent SpecAppliedFail(string requestId, IEnumerable<SeamError> errors, long buildMs)
        {
            var e = new SeamEvent { Kind = SeamCodes.SpecApplied, RequestId = requestId, Ok = false, BuildMs = buildMs };
            e.Errors.AddRange(errors);
            return e;
        }

        public static SeamEvent Error(string requestId, string code, string message) =>
            new SeamEvent { Kind = SeamCodes.RuntimeError, RequestId = requestId, Code = code, Message = message };

        public static SeamEvent Ready(string requestId) =>
            new SeamEvent { Kind = SeamCodes.RuntimeReady, RequestId = requestId, BuilderVersion = "1.0.0", Engine = "unity-6000.3", Pipeline = "hdrp" };

        public static SeamEvent ConditionDone(string requestId, string condition, string transition, long ms) =>
            new SeamEvent { Kind = SeamCodes.ConditionSwitched, RequestId = requestId, Condition = condition, Transition = transition, Ms = ms };

        public static SeamEvent Screenshot(string requestId) =>
            new SeamEvent { Kind = SeamCodes.ScreenshotCaptured, RequestId = requestId };

        public static SeamEvent PairResult(string requestId, bool ok, IEnumerable<string> codes, IEnumerable<string> diffPaths, string activeCondition)
        {
            return PairResult(requestId, ok, codes, diffPaths, activeCondition,
                Enumerable.Empty<SeamError>(), Enumerable.Empty<string>());
        }

        public static SeamEvent PairResult(string requestId, bool ok, IEnumerable<string> codes,
            IEnumerable<string> diffPaths, string activeCondition, IEnumerable<SeamError> violations,
            IEnumerable<string> notes)
        {
            var e = new SeamEvent { Kind = SeamCodes.PairLoaded, RequestId = requestId, Ok = ok, PairOk = ok, ActiveCondition = activeCondition };
            e.PairViolationCodes.AddRange(codes);
            e.PairDiffPaths.AddRange(diffPaths);
            e.PairViolations.AddRange(violations);
            e.PairNotes.AddRange(notes);
            return e;
        }

        /// <summary>The envelope this event serializes to (provenance log + NetworkChannel payload).</summary>
        public string ToJson()
        {
            var payload = new JObject();
            switch (Kind)
            {
                case SeamCodes.SpecApplied:
                    payload["ok"] = Ok;
                    payload["errors"] = new JArray(Errors.Select(ErrToJson));
                    payload["build_ms"] = BuildMs;
                    if (Ok) payload["spec_sha256"] = SpecSha256;
                    if (Superseded) payload["superseded"] = true;
                    break;
                case SeamCodes.PairLoaded:
                    payload["ok"] = Ok;
                    payload["validation"] = new JObject
                    {
                        ["ok"] = PairOk,
                        ["violations"] = new JArray(PairViolations.Select(ErrToJson)),
                        ["violation_codes"] = new JArray(PairViolationCodes),
                        ["diff_paths"] = new JArray(PairDiffPaths),
                        ["notes"] = new JArray(PairNotes),
                    };
                    payload["active_condition"] = ActiveCondition;
                    break;
                case SeamCodes.ConditionSwitched:
                    payload["condition"] = Condition;
                    payload["transition"] = Transition;
                    payload["ms"] = Ms;
                    break;
                case SeamCodes.RuntimeError:
                    payload["code"] = Code;
                    payload["message"] = Message;
                    break;
                case SeamCodes.RuntimeReady:
                    payload["builder_version"] = BuilderVersion;
                    payload["engine"] = Engine;
                    payload["pipeline"] = Pipeline;
                    break;
                case SeamCodes.ScreenshotCaptured:
                    payload["request_id"] = RequestId;
                    break;
            }
            var envelope = new JObject
            {
                ["seam_version"] = SeamCodes.Version,
                ["kind"] = Kind,
                ["request_id"] = RequestId,
                ["payload"] = payload,
            };
            return envelope.ToString(Newtonsoft.Json.Formatting.None);
        }

        static JObject ErrToJson(SeamError e)
        {
            var o = new JObject { ["code"] = e.Code };
            if (!string.IsNullOrEmpty(e.Path)) o["path"] = e.Path;
            o["message"] = e.Message;
            return o;
        }
    }

    /// <summary>Fire-and-forget submission surface; results arrive on OnEvent (contract §interfaces).</summary>
    public interface ISpecChannel
    {
        void Apply(string specJson, string requestId);
        void LoadPair(string controlJson, string treatmentJson, string requestId);
        void SwitchCondition(string condition, string transition, string requestId);
        void SetCameraMode(string mode, string requestId);
        void CaptureScreenshot(string requestId);
        event Action<SeamEvent> OnEvent;
    }

    /// <summary>The generator-side synchronous core the channel wraps. Returns the event to dispatch
    /// (null = accepted, no event — e.g. a camera-mode change has no contract event).</summary>
    public interface IRoomRuntime
    {
        SeamEvent Apply(string canonicalSpecJson, string requestId);
        SeamEvent LoadPair(string controlJson, string treatmentJson, string requestId);
        SeamEvent SwitchCondition(string condition, string transition, string requestId);
        SeamEvent SetCameraMode(string mode, string requestId);
        SeamEvent CaptureScreenshot(string requestId, int width, int height);
        SeamEvent Ready(string requestId);
    }
}
