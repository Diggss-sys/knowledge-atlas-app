using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RoomGen.Adapter;
using RoomGen.Contracts;
using RoomGen.Gate;
using UnityEngine;

namespace RoomGen.Runner
{
    /// <summary>
    /// R2-lite: a HUMAN-driven study session — the participant-facing counterpart to
    /// <see cref="StudyRunner"/> (which uses a deterministic fake responder). Same honesty gate, same
    /// seeded trial order, same validated rows; the only difference R2 adds is a real participant id
    /// and one real rating per trial, supplied one at a time as the person walks each room and rates it.
    ///
    /// Deliberately pure logic — no UIToolkit, no cameras, no rendering — so an EditMode test can drive
    /// a whole session (the scripted stand-in for the human) and assert the CSV validates against the
    /// contract schema. The MonoBehaviour <c>ParticipantFlow</c> is the neutral UI wrapped around this.
    /// </summary>
    public sealed class ParticipantSession
    {
        public bool CanRun { get; }
        public string Reason { get; } = "";

        public string Prompt { get; } = "";
        public int ScaleMin { get; } = 1;
        public int ScaleMax { get; } = 7;

        public int TrialCount => _plans?.Count ?? 0;
        public int Index { get; private set; }
        public bool IsComplete => _plans == null || Index >= _plans.Count;
        public bool IsAborted { get; private set; }
        public string AbortReason { get; private set; } = "";
        public int Written => _writer?.WrittenCount ?? 0;
        public bool AllValid { get; private set; } = true;
        public string CsvPath { get; }
        public string JsonlPath { get; }
        public string IncompleteCsvPath { get; }
        public string IncompleteJsonlPath { get; }
        public IReadOnlyList<string> AdaptationWarnings => _adaptationWarnings;

        // Identity of this session — exposed so the perf sidecar log can key its rows to the same
        // session/participant/study as the response CSV (see PerfLog).
        public string SessionId => _sessionId;
        public string ParticipantId => _participantId;
        public string StudyId => _studyId;

        /// <summary>The condition being presented (control/treatment) — used to pick the room to build.
        /// NEVER shown to the participant: a visible condition label would be a demand cue.</summary>
        public string CurrentCondition => IsComplete ? null : _plans[Index].Condition;
        public string CurrentSpecSha256 => IsComplete ? null :
            (_plans[Index].Condition == "treatment" ? _treatmentSpecSha256 : _controlSpecSha256);

        /// <summary>The internal RoomSpec for the current trial's room (for the walk). Null when complete.</summary>
        public RoomSpec CurrentSpec => IsComplete ? null : (_plans[Index].Condition == "treatment" ? _treatment : _control);

        readonly List<TrialPlan> _plans;
        readonly ResponseWriter _writer;
        readonly Func<string> _nowUtc;
        readonly string _studyId, _pairId, _modality, _sessionId, _participantId;
        readonly int _seed;
        readonly List<string> _manipulated = new List<string>();
        readonly List<string> _adaptationWarnings = new List<string>();
        readonly RoomSpec _control, _treatment;
        readonly string _controlSpecSha256, _treatmentSpecSha256;

        /// <param name="sessionId">Pass a fixed id in tests; empty ⇒ a fresh GUID (one session = one id).</param>
        /// <param name="nowUtc">Row timestamp source; null ⇒ real UtcNow. Injectable for deterministic tests.</param>
        public ParticipantSession(string studyJson, string participantId, int seed, string sessionId,
            string csvPath, string jsonlPath, Func<string> nowUtc = null)
        {
            CsvPath = csvPath;
            JsonlPath = jsonlPath;
            IncompleteCsvPath = IncompletePath(csvPath);
            IncompleteJsonlPath = IncompletePath(jsonlPath);
            _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture));
            _participantId = Sanitize(participantId);
            _sessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString() : sessionId;
            _seed = seed;

            // Same honesty gate the config runner uses: an unpublished / unvalidated study cannot run.
            if (!StudyGate.CanRun(studyJson, out var reason)) { Reason = reason; return; }

            var study = ResponseJson.Parse(studyJson);
            _studyId = (string)study["study_id"];
            _pairId = (string)study["pair_id"];
            _modality = (string)study["modality"] ?? "desktop_3d";

            var task = study["task"];
            if ((string)task?["type"] != "rating") { Reason = "the participant flow supports rating tasks only"; return; }
            var cfg = task["config"];
            Prompt = (string)cfg?["prompt"] ?? "How does this room feel?";
            ScaleMin = (int?)cfg?["scale_min"] ?? 1;
            ScaleMax = (int?)cfg?["scale_max"] ?? 7;
            if (ScaleMax < ScaleMin) { Reason = $"degenerate rating scale [{ScaleMin},{ScaleMax}]"; return; }
            var trials = (int?)cfg?["trials"] ?? 2;
            var strategy = (string)study["counterbalance"]?["order_strategy"] ?? "seeded_shuffle";

            if (study["validation"]?["diff"] is JObject diff)
                foreach (var prop in diff.Properties()) _manipulated.Add(prop.Name);

