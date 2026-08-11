using System.IO;
using NUnit.Framework;
using RoomGen.Contracts;
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
            if (File.Exists(csv)) File.Delete(csv);
            if (File.Exists(jsonl)) File.Delete(jsonl);

            var session = new ParticipantSession(SampleStudy, "P 07!", seed: 160,
                sessionId: "11111111-1111-4111-8111-111111111111",
                csvPath: csv, jsonlPath: jsonl, nowUtc: () => "2026-07-20T18:42:11Z");

            Assert.IsTrue(session.CanRun, "a published, validated study must run: " + session.Reason);
            Assert.AreEqual(4, session.TrialCount, "the study declares 4 trials");

            var guard = 0;
            float? controlHeight = null, treatmentHeight = null;
            while (!session.IsComplete && guard++ < 32)
            {
                if (session.CurrentCondition == "treatment")
                    treatmentHeight = session.CurrentSpec.Geometry.CeilingHeightM;
                else
                    controlHeight = session.CurrentSpec.Geometry.CeilingHeightM;

                // The "human": treatment (taller ceiling) rated higher than control — plausible ratings,
                // always in range so the writer accepts them.
                var value = session.CurrentCondition == "treatment" ? session.ScaleMax : session.ScaleMin + 1;
                var errors = session.SubmitRating(value, rtMs: 4200);
                Assert.AreEqual(0, errors.Count, "the writer refused a row: " + string.Join(" | ", errors));
            }

            Assert.IsTrue(session.IsComplete, "the session did not complete");
            Assert.AreEqual(4, session.Written, "every trial must produce a written row");
            Assert.IsTrue(session.AllValid, "every row must validate against response_log.schema");
            Assert.IsFalse(File.Exists(session.IncompleteCsvPath), "completed CSV must leave the incomplete namespace");
            Assert.IsFalse(File.Exists(session.IncompleteJsonlPath), "completed JSONL must leave the incomplete namespace");
            Assert.AreEqual(2.4f, controlHeight.Value, 0.0001f, "canonical control must adapt to its authored ceiling");
            Assert.AreEqual(3.2f, treatmentHeight.Value, 0.0001f, "canonical treatment must adapt to its authored ceiling");
            Assert.AreNotEqual(controlHeight.Value, treatmentHeight.Value,
                "the participant must actually see different rooms for the declared variable");

            var lines = File.ReadAllLines(csv);
            Assert.AreEqual(5, lines.Length, "header + 4 rows");
            var text = File.ReadAllText(csv);
            StringAssert.Contains("shell.ceiling_height_m", text, "manipulated variable stamped into rows");
            StringAssert.Contains("P07", text, "sanitized participant id present ('P 07!' -> 'P07')");

            var controlSha = CanonicalJson.Sha256Property(SampleStudy, "control_spec");
            var treatmentSha = CanonicalJson.Sha256Property(SampleStudy, "treatment_spec");
            var responseLines = File.ReadAllLines(session.JsonlPath);
            Assert.AreEqual(4, responseLines.Length);
            foreach (var line in responseLines)
            {
                var expected = line.Contains("\"condition\":\"treatment\"") ? treatmentSha : controlSha;
                StringAssert.Contains("\"spec_sha256\":\"" + expected + "\"", line,
                    "each row must identify the exact embedded canonical room shown in that trial");
            }
        }

        [Test]
        public void An_aborted_session_refuses_all_future_response_rows()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "participant-aborted.csv");
            var jsonl = csv + ".jsonl";
            if (File.Exists(csv)) File.Delete(csv);
            if (File.Exists(jsonl)) File.Delete(jsonl);
            var session = new ParticipantSession(SampleStudy, "P01", 1, "session-abort",
                csv, jsonl, () => "2026-07-20T18:42:11Z");

            Assert.IsTrue(session.CanRun, session.Reason);
            session.Abort("missing furniture asset");
            var errors = session.SubmitRating(4, 1000);

            Assert.IsTrue(session.IsAborted);
            Assert.AreEqual("missing furniture asset", session.AbortReason);
            Assert.IsFalse(session.AllValid, "an aborted session cannot report a successful completion state");
            Assert.AreEqual(0, errors.Count, "rating is an intentional no-op after abort");
            Assert.AreEqual(0, session.Written);
            Assert.IsFalse(File.Exists(csv));
            Assert.IsFalse(File.Exists(jsonl));
        }

        [Test]
        public void A_partially_written_aborted_session_never_enters_the_completed_response_corpus()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "response-partial-abort.csv");
            var jsonl = Path.Combine(Application.temporaryCachePath, "response-partial-abort.jsonl");
            var incompleteCsv = Path.Combine(Path.GetDirectoryName(csv), "incomplete-" + Path.GetFileName(csv));
            var incompleteJsonl = Path.Combine(Path.GetDirectoryName(jsonl), "incomplete-" + Path.GetFileName(jsonl));
            foreach (var path in new[] { csv, jsonl, incompleteCsv, incompleteJsonl })
                if (File.Exists(path)) File.Delete(path);

            try
            {
                var session = new ParticipantSession(SampleStudy, "P01", 1, "partial-abort",
                    csv, jsonl, () => "2026-07-20T18:42:11Z");

                Assert.IsTrue(session.CanRun, session.Reason);
                Assert.IsEmpty(session.SubmitRating(4, 1000));
                Assert.AreEqual(1, session.Written);
                Assert.IsTrue(File.Exists(session.IncompleteCsvPath));
                Assert.IsTrue(File.Exists(session.IncompleteJsonlPath));
                Assert.IsFalse(File.Exists(session.CsvPath),
                    "partial rows must not use the response-* completed-session filename");
                Assert.IsFalse(File.Exists(session.JsonlPath));

                session.Abort("second room failed to build");

                Assert.IsTrue(session.IsAborted);
                Assert.IsFalse(File.Exists(session.CsvPath));
                Assert.IsFalse(File.Exists(session.JsonlPath));
                StringAssert.StartsWith("incomplete-", Path.GetFileName(session.IncompleteCsvPath));
            }
            finally
            {
                foreach (var path in new[] { csv, jsonl, incompleteCsv, incompleteJsonl })
                    if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void Existing_session_output_is_refused_instead_of_overwritten()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "participant-existing.csv");
            var jsonl = csv + ".jsonl";
            File.WriteAllText(csv, "prior participant data");

            var session = new ParticipantSession(SampleStudy, "P01", 1, "same-session",
                csv, jsonl, () => "2026-07-20T18:42:11Z");

            Assert.IsFalse(session.CanRun);
            StringAssert.Contains("refusing to overwrite", session.Reason);
            Assert.AreEqual("prior participant data", File.ReadAllText(csv));
            Assert.IsFalse(File.Exists(jsonl));
            File.Delete(csv);
        }

        [Test]
        public void A_draft_study_is_refused()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "participant-refuse.csv");
            var session = new ParticipantSession("{\"status\":\"draft\",\"validation\":{\"ok\":true}}",
                "P01", 1, "s", csv, csv + ".jsonl", () => "2026-07-20T18:42:11Z");

            Assert.IsFalse(session.CanRun, "a draft study must be refused");
            StringAssert.Contains("published", session.Reason);
            Assert.IsTrue(session.IsComplete, "a refused session has no plans and must be safe to inspect");
            Assert.IsNull(session.CurrentCondition);
            Assert.IsNull(session.CurrentSpec);
            Assert.IsNull(session.CurrentSpecSha256);
            Assert.AreEqual(0, session.SubmitRating(4).Count, "submit is a no-op when the study refused");
            Assert.AreEqual(0, session.Written);
        }

        [Test]
        public void A_missing_control_spec_reports_the_precise_canonical_shape_error()
        {
            var missingControl = SampleStudy.Replace("\"control_spec\"", "\"missing_control_spec\"");
            var csv = Path.Combine(Application.temporaryCachePath, "participant-missing-control.csv");

            var session = new ParticipantSession(missingControl, "P01", 1, "s", csv, csv + ".jsonl");

            Assert.IsFalse(session.CanRun);
            StringAssert.Contains("control_spec must be canonical", session.Reason);
            StringAssert.DoesNotContain("Object reference", session.Reason);
        }

        [Test]
        public void An_internal_form_study_is_refused_instead_of_silently_defaulting_geometry()
        {
            var internalShape = SampleStudy.Replace("\"shell\":", "\"geometry\":");
            var csv = Path.Combine(Application.temporaryCachePath, "participant-internal-refuse.csv");

            var session = new ParticipantSession(internalShape, "P01", 1, "s", csv, csv + ".jsonl");

            Assert.IsFalse(session.CanRun);
            StringAssert.Contains("must be canonical", session.Reason);
            StringAssert.Contains("internal geometry/room_id form is not accepted", session.Reason);
        }

        [Test]
        public void Lossy_adapter_warnings_refuse_the_session_and_are_exposed()
        {
            var tinted = SampleStudy.Replace(
                "\"wall\": { \"material\": \"plaster\" }",
                "\"wall\": { \"material\": \"plaster\", \"tint_hex\": \"#ece7dc\" }");
            var csv = Path.Combine(Application.temporaryCachePath, "participant-warning-refuse.csv");

            var session = new ParticipantSession(tinted, "P01", 1, "s", csv, csv + ".jsonl");

            Assert.IsFalse(session.CanRun);
            Assert.IsNotEmpty(session.AdaptationWarnings);
            StringAssert.Contains("tint_hex", session.Reason);
            StringAssert.Contains("lossy", session.Reason);
        }
    }
}
