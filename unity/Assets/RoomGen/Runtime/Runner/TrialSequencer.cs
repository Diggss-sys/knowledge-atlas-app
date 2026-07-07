using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoomGen.Runner
{
    /// <summary>One planned trial: which condition(s) to present, in presentation order.</summary>
    public struct TrialPlan
    {
        public int TrialIndex;
        public string TaskType;        // rating | choice
        public string Condition;       // control | treatment | both
        public string LeftCondition;   // choice only (counterbalanced by seed)
        public string RightCondition;  // choice only
    }

    /// <summary>
    /// Deterministic, seeded trial ordering (RESPONSE_LOG.md determinism rule 4). Uses
    /// System.Random(seed) Fisher-Yates — NEVER UnityEngine.Random, whose unseeded state would make
    /// a session unreproducible. The seed is recorded in every row so seed + trial_index fully
    /// reconstructs what was shown when.
    /// </summary>
    public static class TrialSequencer
    {
        /// <summary>
        /// A rating session presents each condition; the base set alternates control/treatment to a
        /// balanced count of `trials` (min 2), then is ordered by strategy.
        /// </summary>
        public static List<TrialPlan> BuildRating(int trials, string orderStrategy, int seed)
        {
            trials = Mathf.Max(2, trials);
            var conditions = new List<string>();
            for (var i = 0; i < trials; i++)
                conditions.Add(i % 2 == 0 ? "control" : "treatment");

            switch (orderStrategy)
            {
                case "fixed":
                    break; // alternating, as built
                case "latin_square":
                    // Two conditions -> a 2x2 Latin square is {AB, BA}; the seed selects the row.
                    if ((seed & 1) == 1)
                        conditions = conditions.Select(c => c == "control" ? "treatment" : "control").ToList();
                    break;
                default: // "seeded_shuffle"
                    Shuffle(conditions, new System.Random(seed));
                    break;
            }

            var plans = new List<TrialPlan>(conditions.Count);
            for (var i = 0; i < conditions.Count; i++)
                plans.Add(new TrialPlan { TrialIndex = i, TaskType = "rating", Condition = conditions[i] });
            return plans;
        }

        /// <summary>
        /// A choice session presents the pair (condition "both") `trials` times with the sides
        /// counterbalanced: a balanced set of control-left / treatment-left, shuffled by seed.
        /// </summary>
        public static List<TrialPlan> BuildChoice(int trials, int seed)
        {
            trials = Mathf.Max(1, trials);
            var controlLeft = new List<bool>();
            for (var i = 0; i < trials; i++)
                controlLeft.Add(i < trials / 2); // balanced; odd trials give the extra to treatment-left
            Shuffle(controlLeft, new System.Random(seed));

            var plans = new List<TrialPlan>(trials);
            for (var i = 0; i < trials; i++)
                plans.Add(new TrialPlan
                {
                    TrialIndex = i,
                    TaskType = "choice",
                    Condition = "both",
                    LeftCondition = controlLeft[i] ? "control" : "treatment",
                    RightCondition = controlLeft[i] ? "treatment" : "control"
                });
            return plans;
        }

        static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
