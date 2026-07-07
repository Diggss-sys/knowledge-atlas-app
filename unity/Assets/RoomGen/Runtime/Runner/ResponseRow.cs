using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoomGen.Runner
{
    /// <summary>
    /// One participant response = one row (spec/response_log.schema.json + spec/RESPONSE_LOG.md).
    /// A plain builder-friendly model; ToJson() produces the schema-validated object and the CSV
    /// writer serialises it in canonical column order. Optional fields (rt_ms, spec_sha256) are
    /// omitted from the JSON when unset — never emitted as null (honesty rule).
    /// </summary>
    public sealed class ResponseRow
    {
        public string SchemaVersion = RunnerInfo.SchemaVersion;
        public string StudyId;
        public string PairId;
        public string ParticipantId;
        public string SessionId;
        public int TrialIndex;
        public string TaskType;   // rating | choice
        public string Condition;  // control | treatment | both
        public List<string> ManipulatedVariables = new List<string>();
        public string Modality = "desktop_3d";
        public string ExecutionPath = "live";
        public JObject Response;   // task-specific payload
        public double? RtMs;       // optional
        public string TimestampUtc;
        public int PresentationOrderSeed;
        public string RunnerVersion = RunnerInfo.Version;
        public string SpecSha256;  // optional

        /// <summary>
        /// Clamps a rating into [min,max] — for INPUT layers (UI widgets, fake responders) that
        /// generate values. The writer does NOT clamp: an out-of-range value reaching the sink is
        /// refused, never silently altered (see ResponseWriter — mutating a response at the data
        /// layer would be fabrication and can mask task-screen bugs).
        /// </summary>
        public static int ClampRating(int scaleMin, int scaleMax, int value)
        {
            var clamped = Mathf.Clamp(value, scaleMin, scaleMax);
            if (clamped != value)
                Debug.LogWarning($"ResponseRow: rating {value} out of [{scaleMin},{scaleMax}] — clamped to {clamped}.");
            return clamped;
        }

        /// <summary>Rating payload, written verbatim — range enforcement happens at the writer.</summary>
        public static JObject RatingResponse(int scaleMin, int scaleMax, int value, string prompt = null)
        {
            var o = new JObject
            {
                ["scale_min"] = scaleMin,
                ["scale_max"] = scaleMax,
                ["value"] = value
            };
            if (!string.IsNullOrEmpty(prompt)) o["prompt"] = prompt;
            return o;
        }

        /// <summary>A/B choice payload; side conditions are counterbalanced by the session seed.</summary>
        public static JObject ChoiceResponse(string chosen, string leftCondition, string rightCondition, string prompt = null)
        {
            var o = new JObject
            {
                ["chosen"] = chosen,
                ["left_condition"] = leftCondition,
                ["right_condition"] = rightCondition
            };
            if (!string.IsNullOrEmpty(prompt)) o["prompt"] = prompt;
            return o;
        }

        public JObject ToJson()
        {
            var o = new JObject
            {
                ["schema_version"] = SchemaVersion,
                ["study_id"] = StudyId,
                ["pair_id"] = PairId,
                ["participant_id"] = ParticipantId,
                ["session_id"] = SessionId,
                ["trial_index"] = TrialIndex,
                ["task_type"] = TaskType,
                ["condition"] = Condition,
                ["manipulated_variables"] = new JArray(ManipulatedVariables ?? new List<string>()),
                ["modality"] = Modality,
                ["execution_path"] = ExecutionPath,
                ["response"] = Response,
                ["timestamp_utc"] = TimestampUtc,
                ["presentation_order_seed"] = PresentationOrderSeed,
                ["runner_version"] = RunnerVersion
            };
            if (RtMs.HasValue) o["rt_ms"] = RtMs.Value;
            if (!string.IsNullOrEmpty(SpecSha256)) o["spec_sha256"] = SpecSha256;
            return o;
        }
    }
}
