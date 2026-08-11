using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomGen.Contracts;
using RoomGen.Runner;
using UnityEngine;

namespace RoomGen.Tests
{
    public class ResponseLogTests
    {
        static string Schema => Resources.Load<TextAsset>("RoomGen/response_log.schema").text;
        static string Fixtures => Resources.Load<TextAsset>("RoomGen/Fixtures/response_rows").text;

        [Test]
        public void Golden_fixtures_validate_and_refuse_as_declared()
        {
            var outcomes = ResponseLogFixtureRunner.Run(Fixtures, Schema);
            Assert.IsNotEmpty(outcomes, "No fixtures were evaluated.");
            var failures = outcomes.Where(o => !o.Match)
                .Select(o => $"{o.Name}: expectedValid={o.ExpectedValid} actualValid={o.ActualValid} ({o.Detail})")
                .ToList();
            Assert.IsEmpty(failures, "Fixture mismatches:\n" + string.Join("\n", failures));
        }

        [Test]
        public void Builder_reproduces_the_rating_golden_row()
        {
            Assert.IsTrue(ResponseLogFixtureRunner.BuilderReproducesRatingGolden(Fixtures, out var detail), detail);
        }

        [Test]
        public void Csv_header_is_canonical_order()
        {
            Assert.AreEqual(
                "schema_version,study_id,pair_id,participant_id,session_id,trial_index,task_type,condition," +
                "manipulated_variables,modality,execution_path,response_json,rt_ms,timestamp_utc," +
                "presentation_order_seed,runner_version,spec_sha256",
                ResponseCsv.Header());
        }

        [Test]
        public void Csv_joins_variables_leaves_optionals_empty_and_quotes_json()
        {
            var row = ResponseLogFixtureRunner.SampleTwoVariableRatingRow();

            var cols = ResponseCsv.ToColumns(row);
            Assert.AreEqual("shell.ceiling_height_m;lighting.warmth", cols[8], "manipulated_variables must be ';'-joined.");
            Assert.AreEqual("", cols[12], "rt_ms must be empty when unset.");
            Assert.AreEqual("", cols[16], "spec_sha256 must be empty when unset.");
            Assert.AreEqual(ResponseCsv.Columns.Length, cols.Length);

            var line = ResponseCsv.ToCsvLine(row);
            StringAssert.Contains("\"{", line, "response_json (with commas/quotes) must be RFC-4180 quoted.");
        }

        [Test]
        public void Rating_value_is_clamped_into_range()
        {
            Assert.AreEqual(7, ResponseRow.ClampRating(1, 7, 12), "Above-max rating clamps to max.");
            Assert.AreEqual(1, ResponseRow.ClampRating(1, 7, 0), "Below-min rating clamps to min.");
            Assert.AreEqual(4, ResponseRow.ClampRating(1, 7, 4), "In-range rating is unchanged.");
        }

        [Test]
        public void Writer_refuses_semantically_invalid_rows_instead_of_repairing()
        {
            var csv = System.IO.Path.Combine(Application.temporaryCachePath, "refuse-semantic.csv");
            var jsonl = csv + ".jsonl";
            if (System.IO.File.Exists(csv)) System.IO.File.Delete(csv);
            if (System.IO.File.Exists(jsonl)) System.IO.File.Delete(jsonl);
            var writer = ResponseWriter.CreateDefault(csv, jsonl);

            // Out-of-range rating: schema-valid (the range is "runner-enforced"), must be REFUSED —
            // never clamped/altered at the sink (that would be data fabrication).
            var outOfRange = ResponseLogFixtureRunner.SampleOutOfRangeRatingRow();
            var errors1 = writer.Write(outOfRange);
            Assert.IsNotEmpty(errors1, "Out-of-range rating must be refused.");

            // Degenerate choice (both sides the same condition): schema-valid, semantically broken.
            var degenerate = ResponseLogFixtureRunner.SampleDegenerateChoiceRow();
            var errors2 = writer.Write(degenerate);
            Assert.IsNotEmpty(errors2, "left==right choice must be refused.");

            Assert.AreEqual(0, writer.WrittenCount, "Refused rows must not reach the CSV.");
            Assert.IsFalse(System.IO.File.Exists(csv), "No file should be created by refused rows.");
        }

        [Test]
        public void Canonical_hash_ignores_object_key_order_and_whitespace_but_not_array_order()
        {
            const string a = "{\"b\":2,\"a\":{\"y\":[1,2],\"x\":true}}";
            const string same = "{ \"a\" : { \"x\": true, \"y\": [1, 2] }, \"b\": 2 }";
            const string differentArray = "{\"a\":{\"x\":true,\"y\":[2,1]},\"b\":2}";

            Assert.AreEqual(CanonicalJson.Sha256(a), CanonicalJson.Sha256(same));
            Assert.AreNotEqual(CanonicalJson.Sha256(a), CanonicalJson.Sha256(differentArray));
            Assert.AreEqual(64, CanonicalJson.Sha256(a).Length);
        }

        [Test]
        public void Session_events_are_written_only_to_their_own_jsonl_sidecar()
        {
            var path = Path.Combine(Application.temporaryCachePath, "session-events.jsonl");
            if (File.Exists(path)) File.Delete(path);
            var log = new SessionEventLog(path, () => "2026-08-10T20:00:00Z");

            log.Write("trial_aborted", 2, "Missing furniture asset: unknown_chair");

            Assert.AreEqual(1, log.WrittenCount);
            var row = File.ReadAllText(path);
            StringAssert.Contains("\"ts\":\"2026-08-10T20:00:00Z\"", row);
            StringAssert.Contains("\"kind\":\"trial_aborted\"", row);
            StringAssert.Contains("\"trial_index\":2", row);
            StringAssert.Contains("unknown_chair", row);
        }
    }
}
