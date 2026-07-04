using System.Collections.Generic;
using System.IO;
using System.Linq;
using KnowledgeAtlas.Seam;
using NUnit.Framework;
using RoomGen.Generation;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// G4 — the runtime seam (ENGINE_SEAM.md, seam_version 1). Proves: the seam_messages fixtures
    /// route correctly (valid accepted, invalid rejected with their named codes); apply_spec of a
    /// valid spec emits spec_applied{ok:true}; a failed apply keeps the last good room (atomic apply);
    /// load_pair runs the G3 gate; and the channel writes a JSONL provenance log.
    /// </summary>
    public sealed class SeamTests
    {
        readonly List<GameObject> cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in cleanup) if (go != null) Object.DestroyImmediate(go);
            cleanup.Clear();
        }

        static string Schema => Resources.Load<TextAsset>("RoomGen/room_spec.schema").text;
        static string SeamMessages => Resources.Load<TextAsset>("RoomGen/Fixtures/seam_messages").text;

        RoomRuntime NewRuntime()
        {
            var go = new GameObject("Runtime Generator");
            cleanup.Add(go);
            var generator = go.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(0);
            return new RoomRuntime(generator, Schema);
        }

        // A minimal, schema-valid canonical spec (matches the apply_spec_minimal fixture shape).
        const string ValidSpec = @"{
            ""spec_version"":""1.0"",""room_type"":""dining_room"",
            ""shell"":{""width_m"":4.5,""length_m"":6.0,""ceiling_height_m"":2.8,""contour"":""angular""},
            ""surfaces"":{""wall"":{""material"":""plaster""},""floor"":{""material"":""wood""}},
            ""lighting"":{""preset"":""warm_evening""}
        }";

        // Same spec but the ceiling exceeds the schema maximum (4.5 m) -> schema_invalid.
        const string OutOfRangeSpec = @"{
            ""spec_version"":""1.0"",""room_type"":""dining_room"",
            ""shell"":{""width_m"":4.5,""length_m"":6.0,""ceiling_height_m"":9.9,""contour"":""angular""},
            ""surfaces"":{""wall"":{""material"":""plaster""},""floor"":{""material"":""wood""}},
            ""lighting"":{""preset"":""warm_evening""}
        }";

        [Test]
        public void Seam_message_fixtures_route_as_specified()
        {
            var outcomes = SeamFixtureRunner.RunEnvelopeFixtures(SeamMessages);
            Assert.IsNotEmpty(outcomes);
            var failures = outcomes.Where(o => !o.Match).Select(o => $"  [{o.Group}] {o.Name}: {o.Detail}").ToList();
            Assert.IsEmpty(failures, "seam envelope routing diverged from the contract:\n" + string.Join("\n", failures));
        }

        [Test]
        public void Apply_valid_spec_emits_spec_applied_ok_with_sha_and_build_ms()
        {
            var runtime = NewRuntime();
            var ev = runtime.Apply(ValidSpec, "r-1");

            Assert.AreEqual(SeamCodes.SpecApplied, ev.Kind);
            Assert.IsTrue(ev.Ok, "errors: " + string.Join(", ", ev.Errors.Select(e => e.Code + " " + e.Message)));
            Assert.IsEmpty(ev.Errors);
            Assert.AreEqual(64, ev.SpecSha256.Length, "spec_sha256 should be a hex SHA-256");
            Assert.GreaterOrEqual(ev.BuildMs, 0);
        }

        [Test]
        public void Failed_apply_keeps_the_last_good_room()
        {
            var runtime = NewRuntime();

            var ok = runtime.Apply(ValidSpec, "r-1");
            Assert.IsTrue(ok.Ok);
            var goodSha = ok.SpecSha256;

            var bad = runtime.Apply(OutOfRangeSpec, "r-2");
            Assert.IsFalse(bad.Ok, "a ceiling of 9.9 m must be rejected");
            Assert.Contains(SeamCodes.SchemaInvalid, bad.ErrorCodes);

            // Atomic apply: the previous good room is still current, untouched.
            Assert.AreEqual(goodSha, runtime.LastGoodSha, "the last good spec must be unchanged after a failed apply");
        }

        [Test]
        public void Load_pair_runs_the_gate_and_reports_the_single_variable_diff()
        {
            var control = Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-control").text;
            var treatment = Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-treatment").text;

            var ev = NewRuntime().LoadPair(control, treatment, "r-3");

            Assert.AreEqual(SeamCodes.PairLoaded, ev.Kind);
            Assert.IsTrue(ev.Ok, "the canonical ceiling pair is a valid single-variable pair; gate codes: "
                + string.Join(", ", ev.PairViolationCodes));
            Assert.AreEqual(new[] { "shell.ceiling_height_m" }, ev.PairDiffPaths.ToArray());
            Assert.AreEqual("control", ev.ActiveCondition);
        }

        [Test]
        public void Local_channel_writes_a_jsonl_provenance_log_and_dispatches_events()
        {
            var logPath = Path.Combine(Application.temporaryCachePath, "seam-test-session.jsonl");
            if (File.Exists(logPath)) File.Delete(logPath);

            var received = new List<SeamEvent>();
            var channel = new LocalChannel(NewRuntime(), logPath);
            channel.OnEvent += received.Add;

            channel.Apply(ValidSpec, "r-1");
            channel.SetCameraMode("walk", "r-2"); // accepted, emits no event

            Assert.AreEqual(1, received.Count, "apply emits one event; set_camera_mode emits none");
            Assert.AreEqual(SeamCodes.SpecApplied, received[0].Kind);

            var lines = File.ReadAllLines(logPath).Where(l => l.Trim().Length > 0).ToArray();
            // apply: call + event (2) ; set_camera_mode: call only (1) = 3 lines.
            Assert.AreEqual(3, lines.Length, "provenance log should record every call and event");
            Assert.IsTrue(lines[0].Contains("\"kind\":\"apply_spec\""));
            Assert.IsTrue(lines[1].Contains("\"kind\":\"spec_applied\""));
            Assert.IsTrue(lines[2].Contains("\"kind\":\"set_camera_mode\""));
        }
    }
}
