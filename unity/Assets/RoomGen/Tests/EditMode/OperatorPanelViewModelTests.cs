using System;
using System.Collections.Generic;
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

        sealed class DeferredChannel : ISpecChannel
        {
            public readonly List<string> ApplyRequestIds = new List<string>();
            public event Action<SeamEvent> OnEvent;

            public void Apply(string specJson, string requestId) => ApplyRequestIds.Add(requestId);
            public void LoadPair(string controlJson, string treatmentJson, string requestId) { }
            public void SwitchCondition(string condition, string transition, string requestId) { }
            public void SetCameraMode(string mode, string requestId) { }
            public void CaptureScreenshot(string requestId) { }
            public void Complete(SeamEvent ev) => OnEvent?.Invoke(ev);
        }

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

            ch.EnqueueApplyFailure("apply-1",
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
            ch.EnqueueApplyFailure("apply-1", ("schema_invalid", "shell.width_m", "bad"));
            vm.SetField("shell.width_m", 5.0);
            vm.Tick();
            Assert.IsNotEmpty(vm.Errors);

            vm.SetField("shell.width_m", 4.0);
            vm.Tick();
            Assert.AreEqual("applied", vm.Status);
            Assert.IsEmpty(vm.Errors);
        }

        [Test]
        public void Apply_status_moves_from_edited_to_applying_then_matching_result_only()
        {
            var ch = new DeferredChannel();
            var clk = new Clock { T = 1.0 };
            var vm = new OperatorPanelViewModel(ch, () => clk.T, debounceSeconds: 0.15);
            vm.LoadPreset(Preset);

            vm.SetField("shell.ceiling_height_m", 3.1);
            Assert.AreEqual("edited", vm.Status);
            Assert.IsFalse(vm.ApplyPending);
            Assert.IsTrue(vm.PreviewPending);

            clk.T = 1.2;
            Assert.IsTrue(vm.Tick());
            Assert.AreEqual("applying…", vm.Status);
            Assert.IsTrue(vm.ApplyPending);
            var requestId = ch.ApplyRequestIds.Single();

            ch.Complete(SeamEvent.SpecAppliedOk("older-apply", 2, "old", superseded: true));
            Assert.AreEqual("applying…", vm.Status, "an older response must not overwrite the current request");
            Assert.IsTrue(vm.ApplyPending, "an older response must not clear the current request");

            ch.Complete(SeamEvent.SpecAppliedOk(requestId, 3, "current"));
            Assert.AreEqual("applied", vm.Status);
            Assert.IsFalse(vm.ApplyPending);
            Assert.IsTrue(vm.PreviewPending, "engine acceptance does not prove the preview has repainted");

            vm.MarkPreviewRebuilt();
            Assert.IsFalse(vm.PreviewPending);
        }

        [Test]
        public void Newer_apply_ignores_late_result_and_matching_rejection_surfaces_verbatim()
        {
            var ch = new DeferredChannel();
            var clk = new Clock();
            var vm = new OperatorPanelViewModel(ch, () => clk.T, debounceSeconds: 0.0);
            vm.LoadPreset(Preset);

            vm.SetField("shell.width_m", 5.0);
            Assert.IsTrue(vm.Tick());
            var firstRequest = ch.ApplyRequestIds[0];

            vm.SetField("shell.width_m", 5.2);
            Assert.AreEqual("edited", vm.Status);
            Assert.IsFalse(vm.ApplyPending, "editing invalidates the outstanding request for the older model");
            Assert.IsTrue(vm.Tick());
            var secondRequest = ch.ApplyRequestIds[1];

            ch.Complete(SeamEvent.SpecAppliedOk(firstRequest, 4, "old", superseded: true));
            Assert.AreEqual("applying…", vm.Status);
            Assert.IsTrue(vm.ApplyPending);

            var message = "width cannot be built";
            ch.Complete(SeamEvent.SpecAppliedFail(secondRequest,
                new[] { new SeamError("schema_invalid", "shell.width_m", message) }, 5));
            Assert.AreEqual("rejected", vm.Status);
            Assert.IsFalse(vm.ApplyPending);
            Assert.AreEqual(message, vm.Errors.Single().Message);
        }

        [Test]
        public void Publish_is_disabled_until_a_passing_pair_validation_arrives()
        {
            var (vm, ch, _) = NewLoaded();

            Assert.IsFalse(vm.PublishEnabled, "no validation yet -> publish disabled");

            // Confounded pair first: publish stays disabled, confound codes surface.
            ch.EnqueuePairFailure("p", new[] { "shell.ceiling_height_m", "surfaces.wall.material" },
                new[] { new SeamError("undeclared_change", "surfaces.wall.material", "wall material differs") },
                new[] { "coupled: shell.ceiling_height_m also changes room volume" });
            vm.SetAsControl();
            vm.SubmitPair();
            Assert.IsFalse(vm.PublishEnabled, "a confounded pair can be edited but never published (DL-6)");
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.Fresh, vm.ValidationStatus);
            CollectionAssert.Contains(vm.Validation.ViolationCodes.ToList(), "undeclared_change");
            Assert.AreEqual("surfaces.wall.material", vm.Validation.Violations[0].Path);
            Assert.AreEqual("wall material differs", vm.Validation.Violations[0].Message,
                "the UI model must retain the gate message verbatim");
            Assert.AreEqual(1, vm.Validation.Notes.Count);

            // Clean single-variable pair: default mock result is ok -> publish flips on.
            vm.SubmitPair();
            Assert.IsTrue(vm.Validation.Ok);
            Assert.IsTrue(vm.PublishEnabled, "PublishEnabled requires a fresh passing verdict");
        }

        [Test]
        public void Editing_after_a_passing_validation_makes_the_verdict_stale_until_revalidated()
        {
            var (vm, _, _) = NewLoaded();
            vm.SetAsControl();
            vm.SetField("shell.ceiling_height_m", 3.2);
            vm.SubmitPair();
            Assert.IsTrue(vm.PublishEnabled);

            vm.SetField("shell.ceiling_height_m", 3.1);
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.Stale, vm.ValidationStatus);
            Assert.IsFalse(vm.Validation.Ok, "stale state must not retain a pass for future consumers");
            Assert.IsEmpty(vm.Validation.Violations, "stale state must not retain old violation details");
            Assert.IsEmpty(vm.Validation.Notes, "stale state must not retain old notes");
            Assert.IsFalse(vm.PublishEnabled, "a passing verdict cannot authorize edited specs");

            vm.SubmitPair();
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.Fresh, vm.ValidationStatus);
            Assert.IsTrue(vm.PublishEnabled, "a new passing gate result restores publishing");
        }

        [Test]
        public void Changing_the_declaration_or_refreezing_control_stales_a_fresh_verdict()
        {
            var (vm, ch, _) = NewLoaded();
            vm.SetAsControl();
            vm.SetField("shell.ceiling_height_m", 3.2);
            vm.SubmitPair();
            Assert.IsTrue(vm.PublishEnabled);

            vm.DeclaredVariable = "lighting.warmth";
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.Stale, vm.ValidationStatus);
            Assert.IsFalse(vm.PublishEnabled);

            ch.EnqueuePairFailure("p", new[] { "shell.ceiling_height_m" },
                new[]
                {
                    new SeamError("undeclared_change", "shell.ceiling_height_m", "ceiling differs but was not declared"),
                    new SeamError("declared_unchanged", "lighting.warmth", "declared but unchanged"),
                },
                new[] { "coupled: lighting.warmth perceived brightness shifts with color temperature" });
            vm.SubmitPair();
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.Fresh, vm.ValidationStatus,
                "even a failing result is fresh for the current pair");
            Assert.IsFalse(vm.PublishEnabled, "the new declaration does not cover the ceiling diff");

            vm.SetAsControl();
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.Stale, vm.ValidationStatus);
            Assert.IsFalse(vm.PublishEnabled);
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

        [Test]
        public void Freeze_edit_unfreeze_preserves_the_live_treatment_and_undo_restores_the_frozen_pair()
        {
            var (vm, _, _) = NewLoaded();

            vm.SetAsControl();
            var frozenControl = vm.ControlSpecJson;
            vm.SetField("shell.ceiling_height_m", 3.4);
            var treatment = vm.CurrentSpecJson;

            vm.Unfreeze();

            Assert.IsFalse(vm.HasControl, "unfreeze returns to one live room");
            Assert.AreEqual(OperatorPanelViewModel.PairWorkflowState.ControlUnfrozen, vm.WorkflowState);
            Assert.AreEqual(treatment, vm.CurrentSpecJson, "unfreeze must not discard treatment edits");

            vm.Undo();

            Assert.IsTrue(vm.HasControl, "undo across unfreeze restores the frozen baseline");
            Assert.AreEqual(frozenControl, vm.ControlSpecJson);
            Assert.AreEqual(treatment, vm.CurrentSpecJson);
            Assert.AreEqual(OperatorPanelViewModel.PairWorkflowState.ControlFrozen, vm.WorkflowState);
        }

        [Test]
        public void Undo_across_freeze_restores_the_pre_freeze_single_room_and_reapplies_it()
        {
            var (vm, ch, _) = NewLoaded(debounce: 0.0);
            vm.SetField("shell.ceiling_height_m", 3.1);
            var preFreeze = vm.CurrentSpecJson;

            vm.SetAsControl();
            Assert.IsTrue(vm.HasControl);

            vm.Undo();

            Assert.IsFalse(vm.HasControl);
            Assert.AreEqual(preFreeze, vm.CurrentSpecJson);
            Assert.AreEqual(OperatorPanelViewModel.PairWorkflowState.SingleRoom, vm.WorkflowState);
            Assert.IsTrue(vm.Tick(), "undo marks the restored room dirty so the seam follows it");
            StringAssert.Contains("3.1", ch.Calls.Last(c => c.Kind == SeamCodes.ApplySpec).PayloadJson);
        }

        [Test]
        public void Reset_to_preset_defaults_is_destructive_but_undoable()
        {
            var (vm, _, _) = NewLoaded();
            vm.SetAsControl();
            vm.SetField("shell.ceiling_height_m", 3.4);
            vm.SubmitPair();
            Assert.IsTrue(vm.PublishEnabled, "pre-reset pair has a fresh passing verdict");
            var editedTreatment = vm.CurrentSpecJson;
            var frozenControl = vm.ControlSpecJson;

            vm.ResetToPreset();

            Assert.IsFalse(vm.HasControl);
            Assert.AreEqual(2.8, vm.GetField("shell.ceiling_height_m"), 1e-9);
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.None, vm.ValidationStatus);
            Assert.AreEqual(OperatorPanelViewModel.PairWorkflowState.SingleRoom, vm.WorkflowState);
            Assert.IsTrue(vm.PreviewPending,
                "reset changes the model before the preview panes have rebuilt");

            vm.Undo();

            Assert.AreEqual(editedTreatment, vm.CurrentSpecJson);
            Assert.AreEqual(frozenControl, vm.ControlSpecJson);
            Assert.AreEqual(OperatorPanelViewModel.PairWorkflowState.ControlFrozen, vm.WorkflowState);
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.Fresh, vm.ValidationStatus,
                "undo restores the validation freshness captured with the exact pair");
            Assert.IsTrue(vm.Validation.Ok);
            Assert.IsTrue(vm.PublishEnabled);
        }

        [Test]
        public void Rapid_slider_frames_create_one_undo_point_at_the_debounced_tick()
        {
            var (vm, _, clk) = NewLoaded(debounce: 0.15);

            clk.T = 1.0;
            vm.SetField("shell.ceiling_height_m", 3.0);
            clk.T = 1.05;
            vm.SetField("shell.ceiling_height_m", 3.2);
            clk.T = 1.25; // deliberately beyond the floating-point debounce boundary
            Assert.IsTrue(vm.Tick());

            vm.Undo();

            Assert.AreEqual(2.8, vm.GetField("shell.ceiling_height_m"), 1e-9,
                "one undo reverses the whole drag stream, not just its last frame");
            Assert.IsFalse(vm.CanUndo, "the drag emitted exactly one history entry");
        }

        [Test]
        public void Editing_again_before_an_undo_reapply_flushes_keeps_a_new_undo_point()
        {
            var (vm, _, _) = NewLoaded(debounce: 0.0);
            vm.SetField("shell.ceiling_height_m", 3.2);
            vm.Tick();

            vm.Undo(); // restores 2.8 and marks it dirty for reapply
            vm.SetField("shell.ceiling_height_m", 3.0); // arrives before that reapply Tick
            vm.Tick();
            vm.Undo();

            Assert.AreEqual(2.8, vm.GetField("shell.ceiling_height_m"), 1e-9,
                "the post-undo edit must capture the restored state even while an Apply is pending");
        }

        [Test]
        public void Setting_a_field_to_its_current_value_is_not_an_edit_or_an_undo_point()
        {
            var (vm, _, _) = NewLoaded(debounce: 0.0);

            vm.SetField("shell.ceiling_height_m", 2.8f); // the widget sends a float promoted to double

            Assert.IsFalse(vm.CanUndo);
            Assert.IsFalse(vm.Tick(), "a no-op widget event must not send an Apply");
            Assert.AreEqual(OperatorPanelViewModel.ValidationFreshness.None, vm.ValidationStatus);
        }

        [Test]
        public void Undo_restores_status_and_errors_with_the_model_snapshot()
        {
            var (vm, ch, _) = NewLoaded(debounce: 0.0);

            vm.SetField("shell.ceiling_height_m", 3.0);
            vm.Tick();
            Assert.AreEqual("applied", vm.Status);

            ch.EnqueueApplyFailure("apply-2", ("schema_invalid", "shell.ceiling_height_m", "rejected value"));
            vm.SetField("shell.ceiling_height_m", 3.2);
            vm.Tick();
            Assert.AreEqual("rejected", vm.Status);
            Assert.IsNotEmpty(vm.Errors);

            vm.Undo();

            Assert.AreEqual(3.0, vm.GetField("shell.ceiling_height_m"), 1e-9);
            Assert.AreEqual("applied", vm.Status);
            Assert.IsEmpty(vm.Errors, "the restored successful model must not retain a later apply error");
        }

        [Test]
        public void Undo_history_is_bounded_to_twenty_entries()
        {
            var (vm, _, _) = NewLoaded(debounce: 0.0);

            for (var i = 0; i < 25; i++)
            {
                vm.SetField("shell.width_m", 3.5 + i * 0.1);
                Assert.IsTrue(vm.Tick());
            }

            var undoCount = 0;
            while (vm.CanUndo)
            {
                vm.Undo();
                undoCount++;
                Assert.LessOrEqual(undoCount, 20, "history must never grow beyond its ring bound");
            }

            Assert.AreEqual(20, undoCount);
        }
    }
}
