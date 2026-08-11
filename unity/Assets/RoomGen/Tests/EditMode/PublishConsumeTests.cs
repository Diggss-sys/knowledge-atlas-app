using System.IO;
using NUnit.Framework;
using RoomGen.Runner;
using RoomGen.UI;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// The author → run loop as one test: StudyPublisher (A1's Publish) emits a study document, and a
    /// fresh ParticipantSession (A2) consumes THAT document and writes valid rows. Proves the two
    /// contracts actually agree — a published study is exactly what the runner accepts — so drift in
    /// one can't silently break the other. (No Newtonsoft: this assembly can't resolve it; the specs
    /// are plain strings — control_spec is only walked, never validated, so minimal is fine.)
    /// </summary>
    public class PublishConsumeTests
    {
        // The frozen contract fixtures intentionally include tint_hex, which the current adapter
        // cannot render. A newly authored operator study strips that unsupported decoration before
        // publish, so this author-to-run test mirrors the real completion pipeline.
        static string ControlSpec => Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-control").text
            .Replace(", \"tint_hex\": \"#ece7dc\"", "");
        static string TreatmentSpec => Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-treatment").text
            .Replace(", \"tint_hex\": \"#ece7dc\"", "");

        [Test]
        public void A_published_study_is_consumable_by_the_participant_runner()
        {
            var result = StudyPublisher.CreateDefault().Publish(new StudyPublisher.StudyInput
            {
                StudyId = "operator_authored_study",
                PairId = "ceiling_height_study_01",
                Title = "Operator-authored study",
                Modality = "desktop_3d",
                ControlSpecJson = ControlSpec,
                TreatmentSpecJson = TreatmentSpec,
                Task = new StudyPublisher.TaskConfig
                {
                    Type = "rating",
                    Prompt = "How pleasant does this room feel?",
                    ScaleMin = 1,
                    ScaleMax = 7,
                },
                CreatedAtIso = "2026-07-10T12:00:00Z",
            });

            Assert.IsTrue(result.Ok, "publish should succeed: "
                + (result.Errors != null ? string.Join(" | ", result.Errors) : ""));

            // Now run that exact document through the participant session.
            var csv = Path.Combine(Application.temporaryCachePath, "publish-consume.csv");
            var jsonl = Path.Combine(Application.temporaryCachePath, "publish-consume.jsonl");
            var session = new ParticipantSession(result.StudyJson, "P01", seed: 7,
                sessionId: "sess-pc", csvPath: csv, jsonlPath: jsonl, nowUtc: () => "2026-07-10T12:00:00Z");

            Assert.IsTrue(session.CanRun, "the published study must run: " + session.Reason);

            var guard = 0;
            while (!session.IsComplete && guard++ < 16)
            {
                var expectedHeight = session.CurrentCondition == "treatment" ? 2.6f : 3.2f;
                Assert.AreEqual(expectedHeight, session.CurrentSpec.Geometry.CeilingHeightM, 0.0001f,
                    "canonical study specs must pass through RoomSpecAdapter before the walk");
                var errors = session.SubmitRating(session.CurrentCondition == "treatment" ? 6 : 3);
                Assert.AreEqual(0, errors.Count, "the writer refused a row: " + string.Join(" | ", errors));
            }

            Assert.IsTrue(session.IsComplete, "session did not complete");
            Assert.AreEqual(session.TrialCount, session.Written, "every trial produces a row");
            Assert.IsTrue(session.AllValid, "every row from the published study must validate");
            StringAssert.Contains("shell.ceiling_height_m", File.ReadAllText(csv),
                "the declared variable (from validation.diff) is stamped into the rows");
        }
    }
}
