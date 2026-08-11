using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.UI
{
    /// <summary>
    /// The dumb binder (S2): wires the OperatorPanel.uxml controls to an OperatorPanelViewModel and
    /// pumps the debounce each frame. It holds no state and makes no decisions — correctness lives in
    /// the view-model. The wiring itself is a static method (<see cref="Bind"/>) taking a plain
    /// VisualElement + view-model, so an EditMode test can exercise it against an instantiated UXML
    /// tree with no GameObject/UIDocument and prove a slider change reaches the channel.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OperatorPanelController : MonoBehaviour
    {
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
        public static void Bind(VisualElement root, OperatorPanelViewModel vm)
        {
            var wired = new HashSet<string>();
            foreach (var field in vm.Fields)
            {
                var slider = root.Q<Slider>(field.Path);
                if (slider == null) continue;
                slider.lowValue = (float)field.Min;
                slider.highValue = (float)field.Max;
                slider.SetValueWithoutNotify((float)field.Value);
                var path = field.Path;
                slider.RegisterValueChangedCallback(evt => vm.SetField(path, evt.newValue));
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
                dropdown.RegisterValueChangedCallback(evt => vm.DeclaredVariable = evt.newValue);
            }

            Refresh(root, vm);
        }

        /// <summary>Reflect view-model observable state back onto the controls (status, diff, publish gate).</summary>
        public static void Refresh(VisualElement root, OperatorPanelViewModel vm)
        {
            // Which room do the sliders shape right now? Before Set-as-control everything is the
            // control-to-be (both previews mirror it); after, the control is frozen and edits flow to
            // the treatment. The operator kept losing track of this — say it, loudly, at the top.
            var banner = root.Q<Label>("editing-status");
            if (banner != null)
            {
                banner.text = vm.HasControl
                    ? "CONTROL FROZEN — editing: TREATMENT. Change exactly one thing, then Validate pair."
                    : "EDITING: CONTROL — shape this room, then press 'Set as control' to freeze the baseline.";
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

            if (diffList != null)
            {
                diffList.Clear();
                foreach (var path in vm.Validation.DiffPaths)
                    diffList.Add(new Label(path) { name = "diff-row" });
            }

            var publish = root.Q<Button>("publish-button");
            if (publish != null) publish.SetEnabled(vm.PublishEnabled);
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
