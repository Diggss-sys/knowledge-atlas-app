using System.Collections.Generic;
using System.Linq;
using KnowledgeAtlas.Seam;
using NUnit.Framework;
using RoomGen.Testing;

namespace RoomGen.Tests
{
    /// <summary>
    /// S0 — the mock-first rule made real. The MockSpecChannel is the operator UI's stand-in for the
    /// engine: it records every submission and raises contract events synchronously, so the view-model
    /// tests never touch the generator. These tests pin the behaviour the view-model relies on:
    /// calls are recorded verbatim, default events fire, and scripted failures replay in FIFO order.
    /// The public surface is plain types only — this assembly cannot reference Newtonsoft.
    /// </summary>
    public sealed class MockSpecChannelTests
    {
        static List<SeamEvent> Capture(MockSpecChannel ch)
        {
            var events = new List<SeamEvent>();
            ch.OnEvent += events.Add;
            return events;
        }

        [Test]
        public void Apply_records_the_call_and_raises_spec_applied_ok_by_default()
        {
            var ch = new MockSpecChannel();
            var events = Capture(ch);

            ch.Apply("{\"spec\":true}", "r-1");

            Assert.AreEqual(1, ch.Calls.Count, "the apply must be recorded");
            Assert.AreEqual(SeamCodes.ApplySpec, ch.Calls[0].Kind);
            Assert.AreEqual("r-1", ch.Calls[0].RequestId);
            Assert.AreEqual("{\"spec\":true}", ch.Calls[0].PayloadJson, "payload recorded verbatim");

            Assert.AreEqual(1, events.Count, "events dispatch synchronously");
            Assert.AreEqual(SeamCodes.SpecApplied, events[0].Kind);
            Assert.IsTrue(events[0].Ok);
            Assert.IsEmpty(events[0].Errors);
            Assert.AreEqual(64, events[0].SpecSha256.Length, "default ok carries a sha stub");
        }

        [Test]
        public void Scripted_apply_failures_replay_in_fifo_order_before_the_default()
        {
            var ch = new MockSpecChannel();
            var events = Capture(ch);

            ch.EnqueueApplyFailure("r-1", ("schema_invalid", "shell.ceiling_height_m", "above maximum"));
            ch.EnqueueApplyFailure("r-2", ("unknown_catalog_id", "furniture[0]", "no such id"));

            ch.Apply("{}", "r-1");
            ch.Apply("{}", "r-2");
            ch.Apply("{}", "r-3"); // queue drained -> default ok

            Assert.AreEqual(3, events.Count);

            Assert.IsFalse(events[0].Ok);
            Assert.AreEqual("schema_invalid", events[0].Errors[0].Code);
            Assert.AreEqual("shell.ceiling_height_m", events[0].Errors[0].Path);

            Assert.IsFalse(events[1].Ok, "second scripted failure replays after the first (FIFO)");
            Assert.AreEqual("unknown_catalog_id", events[1].Errors[0].Code);

            Assert.IsTrue(events[2].Ok, "with the queue drained, Apply falls back to the default ok");
        }

        [Test]
        public void Load_pair_records_both_specs_and_reports_the_single_variable_diff_by_default()
        {
            var ch = new MockSpecChannel();
            var events = Capture(ch);

            ch.LoadPair("{\"c\":1}", "{\"t\":2}", "r-9");

            var pairCalls = ch.Calls.Where(c => c.Kind == SeamCodes.LoadPair).ToList();
            Assert.AreEqual(2, pairCalls.Count, "both control and treatment JSON are recorded");
            Assert.AreEqual("{\"c\":1}", pairCalls[0].PayloadJson);
            Assert.AreEqual("{\"t\":2}", pairCalls[1].PayloadJson);

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(SeamCodes.PairLoaded, events[0].Kind);
            Assert.IsTrue(events[0].Ok);
            Assert.AreEqual(new[] { "shell.ceiling_height_m" }, events[0].PairDiffPaths.ToArray());
            Assert.AreEqual("control", events[0].ActiveCondition);
        }

        [Test]
        public void Scripted_pair_failure_surfaces_the_confound_codes()
        {
            var ch = new MockSpecChannel();
            var events = Capture(ch);

            ch.EnqueuePairFailure("r-c", new[] { "undeclared_change" }, new[] { "shell.ceiling_height_m", "surfaces.wall.material" });
            ch.LoadPair("{}", "{}", "r-c");

            Assert.IsFalse(events[0].Ok);
            Assert.Contains("undeclared_change", events[0].PairViolationCodes.ToList());
            Assert.AreEqual(2, events[0].PairDiffPaths.Count);
        }

        [Test]
        public void Scripted_pair_failure_surfaces_full_violations_and_notes()
        {
            var ch = new MockSpecChannel();
            var events = Capture(ch);
            var violations = new[]
            {
                new SeamError("undeclared_change", "surfaces.wall.material", "wall material differs"),
            };
            var notes = new[] { "coupled: shell.ceiling_height_m also changes room volume" };

            ch.EnqueuePairFailure("r-c", new[] { "undeclared_change" },
                new[] { "shell.ceiling_height_m", "surfaces.wall.material" }, violations, notes);
            ch.LoadPair("{}", "{}", "r-c");

            Assert.AreEqual(1, events[0].PairViolations.Count);
            Assert.AreEqual("surfaces.wall.material", events[0].PairViolations[0].Path);
            Assert.AreEqual("wall material differs", events[0].PairViolations[0].Message);
            CollectionAssert.AreEqual(notes, events[0].PairNotes);
        }

        [Test]
        public void Set_camera_mode_is_accepted_with_no_event_switch_and_screenshot_emit_one_each()
        {
            var ch = new MockSpecChannel();
            var events = Capture(ch);

            ch.SetCameraMode("walk", "r-1");           // accepted, no event
            ch.SwitchCondition("treatment", "fade", "r-2");
            ch.CaptureScreenshot("r-3");

            Assert.AreEqual(3, ch.Calls.Count, "all three calls are recorded");
            Assert.AreEqual(2, events.Count, "only switch_condition and capture_screenshot emit events");
            Assert.AreEqual(SeamCodes.ConditionSwitched, events[0].Kind);
            Assert.AreEqual("treatment", events[0].Condition);
            Assert.AreEqual(SeamCodes.ScreenshotCaptured, events[1].Kind);
        }
    }
}
