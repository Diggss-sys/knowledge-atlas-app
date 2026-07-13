using System;
using System.Globalization;
using System.IO;
using RoomGen.Generation;
using RoomGen.Metrics;
using RoomGen.Runner;
using RoomGen.VR;
using UnityEngine;
using UnityEngine.UIElements;

namespace RoomGen.UI
{
    /// <summary>
    /// A2 — the participant runner UI. Neutral by scientific requirement (COORDINATION.md): four plain
    /// screens (id → instructions → walk → rate, looping per trial → done) wrapped around
    /// <see cref="ParticipantSession"/>, which does the real work (honesty gate, seeded order, validated
    /// rows to CSV). Each trial builds the condition's room and drops the participant into
    /// <see cref="DesktopWalkMode"/>; Esc ends the walk and reveals the single rating question. The
    /// participant never sees the condition — that would be a demand cue.
    ///
    /// Boot is deferred (same pattern as OperatorStudio) so the scene auto-boots in Start and a PlayMode
    /// capture can boot with a hand-built panel + injected clock and drive the screens directly.
    /// </summary>
    public sealed class ParticipantFlow : MonoBehaviour
    {
        const int RoomLayer = 11;   // participant walk room (kept clear of the studio's 8/9/10)

        VisualElement _root;
        string _studyJson;
        string _outputDir;
        Func<string> _nowUtc;

        ParticipantSession _session;
        RoomGenerator _roomGen;
        DesktopWalkMode _walk;
        Camera _backdrop;

        // Frame-rate is measured PER WALK (the render-heavy part) and logged per trial to a sidecar,
        // never shown to the participant. Warmup 3 drops the one-time room-build hitch on entry.
        readonly PerfAccumulator _trialPerf = new PerfAccumulator(warmupFrames: 3);
        PerfLog _perfLog;
        PerfSample _lastWalkPerf;

        bool _booted;
        bool _walking;
        float _ratingShownAt;

        public ParticipantSession Session => _session;

        void Start()
        {
            if (_booted) return;
            var doc = GetComponent<UIDocument>();
#if UNITY_EDITOR
            // Self-heal a scene saved by the pre-fix generator (m_PanelSettings serialized as
            // {fileID: 0} in batchmode) — without a PanelSettings the panel binds to a detached
            // root and renders nowhere. See OperatorStudio.Start for the full story.
            if (doc != null && doc.panelSettings == null)
            {
                doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/RoomGen/UI/Runner/ParticipantPanelSettings.asset");
                if (doc.panelSettings != null)
                    Debug.LogWarning("ParticipantFlow: UIDocument had no PanelSettings (stale scene) — healed from the asset.");
            }
#endif
            if (doc != null && doc.panelSettings == null)
            {
                Debug.LogError("ParticipantFlow: UIDocument has NO PanelSettings — the screens would render " +
                               "nowhere. Re-run RoomGen ▸ Setup Participant Runner Scene.");
                return;
            }
            var study = Resources.Load<TextAsset>("RoomGen/Examples/ceiling-study");
            if (doc != null && doc.rootVisualElement != null && study != null)
                Boot(doc.rootVisualElement, study.text, null, Application.persistentDataPath);
            else
                Debug.LogWarning("ParticipantFlow: missing UIDocument root or ceiling-study resource — not booting.");
        }

        /// <summary>Wire the screens. <paramref name="nowUtc"/> null ⇒ real UtcNow (injectable for tests).</summary>
        public bool Boot(VisualElement root, string studyJson, Func<string> nowUtc, string outputDir)
        {
            if (_booted) return true;
            if (root == null) throw new ArgumentNullException(nameof(root));
            _root = root;
            _studyJson = studyJson;
            _nowUtc = nowUtc;
            _outputDir = outputDir;

            RenderQualityProfiles.ApplyDesktop();

            // Backdrop camera: the screens have no other camera rendering to the display, and the UI
            // overlay never composites onto a camera-less display ("No cameras rendering" + black).
            // Draws nothing, clears to the neutral off-white, steps aside during the room walk.
            var backdropGo = new GameObject("UI Backdrop Camera");
            backdropGo.transform.SetParent(transform, false);
            _backdrop = backdropGo.AddComponent<Camera>();
            _backdrop.cullingMask = 0;
            _backdrop.depth = -100f;
            _backdrop.clearFlags = CameraClearFlags.SolidColor;
            _backdrop.backgroundColor = new Color(0.98f, 0.98f, 0.98f, 1f); // neutral, not a brand colour

            var go = new GameObject("Participant Room");
            go.transform.SetParent(transform, false);
            _roomGen = go.AddComponent<RoomGenerator>();
            _roomGen.SetGeneratedLayer(RoomLayer);
            _walk = gameObject.AddComponent<DesktopWalkMode>();

            Button("begin-button", () => Begin(_root.Q<TextField>("participant-id")?.value));
            Button("start-button", BeginTrials);
            Button("enter-walk-button", EnterRoom);

            ShowOnly("screen-id");
            _booted = true;
            return true;
        }

