using System.Linq;
using KnowledgeAtlas.Seam;
using NUnit.Framework;
using RoomGen.Testing;
using RoomGen.UI;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// S1 — the single-room editor's brain (OperatorPanelViewModel). Driven entirely against the
    /// MockSpecChannel with an injected clock, these tests pin the behaviours the handoff makes
    /// load-bearing: debounce collapses a slider stream to one Apply, values clamp to the preset's
    /// ranges, engine errors surface verbatim, publish is gated on pair validation (DL-6), and the
    /// pair workflow sends both the frozen control and the live treatment.
    /// </summary>
    public sealed class OperatorPanelViewModelTests
    {
        static string Preset => Resources.Load<TextAsset>("RoomGen/dining_room.preset").text;

        // A driftable clock so debounce is testable without waiting.
        sealed class Clock { public double T; }

        static (OperatorPanelViewModel vm, MockSpecChannel ch, Clock clk) NewLoaded(double debounce = 0.15)
        {
            var ch = new MockSpecChannel();
            var clk = new Clock();
            var vm = new OperatorPanelViewModel(ch, () => clk.T, debounce);
            vm.LoadPreset(Preset);
            return (vm, ch, clk);
        }

        [Test]
        public void LoadPreset_exposes_range_fields_with_labels_and_manipulable_flags()
        {
            var (vm, _, _) = NewLoaded();

            var ceiling = vm.Fields.Single(f => f.Path == "shell.ceiling_height_m");
            Assert.AreEqual(2.4, ceiling.Min, 1e-9);
            Assert.AreEqual(3.4, ceiling.Max, 1e-9);
            Assert.AreEqual(2.8, ceiling.Value, 1e-9, "value seeded from preset defaults");
            Assert.AreEqual("ceiling height m", ceiling.Label, "underscores -> spaces, last path segment");
            Assert.IsTrue(ceiling.IsManipulable, "shell.ceiling_height_m is in manipulable_variables");

            var width = vm.Fields.Single(f => f.Path == "shell.width_m");
            Assert.IsFalse(width.IsManipulable, "shell.width_m is a range field but NOT manipulable");

            CollectionAssert.Contains(vm.ManipulableVariables.ToList(), "lighting.warmth");
        }

        [Test]
        public void SetField_clamps_to_the_preset_range()
        {
            var (vm, _, _) = NewLoaded();

            vm.SetField("shell.ceiling_height_m", 99.0);
            Assert.AreEqual(3.4, vm.GetField("shell.ceiling_height_m"), 1e-9, "above-max clamps to max");

            vm.SetField("shell.ceiling_height_m", 0.0);
            Assert.AreEqual(2.4, vm.GetField("shell.ceiling_height_m"), 1e-9, "below-min clamps to min");

            vm.SetField("shell.ceiling_height_m", 3.0);
            Assert.AreEqual(3.0, vm.GetField("shell.ceiling_height_m"), 1e-9, "in-range unchanged");
        }

        [Test]
        public void Two_rapid_edits_debounce_into_a_single_apply_after_the_window()
        {
            var (vm, ch, clk) = NewLoaded(0.15);

            clk.T = 10.0;
            vm.SetField("shell.ceiling_height_m", 3.0);
            clk.T = 10.05;
            vm.SetField("shell.ceiling_height_m", 3.1); // second edit resets the window

            Assert.IsFalse(vm.Tick(), "still inside the debounce window -> no Apply");
            Assert.AreEqual(0, ch.Calls.Count(c => c.Kind == SeamCodes.ApplySpec));

            clk.T = 10.05 + 0.15; // window elapsed since the LAST edit
            Assert.IsTrue(vm.Tick(), "one Apply fires after the window");
            Assert.AreEqual(1, ch.Calls.Count(c => c.Kind == SeamCodes.ApplySpec),
                "the two rapid edits collapse to exactly one channel Apply");

            Assert.IsFalse(vm.Tick(), "nothing dirty -> no further Apply");
        }

        [Test]
        public void Engine_errors_surface_verbatim_on_a_rejected_apply()
        {
            var (vm, ch, clk) = NewLoaded(0.0);

            ch.EnqueueApplyFailure("ignored",
                ("schema_invalid", "shell.ceiling_height_m", "9.9 is above maximum 3.4"));

            vm.SetField("shell.ceiling_height_m", 3.4);
            Assert.IsTrue(vm.Tick());

            Assert.AreEqual("rejected", vm.Status);
            Assert.AreEqual(1, vm.Errors.Count);
            Assert.AreEqual("schema_invalid", vm.Errors[0].Code);
            Assert.AreEqual("shell.ceiling_height_m", vm.Errors[0].Path);
            Assert.AreEqual("9.9 is above maximum 3.4", vm.Errors[0].Message, "message rendered verbatim, never re-worded");
        }

        [Test]
        public void A_successful_apply_clears_errors_and_sets_applied_status()
        {
            var (vm, ch, clk) = NewLoaded(0.0);

            // First a failure, then a success — proves errors clear.
            ch.EnqueueApplyFailure("x", ("schema_invalid", "shell.width_m", "bad"));
            vm.SetField("shell.width_m", 5.0);
            vm.Tick();
            Assert.IsNotEmpty(vm.Errors);

            vm.SetField("shell.width_m", 4.0);
            vm.Tick();
            Assert.AreEqual("applied", vm.Status);
            Assert.IsEmpty(vm.Errors);
        }

        [Test]
        public void Publish_is_disabled_until_a_passing_pair_validation_arrives()
        {
            var (vm, ch, _) = NewLoaded();

            Assert.IsFalse(vm.PublishEnabled, "no validation yet -> publish disabled");

            // Confounded pair first: publish stays disabled, confound codes surface.
            ch.EnqueuePairFailure("p", new[] { "undeclared_change" }, new[] { "shell.ceiling_height_m", "surfaces.wall.material" });
            vm.SetAsControl();
            vm.SubmitPair();
            Assert.IsFalse(vm.PublishEnabled, "a confounded pair can be edited but never published (DL-6)");
            CollectionAssert.Contains(vm.Validation.ViolationCodes.ToList(), "undeclared_change");

            // Clean single-variable pair: default mock result is ok -> publish flips on.
            vm.SubmitPair();
            Assert.IsTrue(vm.Validation.Ok);
            Assert.IsTrue(vm.PublishEnabled, "PublishEnabled tracks Validation.Ok");
        }

        [Test]
        public void SetAsControl_then_SubmitPair_sends_both_the_frozen_control_and_the_live_treatment()
        {
            var (vm, ch, _) = NewLoaded();

            vm.SetField("shell.ceiling_height_m", 2.8);
            vm.SetAsControl();
            Assert.IsTrue(vm.HasControl);

            // Edit the treatment AFTER freezing the control.
            vm.SetField("shell.ceiling_height_m", 3.4);
            vm.SubmitPair();

            var pairCalls = ch.Calls.Where(c => c.Kind == SeamCodes.LoadPair).ToList();
            Assert.AreEqual(2, pairCalls.Count, "SubmitPair records control + treatment");
            StringAssert.Contains("2.8", pairCalls[0].PayloadJson, "control snapshot frozen at 2.8");
            StringAssert.Contains("3.4", pairCalls[1].PayloadJson, "treatment reflects the post-freeze edit");
        }
    }
}
