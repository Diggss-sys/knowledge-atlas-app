using System;
using System.Globalization;
using System.IO;
using KnowledgeAtlas.Seam;
using Newtonsoft.Json.Linq;
using RoomGen.Adapter;
using RoomGen.Contracts;
using RoomGen.Generation;
using RoomGen.Runner;
using RoomGen.VR;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.UI
{
    /// <summary>
    /// A1 — the INTERACTIVE operator studio. Composes the existing pieces into one live instrument:
    /// the UXML/USS panel + <see cref="OperatorPanelViewModel"/> drive the REAL engine through
    /// <see cref="LocalChannel"/> (wrapped by <see cref="StudioSpecChannel"/> so the panel's partial
    /// spec is completed into a full canonical one), two <see cref="PreviewRenderer"/>s paint the live
    /// control/treatment previews, and a Walk button enters <see cref="DesktopWalkMode"/>. Dragging a
    /// slider rebuilds the previews; "Validate pair" drives the real gate (a confounded pair shows red
    /// and keeps publish locked); "Walk this room" drops you into the room you are editing.
    ///
    /// It ships BESIDE the legacy IMGUI RoomStudioController (its own additive scene) and touches none
    /// of the engine, the preset, or the contracts — this is composition, not invention.
    ///
    /// Boot is deferred (not in Awake) so the same object serves two callers: the production scene
    /// auto-boots in Start from its serialized UIDocument with a Time-based clock; a PlayMode test
    /// boots explicitly with a hand-built panel root and a manual clock to cross the debounce
    /// deterministically.
    /// </summary>
    public sealed class OperatorStudio : MonoBehaviour
    {
        const int ControlLayer = 8;    // control preview room + camera
        const int TreatmentLayer = 9;  // treatment preview room + camera; also the room you walk
        const int SeamLayer = 10;      // the room RoomRuntime builds on apply (validation/atomic-apply proof; no camera)

        OperatorPanelViewModel _vm;
        VisualElement _root;
        JObject _baseTemplate;

        PreviewRenderer _previewControl;
        PreviewRenderer _previewTreatment;
        RoomGenerator _seamGenerator;
        DesktopWalkMode _walk;
        Camera _backdrop;

        RoomSpec _controlSpec;    // last-adapted control spec (frozen control, or current before Set-as-control)
        RoomSpec _treatmentSpec;  // last-adapted current/treatment spec — the room the Walk button enters

        bool _booted;
        bool _walking;

        public OperatorPanelViewModel ViewModel => _vm;
        public RenderTexture ControlTexture => _previewControl?.Texture;
        public RenderTexture TreatmentTexture => _previewTreatment?.Texture;
        public bool IsWalking => _walking;

        void Start()
        {
            if (_booted) return;
            var doc = GetComponent<UIDocument>();
#if UNITY_EDITOR
            // Self-heal: the pre-fix scene generator serialized m_PanelSettings as {fileID: 0} in
            // batchmode (the visualTreeAsset assignment stuck; panelSettings didn't), so the panel
            // bound to a DETACHED root and rendered nowhere — grey screen, zero errors. The scene
            // YAML is fixed, but a stale in-memory copy of the old scene heals here too.
            if (doc != null && doc.panelSettings == null)
            {
                doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/RoomGen/UI/Operator/OperatorPanelSettings.asset");
                if (doc.panelSettings != null)
                    Debug.LogWarning("OperatorStudio: UIDocument had no PanelSettings (stale scene) — healed from the asset.");
            }
#endif
            if (doc != null && doc.panelSettings == null)
            {
                Debug.LogError("OperatorStudio: UIDocument has NO PanelSettings — the panel would render " +
                               "nowhere. Re-run RoomGen ▸ Setup Operator Studio Scene.");
                return;
            }
            if (doc != null && doc.rootVisualElement != null)
                Boot(doc.rootVisualElement, () => Time.timeAsDouble);
            else
                Debug.LogWarning("OperatorStudio: no UIDocument/root on this GameObject — nothing to boot.");
        }

        /// <summary>
        /// Wire the panel to the real engine. Idempotent. <paramref name="now"/> is the clock the
        /// view-model debounce reads (Time in production; a manual value in tests). Returns false if a
        /// required resource is missing so the caller can surface it rather than half-boot.
        /// </summary>
        public bool Boot(VisualElement root, Func<double> now)
        {
            if (_booted) return true;
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (now == null) throw new ArgumentNullException(nameof(now));

            var schema = Resources.Load<TextAsset>("RoomGen/room_spec.schema");
            var template = Resources.Load<TextAsset>("RoomGen/Examples/ka-ceiling-control");
            var preset = Resources.Load<TextAsset>("RoomGen/dining_room.preset");
            if (schema == null || template == null || preset == null)
            {
                Debug.LogError("OperatorStudio: missing schema/template/preset under Resources/RoomGen — cannot boot.");
                return false;
            }

            RenderQualityProfiles.ApplyAuto();   // Mac → portable tier
            _root = root;
            _baseTemplate = StudioSpecChannel.BuildBase(template.text);

            // Backdrop camera: the ONLY thing rendering to the display on the panel screens (the
            // preview cameras target RenderTextures). Without it the display has no camera at all and
            // the UI overlay never composites ("No cameras rendering" + black screen). It draws
            // nothing (cullingMask 0) and clears to the panel's cream, then steps aside for the walk
            // camera. Pairs with clearColor=false on the PanelSettings — that fix alone left the
            // display camera-less.
            var backdropGo = new GameObject("UI Backdrop Camera");
            backdropGo.transform.SetParent(transform, false);
            _backdrop = backdropGo.AddComponent<Camera>();
            _backdrop.cullingMask = 0;
            _backdrop.depth = -100f;
            _backdrop.clearFlags = CameraClearFlags.SolidColor;
            _backdrop.backgroundColor = new Color(0.968f, 0.956f, 0.937f, 1f); // --ka-cream

            // Real engine: apply/load_pair are validated + built + gated by the runtime. The scaffold
            // channel completes the panel's partial spec into a full canonical one on the way in.
            _seamGenerator = CreateGenerator("Operator Studio Seam Room", SeamLayer);
            var runtime = new RoomRuntime(_seamGenerator, schema.text);
            var logPath = Path.Combine(Application.persistentDataPath, "operator-studio-session.jsonl");
            // declaredVariable is read at send time — _vm is assigned just below, before any LoadPair.
            ISpecChannel channel = new StudioSpecChannel(
                new LocalChannel(runtime, logPath), _baseTemplate, () => _vm?.DeclaredVariable);

            _vm = new OperatorPanelViewModel(channel, now);
            _vm.LoadPreset(preset.text);
            OperatorPanelController.Bind(_root, _vm, RenderPreviews);

            _previewControl = new PreviewRenderer("Control", ControlLayer);
            _previewTreatment = new PreviewRenderer("Treatment", TreatmentLayer);
            RenderPreviews();
            OperatorPanelController.SetPreviews(_root, _previewControl.Texture, _previewTreatment.Texture);

            _walk = gameObject.AddComponent<DesktopWalkMode>();
            gameObject.AddComponent<PerfHud>();   // always-on fps readout (survives the panel hiding during a walk)
            WireButtons();

            _booted = true;
            return true;
        }

        void WireButtons()
        {
            var walk = _root.Q<Button>("walk-button");
            if (walk != null) walk.clicked += ToggleWalk;

            var publish = _root.Q<Button>("publish-button");
            if (publish != null) publish.clicked += PublishStudy;
        }

        void Update()
        {
            if (!_booted) return;

            // Walker exited (Esc) — bring the panel back.
            if (_walking && (_walk == null || !_walk.IsRunning))
            {
                _walking = false;
                if (_backdrop != null) _backdrop.enabled = true;
                ShowPanel(true);
            }
            if (_walking) return;   // panel hidden; don't pump/refresh under the walk view

            PumpOnce();
            OperatorPanelController.Refresh(_root, _vm);
        }

        /// <summary>
        /// One debounce pump: if a debounced Apply landed, repaint the live previews. Returns whether
        /// an Apply was sent this call (the test asserts on this + the resulting view-model Status).
        /// </summary>
        public bool PumpOnce()
        {
            if (_vm == null) return false;
            var applied = _vm.Tick();
            if (applied) RenderPreviews();
            return applied;
        }

        void RenderPreviews()
        {
            if (_previewControl == null || _previewTreatment == null || _vm == null) return;
            var controlJson = _vm.ControlSpecJson ?? _vm.CurrentSpecJson;   // frozen control, or current before Set-as-control
            var treatmentJson = _vm.CurrentSpecJson;
            if (controlJson == null || treatmentJson == null) return;

            try
            {
                _controlSpec = RoomSpecAdapter.Adapt(StudioSpecChannel.Complete(_baseTemplate, controlJson)).Spec;
                _treatmentSpec = RoomSpecAdapter.Adapt(StudioSpecChannel.Complete(_baseTemplate, treatmentJson)).Spec;
                _previewControl.Render(_controlSpec);
                _previewTreatment.Render(_treatmentSpec);
            }
            catch (Exception e)
            {
                // A transient intermediate spec (e.g. furniture that won't fit a very narrow width) must
                // not kill the live panel — the authoritative verdict is the panel's apply-status.
                Debug.LogWarning("OperatorStudio: preview render skipped — " + e.Message);
            }
        }

        void ToggleWalk()
        {
            if (_walk == null) return;
            if (_walk.IsRunning)
            {
                _walk.StopSession();
                _walking = false;
                if (_backdrop != null) _backdrop.enabled = true;
                ShowPanel(true);
                return;
            }

            RenderPreviews();                       // make sure the treatment room is built
            var roomRoot = _previewTreatment?.Root;
            if (roomRoot == null) return;
            _walk.Toggle(TreatmentLayer, roomRoot, _treatmentSpec);
            _walking = _walk.IsRunning;
            if (_walking)
            {
                if (_backdrop != null) _backdrop.enabled = false; // the walk camera owns the display now
                ShowPanel(false);
            }
        }

        // Author → run: turn the validated pair into a study JSON (StudyPublisher gates it again — a
        // confounded pair is refused) and drop it where the participant runner picks it up. Closes the
        // last fixture gap: the operator can author the very study a participant then walks.
        void PublishStudy()
        {
            if (_vm == null || !_vm.PublishEnabled) return;   // belt-and-braces; the binder also disables it
            RenderPreviews();                                  // make sure the adapted specs are current
            if (_controlSpec == null || _treatmentSpec == null) return;

            var nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var controlCanonical = StudioSpecChannel.CompleteAndStamp(
                _baseTemplate, _vm.ControlSpecJson, "control", _vm.DeclaredVariable);
            var treatmentCanonical = StudioSpecChannel.CompleteAndStamp(
                _baseTemplate, _vm.CurrentSpecJson, "treatment", _vm.DeclaredVariable);
            var result = StudyPublisher.CreateDefault().Publish(new StudyPublisher.StudyInput
            {
                StudyId = "operator_authored_study",
                PairId = StudioSpecChannel.PairId,
                Title = "Operator-authored study",
                Modality = "desktop_3d",
                ControlSpecJson = controlCanonical,
                TreatmentSpecJson = treatmentCanonical,
                Task = new StudyPublisher.TaskConfig
                {
                    Type = "rating",
                    Prompt = "How pleasant does this room feel?",
                    ScaleMin = 1,
                    ScaleMax = 7,
                },
                CreatedAtIso = nowIso,
            });

            var publishButton = _root.Q<Button>("publish-button");
            if (!result.Ok)
            {
                Debug.LogWarning("OperatorStudio: publish refused — " + string.Join(" | ", result.Errors));
                if (publishButton != null) publishButton.text = "Publish refused — see console";
                return;
            }

            var archivePath = StudyHandoff.Publish(result.StudyJson, "operator_authored_study");
            Debug.Log("OperatorStudio: immutable study archive " + archivePath);
            Debug.Log("OperatorStudio: study published → " + StudyHandoff.PublishedStudyPath);
            if (publishButton != null) publishButton.text = "Published ✓ — participant app will run this study";
        }

        void ShowPanel(bool show)
        {
            if (_root != null) _root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        RoomGenerator CreateGenerator(string objectName, int layer)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var generator = go.AddComponent<RoomGenerator>();
            generator.SetGeneratedLayer(layer);
            return generator;
        }

        void OnDestroy()
        {
            _previewControl?.Dispose();
            _previewTreatment?.Dispose();
        }
    }
}
