using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RoomGen.Gate;
using RoomGen.Runner;
using UnityEngine;

namespace RoomGen.UI
{
    /// <summary>
    /// Turns a canonical control/treatment pair and task into a self-contained study document.
    /// The publisher is the final integrity sink: it revalidates the exact embedded specs and derives
    /// the validation stamp itself, so stale or caller-forged UI state can never publish a confound.
    /// </summary>
    public sealed class StudyPublisher
    {
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
            public string Hypothesis;
            public string Modality;
            public string ControlSpecJson;
            public string TreatmentSpecJson;
            public TaskConfig Task;
            public string CreatedAtIso;
        }

        public struct Result
        {
            public bool Ok;
            public string StudyJson;
            public IReadOnlyList<string> Errors;
            public string EmbeddedControlSpecJson;
            public string EmbeddedTreatmentSpecJson;
            public IReadOnlyList<string> ValidationDiffPaths;
            public IReadOnlyList<string> ValidationNotes;
            public string Validator;
            public string ValidatedAtIso;
        }

        readonly JObject _studySchema;
        readonly JObject _roomSchema;

        public StudyPublisher(JObject studySchema) : this(studySchema, LoadRoomSchema()) { }

        public StudyPublisher(JObject studySchema, JObject roomSchema)
        {
            _studySchema = studySchema;
            _roomSchema = roomSchema;
        }

        public static JObject LoadSchema()
        {
            var text = Resources.Load<TextAsset>("RoomGen/study.schema");
            return text != null ? ResponseJson.Parse(text.text) : null;
        }

        public static JObject LoadRoomSchema()
        {
            var text = Resources.Load<TextAsset>("RoomGen/room_spec.schema");
            return text != null ? ResponseJson.Parse(text.text) : null;
        }

        public static StudyPublisher CreateDefault() => new StudyPublisher(LoadSchema(), LoadRoomSchema());

        /// <summary>Build and validate the study. No document is emitted when any gate refuses it.</summary>
        public Result Publish(StudyInput input)
        {
            var errors = new List<string>();
            PairGate.Result gate = null;

            if (_roomSchema == null)
            {
                errors.Add("room_spec schema is unavailable");
            }
            else
            {
                try
                {
                    var control = ResponseJson.Parse(input.ControlSpecJson);
                    var treatment = ResponseJson.Parse(input.TreatmentSpecJson);
                    gate = PairGate.Validate(control, treatment, _roomSchema);

                    if (!gate.Ok)
                        foreach (var violation in gate.Violations)
                            errors.Add($"{violation.Code} @ {violation.Path}: {violation.Message}");

                    ValidatePairMemberMetadata(control, "control", input.PairId, errors);
                    ValidatePairMemberMetadata(treatment, "treatment", input.PairId, errors);
                }
                catch (Exception e)
                {
                    errors.Add("pair validation could not parse the embedded specs: " + e.Message);
                }
            }

            errors.AddRange(TaskSemanticErrors(input.Task));
            if (errors.Count > 0)
                return new Result { Ok = false, StudyJson = null, Errors = errors };

            var doc = BuildDocument(input, gate);

            if (_studySchema == null)
                errors.Add("study schema is unavailable");
            else
                foreach (var e in JsonSchemaLite.Validate(doc, _studySchema))
                    errors.Add($"{e.Path}: {e.Message}");

            var json = doc.ToString(Newtonsoft.Json.Formatting.None);
            if (!StudyGate.CanRun(json, out var reason))
                errors.Add($"StudyGate refused the produced document: {reason}");

            if (errors.Count > 0)
            {
                Debug.LogWarning($"StudyPublisher refused a document: {string.Join(" | ", errors)}");
                return new Result { Ok = false, StudyJson = null, Errors = errors };
            }

            return new Result
            {
                Ok = true,
                StudyJson = json,
                Errors = errors,
                EmbeddedControlSpecJson = doc["control_spec"].ToString(Newtonsoft.Json.Formatting.None),
                EmbeddedTreatmentSpecJson = doc["treatment_spec"].ToString(Newtonsoft.Json.Formatting.None),
                ValidationDiffPaths = new List<string>(gate.DiffPaths),
                ValidationNotes = new List<string>(gate.Notes),
                Validator = "PairGate.cs@1.0",
                ValidatedAtIso = input.CreatedAtIso,
            };
        }

        static JObject BuildDocument(StudyInput input, PairGate.Result gate)
        {
            var validation = new JObject
            {
                ["ok"] = gate.Ok,
                ["validated_at"] = input.CreatedAtIso,
                ["validator"] = "PairGate.cs@1.0",
            };

            if (gate.DiffPaths.Count > 0)
            {
                var diff = new JObject();
                foreach (var path in gate.DiffPaths)
                    diff[path] = new JArray();
                validation["diff"] = diff;
            }

            if (gate.Notes.Count > 0)
                validation["notes"] = new JArray(gate.Notes);

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

        static void ValidatePairMemberMetadata(
            JObject spec, string expectedCondition, string expectedPairId, List<string> errors)
        {
            var experiment = spec["experiment"] as JObject;
            var condition = (string)experiment?["condition"];
            var pairId = (string)experiment?["pair_id"];

            if (!string.Equals(condition, expectedCondition, StringComparison.Ordinal))
                errors.Add($"study_pair_role_mismatch: {expectedCondition}_spec declares condition '{condition}'");
            if (!string.Equals(pairId, expectedPairId, StringComparison.Ordinal))
                errors.Add($"study_pair_id_mismatch: {expectedCondition}_spec pair_id '{pairId}' does not match study pair_id '{expectedPairId}'");
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
            else if (!task.Trials.HasValue || task.Trials.Value < 1)
            {
                yield return $"choice task requires trials >= 1 (got {(task.Trials.HasValue ? task.Trials.Value.ToString() : "none")})";
            }
        }
    }
}
