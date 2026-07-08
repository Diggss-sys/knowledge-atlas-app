using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RoomGen.Gate;
using RoomGen.Runner;
using UnityEngine;

namespace RoomGen.UI
{
    /// <summary>
    /// U3's brain (no form yet): turns a validated control/treatment pair + a task into a study JSON
    /// document that is schema-valid against study.schema.json, self-contained (full spec snapshots
    /// embedded), and stamped with the single-variable validation result. Publishing is REFUSED — the
    /// document is never emitted — when the pair validation is not ok (locked decision DL-6, "a
    /// confounded pair can be edited freely but never saved as a study"), and when the task config is
    /// degenerate (a rating scale with min >= max, an empty prompt, choice trials &lt; 1).
    ///
    /// All inputs/outputs are plain types so the EditMode test drives it without naming Newtonsoft;
    /// created_at is injected by the caller (no DateTime.Now inside — determinism + testability), the
    /// same rule the response-log builder follows. The produced document is finally self-checked with
    /// StudyGate.CanRun, so what Publish returns is exactly what the runner would accept.
    /// </summary>
    public sealed class StudyPublisher
    {
        // ---- plain input value objects ----

        public struct ValidationStamp
        {
            public bool Ok;
            public IReadOnlyList<string> DiffPaths;
            public IReadOnlyList<string> Notes;
            public string Validator;      // e.g. "PairGate.cs@1.0"
            public string ValidatedAtIso; // ISO date-time, caller-supplied
        }

        public struct TaskConfig
        {
            public string Type;   // "rating" | "choice"
            public string Prompt;
            public int? ScaleMin; // rating
            public int? ScaleMax; // rating
            public int? Trials;   // choice
        }

        public struct StudyInput
        {
            public string StudyId;
            public string PairId;
            public string Title;
            public string Hypothesis; // optional
            public string Modality;   // "desktop_3d" | "pcvr" | "standalone_vr"
            public string ControlSpecJson;
            public string TreatmentSpecJson;
            public ValidationStamp Validation;
            public TaskConfig Task;
            public string CreatedAtIso; // injected
        }

        public struct Result
        {
            public bool Ok;
            public string StudyJson;                 // null when refused
            public IReadOnlyList<string> Errors;     // why it was refused (empty on success)
        }

        readonly JObject _schema;

        public StudyPublisher(JObject schema) { _schema = schema; }

        public static JObject LoadSchema()
        {
            var text = Resources.Load<TextAsset>("RoomGen/study.schema");
            return text != null ? ResponseJson.Parse(text.text) : null;
        }

        public static StudyPublisher CreateDefault() => new StudyPublisher(LoadSchema());

        /// <summary>Build + validate the study. Never emits a document when refused.</summary>
        public Result Publish(StudyInput input)
        {
            var errors = new List<string>();

            // DL-6: a confounded (or unvalidated) pair can be edited but never saved as a study.
            if (!input.Validation.Ok)
                errors.Add("validation.ok is false — a confounded/unvalidated pair cannot be published (DL-6)");

            // Semantic task checks the schema cannot express (refuse, never repair).
            errors.AddRange(TaskSemanticErrors(input.Task));

            if (errors.Count > 0)
                return new Result { Ok = false, StudyJson = null, Errors = errors };

            var doc = BuildDocument(input);

            // Schema gate (JsonSchemaLite, the same subset the pair gate uses).
            foreach (var e in JsonSchemaLite.Validate(doc, _schema))
                errors.Add($"{e.Path}: {e.Message}");

            // Final self-check: exactly what the runner's honesty gate would accept.
            var json = doc.ToString(Newtonsoft.Json.Formatting.None);
            if (!StudyGate.CanRun(json, out var reason))
                errors.Add($"StudyGate refused the produced document: {reason}");

            if (errors.Count > 0)
            {
                Debug.LogWarning($"StudyPublisher refused a document: {string.Join(" | ", errors)}");
                return new Result { Ok = false, StudyJson = null, Errors = errors };
            }

            return new Result { Ok = true, StudyJson = json, Errors = errors };
        }

        JObject BuildDocument(StudyInput input)
        {
            var validation = new JObject
            {
                ["ok"] = input.Validation.Ok,
                ["validated_at"] = input.Validation.ValidatedAtIso,
                ["validator"] = input.Validation.Validator,
            };
            if (input.Validation.DiffPaths != null && input.Validation.DiffPaths.Count > 0)
            {
                var diff = new JObject();
                foreach (var p in input.Validation.DiffPaths) diff[p] = new JArray(); // path recorded; values are in the specs
                validation["diff"] = diff;
            }
            if (input.Validation.Notes != null && input.Validation.Notes.Count > 0)
                validation["notes"] = new JArray(input.Validation.Notes);

            var doc = new JObject
            {
                ["schema_version"] = "1.0",
                ["study_id"] = input.StudyId,
                ["title"] = input.Title,
                ["pair_id"] = input.PairId,
                ["control_spec"] = ResponseJson.Parse(input.ControlSpecJson),
                ["treatment_spec"] = ResponseJson.Parse(input.TreatmentSpecJson),
                ["validation"] = validation,
                ["task"] = BuildTask(input.Task),
                ["modality"] = input.Modality,
                ["status"] = "published",
                ["created_at"] = input.CreatedAtIso,
            };
            if (!string.IsNullOrEmpty(input.Hypothesis)) doc["hypothesis"] = input.Hypothesis;
            return doc;
        }

        static JObject BuildTask(TaskConfig task)
        {
            var config = new JObject();
            if (!string.IsNullOrEmpty(task.Prompt)) config["prompt"] = task.Prompt;
            if (task.Type == "rating")
            {
                if (task.ScaleMin.HasValue) config["scale_min"] = task.ScaleMin.Value;
                if (task.ScaleMax.HasValue) config["scale_max"] = task.ScaleMax.Value;
            }
            else if (task.Type == "choice")
            {
                if (task.Trials.HasValue) config["trials"] = task.Trials.Value;
            }
            return new JObject { ["type"] = task.Type, ["config"] = config };
        }

        static IEnumerable<string> TaskSemanticErrors(TaskConfig task)
        {
            if (task.Type != "rating" && task.Type != "choice")
            {
                yield return $"task.type '{task.Type}' is not a known task (rating|choice)";
                yield break;
            }
            if (string.IsNullOrEmpty(task.Prompt))
                yield return "task.config.prompt is empty";

            if (task.Type == "rating")
            {
                if (!task.ScaleMin.HasValue || !task.ScaleMax.HasValue)
                    yield return "rating task requires scale_min and scale_max";
                else if (task.ScaleMax.Value <= task.ScaleMin.Value)
                    yield return $"rating scale is degenerate: scale_max {task.ScaleMax} <= scale_min {task.ScaleMin}";
            }
            else // choice
            {
                if (!task.Trials.HasValue || task.Trials.Value < 1)
                    yield return $"choice task requires trials >= 1 (got {(task.Trials.HasValue ? task.Trials.Value.ToString() : "none")})";
            }
        }
    }
}
