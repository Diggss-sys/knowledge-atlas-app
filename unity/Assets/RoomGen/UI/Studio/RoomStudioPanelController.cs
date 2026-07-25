using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoomGen.Contracts;
using RoomGen.Lighting;
using RoomGen.Validation;
using RoomGen.VR;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.UI.Studio
{
    /// <summary>
    /// The Room Studio's view. Binds RoomStudioPanel.uxml to <see cref="RoomStudioViewModel"/> and
    /// owns the two live <see cref="PreviewRenderer"/> panes plus the walk/VR modes. All behaviour
    /// lives in the view-model; this class only wires widgets and paints results.
    ///
    /// Ships BESIDE the legacy IMGUI RoomStudioController — that one keeps working until Diego
    /// confirms parity. Boot is deferred (not Awake) so a PlayMode test can boot it against a
    /// hand-built root, the same pattern OperatorStudio uses.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RoomStudioPanelController : MonoBehaviour
    {
        const int ControlLayer = 8;
        const int TreatmentLayer = 9;

        static readonly string[] FloorMaterials =
            { "builtin.oak", "builtin.walnut", "cc0.carpet", "cc0.tile" };

        RoomStudioViewModel _vm;
        VisualElement _root;
        PreviewRenderer _control;
        PreviewRenderer _treatment;
        DesktopWalkMode _walk;
        VrExplorationMode _vr;
        ValidationReport _report;

        string _roomId = RoomStudioViewModel.Rooms[0].Id;
        string _status = "";
        float _sunHour = -1f;   // <0 = the static calibrated sun
        bool _booted;

        public RoomStudioViewModel ViewModel => _vm;

        void Start()
        {
            if (_booted) return;
            var doc = GetComponent<UIDocument>();
            if (doc?.rootVisualElement == null)
            {
                Debug.LogError("RoomStudioPanelController: no UIDocument root — panel cannot bind.");
                return;
            }
            Boot(doc.rootVisualElement);
        }

        public void Boot(VisualElement root)
        {
            if (_booted) return;
            _booted = true;
            _root = root;

            _vm = new RoomStudioViewModel(LoadPair(_roomId));
            _vm.RebuildRequested += Rebuild;
            _vm.SetVrAvailable(UnityEngine.XR.XRSettings.isDeviceActive);

            _control = new PreviewRenderer("Control", ControlLayer);
            _treatment = new PreviewRenderer("Treatment", TreatmentLayer);
            _walk = gameObject.AddComponent<DesktopWalkMode>();
            _vr = gameObject.AddComponent<VrExplorationMode>();

            BindRoom();
            BindVariable();
            BindSharedSliders();
            BindLighting();
            BindActions();

            Rebuild();
        }

        void Update()
        {
            // The debounce clock. Edits made while dragging coalesce into ONE rebuild here instead
            // of regenerating the room mesh on every slider frame (the legacy panel's behaviour).
            _vm?.Tick(Time.deltaTime);
            if (_vm != null) _vm.SetWalking(_walk != null && _walk.IsRunning);
        }

        // ---- binding ----------------------------------------------------------------------------

        void BindRoom()
        {
            var dropdown = _root.Q<DropdownField>("room-select");
            if (dropdown == null) return;
            dropdown.choices = RoomStudioViewModel.Rooms.Select(r => r.Label).ToList();
            dropdown.index = 0;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var choice = RoomStudioViewModel.Rooms
                    .FirstOrDefault(r => r.Label == evt.newValue);
                if (choice.Id == null) return;
                _roomId = choice.Id;
                var loaded = LoadPair(_roomId);
                if (loaded == null)
                {
                    SetStatus($"'{choice.Label}' could not be loaded.");
                    return;
                }
                _vm.SetPair(loaded);
                SetStatus($"Loaded {choice.Label}.");
                RefreshSliders();
            });
            SetLabel("room-hint", "The glazed room is the realism reference — it is what daylight work is judged on.");
        }

        void BindVariable()
        {
            var dropdown = _root.Q<DropdownField>("variable-select");
            if (dropdown == null) return;
            dropdown.choices = RoomStudioViewModel.VariableLabels.ToList();
            dropdown.index = Mathf.Max(0,
                Array.IndexOf(RoomStudioViewModel.VariablePaths, _vm.SelectedVariable));
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var index = Array.IndexOf(RoomStudioViewModel.VariableLabels, evt.newValue);
                if (index < 0) return;
                _vm.Pair.ManipulatedVariable = RoomStudioViewModel.VariablePaths[index];
                _vm.MarkDirty();
                RefreshSliders();
            });

            BindConditionSlider("control-value", treatment: false);
            BindConditionSlider("treatment-value", treatment: true);
            SetLabel("variable-hint",
                "Exactly one variable may differ between the two rooms — everything else is shared.");
        }

        void BindConditionSlider(string elementName, bool treatment)
        {
            var slider = _root.Q<Slider>(elementName);
            if (slider == null) return;
            var spec = _vm.ConditionSlider(treatment);
            slider.lowValue = spec.Min;
            slider.highValue = spec.Max;
            slider.SetValueWithoutNotify(spec.Value);
            SliderFill.Attach(slider);
            slider.RegisterValueChangedCallback(evt => _vm.SetConditionValue(treatment, evt.newValue));
        }

        void BindSharedSliders()
        {
            foreach (var spec in _vm.SharedSliders())
            {
                var slider = _root.Q<Slider>(spec.Key);
                if (slider == null) continue;
                var key = spec.Key;
                slider.lowValue = spec.Min;
                slider.highValue = spec.Max;
                slider.SetValueWithoutNotify(spec.Value);
                SliderFill.Attach(slider);
                slider.RegisterValueChangedCallback(evt => _vm.SetSharedValue(key, evt.newValue));
            }
        }

        void BindLighting()
        {
            var sun = _root.Q<Slider>("shared.sun_hour");
            if (sun != null)
            {
                sun.lowValue = SunSkySystem.MinHour;
                sun.highValue = SunSkySystem.MaxHour;
                sun.SetValueWithoutNotify(SunSkySystem.PeakHour);
                SliderFill.Attach(sun);
                sun.RegisterValueChangedCallback(evt =>
                {
                    _sunHour = Mathf.Round(evt.newValue * 4f) / 4f; // 15-minute steps
                    ApplySun();
                });
            }

            Click("reset-sun-button", () =>
            {
                _sunHour = -1f;
                ApplySun();
                SetStatus("Sun reset to the calibrated angle.");
            });

            Click("floor-button", () =>
            {
                var current = Array.IndexOf(FloorMaterials, _vm.Pair.Control.Surfaces.FloorMaterialId);
                var next = FloorMaterials[(current + 1 + FloorMaterials.Length) % FloorMaterials.Length];
                _vm.Pair.Control.Surfaces.FloorMaterialId = next;
                _vm.Pair.Treatment.Surfaces.FloorMaterialId = next;
                _vm.MarkDirty();
            });

            SetLabel("lighting-hint",
                "Lighting is shared by both rooms, so it can never become a second manipulated variable.");
        }

        void BindActions()
        {
            Click("walk-control-button", () => Walk(ControlLayer, _control, _vm.Pair.Control));
            Click("walk-treatment-button", () => Walk(TreatmentLayer, _treatment, _vm.Pair.Treatment));
            Click("walk-seam-button", () => Walk(ControlLayer, _control, _vm.Pair.Control));
            Click("vr-button", () =>
            {
                if (!_vm.VrAction().Enabled) return;
                _vr.Toggle(ControlLayer, _vm.Pair.Control);
            });

            Click("save-button", () =>
            {
                File.WriteAllText(SavePath, RoomJson.Serialize(_vm.Pair));
                SetStatus("Saved to " + SavePath);
            });
            Click("load-button", () =>
            {
                if (!File.Exists(SavePath)) { SetStatus("No saved pair found."); return; }
                _vm.SetPair(RoomJson.Deserialize<ConditionPairSpec>(File.ReadAllText(SavePath)));
                RefreshSliders();
                SetStatus("Loaded the saved pair.");
            });
            Click("export-button", () =>
            {
                var dir = Path.Combine(Application.persistentDataPath, "research-package");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "pair.json"), RoomJson.Serialize(_vm.Pair));
                SetStatus("Exported to " + dir);
            });
            SetLabel("data-hint", "Saved pairs live in the app's persistent data folder.");
        }

        static string SavePath =>
            Path.Combine(Application.persistentDataPath, "room-studio-pair.json");

        // ---- behaviour --------------------------------------------------------------------------

        void Walk(int layer, PreviewRenderer preview, RoomSpec spec)
        {
            if (preview?.Root == null)
            {
                SetStatus("That room has not been built yet.");
                return;
            }
            _walk.Toggle(layer, preview.Root, spec);
        }

        void ApplySun()
        {
            foreach (var preview in new[] { _control, _treatment })
            {
                if (preview?.Root == null) continue;
                var spec = preview == _control ? _vm.Pair.Control : _vm.Pair.Treatment;
                if (_sunHour < 0f)
                    SunSkySystem.Reset(preview.Root.transform,
                        spec.Lighting.ColorTemperatureK, spec.Lighting.TargetLux);
                else
                    SunSkySystem.Apply(preview.Root.transform, _sunHour, spec.Lighting.TargetLux);
            }
        }

        void Rebuild()
        {
            _control.Render(_vm.Pair.Control);
            _treatment.Render(_vm.Pair.Treatment);
            ApplySun();

            _report = PairValidator.Validate(_vm.Pair);
            PaintPreviews();
            PaintStatus();
        }

        void PaintPreviews()
        {
            SetBackground("control-preview", _control.Texture);
            SetBackground("treatment-preview", _treatment.Texture);

            var c = _vm.Pair.Control.Geometry;
            var t = _vm.Pair.Treatment.Geometry;
            SetLabel("control-caption",
                $"{c.CeilingHeightM:0.00} m ceiling · {c.WidthM:0.0} x {c.LengthM:0.0} m");
            SetLabel("treatment-caption",
                $"{t.CeilingHeightM:0.00} m ceiling · {t.WidthM:0.0} x {t.LengthM:0.0} m");

            var title = _root.Q<Label>("room-title");
            if (title != null) title.text = _vm.RoomTitle(_roomId);
        }

        void PaintStatus()
        {
            var verdict = _root.Q<Label>("verdict");
            if (verdict != null)
            {
                var ok = _report != null && _report.Ok;
                verdict.text = ok ? "VALID" : "BLOCKED";
                verdict.RemoveFromClassList("ka-status--ok");
                verdict.RemoveFromClassList("ka-status--error");
                verdict.AddToClassList(ok ? "ka-status--ok" : "ka-status--error");
            }

            SetLabel("status-text", _status);

            var list = _root.Q<VisualElement>("issue-list");
            if (list == null) return;
            list.Clear();
            if (_report == null) return;
            // Engine errors are shown VERBATIM (locked decision) — never reworded or summarised.
            foreach (var issue in _report.Issues.Take(4))
            {
                var row = new Label(issue.Path + ": " + issue.Message);
                row.AddToClassList("ka-diff-row");
                list.Add(row);
            }

            var vr = _vm.VrAction();
            var seam = _vm.SeamWalkAction(_control?.Root != null);
            SetEnabled("vr-button", vr.Enabled);
            SetEnabled("walk-seam-button", seam.Enabled);
            SetLabel("view-hint", !vr.Enabled ? vr.Reason : !seam.Enabled ? seam.Reason : "");

            var floor = _root.Q<Button>("floor-button");
            if (floor != null)
                floor.text = "Floor: " + _vm.Pair.Control.Surfaces.FloorMaterialId
                    .Replace("builtin.", "").Replace("cc0.", "").Replace('-', ' ');
        }

        void RefreshSliders()
        {
            BindConditionSlider("control-value", treatment: false);
            BindConditionSlider("treatment-value", treatment: true);
            foreach (var spec in _vm.SharedSliders())
            {
                var slider = _root.Q<Slider>(spec.Key);
                if (slider == null) continue;
                slider.lowValue = spec.Min;
                slider.highValue = spec.Max;
                slider.SetValueWithoutNotify(spec.Value);
            }
        }

        ConditionPairSpec LoadPair(string roomId)
        {
            switch (roomId)
            {
                case "saved":
                    return File.Exists(SavePath)
                        ? RoomJson.Deserialize<ConditionPairSpec>(File.ReadAllText(SavePath))
                        : null;
                case "ka-spec":
                {
                    var control = Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-control");
                    var treatment = Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-treatment");
                    if (control == null || treatment == null) return null;
                    return RoomGen.Adapter.RoomSpecAdapter.AdaptPair(control.text, treatment.text);
                }
                default:
                {
                    var source = Resources.Load<TextAsset>("RoomGen/Examples/" + roomId);
                    return source == null
                        ? null
                        : RoomJson.Deserialize<ConditionPairSpec>(source.text);
                }
            }
        }

        // ---- tiny view helpers ------------------------------------------------------------------

        void SetStatus(string text)
        {
            _status = text;
            SetLabel("status-text", text);
        }

        void SetLabel(string elementName, string text)
        {
            var label = _root?.Q<Label>(elementName);
            if (label != null) label.text = text;
        }

        void SetEnabled(string elementName, bool enabled)
        {
            var element = _root?.Q<VisualElement>(elementName);
            element?.SetEnabled(enabled);
        }

        void SetBackground(string elementName, Texture texture)
        {
            var element = _root?.Q<VisualElement>(elementName);
            if (element != null && texture != null)
                element.style.backgroundImage = Background.FromRenderTexture((RenderTexture)texture);
        }

        void Click(string elementName, Action action)
        {
            var button = _root?.Q<Button>(elementName);
            if (button != null) button.clicked += action;
        }

        void OnDestroy()
        {
            _control?.Dispose();
            _treatment?.Dispose();
        }
    }
}
