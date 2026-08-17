using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.UI
{
    /// <summary>
    /// The dumb binder (S2): wires the OperatorPanel.uxml controls to an OperatorPanelViewModel and
    /// pumps the debounce each frame. Workflow correctness lives in the view-model; the only UI-local
    /// state is which destructive action is awaiting its second confirmation click. The wiring itself
    /// is a static method (<see cref="Bind"/>) taking a plain
    /// VisualElement + view-model, so an EditMode test can exercise it against an instantiated UXML
    /// tree with no GameObject/UIDocument and prove a slider change reaches the channel.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OperatorPanelController : MonoBehaviour
    {
        const string UndoLabel = "Undo last change";
        const string UnfreezeLabel = "Unfreeze control";
        const string ResetLabel = "Reset to preset";
        const string ArmedUnfreezeLabel = "Click again: discard frozen baseline (keep treatment)";
        const string ArmedResetLabel = "Click again: discard all room and pair edits";

        public enum ArmedPairAction
        {
            None,
            Unfreeze,
            Reset,
        }

        /// <summary>
        /// Plain, testable two-step confirmation state. The first click arms; a matching second click
        /// executes. A different or unrelated interaction disarms it.
        /// </summary>
        public sealed class PairActionConfirmation
        {
            public ArmedPairAction ArmedAction { get; private set; }

            public bool Confirm(ArmedPairAction action)
            {
                if (action == ArmedPairAction.None) throw new ArgumentOutOfRangeException(nameof(action));
                if (ArmedAction == action)
                {
                    ArmedAction = ArmedPairAction.None;
                    return true;
                }

                ArmedAction = action;
                return false;
            }

            public void Disarm() => ArmedAction = ArmedPairAction.None;
        }

        OperatorPanelViewModel _vm;
        VisualElement _root;

        /// <summary>Inject the view-model (real LocalChannel in the studio, mock in tests) and bind.</summary>
        public void Initialize(OperatorPanelViewModel vm)
        {
            _vm = vm;
            _root = GetComponent<UIDocument>().rootVisualElement;
            Bind(_root, _vm);
        }

        void Update()
        {
            if (_vm == null || _root == null) return;
            if (_vm.Tick()) Refresh(_root, _vm); // an Apply landed -> reflect status/errors
            Refresh(_root, _vm);                 // validation may change without a local Apply
        }

        /// <summary>
        /// Register control -> view-model wiring on an already-built tree. Sliders are matched by name
        /// (== their canonical dotted path); each change routes to SetField. The declared-variable
        /// dropdown is populated from the preset's manipulable_variables; the publish button reflects
        /// PublishEnabled. Safe to call headlessly — no rendering required.
        /// </summary>
        public static void Bind(VisualElement root, OperatorPanelViewModel vm, Action roomStateChanged = null)
        {
            var confirmation = new PairActionConfirmation();
            var wired = new HashSet<string>();
            foreach (var field in vm.Fields)
            {
                var slider = root.Q<Slider>(field.Path);
                if (slider == null) continue;
                slider.lowValue = (float)field.Min;
                slider.highValue = (float)field.Max;
                slider.SetValueWithoutNotify((float)field.Value);
                var path = field.Path;
                slider.RegisterValueChangedCallback(evt =>
                {
                    confirmation.Disarm();
                    UpdateConfirmationLabels(root, confirmation);
                    vm.SetField(path, evt.newValue);
                });
                SliderFill.Attach(slider); // site-style value fill (visual only)
                wired.Add(field.Path);
            }

            // Sliders in the UXML with no matching preset range (today: lighting.warmth / .intensity)
            // would drag freely while doing nothing — a silent no-op control misleads the operator, so
            // they are disabled until the preset grows their ranges (a contract change; Diego's call).
            root.Query<Slider>().ForEach(s => { if (!wired.Contains(s.name)) s.SetEnabled(false); });

            var dropdown = root.Q<DropdownField>("declared-variable");
            if (dropdown != null)
            {
                // Offer only declarable variables the panel can actually change today (the preset's
                // manipulable list ∩ its wired ranges): declaring a variable with no wired control can
                // only ever produce a declared_unchanged red verdict — a dead end for the operator.
                // The rest return as the preset gains ranges (the UXML hint says so).
                dropdown.choices = new List<string>();
                foreach (var v in vm.ManipulableVariables)
                    if (wired.Contains(v)) dropdown.choices.Add(v);
                if (dropdown.choices.Count > 0)
                {
                    dropdown.index = 0;
                    vm.DeclaredVariable = dropdown.choices[0];
                }
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    confirmation.Disarm();
                    UpdateConfirmationLabels(root, confirmation);
                    vm.DeclaredVariable = evt.newValue;
                });
            }

            BindPairActions(root, vm, confirmation, roomStateChanged);

            Refresh(root, vm);
        }

        static void BindPairActions(
            VisualElement root,
            OperatorPanelViewModel vm,
            PairActionConfirmation confirmation,
            Action roomStateChanged)
        {
            var undo = root.Q<Button>("undo-button");
            var unfreeze = root.Q<Button>("unfreeze-button");
            var reset = root.Q<Button>("reset-button");

            var setControl = root.Q<Button>("set-control-button");
            if (setControl != null) setControl.clicked += () =>
            {
                confirmation.Disarm();
                vm.SetAsControl();
                roomStateChanged?.Invoke();
                Refresh(root, vm);
                UpdateConfirmationLabels(root, confirmation);
            };

            var submit = root.Q<Button>("submit-pair-button");
            if (submit != null) submit.clicked += () =>
            {
                confirmation.Disarm();
                vm.SubmitPair();
                Refresh(root, vm);
                UpdateConfirmationLabels(root, confirmation);
            };

            if (undo != null) undo.clicked += () =>
            {
                confirmation.Disarm();
                vm.Undo();
                roomStateChanged?.Invoke();
                Refresh(root, vm);
                UpdateConfirmationLabels(root, confirmation);
            };

            if (unfreeze != null) unfreeze.clicked += () =>
            {
                if (confirmation.Confirm(ArmedPairAction.Unfreeze))
                {
                    vm.Unfreeze();
                    roomStateChanged?.Invoke();
                }
                Refresh(root, vm);
                UpdateConfirmationLabels(root, confirmation);
            };

            if (reset != null) reset.clicked += () =>
            {
                if (confirmation.Confirm(ArmedPairAction.Reset))
                {
                    vm.ResetToPreset();
                    roomStateChanged?.Invoke();
                }
                Refresh(root, vm);
                UpdateConfirmationLabels(root, confirmation);
            };

            // Pointer-down runs before a button's click callback. Ignore the two confirmation buttons,
            // but let every other click (blank space, slider, dropdown, another action) cancel an arm.
            root.RegisterCallback<PointerDownEvent>(evt =>
            {
                var target = evt.target as VisualElement;
                if (IsInside(target, unfreeze) || IsInside(target, reset)) return;
                confirmation.Disarm();
                UpdateConfirmationLabels(root, confirmation);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(evt =>
            {
                var target = evt.target as VisualElement;
                if (IsInside(target, unfreeze) || IsInside(target, reset)) return;
                confirmation.Disarm();
                UpdateConfirmationLabels(root, confirmation);
            });

            UpdateConfirmationLabels(root, confirmation);
        }

        static bool IsInside(VisualElement element, VisualElement ancestor)
        {
            if (element == null || ancestor == null) return false;
            for (var current = element; current != null; current = current.parent)
                if (ReferenceEquals(current, ancestor)) return true;
            return false;
        }

        static void UpdateConfirmationLabels(VisualElement root, PairActionConfirmation confirmation)
        {
            var undo = root.Q<Button>("undo-button");
            if (undo != null) undo.text = UndoLabel;
            var unfreeze = root.Q<Button>("unfreeze-button");
            if (unfreeze != null)
                unfreeze.text = confirmation.ArmedAction == ArmedPairAction.Unfreeze
                    ? ArmedUnfreezeLabel
                    : UnfreezeLabel;
            var reset = root.Q<Button>("reset-button");
            if (reset != null)
                reset.text = confirmation.ArmedAction == ArmedPairAction.Reset
                    ? ArmedResetLabel
                    : ResetLabel;
        }

        /// <summary>Reflect view-model observable state back onto the controls (status, diff, publish gate).</summary>
        public static void Refresh(VisualElement root, OperatorPanelViewModel vm)
        {
            // Which room do the sliders shape right now? Before Set-as-control everything is the
            // control-to-be (both previews mirror it); after, the control is frozen and edits flow to
            // the treatment. The operator kept losing track of this — say it, loudly, at the top.
            var banner = root.Q<Label>("editing-status");

            // Undo/reset mutate the model without emitting slider ChangeEvents. Keep every widget in
            // lockstep with the restored FieldSpec values without feeding those writes back into the VM.
            foreach (var field in vm.Fields)
            {
                var slider = root.Q<Slider>(field.Path);
                if (slider == null) continue;
                slider.SetValueWithoutNotify((float)field.Value);
                SliderFill.Refresh(slider);
            }

            if (banner != null)
            {
                switch (vm.WorkflowState)
                {
                    case OperatorPanelViewModel.PairWorkflowState.ControlFrozen:
                        banner.text = "CONTROL FROZEN — editing: TREATMENT. Change exactly one thing, then Validate pair.";
                        break;
                    case OperatorPanelViewModel.PairWorkflowState.ControlUnfrozen:
                        banner.text = "CONTROL UNFROZEN — you are editing a single room again.";
                        break;
                    default:
                        banner.text = "EDITING: CONTROL — shape this room, then press 'Set as control' to freeze the baseline.";
                        break;
                }
                banner.EnableInClassList("ka-banner--treatment", vm.HasControl);
            }

            var controlCaption = root.Q<Label>("control-caption");
            if (controlCaption != null)
                controlCaption.text = vm.HasControl ? "Control — frozen baseline" : "Control — editing now";
            var treatmentCaption = root.Q<Label>("treatment-caption");
            if (treatmentCaption != null)
                treatmentCaption.text = vm.HasControl ? "Treatment — editing now" : "Treatment — starts as a copy of the control";

            var applyStatus = root.Q<Label>("apply-status");
            if (applyStatus != null)
            {
                applyStatus.text = vm.Errors.Count > 0
                    ? string.Join("\n", ErrorLines(vm))
                    : vm.Status;
                applyStatus.EnableInClassList("ka-status--error", vm.Errors.Count > 0);
                applyStatus.EnableInClassList("ka-status--ok", vm.Errors.Count == 0 && vm.Status == "applied");
            }

            var validationStatus = root.Q<Label>("validation-status");
            var diffList = root.Q<VisualElement>("diff-list");
            if (validationStatus != null && vm.ValidationStatus == OperatorPanelViewModel.ValidationFreshness.Stale)
            {
                validationStatus.text = "edited since validation — re-validate";
                validationStatus.EnableInClassList("ka-status--ok", false);
                validationStatus.EnableInClassList("ka-status--error", true);
            }
            else if (validationStatus != null && vm.Validation.ActiveCondition != null)
            {
                validationStatus.text = vm.Validation.Ok
                    ? "differs only in: " + string.Join(", ", vm.Validation.DiffPaths)
                    : "confounded: " + string.Join(", ", vm.Validation.ViolationCodes);
                validationStatus.EnableInClassList("ka-status--ok", vm.Validation.Ok);
                validationStatus.EnableInClassList("ka-status--error", !vm.Validation.Ok);
            }
            else if (validationStatus != null)
            {
                validationStatus.text = "No pair validated yet.";
                validationStatus.EnableInClassList("ka-status--ok", false);
                validationStatus.EnableInClassList("ka-status--error", false);
            }

            if (diffList != null)
            {
                diffList.Clear();
                foreach (var path in vm.Validation.DiffPaths)
                    diffList.Add(new Label(path) { name = "diff-row" });
            }

            var publish = root.Q<Button>("publish-button");
            if (publish != null) publish.SetEnabled(vm.PublishEnabled);

            var undo = root.Q<Button>("undo-button");
            if (undo != null) undo.SetEnabled(vm.CanUndo);
            var unfreeze = root.Q<Button>("unfreeze-button");
            if (unfreeze != null) unfreeze.SetEnabled(vm.HasControl);
            var submit = root.Q<Button>("submit-pair-button");
            if (submit != null) submit.SetEnabled(vm.HasControl);
        }

        /// <summary>
        /// Point the two live-preview panes at the control/treatment RenderTextures (from
        /// PreviewRenderer). The panel only DISPLAYS them — it neither owns the cameras nor the rooms.
        /// </summary>
        public static void SetPreviews(VisualElement root, RenderTexture control, RenderTexture treatment)
        {
            var c = root.Q<VisualElement>("control-preview");
            if (c != null && control != null) c.style.backgroundImage = Background.FromRenderTexture(control);
            var t = root.Q<VisualElement>("treatment-preview");
            if (t != null && treatment != null) t.style.backgroundImage = Background.FromRenderTexture(treatment);
        }

        static IEnumerable<string> ErrorLines(OperatorPanelViewModel vm)
        {
            foreach (var e in vm.Errors)
                yield return string.IsNullOrEmpty(e.Path) ? $"{e.Code}: {e.Message}" : $"{e.Code} @ {e.Path}: {e.Message}";
        }
    }
}
