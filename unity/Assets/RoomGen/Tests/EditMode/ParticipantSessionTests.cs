using System.IO;
using NUnit.Framework;
using RoomGen.Runner;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// A2 acceptance, scripted: a "human" completes a whole rating session through ParticipantSession
    /// (the exact class the exe's UI drives) and the CSV must validate against the contract schema in
    /// canonical column order. This is the R2-lite proof — same validated rows as StudyRunner, but from
    /// a real participant id + per-trial ratings instead of the fake responder.
    /// </summary>
    public class ParticipantSessionTests
    {
        static string SampleStudy => Resources.Load<TextAsset>("RoomGen/Examples/ceiling-study").text;

        [Test]
        public void A_human_session_writes_a_valid_csv_with_the_real_participant_id()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "participant-run.csv");
            var jsonl = Path.Combine(Application.temporaryCachePath, "participant-run.jsonl");

            var session = new ParticipantSession(SampleStudy, "P 07!", seed: 160,
                sessionId: "11111111-1111-4111-8111-111111111111",
                csvPath: csv, jsonlPath: jsonl, nowUtc: () => "2026-07-20T18:42:11Z");

            Assert.IsTrue(session.CanRun, "a published, validated study must run: " + session.Reason);
            Assert.AreEqual(4, session.TrialCount, "the study declares 4 trials");

            var guard = 0;
            while (!session.IsComplete && guard++ < 32)
            {
                // The "human": treatment (taller ceiling) rated higher than control — plausible ratings,
                // always in range so the writer accepts them.
                var value = session.CurrentCondition == "treatment" ? session.ScaleMax : session.ScaleMin + 1;
                var errors = session.SubmitRating(value, rtMs: 4200);
                Assert.AreEqual(0, errors.Count, "the writer refused a row: " + string.Join(" | ", errors));
            }

            Assert.IsTrue(session.IsComplete, "the session did not complete");
            Assert.AreEqual(4, session.Written, "every trial must produce a written row");
            Assert.IsTrue(session.AllValid, "every row must validate against response_log.schema");

            var lines = File.ReadAllLines(csv);
            Assert.AreEqual(5, lines.Length, "header + 4 rows");
            var text = File.ReadAllText(csv);
            StringAssert.Contains("shell.ceiling_height_m", text, "manipulated variable stamped into rows");
            StringAssert.Contains("P07", text, "sanitized participant id present ('P 07!' -> 'P07')");
        }

        [Test]
        public void A_draft_study_is_refused()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "participant-refuse.csv");
            var session = new ParticipantSession("{\"status\":\"draft\",\"validation\":{\"ok\":true}}",
                "P01", 1, "s", csv, csv + ".jsonl", () => "2026-07-20T18:42:11Z");

            Assert.IsFalse(session.CanRun, "a draft study must be refused");
            StringAssert.Contains("published", session.Reason);
            Assert.AreEqual(0, session.SubmitRating(4).Count, "submit is a no-op when the study refused");
            Assert.AreEqual(0, session.Written);
        }
    }
}
