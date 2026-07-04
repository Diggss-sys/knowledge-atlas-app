using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KnowledgeAtlas.Seam
{
    /// <summary>
    /// Parses a wire envelope { seam_version, kind, request_id, payload } and routes it to the
    /// runtime. Envelope-level failures produce runtime_error with the frozen codes the
    /// spec/fixtures/seam_messages.json "invalid" set names (unsupported_seam_version / bad_request),
    /// checked BEFORE any runtime call. Known event kinds (things the runtime emits) are accepted as
    /// well-formed passthroughs. This is the single point both LocalChannel and a future
    /// NetworkChannel share.
    /// </summary>
    public static class SeamEnvelope
    {
        /// <summary>Route one envelope. Returns the event to dispatch, or null when a valid call
        /// produced no event (e.g. set_camera_mode).</summary>
        public static SeamEvent Route(string envelopeJson, IRoomRuntime runtime)
        {
            JObject env;
            try { env = JObject.Parse(envelopeJson); }
            catch (Exception e) { return SeamEvent.Error(null, SeamCodes.BadRequest, "malformed envelope: " + e.Message); }

            // 1. Version (checked first: wrong_seam_version carries a valid request_id).
            var versionToken = env["seam_version"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                return SeamEvent.Error((string)env["request_id"], SeamCodes.BadRequest, "seam_version missing");
            if ((int)versionToken != SeamCodes.Version)
                return SeamEvent.Error((string)env["request_id"], SeamCodes.UnsupportedSeamVersion,
                    $"seam_version {versionToken} is not supported (this runtime speaks {SeamCodes.Version})");

            // 2. request_id present and non-empty.
            var requestId = (string)env["request_id"];
            if (string.IsNullOrEmpty(requestId))
                return SeamEvent.Error(null, SeamCodes.BadRequest, "request_id is required");

            // 3. kind known.
            var kind = (string)env["kind"];
            if (string.IsNullOrEmpty(kind))
                return SeamEvent.Error(requestId, SeamCodes.BadRequest, "kind is required");
            if (SeamCodes.EventKinds.Contains(kind))
                // An event message (runtime output) is a well-formed seam message; accept as parsed.
                return new SeamEvent { Kind = kind, RequestId = requestId };
            if (!SeamCodes.CallKinds.Contains(kind))
                return SeamEvent.Error(requestId, SeamCodes.BadRequest, $"unknown kind '{kind}'");

            var payload = env["payload"] as JObject ?? new JObject();

            // 4. Per-kind payload validation, then route.
            switch (kind)
            {
                case SeamCodes.ApplySpec:
                    if (!(payload["spec"] is JObject specObj))
                        return SeamEvent.Error(requestId, SeamCodes.BadRequest, "apply_spec.payload.spec is required");
                    return runtime.Apply(specObj.ToString(Newtonsoft.Json.Formatting.None), requestId);

                case SeamCodes.LoadPair:
                    if (!(payload["control"] is JObject c) || !(payload["treatment"] is JObject t))
                        return SeamEvent.Error(requestId, SeamCodes.BadRequest, "load_pair requires payload.control and payload.treatment");
                    return runtime.LoadPair(c.ToString(Newtonsoft.Json.Formatting.None),
                                            t.ToString(Newtonsoft.Json.Formatting.None), requestId);

                case SeamCodes.SwitchCondition:
                    var condition = (string)payload["condition"];
                    var transition = (string)payload["transition"];
                    if (!SeamCodes.Conditions.Contains(condition))
                        return SeamEvent.Error(requestId, SeamCodes.BadRequest, $"invalid condition '{condition}'");
                    if (!SeamCodes.Transitions.Contains(transition))
                        return SeamEvent.Error(requestId, SeamCodes.BadRequest, $"invalid transition '{transition}'");
                    return runtime.SwitchCondition(condition, transition, requestId);

                case SeamCodes.SetCameraMode:
                    var mode = (string)payload["mode"];
                    if (!SeamCodes.CameraModes.Contains(mode))
                        return SeamEvent.Error(requestId, SeamCodes.BadRequest, $"invalid camera mode '{mode}'");
                    return runtime.SetCameraMode(mode, requestId);

                case SeamCodes.CaptureScreenshot:
                    var width = payload["width"]?.Value<int>() ?? 1920;
                    var height = payload["height"]?.Value<int>() ?? 1080;
                    return runtime.CaptureScreenshot(requestId, width, height);

                default:
                    return SeamEvent.Error(requestId, SeamCodes.BadRequest, $"unroutable kind '{kind}'");
            }
        }
    }
}
