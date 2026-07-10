using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomGen.Runner;
using UnityEngine;

namespace RoomGen.Tests
{
    public class TrialSequencerTests
    {
        static string[] RatingOrder(int trials, string strategy, int seed)
            => TrialSequencer.BuildRating(trials, strategy, seed).Select(p => p.Condition).ToArray();

        [Test]
        public void Same_seed_reproduces_the_same_order()
        {
            CollectionAssert.AreEqual(
                RatingOrder(6, "seeded_shuffle", 160),
                RatingOrder(6, "seeded_shuffle", 160));
        }

        [Test]
        public void Seeded_shuffle_varies_across_seeds()
        {
            var distinct = new HashSet<string>();
            for (var s = 1; s <= 8; s++)
                distinct.Add(string.Join(",", RatingOrder(6, "seeded_shuffle", s)));
            Assert.Greater(distinct.Count, 1, "Different seeds should produce different orders.");
        }

        [Test]
        public void Fixed_strategy_is_deterministic_alternating()
        {
            CollectionAssert.AreEqual(
                new[] { "control", "treatment", "control", "treatment" },
                RatingOrder(4, "fixed", 999));
        }

        [Test]
        public void Rating_conditions_are_balanced()
        {
            var order = RatingOrder(6, "seeded_shuffle", 160);
            Assert.AreEqual(3, order.Count(c => c == "control"));
            Assert.AreEqual(3, order.Count(c => c == "treatment"));
        }

        [Test]
        public void Trial_indices_are_zero_based_and_contiguous()
        {
            var plans = TrialSequencer.BuildRating(5, "seeded_shuffle", 7);
            CollectionAssert.AreEqual(Enumerable.Range(0, plans.Count).ToArray(),
                plans.Select(p => p.TrialIndex).ToArray());
        }

        [Test]
        public void Choice_presents_both_with_counterbalanced_sides()
        {
            var plans = TrialSequencer.BuildChoice(6, 160);
            Assert.IsTrue(plans.All(p => p.Condition == "both"));
            Assert.IsTrue(plans.All(p =>
                (p.LeftCondition == "control" && p.RightCondition == "treatment") ||
                (p.LeftCondition == "treatment" && p.RightCondition == "control")));
            Assert.AreEqual(3, plans.Count(p => p.LeftCondition == "control"), "Sides should be balanced.");
        }

        [Test]
        public void Gate_allows_published_validated_study()
        {
            Assert.IsTrue(StudyGate.CanRun("{\"status\":\"published\",\"validation\":{\"ok\":true}}", out _));
        }

        [Test]
        public void Gate_refuses_draft_and_unvalidated_studies()
        {
            Assert.IsFalse(StudyGate.CanRun("{\"status\":\"draft\",\"validation\":{\"ok\":true}}", out var r1));
            StringAssert.Contains("published", r1);
            Assert.IsFalse(StudyGate.CanRun("{\"status\":\"published\",\"validation\":{\"ok\":false}}", out var r2));
            StringAssert.Contains("validation", r2);
        }

        [Test]
        public void Scripted_rating_session_writes_a_valid_seeded_csv()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "scripted-test.csv");
            var jsonl = Path.Combine(Application.temporaryCachePath, "scripted-test.jsonl");

            var run = ScriptedSession.RunRating(6, "seeded_shuffle", 160, csv, jsonl);
            Assert.AreEqual(6, run.Written, "Every trial should produce a written row.");
            Assert.IsTrue(run.AllValid, "Every scripted row must pass schema validation.");

            // Determinism: the same seed reproduces the same presentation order.
            var rerun = ScriptedSession.RunRating(6, "seeded_shuffle", 160, csv, jsonl);
            CollectionAssert.AreEqual(run.ConditionOrder, rerun.ConditionOrder);

            var lines = File.ReadAllLines(csv);
            Assert.AreEqual(7, lines.Length, "Header + 6 rows.");
            StringAssert.StartsWith("schema_version,study_id", lines[0]);
        }

        [Test]
        public void Scripted_choice_session_writes_valid_counterbalanced_csv()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "scripted-choice.csv");
            var jsonl = Path.Combine(Application.temporaryCachePath, "scripted-choice.jsonl");

            var run = ScriptedSession.RunChoice(6, 160, csv, jsonl);
            Assert.AreEqual(6, run.Written);
            Assert.IsTrue(run.AllValid, "Every choice row (condition=both, choice payload) must validate.");

            // Sides are counterbalanced: both orientations appear across the session.
            Assert.IsTrue(run.ConditionOrder.Any(o => o == "control/treatment"), "control-left never appeared.");
            Assert.IsTrue(run.ConditionOrder.Any(o => o == "treatment/control"), "treatment-left never appeared.");

            var lines = File.ReadAllLines(csv);
            Assert.AreEqual(7, lines.Length, "Header + 6 choice rows.");
        }
    }
}