        /// <summary>Create the session for this participant. Returns false (and shows the reason) when the
        /// study can't run; otherwise advances to the instructions.</summary>
        public bool Begin(string participantId)
        {
            var id = string.IsNullOrWhiteSpace(participantId) ? "anonymous" : participantId.Trim();
            var sessionId = Guid.NewGuid().ToString();
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var safe = SafeFile(id);
            var csv = Path.Combine(_outputDir, $"response-{safe}-{stamp}.csv");
            var jsonl = Path.Combine(_outputDir, $"response-{safe}-{stamp}.jsonl");

            _session = new ParticipantSession(_studyJson, id, StableSeed(id), sessionId, csv, jsonl, _nowUtc);
            if (!_session.CanRun)
            {
                var err = _root.Q<Label>("id-error");
                if (err != null) err.text = "Cannot start this session: " + _session.Reason;
                return false;
            }
            // Frame-rate sidecar next to the response CSV (joins on session_id + trial_index).
            _perfLog = new PerfLog(Path.Combine(_outputDir, $"response-{safe}-{stamp}.perf.csv"));
            ShowOnly("screen-instructions");
            return true;
        }

        public void BeginTrials() => ShowWalk();

        void ShowWalk()
        {
            SetText("walk-progress", $"Room {_session.Index + 1} of {_session.TrialCount}");
            ShowOnly("screen-walk");
        }

        /// <summary>Build the current condition's room and enter the desktop walk. Esc ends it (detected
        /// in Update), which reveals the rating screen.</summary>
        public void EnterRoom()
        {
            if (_session == null || _session.IsComplete) return;
            _roomGen.Build(_session.CurrentSpec);
            ShowPanel(false);
            _trialPerf.Reset();   // start measuring this room's frame rate fresh
            _walk.Toggle(RoomLayer, _roomGen.LastResult?.Root, _session.CurrentSpec);
            _walking = _walk.IsRunning;
            if (_walking && _backdrop != null) _backdrop.enabled = false; // walk camera owns the display
            if (!_walking) { ShowPanel(true); ShowRating(); }   // room failed to build — don't strand the participant
        }

        /// <summary>Show the single rating question for the current trial and start its response timer.</summary>
        public void ShowRating()
        {
            if (_session == null || _session.IsComplete) return;
            SetText("rating-progress", $"Room {_session.Index + 1} of {_session.TrialCount}");
            SetText("rating-prompt", _session.Prompt);
            SetText("scale-low-label", $"{_session.ScaleMin}  ·  least");
            SetText("scale-high-label", $"{_session.ScaleMax}  ·  most");
            BuildScale();
            _ratingShownAt = Time.time;
            ShowOnly("screen-rating");
        }

        /// <summary>Record the human's rating (with response time) and move to the next room, or finish.</summary>
        public void Rate(int value)
        {
            if (_session == null || _session.IsComplete) return;
            var rtMs = Mathf.Max(0f, (Time.time - _ratingShownAt) * 1000f);

            // Capture the trial identity BEFORE submitting (SubmitRating advances the cursor), so the
            // perf sidecar row keys to the same trial + condition as the response row just written.
            var trialIndex = _session.Index;
            var condition = _session.CurrentCondition;
            _session.SubmitRating(value, rtMs);
            _perfLog?.Write(_session.SessionId, _session.ParticipantId, _session.StudyId,
                trialIndex, condition, _lastWalkPerf);
            _lastWalkPerf = default;

            if (_session.IsComplete) ShowDone();
            else ShowWalk();
        }

        void ShowDone()
        {
            SetText("done-path", "Saved to " + _session.CsvPath);
            ShowOnly("screen-done");
        }

        void Update()
        {
            if (!_booted) return;
            if (_walk != null && _walk.IsRunning)
                _trialPerf.Add(Time.unscaledDeltaTime);   // measure real frame time while walking

            if (_walking && (_walk == null || !_walk.IsRunning))   // participant pressed Esc
            {
                _walking = false;
                _lastWalkPerf = _trialPerf.Snapshot();   // freeze this room's fps for the perf sidecar
                if (_backdrop != null) _backdrop.enabled = true;
                ShowPanel(true);
                ShowRating();
            }
        }

        void BuildScale()
        {
            var scale = _root.Q<VisualElement>("rating-scale");
            if (scale == null) return;
            scale.Clear();
            for (var v = _session.ScaleMin; v <= _session.ScaleMax; v++)
            {
                var value = v;   // capture per button
                var b = new Button(() => Rate(value)) { text = value.ToString(), name = "rate-" + value };
                b.AddToClassList("runner-scale-button");
                scale.Add(b);
            }
        }

        void ShowOnly(string screenName)
        {
            foreach (var name in new[] { "screen-id", "screen-instructions", "screen-walk", "screen-rating", "screen-done" })
            {
                var s = _root.Q<VisualElement>(name);
                if (s != null) s.style.display = name == screenName ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void ShowPanel(bool show)
        {
            if (_root != null) _root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Button(string name, Action onClick)
        {
            var b = _root.Q<Button>(name);
            if (b != null) b.clicked += onClick;
        }

        void SetText(string name, string text)
        {
            var l = _root.Q<Label>(name);
            if (l != null) l.text = text;
        }

        // Stable per-participant seed: different people get different (reproducible) trial orders, and the
        // same id always reproduces the same order. Not Object.GetHashCode (unstable across runs).
        static int StableSeed(string id)
        {
            unchecked
            {
                var h = 17;
                foreach (var c in id) h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        static string SafeFile(string id)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in id)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "anonymous";
        }

        void OnDestroy() { }
    }
}
