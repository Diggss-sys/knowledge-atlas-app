using System.Linq;
using NUnit.Framework;
using RoomGen.Runner;
using RoomGen.UI;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// S3 — the publish gate as a pure builder (StudyPublisher). Proves: a published document validates
    /// against study.schema.json AND passes the runner's StudyGate; a confounded (validation.ok=false)
    /// pair is provably impossible to publish (DL-6); and a degenerate task config is refused rather
    /// than repaired. All inputs are plain types — the test names no Newtonsoft type; created_at is
    /// injected so the output is deterministic.
    /// </summary>
    public sealed class StudyPublisherTests
    {
        static string Control => Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-control").text;
        static string Treatment => Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-treatment").text;

        static StudyPublisher.StudyInput ValidInput()
        {
            return new StudyPublisher.StudyInput
            {
                StudyId = "ceiling_pilot_01",
                PairId = "ceiling_height_study_01",
                Title = "Ceiling height and perceived spaciousness",
                Hypothesis = "higher ceilings feel more spacious",
                Modality = "desktop_3d",
                ControlSpecJson = Control,
                TreatmentSpecJson = Treatment,
                Validation = new StudyPublisher.ValidationStamp
                {
                    Ok = true,
                    DiffPaths = new[] { "shell.ceiling_height_m" },
                    Notes = new string[0],
                    Validator = "PairGate.cs@1.0",
                    ValidatedAtIso = "2026-07-07T12:00:00Z",
                },
                Task = new StudyPublisher.TaskConfig
                {
                    Type = "rating",
                    Prompt = "How pleasant does this room feel?",
                    ScaleMin = 1,
                    ScaleMax = 7,
                },
                CreatedAtIso = "2026-07-07T12:00:05Z",
            };
        }

        [Test]
        public void A_valid_pair_publishes_a_schema_valid_study_that_passes_the_study_gate()
        {
            var result = StudyPublisher.CreateDefault().Publish(ValidInput());

            Assert.IsTrue(result.Ok, "publish should succeed; errors: " + string.Join(" | ", result.Errors));
            Assert.IsNotNull(result.StudyJson);
            Assert.IsEmpty(result.Errors);

            // The runner's honesty gate accepts exactly what publish produced.
            Assert.IsTrue(StudyGate.CanRun(result.StudyJson, out var reason), reason);

            // Self-contained + stamped as required.
            StringAssert.Contains("\"status\":\"published\"", result.StudyJson);
            StringAssert.Contains("\"created_at\":\"2026-07-07T12:00:05Z\"", result.StudyJson);
            StringAssert.Contains("control_spec", result.StudyJson);
            StringAssert.Contains("treatment_spec", result.StudyJson);
        }

        [Test]
        public void A_confounded_pair_can_never_be_published()
        {
            var input = ValidInput();
            input.Validation.Ok = false; // the diff panel is red

            var result = StudyPublisher.CreateDefault().Publish(input);

            Assert.IsFalse(result.Ok, "DL-6: a confounded pair must be refused");
            Assert.IsNull(result.StudyJson, "no document may be emitted for a confounded pair");
            Assert.IsTrue(result.Errors.Any(e => e.Contains("DL-6")), "the refusal must cite the locked decision");
        }

        [Test]
        public void A_degenerate_rating_scale_is_refused()
        {
            var input = ValidInput();
            input.Task.ScaleMin = 5;
            input.Task.ScaleMax = 5; // min >= max

            var result = StudyPublisher.CreateDefault().Publish(input);

            Assert.IsFalse(result.Ok);
            Assert.IsNull(result.StudyJson);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("degenerate")), "the scale degeneracy must be named");
        }

        [Test]
        public void An_empty_prompt_is_refused()
        {
            var input = ValidInput();
            input.Task.Prompt = "";

            var result = StudyPublisher.CreateDefault().Publish(input);

            Assert.IsFalse(result.Ok);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("prompt")));
        }

        [Test]
        public void A_choice_task_with_zero_trials_is_refused()
        {
            var input = ValidInput();
            input.Task = new StudyPublisher.TaskConfig { Type = "choice", Prompt = "Which room feels larger?", Trials = 0 };

            var result = StudyPublisher.CreateDefault().Publish(input);

            Assert.IsFalse(result.Ok);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("trials")));
        }

        [Test]
        public void A_valid_choice_task_publishes()
        {
            var input = ValidInput();
            input.Task = new StudyPublisher.TaskConfig { Type = "choice", Prompt = "Which room feels larger?", Trials = 12 };

            var result = StudyPublisher.CreateDefault().Publish(input);

            Assert.IsTrue(result.Ok, "errors: " + string.Join(" | ", result.Errors));
            StringAssert.Contains("\"type\":\"choice\"", result.StudyJson);
            StringAssert.Contains("\"trials\":12", result.StudyJson);
        }
    }
}
