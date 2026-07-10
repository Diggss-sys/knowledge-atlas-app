using System.Collections.Generic;
using System.IO;

namespace RoomGen.Runner
{
    /// <summary>
    /// Drives a full fake rating session with no UI or renderer: seeded trial order -> deterministic
    /// fake ratings -> validate + write CSV/JSONL. This is the headless proof of R0+R1 (a scripted
    /// session produces a valid CSV in correctly seeded order) that the real participant flow (R2)
    /// swaps a human + rooms into.
    /// </summary>
    public static class ScriptedSession
    {
        public struct Result
        {
            public int Written;
            public bool AllValid;
            public string[] ConditionOrder;
            public string CsvPath;
        }

        public static Result RunRating(int trials, string orderStrategy, int seed, string csvPath, string jsonlPath)
        {
            // Fresh session — start from empty files so appends are deterministic.
            if (File.Exists(csvPath)) File.Delete(csvPath);
            if (File.Exists(jsonlPath)) File.Delete(jsonlPath);

            var schema = ResponseWriter.LoadSchema();
            var writer = new ResponseWriter(csvPath, jsonlPath, schema);
            var plans = TrialSequencer.BuildRating(trials, orderStrategy, seed);

            var order = new List<string>();
            var allValid = true;

            foreach (var plan in plans)
            {
                order.Add(plan.Condition);
                var value = (plan.TrialIndex % 7) + 1; // deterministic fake rating in [1,7]
                var row = new ResponseRow
                {
                    StudyId = "scripted_study",
                    PairId = "ceiling_height_study_01",
                    ParticipantId = "p_scripted",
                    SessionId = "00000000-0000-4000-8000-000000000000",
                    TrialIndex = plan.TrialIndex,
                    TaskType = "rating",
                    Condition = plan.Condition,
                    ManipulatedVariables = { "shell.ceiling_height_m" },
                    Response = ResponseRow.RatingResponse(1, 7, value, "How pleasant does this room feel?"),
                    TimestampUtc = "2026-07-20T18:42:11Z",
                    PresentationOrderSeed = seed
                };
                if (writer.Write(row).Count > 0) allValid = false;
            }

            return new Result
            {
                Written = writer.WrittenCount,
                AllValid = allValid,
                ConditionOrder = order.ToArray(),
                CsvPath = csvPath
            };
        }

        public static Result RunChoice(int trials, int seed, string csvPath, string jsonlPath)
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
            if (File.Exists(jsonlPath)) File.Delete(jsonlPath);

            var schema = ResponseWriter.LoadSchema();
            var writer = new ResponseWriter(csvPath, jsonlPath, schema);
            var plans = TrialSequencer.BuildChoice(trials, seed);

            var order = new List<string>();
            var allValid = true;

            foreach (var plan in plans)
            {
                order.Add(plan.LeftCondition + "/" + plan.RightCondition); // side assignment for this trial
                var chosen = plan.TrialIndex % 2 == 0 ? "left" : "right"; // deterministic fake pick
                var row = new ResponseRow
                {
                    StudyId = "scripted_study",
                    PairId = "ceiling_height_study_01",
                    ParticipantId = "p_scripted",
                    SessionId = "00000000-0000-4000-8000-000000000000",
                    TrialIndex = plan.TrialIndex,
                    TaskType = "choice",
                    Condition = "both",
                    ManipulatedVariables = { "shell.ceiling_height_m" },
                    Response = ResponseRow.ChoiceResponse(chosen, plan.LeftCondition, plan.RightCondition,
                        "Which room would you rather work in?"),
                    TimestampUtc = "2026-07-20T18:47:03Z",
                    PresentationOrderSeed = seed
                };
                if (writer.Write(row).Count > 0) allValid = false;
            }

            return new Result
            {
                Written = writer.WrittenCount,
                AllValid = allValid,
                ConditionOrder = order.ToArray(),
                CsvPath = csvPath
            };
        }
    }
}
