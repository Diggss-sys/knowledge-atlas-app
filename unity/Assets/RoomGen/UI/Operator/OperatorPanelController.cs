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
            foreach (var field in vm.Fields)
            {
                var slider = root.Q<Slider>(field.Path);
                if (slider == null) continue;
                slider.lowValue = (float)field.Min;
                slider.highValue = (float)field.Max;
                slider.SetValueWithoutNotify((float)field.Value);
                var path = field.Path;
                slider.RegisterValueChangedCallback(evt => vm.SetField(path, evt.newValue));
            }

            var dropdown = root.Q<DropdownField>("declared-variable");
            if (dropdown != null)
            {
                dropdown.choices = new List<string>(vm.ManipulableVariables);
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
            if (validationStatus != null && vm.Validation.ActiveCondition != null)
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
