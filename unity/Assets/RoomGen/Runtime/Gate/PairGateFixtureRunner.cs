using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RoomGen.Gate
{
    /// <summary>
    /// Replays spec/fixtures/diff_vectors.json through <see cref="PairGate"/> and compares each case's
    /// four expected fields (ok / violation_codes / diff_paths / notes_count) to the golden block.
    /// Returns plain string/bool structs so the EditMode test assembly can assert without a direct
    /// Newtonsoft reference (the test asmdef cannot see Newtonsoft — see RoomSpecAdapterTests history).
    /// </summary>
    public static class PairGateFixtureRunner
    {
        public struct CaseOutcome
        {
            public string Name;
            public bool Match;
            public string Detail; // human-readable expected-vs-got on mismatch, empty on match
        }

        public static List<CaseOutcome> RunGoldenVectors(string diffVectorsJson, string schemaJson)
        {
            var schema = JObject.Parse(schemaJson);
            var fixture = JObject.Parse(diffVectorsJson);
            var cases = (JArray)fixture["cases"];
            var outcomes = new List<CaseOutcome>();

            foreach (var token in cases.OfType<JObject>())
            {
                var name = (string)token["name"];
                var specA = (JObject)token["spec_a"];
                var specB = (JObject)token["spec_b"];
                var expected = (JObject)token["expected"];

                var expOk = (bool)expected["ok"];
                var expCodes = ((JArray)expected["violation_codes"]).Select(t => (string)t).ToList();
                var expDiff = ((JArray)expected["diff_paths"]).Select(t => (string)t).ToList();
                var expNotes = (int)expected["notes_count"];

                var r = PairGate.Validate(specA, specB, schema);

                var mismatches = new List<string>();
                if (r.Ok != expOk) mismatches.Add($"ok: expected {expOk}, got {r.Ok}");
                if (!r.ViolationCodes.SequenceEqual(expCodes))
                    mismatches.Add($"codes: expected [{string.Join(",", expCodes)}], got [{string.Join(",", r.ViolationCodes)}]");
                if (!r.DiffPaths.SequenceEqual(expDiff))
                    mismatches.Add($"diff_paths: expected [{string.Join(",", expDiff)}], got [{string.Join(",", r.DiffPaths)}]");
                if (r.NotesCount != expNotes)
                    mismatches.Add($"notes_count: expected {expNotes}, got {r.NotesCount}");

                outcomes.Add(new CaseOutcome
                {
                    Name = name,
                    Match = mismatches.Count == 0,
                    Detail = string.Join(" | ", mismatches)
                });
            }
            return outcomes;
        }
    }
}