            try
            {
                _control = DeserializeSpec(study["control_spec"], "control", _adaptationWarnings);
                _treatment = DeserializeSpec(study["treatment_spec"], "treatment", _adaptationWarnings);
                _controlSpecSha256 = CanonicalJson.Sha256(
                    study["control_spec"].ToString(Newtonsoft.Json.Formatting.None));
                _treatmentSpecSha256 = CanonicalJson.Sha256(
                    study["treatment_spec"].ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception e)
            {
                Reason = "study room specification refused: " + e.Message;
                return;
            }

            if (_adaptationWarnings.Count > 0)
            {
                Reason = "study room adaptation is lossy: " + string.Join(" | ", _adaptationWarnings);
                return;
            }

            _plans = TrialSequencer.BuildRating(trials, strategy, seed);

            if (File.Exists(csvPath) || File.Exists(jsonlPath) ||
                File.Exists(IncompleteCsvPath) || File.Exists(IncompleteJsonlPath))
            {
                Reason = "session output path already exists; refusing to overwrite prior participant data";
                return;
            }
            // Rows remain visibly incomplete until the whole session succeeds. Only completed sessions
            // are promoted to the response-* corpus that researchers pool for analysis.
            _writer = new ResponseWriter(IncompleteCsvPath, IncompleteJsonlPath, ResponseWriter.LoadSchema());
            CanRun = true;
        }

        /// <summary>
        /// Record the human's rating for the current trial, write the validated row, and advance. The
        /// value is clamped into the study's scale (the UI only offers in-range values anyway). Returns
        /// the writer's validation errors (empty = written). No-op once complete or if the study refused.
        /// </summary>
        public IReadOnlyList<string> SubmitRating(int value, double? rtMs = null)
        {
            if (!CanRun || IsAborted || IsComplete) return Array.Empty<string>();
            var plan = _plans[Index];
            var row = new ResponseRow
            {
                StudyId = _studyId,
                PairId = _pairId,
                ParticipantId = _participantId,
                SessionId = _sessionId,
                TrialIndex = plan.TrialIndex,
                TaskType = "rating",
                Condition = plan.Condition,
                ManipulatedVariables = new List<string>(_manipulated),
                Modality = _modality,
                Response = ResponseRow.RatingResponse(
                    ScaleMin, ScaleMax, ResponseRow.ClampRating(ScaleMin, ScaleMax, value), Prompt),
                RtMs = rtMs,
                TimestampUtc = _nowUtc(),
                PresentationOrderSeed = _seed,
                SpecSha256 = plan.Condition == "treatment" ? _treatmentSpecSha256 : _controlSpecSha256,
            };
            var errors = new List<string>(_writer.Write(row));
            if (errors.Count > 0)
            {
                AllValid = false;
                IsAborted = true;
                AbortReason = "response row refused: " + string.Join(" | ", errors);
                return errors;
            }
            Index++;

            if (IsComplete)
            {
                try
                {
                    PromoteCompletedOutputs();
                }
                catch (Exception e)
                {
                    AllValid = false;
                    IsAborted = true;
                    AbortReason = "completed response files could not be finalized: " + e.Message;
                    errors.Add(AbortReason);
                }
            }
            return errors;
        }

        /// <summary>Fail the session closed after a room-build integrity failure.</summary>
        public void Abort(string reason)
        {
            if (!CanRun || IsComplete || IsAborted) return;
            IsAborted = true;
            AbortReason = string.IsNullOrWhiteSpace(reason) ? "session aborted" : reason;
            AllValid = false;
        }

        void PromoteCompletedOutputs()
        {
            if (!File.Exists(IncompleteCsvPath) || !File.Exists(IncompleteJsonlPath))
                throw new IOException("one or more incomplete response files are missing");
            if (File.Exists(CsvPath) || File.Exists(JsonlPath))
                throw new IOException("a completed response path appeared during the session; refusing to overwrite it");

            File.Move(IncompleteCsvPath, CsvPath);
            try
            {
                File.Move(IncompleteJsonlPath, JsonlPath);
            }
            catch
            {
                // Best-effort rollback keeps a failed promotion out of the completed response corpus.
                if (File.Exists(CsvPath) && !File.Exists(IncompleteCsvPath))
                    File.Move(CsvPath, IncompleteCsvPath);
                throw;
            }
        }

        static string IncompletePath(string finalPath)
        {
            var directory = Path.GetDirectoryName(finalPath);
            var filename = "incomplete-" + Path.GetFileName(finalPath);
            return string.IsNullOrEmpty(directory) ? filename : Path.Combine(directory, filename);
        }

        static RoomSpec DeserializeSpec(JToken token, string label, List<string> warnings)
        {
            if (!(token is JObject canonical) || !(canonical["shell"] is JObject))
                throw new InvalidDataException(
                    $"{label}_spec must be canonical (room_type/shell/surfaces/lighting); internal geometry/room_id form is not accepted");

            var schemaAsset = Resources.Load<TextAsset>("RoomGen/room_spec.schema");
            if (schemaAsset == null)
                throw new InvalidDataException("room_spec schema resource is unavailable");

            var schema = ResponseJson.Parse(schemaAsset.text);
            var schemaErrors = JsonSchemaLite.Validate(canonical, schema);
            if (schemaErrors.Count > 0)
                throw new InvalidDataException($"{label}_spec is not schema-valid: " +
                    string.Join(" | ", schemaErrors.Select(e => $"{e.Path}: {e.Message}")));

            var adapted = RoomSpecAdapter.Adapt(canonical.ToString(Newtonsoft.Json.Formatting.None));
            foreach (var warning in adapted.Warnings)
                warnings.Add($"{label}_spec: {warning}");
            return adapted.Spec;
        }

        // participant ids reach a CSV cell: keep them to a safe token so a stray comma/newline can't
        // corrupt a row. Empty entry becomes a stable placeholder rather than an invalid empty id.
        static string Sanitize(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "anonymous";
            var sb = new StringBuilder();
            foreach (var c in id.Trim())
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "anonymous";
        }
    }
}
