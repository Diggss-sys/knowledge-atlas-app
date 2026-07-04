using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using RoomGen.Adapter;
using RoomGen.Contracts;
using RoomGen.Gate;
using RoomGen.Generation;
using RoomGen.Validation;

namespace KnowledgeAtlas.Seam
{
    /// <summary>
    /// IRoomRuntime over the generator. Receives CANONICAL RoomSpec JSON and, for every apply:
    /// schema-validate (G3 JsonSchemaLite, against the real room_spec.schema) → adapt (RoomSpecAdapter)
    /// → structural-validate the internal spec (RoomSpecValidator, WITHOUT the preset — mirrors
    /// RoomGenerator.Build, so "the seam accepted it" guarantees "Build will not reject it") → build.
    ///
    /// ATOMIC APPLY (ENGINE_SEAM guarantee #1): the generator is only touched when validation passes;
    /// a rejected spec leaves the previous room intact and visible. Pairs are gated by the G3 PairGate
    /// before building. spec_sha256 = SHA-256 of the adapted internal spec's canonical serialization.
    /// </summary>
    public sealed class RoomRuntime : IRoomRuntime
    {
        readonly RoomGenerator generator;
        readonly string schemaJson;
        readonly JObject schema;

        RoomSpec activeControl;
        RoomSpec activeTreatment;

        public string ActiveCondition { get; private set; }
        public string LastGoodSha { get; private set; }

        public RoomRuntime(RoomGenerator generator, string schemaJson)
        {
            this.generator = generator;
            this.schemaJson = schemaJson;
            this.schema = JObject.Parse(schemaJson);
        }

        public SeamEvent Ready(string requestId) => SeamEvent.Ready(requestId);

        public SeamEvent Apply(string canonicalSpecJson, string requestId)
        {
            var timer = Stopwatch.StartNew();

            JObject specObj;
            try { specObj = JObject.Parse(canonicalSpecJson); }
            catch (Exception e)
            {
                return SeamEvent.SpecAppliedFail(requestId,
                    new[] { new SeamError(SeamCodes.SchemaInvalid, null, "spec is not valid JSON: " + e.Message) },
                    timer.ElapsedMilliseconds);
            }

            // 1. Canonical schema conformance (authoritative schema_invalid, e.g. ceiling out of range).
            var schemaErrors = JsonSchemaLite.Validate(specObj, schema)
                .Select(e => new SeamError(SeamCodes.SchemaInvalid, e.Path, e.Message)).ToList();
            if (schemaErrors.Count > 0)
                return SeamEvent.SpecAppliedFail(requestId, schemaErrors, timer.ElapsedMilliseconds); // room untouched

            // 2. Adapt to the internal spec.
            RoomSpecAdapter.AdaptResult adapted;
            try { adapted = RoomSpecAdapter.Adapt(canonicalSpecJson); }
            catch (Exception e)
            {
                return SeamEvent.SpecAppliedFail(requestId,
                    new[] { new SeamError(SeamCodes.SchemaInvalid, null, "adapter rejected the spec: " + e.Message) },
                    timer.ElapsedMilliseconds);
            }

            // 3. Structural + placement validity (no preset — same gate Build applies).
            var errors = RoomSpecValidator.Validate(adapted.Spec)
                .Where(i => i.Severity == "error").Select(MapIssue).ToList();
            if (errors.Count > 0)
                return SeamEvent.SpecAppliedFail(requestId, errors, timer.ElapsedMilliseconds); // room untouched

            // 4. Commit — only now is the previous room replaced.
            generator.Build(adapted.Spec);
            timer.Stop();
            LastGoodSha = Sha256(RoomJson.Serialize(adapted.Spec));
            activeControl = adapted.Spec;
            ActiveCondition = null;
            return SeamEvent.SpecAppliedOk(requestId, timer.ElapsedMilliseconds, LastGoodSha);
        }

        public SeamEvent LoadPair(string controlJson, string treatmentJson, string requestId)
        {
            // Gate the pair on the canonical specs (G3) BEFORE building — a confounded pair is refused.
            var gate = PairGate.ValidateJson(controlJson, treatmentJson, schemaJson);
            if (gate.Ok)
            {
                var pair = RoomSpecAdapter.AdaptPair(controlJson, treatmentJson);
                activeControl = pair.Control;
                activeTreatment = pair.Treatment;
                generator.Build(pair.Control); // control is shown first
                ActiveCondition = "control";
                LastGoodSha = Sha256(RoomJson.Serialize(pair.Control));
            }
            return SeamEvent.PairResult(requestId, gate.Ok, gate.ViolationCodes, gate.DiffPaths, ActiveCondition ?? "control");
        }

        // Nominal transition durations (ms). cut/teleport swap instantly; fade is a timed crossfade —
        // the runtime reports the intended duration; the studio renders the actual black-overlay fade.
        public const long FadeMs = 220;

        // Geometry-safe condition switch on a loaded pair. The runtime rebuilds the target condition
        // and reports the transition; the visual crossfade for 'fade' is applied by the studio.
        public SeamEvent SwitchCondition(string condition, string transition, string requestId)
        {
            var spec = condition == "treatment" ? activeTreatment : activeControl;
            if (spec == null)
                return SeamEvent.Error(requestId, SeamCodes.BadRequest, "no pair loaded; switch_condition requires a prior load_pair");
            generator.Build(spec);
            ActiveCondition = condition;
            var ms = transition == "fade" ? FadeMs : 0L;
            return SeamEvent.ConditionDone(requestId, condition, transition, ms);
        }

        // Camera mode is a viewport concern with no contract event; the studio wires it to its
        // walk/orbit cameras. Accepted with no event.
        public SeamEvent SetCameraMode(string mode, string requestId) => null;

        public SeamEvent CaptureScreenshot(string requestId, int width, int height) =>
            SeamEvent.Screenshot(requestId);

        static SeamError MapIssue(ValidationIssue issue)
        {
            string code;
            switch (issue.Code)
            {
                case "CATALOG_ID":
                case "ASSET_MISSING":
                    code = SeamCodes.UnknownCatalogId; break;
                case "FURNITURE_WALL_COLLISION":
                case "FURNITURE_CURVE_COLLISION":
                case "OPENING_BOUNDS":
                    code = SeamCodes.FurnitureOutOfBounds; break;
                case "FURNITURE_COLLISION":
                    code = SeamCodes.FurnitureOverlap; break;
                case "FURNITURE_OPENING_COLLISION":
                    code = SeamCodes.FurnitureBlocksDoor; break;
                case "REQUIRED_SLOT":
                    code = SeamCodes.PresetMissing; break;
                default:
                    code = SeamCodes.SchemaInvalid; break;
            }
            return new SeamError(code, issue.Path, issue.Message);
        }

        static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
