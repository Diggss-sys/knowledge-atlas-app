using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KnowledgeAtlas.Seam
{
    /// <summary>
    /// Replays spec/fixtures/seam_messages.json through the envelope router and checks the contract:
    /// every "valid" message is accepted (not rejected), every "invalid" message is rejected with its
    /// named error code. Returns plain structs so the EditMode test needs no Newtonsoft reference.
    /// </summary>
    public static class SeamFixtureRunner
    {
        public struct Outcome
        {
            public string Name;
            public string Group;   // "valid" | "invalid"
            public bool Match;
            public string Detail;  // empty on match
        }

        public static List<Outcome> RunEnvelopeFixtures(string seamMessagesJson)
        {
            var doc = JObject.Parse(seamMessagesJson);
            var runtime = new BenignRuntime();
            var outcomes = new List<Outcome>();

            foreach (var c in (JArray)doc["valid"])
            {
                var name = (string)c["name"];
                var envelope = c["message"].ToString(Formatting.None);
                var ev = SeamEnvelope.Route(envelope, runtime);
                var rejected = ev != null && ev.Kind == SeamCodes.RuntimeError && SeamCodes.IsRejection(ev.Code);
                outcomes.Add(new Outcome
                {
                    Name = name,
                    Group = "valid",
                    Match = !rejected,
                    Detail = rejected ? $"unexpectedly rejected with '{ev.Code}'" : ""
                });
            }

            foreach (var c in (JArray)doc["invalid"])
            {
                var name = (string)c["name"];
                var expected = (string)c["expect_error_code"];
                var envelope = c["message"].ToString(Formatting.None);
                var ev = SeamEnvelope.Route(envelope, runtime);
                var got = ev != null && ev.Kind == SeamCodes.RuntimeError ? ev.Code : null;
                var match = got == expected;
                outcomes.Add(new Outcome
                {
                    Name = name,
                    Group = "invalid",
                    Match = match,
                    Detail = match ? "" : $"expected error '{expected}', got '{got ?? "(no error)"}'"
                });
            }

            return outcomes;
        }

        /// <summary>
        /// Compare one event produced by the runtime with its complete valid fixture message. Unlike
        /// envelope routing (which intentionally accepts event kinds as passthroughs), this pins every
        /// payload field and value emitted on the wire.
        /// </summary>
        public static Outcome MatchValidEventFixture(string seamMessagesJson, string fixtureName,
            SeamEvent actual)
        {
            var doc = JObject.Parse(seamMessagesJson);
            var entry = ((JArray)doc["valid"]).OfType<JObject>()
                .SingleOrDefault(c => (string)c["name"] == fixtureName);
            if (entry == null)
                return new Outcome
                {
                    Name = fixtureName,
                    Group = "valid",
                    Match = false,
                    Detail = "fixture entry not found",
                };

            var expected = (JObject)entry["message"];
            var got = JObject.Parse(actual.ToJson());
            var match = JToken.DeepEquals(expected, got);
            return new Outcome
            {
                Name = fixtureName,
                Group = "valid",
                Match = match,
                Detail = match ? "" : $"expected {expected.ToString(Formatting.None)}, got {got.ToString(Formatting.None)}",
            };
        }

        // A runtime that accepts everything with benign events — the envelope test exercises routing/
        // rejection, which happens BEFORE the runtime is called, so no real generator is needed.
        sealed class BenignRuntime : IRoomRuntime
        {
            public SeamEvent Apply(string canonicalSpecJson, string requestId) =>
                SeamEvent.SpecAppliedOk(requestId, 0, new string('0', 64));
            public SeamEvent LoadPair(string controlJson, string treatmentJson, string requestId) =>
                SeamEvent.PairResult(requestId, true, Enumerable.Empty<string>(), Enumerable.Empty<string>(), "control");
            public SeamEvent SwitchCondition(string condition, string transition, string requestId) =>
                SeamEvent.ConditionDone(requestId, condition, transition, 0);
            public SeamEvent SetCameraMode(string mode, string requestId) => null;
            public SeamEvent CaptureScreenshot(string requestId, int width, int height) =>
                SeamEvent.Screenshot(requestId);
            public SeamEvent Ready(string requestId) => SeamEvent.Ready(requestId);
        }
    }
}
