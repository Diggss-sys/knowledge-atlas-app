using System.Collections.Generic;
using System.Linq;
using KnowledgeAtlas.Seam;
using NUnit.Framework;
using RoomGen.Testing;
using RoomGen.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.Tests
{
    /// <summary>
    /// S2 — the visible shell. Loads the operator UXML (and its shared USS) as assets, instantiates the
    /// VisualElement tree headlessly (no rendering), asserts the expected controls exist by name, and
    /// proves the binder wires a slider change all the way through to a recorded mock-channel Apply.
    /// UI Toolkit trees construct fine in EditMode; to dispatch a real ChangeEvent (so the slider path,
    /// not a direct call, drives the channel) the tree is attached to a live panel via a UIDocument.
    /// </summary>
    public sealed class OperatorPanelBindingTests
    {
        const string UxmlPath = "Assets/RoomGen/UI/Operator/OperatorPanel.uxml";
        const string UssPath = "Assets/RoomGen/UI/Shared/base.uss";

        static string Preset => Resources.Load<TextAsset>("RoomGen/dining_room.preset").text;

        readonly List<Object> _cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        static VisualElement BuildTree()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(vta, $"UXML asset not found at {UxmlPath}");
            return vta.Instantiate();
        }

        /// <summary>Build the tree AND attach it to a live UIDocument panel so ChangeEvents dispatch.</summary>
        VisualElement BuildPaneledTree()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            _cleanup.Add(settings);
            var go = new GameObject("OperatorPanelTestDoc");
            _cleanup.Add(go);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            var tree = BuildTree();
            doc.rootVisualElement.Add(tree);
            return tree;
        }

        [Test]
        public void Shared_uss_asset_loads()
        {
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            Assert.IsNotNull(uss, $"shared base.uss not found at {UssPath}");
        }

        [Test]
        public void Panel_exposes_the_expected_controls_by_name()
        {
            var root = BuildTree();

            Assert.IsNotNull(root.Q<Slider>("shell.width_m"), "shell width slider");
            Assert.IsNotNull(root.Q<Slider>("shell.length_m"), "shell length slider");
            Assert.IsNotNull(root.Q<Slider>("shell.ceiling_height_m"), "ceiling slider");
            Assert.IsNotNull(root.Q<Slider>("lighting.warmth"), "warmth slider");
            Assert.IsNotNull(root.Q<Slider>("lighting.intensity"), "intensity slider");
            Assert.IsNotNull(root.Q<DropdownField>("declared-variable"), "declared-variable dropdown");
            Assert.IsNotNull(root.Q<VisualElement>("diff-list"), "diff-panel list container");
            Assert.IsNotNull(root.Q<Button>("publish-button"), "publish button");
            Assert.IsNotNull(root.Q<Button>("undo-button"), "undo button");
            Assert.IsNotNull(root.Q<Button>("unfreeze-button"), "unfreeze button");
            Assert.IsNotNull(root.Q<Button>("reset-button"), "reset button");
        }

        [Test]
        public void Armed_confirmation_requires_a_matching_second_click_and_unrelated_interaction_disarms()
        {
            var confirmation = new OperatorPanelController.PairActionConfirmation();

            Assert.IsFalse(confirmation.Confirm(OperatorPanelController.ArmedPairAction.Reset),
                "first click only arms the destructive reset");
            Assert.AreEqual(OperatorPanelController.ArmedPairAction.Reset, confirmation.ArmedAction);

            confirmation.Disarm(); // the controller calls this for any unrelated pointer/value action
            Assert.AreEqual(OperatorPanelController.ArmedPairAction.None, confirmation.ArmedAction);
            Assert.IsFalse(confirmation.Confirm(OperatorPanelController.ArmedPairAction.Reset),
                "after disarming, reset again requires a fresh first click");
            Assert.IsTrue(confirmation.Confirm(OperatorPanelController.ArmedPairAction.Reset),
                "only the matching second click executes");
            Assert.AreEqual(OperatorPanelController.ArmedPairAction.None, confirmation.ArmedAction);
        }

        [Test]
        public void Binder_routes_a_slider_change_through_to_a_recorded_channel_apply()
        {
            var ch = new MockSpecChannel();
            var vm = new OperatorPanelViewModel(ch, () => 0.0, debounceSeconds: 0.0);
            vm.LoadPreset(Preset);

            var root = BuildPaneledTree();
            OperatorPanelController.Bind(root, vm);

            // Drive the UI the way a user would: change the slider value (fires the change event).
            var ceiling = root.Q<Slider>("shell.ceiling_height_m");
            ceiling.value = 3.4f;

            Assert.IsTrue(vm.Tick(), "debounce=0 -> the slider change flushes on the next Tick");

            var applies = ch.Calls.Where(c => c.Kind == SeamCodes.ApplySpec).ToList();
            Assert.AreEqual(1, applies.Count, "the slider change reached the channel as one Apply");
            StringAssert.Contains("3.4", applies[0].PayloadJson, "the applied spec carries the edited ceiling");
        }

        [Test]
        public void Refresh_resynchronizes_slider_widgets_after_undo_and_reset()
        {
            var ch = new MockSpecChannel();
            var vm = new OperatorPanelViewModel(ch, () => 0.0, debounceSeconds: 0.0);
            vm.LoadPreset(Preset);
            var root = BuildPaneledTree();
            OperatorPanelController.Bind(root, vm);
            var ceiling = root.Q<Slider>("shell.ceiling_height_m");

            ceiling.value = 3.2f;
            Assert.IsTrue(vm.Tick());
            vm.Undo();
            OperatorPanelController.Refresh(root, vm);
            Assert.AreEqual(2.8f, ceiling.value, 0.0001f,
                "the slider must follow the model restored by Undo");

            ceiling.value = 3.4f;
            Assert.IsTrue(vm.Tick());
            vm.ResetToPreset();
            OperatorPanelController.Refresh(root, vm);
            Assert.AreEqual(2.8f, ceiling.value, 0.0001f,
                "the slider must follow preset defaults after Reset");
        }

        [Test]
        public void Declared_variable_dropdown_offers_only_manipulable_variables_with_a_wired_control()
        {
            var ch = new MockSpecChannel();
            var vm = new OperatorPanelViewModel(ch, () => 0.0);
            vm.LoadPreset(Preset);

            var root = BuildTree();
            OperatorPanelController.Bind(root, vm);

            // manipulable ∩ wired ranges: declaring a variable with no wired slider is a guaranteed
            // declared_unchanged red verdict — the picker must not offer that dead end. Today only the
            // flagship ceiling variable qualifies; contour/wall-material/warmth return with preset ranges.
            var dropdown = root.Q<DropdownField>("declared-variable");
            CollectionAssert.AreEqual(new[] { "shell.ceiling_height_m" }, dropdown.choices,
                "the picker offers only declarable variables the panel can actually change");
            Assert.AreEqual("shell.ceiling_height_m", vm.DeclaredVariable, "default declaration follows the first offered choice");
        }

        [Test]
        public void Sliders_without_a_preset_range_are_disabled_not_left_as_silent_noops()
        {
            var ch = new MockSpecChannel();
            var vm = new OperatorPanelViewModel(ch, () => 0.0);
            vm.LoadPreset(Preset);

            var root = BuildTree();
            OperatorPanelController.Bind(root, vm);

            // warmth/intensity exist in the UXML but have no preset range yet -> the binder can't wire
            // them; dragging a control that does nothing would mislead the operator (Fable review).
            Assert.IsFalse(root.Q<Slider>("lighting.warmth").enabledSelf, "unwired warmth slider must be disabled");
            Assert.IsFalse(root.Q<Slider>("lighting.intensity").enabledSelf, "unwired intensity slider must be disabled");
            Assert.IsTrue(root.Q<Slider>("shell.ceiling_height_m").enabledSelf, "ranged sliders stay live");
        }

        [Test]
        public void Publish_button_is_disabled_until_validation_passes_then_enabled()
        {
            var ch = new MockSpecChannel();
            var vm = new OperatorPanelViewModel(ch, () => 0.0);
            vm.LoadPreset(Preset);

            var root = BuildTree();
            OperatorPanelController.Bind(root, vm);

            var publish = root.Q<Button>("publish-button");
            Assert.IsFalse(publish.enabledSelf, "no validated pair yet -> publish disabled (DL-6)");

            // A clean single-variable pair arrives (default mock result is ok).
            vm.SetAsControl();
            vm.SubmitPair();
            OperatorPanelController.Refresh(root, vm);

            Assert.IsTrue(publish.enabledSelf, "publish enables once validation.ok");

            vm.SetField("shell.ceiling_height_m", 3.2);
            OperatorPanelController.Refresh(root, vm);

            Assert.IsFalse(publish.enabledSelf, "editing invalidates the old verdict");
            StringAssert.Contains("edited since validation", root.Q<Label>("validation-status").text);
        }
    }
}
