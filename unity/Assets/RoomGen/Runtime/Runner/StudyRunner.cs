using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RoomGen.Runner
{
    /// <summary>
    /// Runs a whole session DRIVEN BY A STUDY DOCUMENT (spec/study.schema.json): honesty-gate it, read
    /// the task config (type / prompt / scale / trials / order strategy) and the manipulated variables
    /// (the keys of validation.diff), then produce validated rows with a deterministic fake responder.
    /// This is the config-driven counterpart to <see cref="ScriptedSession"/> and the R1→R2 boundary:
    /// swapping a human + rooms in for the fake responder is all that R2 adds.
    /// </summary>
    public static class StudyRunner
    {
        public struct RunResult
        {
            public bool Ran;
            public string Reason;   // why it refused to run, when Ran is false
            public int Written;
            public bool AllValid;
            public string[] Order;
        }

        public static RunResult Run(string studyJson, int seed, string csvPath, string jsonlPath)
        {
            if (!StudyGate.CanRun(studyJson, out var reason))
                return new RunResult { Ran = false, Reason = reason };

            var study = ResponseJson.Parse(studyJson);
            var studyId = (string)study["study_id"];
            var pairId = (string)study["pair_id"];
            var modality = (string)study["modality"] ?? "desktop_3d";
            var task = study["task"];
            var taskType = (string)task["type"];
            var cfg = task["config"];
            var trials = (int?)cfg?["trials"] ?? 2;
            var prompt = (string)cfg?["prompt"] ?? "";
            var scaleMin = (int?)cfg?["scale_min"] ?? 1;
            var scaleMax = (int?)cfg?["scale_max"] ?? 7;
            if (scaleMax < scaleMin)
                return new RunResult { Ran = false, Reason = $"degenerate rating scale [{scaleMin},{scaleMax}]" };
            var strategy = (string)study["counterbalance"]?["order_strategy"] ?? "seeded_shuffle";

            var manipulated = new List<string>();
            if (study["validation"]?["diff"] is JObject diff)
                foreach (var prop in diff.Properties())
                    manipulated.Add(prop.Name);

            if (File.Exists(csvPath)) File.Delete(csvPath);
            if (File.Exists(jsonlPath)) File.Delete(jsonlPath);
            var writer = new ResponseWriter(csvPath, jsonlPath, ResponseWriter.LoadSchema());

            var order = new List<string>();
            var allValid = true;

            if (taskType == "choice")
            {
                foreach (var plan in TrialSequencer.BuildChoice(trials, seed))
                {
                    order.Add(plan.LeftCondition + "/" + plan.RightCondition);
                    var chosen = plan.TrialIndex % 2 == 0 ? "left" : "right";
                    var row = MakeRow(studyId, pairId, modality, manipulated, seed, plan.TrialIndex,
                        "choice", "both",
                        ResponseRow.ChoiceResponse(chosen, plan.LeftCondition, plan.RightCondition, prompt));
                    if (writer.Write(row).Count > 0) allValid = false;
                }
            }
            else
            {
                foreach (var plan in TrialSequencer.BuildRating(trials, strategy, seed))
                {
                    order.Add(plan.Condition);
                    var value = plan.TrialIndex % (scaleMax - scaleMin + 1) + scaleMin;
                    var row = MakeRow(studyId, pairId, modality, manipulated, seed, plan.TrialIndex,
                        "rating", plan.Condition,
                        ResponseRow.RatingResponse(scaleMin, scaleMax, value, prompt));
                    if (writer.Write(row).Count > 0) allValid = false;
                }
            }

            return new RunResult
            {
                Ran = true,
                Reason = "",
                Written = writer.WrittenCount,
                AllValid = allValid,
                Order = order.ToArray()
            };
        }

        static ResponseRow MakeRow(string studyId, string pairId, string modality, List<string> manipulated,
            int seed, int trialIndex, string taskType, string condition, JObject response)
        {
            return new ResponseRow
            {
                StudyId = studyId,
                PairId = pairId,
                ParticipantId = "p_scripted",
                SessionId = "00000000-0000-4000-8000-000000000000",
                TrialIndex = trialIndex,
                TaskType = taskType,
                Condition = condition,
                ManipulatedVariables = new List<string>(manipulated),
                Modality = modality,
                Response = response,
                TimestampUtc = "2026-07-20T18:42:11Z",
                PresentationOrderSeed = seed
            };
        }
    }
}
