using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoomGen.Runner
{
    /// <summary>
    /// Replays spec/fixtures/response_rows.json against the validator and (for the golden builder
    /// check) the ResponseRow builder, returning plain structs so the EditMode test asserts without
    /// constructing Newtonsoft types itself — the established fixture-runner pattern (see PairGate).
    /// </summary>
    public static class ResponseLogFixtureRunner
    {
        public struct Outcome
        {
            public string Name;
            public bool ExpectedValid;
            public bool ActualValid;
            public bool Match;
            public string Detail; // validation errors when invalid, else empty
        }

        public static List<Outcome> Run(string fixturesJson, string schemaJson)
        {
            var schema = ResponseJson.Parse(schemaJson);
            var fixture = ResponseJson.Parse(fixturesJson);
            var outcomes = new List<Outcome>();

            foreach (var item in (JArray)fixture["valid"])
                outcomes.Add(Eval((JObject)item, schema, expectedValid: true));
            foreach (var item in (JArray)fixture["invalid"])
                outcomes.Add(Eval((JObject)item, schema, expectedValid: false));
            return outcomes;
        }

        static Outcome Eval(JObject item, JObject schema, bool expectedValid)
        {
            var row = (JObject)item["row"];
            var errors = ResponseLogValidator.Validate(row, schema);
            var actualValid = errors.Count == 0;
            return new Outcome
            {
                Name = (string)item["name"],
                ExpectedValid = expectedValid,
                ActualValid = actualValid,
                Match = actualValid == expectedValid,
                Detail = actualValid ? "" : string.Join(" | ", errors)
            };
        }

        /// <summary>
        /// A sample rating row with two manipulated variables and no optional fields — built in
        /// Runtime (where Newtonsoft lives) so the EditMode test can exercise CSV serialisation
        /// without naming a JObject.
        /// </summary>
        public static ResponseRow SampleTwoVariableRatingRow()
        {
            return new ResponseRow
            {
                StudyId = "s", PairId = "p", ParticipantId = "p_1", SessionId = "sess",
                TrialIndex = 2, TaskType = "rating", Condition = "control",
                ManipulatedVariables = { "shell.ceiling_height_m", "lighting.warmth" },
                Response = ResponseRow.RatingResponse(1, 7, 4),
                TimestampUtc = "2026-07-20T18:42:11Z", PresentationOrderSeed = 160, RunnerVersion = "0.1.0"
            };
        }

        /// <summary>A schema-valid row whose rating value lies OUTSIDE its own scale (semantic violation).</summary>
        public static ResponseRow SampleOutOfRangeRatingRow()
        {
            var row = SampleTwoVariableRatingRow();
            row.Response = ResponseRow.RatingResponse(1, 7, 12);
            return row;
        }

        /// <summary>A schema-valid choice row with both sides showing the same condition (semantic violation).</summary>
        public static ResponseRow SampleDegenerateChoiceRow()
        {
            var row = SampleTwoVariableRatingRow();
            row.TaskType = "choice";
            row.Condition = "both";
            row.Response = ResponseRow.ChoiceResponse("left", "control", "control");
            return row;
        }

        /// <summary>
        /// Rebuilds the 'rating_row_control' golden row via the ResponseRow builder and deep-compares
        /// to the fixture — proves the writer can reproduce a golden row exactly (DoD R0).
        /// </summary>
        public static bool BuilderReproducesRatingGolden(string fixturesJson, out string detail)
        {
            var fixture = ResponseJson.Parse(fixturesJson);
            var golden = (JObject)((JArray)fixture["valid"])
                .First(t => (string)t["name"] == "rating_row_control")["row"];

            var built = new ResponseRow
            {
                StudyId = "ceiling_pilot_01",
                PairId = "ceiling_height_study_01",
                ParticipantId = "p_017",
                SessionId = "3f2c9a4e-1d5b-4f7a-9c0e-8a6d2b1e5c44",
                TrialIndex = 0,
                TaskType = "rating",
                Condition = "control",
                ManipulatedVariables = { "shell.ceiling_height_m" },
                Modality = "desktop_3d",
                ExecutionPath = "live",
                Response = ResponseRow.RatingResponse(1, 7, 5, "How pleasant does this room feel?"),
                RtMs = 2350.5,
                TimestampUtc = "2026-07-20T18:42:11Z",
                PresentationOrderSeed = 160,
                RunnerVersion = "0.1.0",
                SpecSha256 = "9b1a5c3e7f2d4a6b8c0e1f3a5b7d9c2e4f6a8b0c1d3e5f7a9b1c3d5e7f9a0b2c"
            }.ToJson();

            var match = JToken.DeepEquals(built, golden);
            detail = match ? "" : $"built={built.ToString(Formatting.None)} golden={golden.ToString(Formatting.None)}";
            return match;
        }
    }
}
