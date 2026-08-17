using System;
using System.Collections.Generic;
using System.Linq;
using KnowledgeAtlas.Seam;
using Newtonsoft.Json.Linq;
using RoomGen.Runner;

namespace RoomGen.UI
{
    /// <summary>
    /// U1's brain: the single-room editor as a pure-C# view-model. It talks to the engine ONLY through
    /// <see cref="ISpecChannel"/> and exposes ONLY plain types, so the EditMode test assembly (which
    /// cannot reference Newtonsoft) drives and asserts every behaviour without touching JSON. The full
    /// canonical RoomSpec lives inside as a <see cref="JObject"/> bind model; the binder (S2) is a dumb
    /// wire between UI Toolkit controls and this class.
    ///
    /// Correctness lives here: slider streams are debounced BEFORE the channel (~150 ms, ENGINE_SEAM),
    /// values clamp to the preset's ranges, engine errors are surfaced VERBATIM (never re-worded), and
    /// publish is disabled unless the pair validation passes (locked decision DL-6).
    /// </summary>
    public sealed class OperatorPanelViewModel
    {
        const int HistoryCapacity = 20;

        public enum ValidationFreshness
        {
            None,
            Fresh,
            Stale,
        }

        public enum PairWorkflowState
        {
            SingleRoom,
            ControlFrozen,
            ControlUnfrozen,
        }

        // ---- plain public value objects (test-assembly readable) ----

        public struct FieldSpec
        {
            public string Path;
            public string Label;
            public double Min;
            public double Max;
            public double Value;
            public bool IsManipulable;
        }

        public struct ErrorRow
        {
            public string Code;
            public string Path;
            public string Message;
        }

        public struct ValidationState
        {
            public bool Ok;
            public IReadOnlyList<string> ViolationCodes;
            public IReadOnlyList<string> DiffPaths;
            public IReadOnlyList<ErrorRow> Violations;
            public IReadOnlyList<string> Notes;
            public string ActiveCondition;
        }

        struct Snapshot
        {
            public string ModelJson;
            public string ControlJson;
            public string DeclaredVariable;
            public string Status;
            public IReadOnlyList<ErrorRow> Errors;
            public ValidationState Validation;
            public ValidationFreshness Freshness;
            public PairWorkflowState WorkflowState;
        }

        readonly ISpecChannel _channel;
        readonly Func<double> _now;
        readonly double _debounce;

        JObject _model;                 // the live canonical RoomSpec being edited
        JObject _controlModel;          // snapshot frozen by SetAsControl()
        string _presetJson;
        string _declaredVariable;
        readonly List<FieldSpec> _fields = new List<FieldSpec>();
        readonly Dictionary<string, (double Min, double Max)> _ranges = new Dictionary<string, (double, double)>();
        readonly List<string> _manipulable = new List<string>();
        readonly List<Snapshot> _history = new List<Snapshot>(HistoryCapacity);
        Snapshot? _pendingEditSnapshot;

        bool _dirty;
        double _lastEditAt;
        int _reqCounter;
        string _pendingReqId;

        // ---- observable state (plain types) ----

        public string Status { get; private set; } = "";
        public IReadOnlyList<ErrorRow> Errors { get; private set; } = new List<ErrorRow>();
        public ValidationState Validation { get; private set; } =
            new ValidationState
            {
                Ok = false,
                ViolationCodes = new List<string>(),
                DiffPaths = new List<string>(),
                Violations = new List<ErrorRow>(),
                Notes = new List<string>(),
                ActiveCondition = null,
            };
        public ValidationFreshness ValidationStatus { get; private set; } = ValidationFreshness.None;
        public PairWorkflowState WorkflowState { get; private set; } = PairWorkflowState.SingleRoom;

        /// <summary>The fields backing sliders/controls: preset ranges + manipulable flag.</summary>
        public IReadOnlyList<FieldSpec> Fields => _fields;

        /// <summary>Paths a student may "declare as the independent variable" (preset manipulable_variables).</summary>
        public IReadOnlyList<string> ManipulableVariables => _manipulable;

        /// <summary>Which manipulable variable the operator has declared for the pair (drives the picker).</summary>
        public string DeclaredVariable
        {
            get => _declaredVariable;
            set
            {
                if (string.Equals(_declaredVariable, value, StringComparison.Ordinal)) return;
                _declaredVariable = value;
                MarkValidationStale();
            }
        }

        /// <summary>DL-6: a confounded pair can be edited freely but never saved as a study.</summary>
        public bool PublishEnabled => ValidationStatus == ValidationFreshness.Fresh && Validation.Ok;

        public bool HasControl => _controlModel != null;

        /// <summary>True after an Apply is sent and until its matching spec_applied event arrives.</summary>
        public bool ApplyPending => !string.IsNullOrEmpty(_pendingReqId);

        /// <summary>True while the visible preview panes no longer represent the current model.</summary>
        public bool PreviewPending { get; private set; }

        /// <summary>True when the latest debounced edit or pair-workflow transition can be restored.</summary>
        public bool CanUndo => _pendingEditSnapshot.HasValue || _history.Count > 0;

        public OperatorPanelViewModel(ISpecChannel channel, Func<double> now, double debounceSeconds = 0.15)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _debounce = debounceSeconds;
            _channel.OnEvent += OnEvent;
        }

        // ---- preset load ----

        /// <summary>
        /// Parse spec/presets/dining_room.preset.json (copied byte-identical into Resources). The
        /// "defaults" object is a full canonical RoomSpec and becomes the bind model; "ranges" (dotted
        /// path -> [min,max]) + "manipulable_variables" produce the FieldSpec list.
        /// </summary>
        public void LoadPreset(string presetJson)
        {
            if (string.IsNullOrWhiteSpace(presetJson)) throw new ArgumentException("Preset JSON is required.", nameof(presetJson));

            _presetJson = presetJson;
            _history.Clear();
            _pendingEditSnapshot = null;
            LoadPresetState(presetJson);
        }

        void LoadPresetState(string presetJson)
        {
            var preset = ResponseJson.Parse(presetJson);

            _model = (JObject)preset["defaults"].DeepClone();
            _controlModel = null;
            Validation = EmptyValidation();
            ValidationStatus = ValidationFreshness.None;
            WorkflowState = PairWorkflowState.SingleRoom;
            Status = "";
            Errors = new List<ErrorRow>();
            _dirty = false;
            _pendingReqId = null;
            PreviewPending = false;

            _ranges.Clear();
            _manipulable.Clear();
            _fields.Clear();

            foreach (var m in (JArray)preset["manipulable_variables"])
                _manipulable.Add((string)m);

            var ranges = (JObject)preset["ranges"];
            foreach (var prop in ranges.Properties())
            {
                var arr = (JArray)prop.Value;
                var min = (double)arr[0];
                var max = (double)arr[1];
                _ranges[prop.Name] = (min, max);

                _fields.Add(new FieldSpec
                {
                    Path = prop.Name,
                    Label = Labelize(prop.Name),
                    Min = min,
                    Max = max,
                    Value = ReadDouble(prop.Name, min),
                    IsManipulable = _manipulable.Contains(prop.Name),
                });
            }
        }

        // ---- editing ----

        /// <summary>Clamp to the field's range, write the dotted path into the model, mark dirty.</summary>
        public void SetField(string path, double value)
        {
            if (_model == null) throw new InvalidOperationException("LoadPreset must be called first.");

            if (_ranges.TryGetValue(path, out var r))
                value = Math.Max(r.Min, Math.Min(r.Max, value));

            var current = ReadDotted(_model, path);
            if (current != null &&
                (current.Type == JTokenType.Integer || current.Type == JTokenType.Float) &&
                Math.Abs(current.Value<double>() - value) < 1e-6)
                return;

            // A slider may emit dozens of frames before Tick flushes. Remember the state before the
            // first frame, then commit exactly that one undo point when the debounced Apply lands.
            if (!_pendingEditSnapshot.HasValue)
                _pendingEditSnapshot = CaptureSnapshot();

            SetDotted(_model, path, value);

            for (var i = 0; i < _fields.Count; i++)
                if (_fields[i].Path == path)
                {
                    var f = _fields[i];
                    f.Value = value;
                    _fields[i] = f;
                }

            _dirty = true;
            _lastEditAt = _now();
            _pendingReqId = null; // any response for the previous model is now stale
            PreviewPending = true;
            Status = "edited";
            Errors = new List<ErrorRow>();
            MarkValidationStale();
        }

        /// <summary>Read the current (clamped) value of a range field, for the binder to reflect back.</summary>
        public double GetField(string path) => ReadDouble(path, _ranges.TryGetValue(path, out var r) ? r.Min : 0);

        /// <summary>
        /// Debounce pump: when dirty and the debounce window has elapsed since the last edit, send ONE
        /// Apply. Two rapid SetFields collapse to a single channel call after the window (slider streams
        /// debounced BEFORE the channel, per the handoff). Returns true if an Apply was sent.
        /// </summary>
        public bool Tick()
        {
            if (!_dirty || _model == null) return false;
            if (_now() - _lastEditAt < _debounce) return false;

            if (_pendingEditSnapshot.HasValue)
            {
                PushSnapshot(_pendingEditSnapshot.Value);
                _pendingEditSnapshot = null;
            }

            _dirty = false;
            _pendingReqId = NextReqId("apply");
            Status = "applying…";
            Errors = new List<ErrorRow>();
            _channel.Apply(_model.ToString(Newtonsoft.Json.Formatting.None), _pendingReqId);
            return true;
        }

        /// <summary>Called by the studio only after both preview renderers complete successfully.</summary>
        public void MarkPreviewRebuilt() => PreviewPending = false;

        // ---- pair workflow ----

        /// <summary>Freeze the current model as the control; editing continues on the treatment.</summary>
        public void SetAsControl()
        {
            if (_model == null) throw new InvalidOperationException("LoadPreset must be called first.");
            SnapshotBeforeTransition();
            _controlModel = (JObject)_model.DeepClone();
            WorkflowState = PairWorkflowState.ControlFrozen;
            MarkValidationStale();
        }

        /// <summary>
        /// Return to single-room editing without discarding the live treatment. Only the frozen
        /// baseline is cleared; the current model remains byte-for-byte unchanged.
        /// </summary>
        public void Unfreeze()
        {
            if (_controlModel == null) return;
            SnapshotBeforeTransition();
            _controlModel = null;
            WorkflowState = PairWorkflowState.ControlUnfrozen;
            MarkValidationStale();
        }

        /// <summary>Restore the loaded preset defaults. The discarded work remains recoverable by Undo.</summary>
        public void ResetToPreset()
        {
            if (_model == null || string.IsNullOrEmpty(_presetJson))
                throw new InvalidOperationException("LoadPreset must be called first.");

            SnapshotBeforeTransition();
            LoadPresetState(_presetJson);
            _dirty = true;
            _lastEditAt = _now();
            PreviewPending = true;
        }

        /// <summary>
        /// Restore the most recent debounced edit or workflow transition. The next Tick reapplies the
        /// restored model through the seam so the rendered room follows the state immediately.
        /// </summary>
        public void Undo()
        {
            Snapshot snapshot;
            if (_pendingEditSnapshot.HasValue)
            {
                snapshot = _pendingEditSnapshot.Value;
                _pendingEditSnapshot = null;
            }
            else
            {
                if (_history.Count == 0) return;
                var last = _history.Count - 1;
                snapshot = _history[last];
                _history.RemoveAt(last);
            }

            RestoreSnapshot(snapshot);
            _dirty = true;
            _lastEditAt = _now();
        }

        /// <summary>Send the frozen control + the current (treatment) model through the gate.</summary>
        public void SubmitPair()
        {
            if (_controlModel == null) throw new InvalidOperationException("SetAsControl must be called before SubmitPair.");
            MarkValidationStale();
            _channel.LoadPair(
                _controlModel.ToString(Newtonsoft.Json.Formatting.None),
                _model.ToString(Newtonsoft.Json.Formatting.None),
                NextReqId("pair"));
        }

        /// <summary>The frozen control spec JSON (for the study publisher's embedded snapshot).</summary>
        public string ControlSpecJson => _controlModel?.ToString(Newtonsoft.Json.Formatting.None);

        /// <summary>The current (treatment) spec JSON.</summary>
        public string CurrentSpecJson => _model?.ToString(Newtonsoft.Json.Formatting.None);

        // ---- event handling ----

        void OnEvent(SeamEvent ev)
        {
            switch (ev.Kind)
            {
                case SeamCodes.SpecApplied:
                    // A newer edit/apply supersedes this response. It must not clear the pending state
                    // or overwrite the status for the current model (last-write-wins at the UI edge).
                    if (string.IsNullOrEmpty(_pendingReqId) ||
                        !string.Equals(ev.RequestId, _pendingReqId, StringComparison.Ordinal))
                        break;

                    _pendingReqId = null;
                    if (ev.Ok)
                    {
                        Status = ev.Superseded ? "applied (superseded)" : "applied";
                        Errors = new List<ErrorRow>();
                    }
                    else
                    {
                        Status = "rejected";
                        // Verbatim: render the engine's codes/paths/messages exactly, never re-worded.
                        Errors = ev.Errors.Select(e => new ErrorRow { Code = e.Code, Path = e.Path, Message = e.Message }).ToList();
                    }
                    break;

                case SeamCodes.PairLoaded:
                    Validation = new ValidationState
                    {
                        Ok = ev.PairOk,
                        ViolationCodes = ev.PairViolationCodes.ToList(),
                        DiffPaths = ev.PairDiffPaths.ToList(),
                        Violations = ev.PairViolations.Select(e => new ErrorRow
                            { Code = e.Code, Path = e.Path, Message = e.Message }).ToList(),
                        Notes = ev.PairNotes.ToList(),
                        ActiveCondition = ev.ActiveCondition,
                    };
                    ValidationStatus = ValidationFreshness.Fresh;
                    break;
            }
        }

        void MarkValidationStale()
        {
            if (ValidationStatus != ValidationFreshness.None)
            {
                ValidationStatus = ValidationFreshness.Stale;
                Validation = EmptyValidation();
            }
        }

        static ValidationState EmptyValidation() => new ValidationState
        {
            Ok = false,
            ViolationCodes = new List<string>(),
            DiffPaths = new List<string>(),
            Violations = new List<ErrorRow>(),
            Notes = new List<string>(),
            ActiveCondition = null,
        };

        Snapshot CaptureSnapshot() => new Snapshot
        {
            ModelJson = _model?.ToString(Newtonsoft.Json.Formatting.None),
            ControlJson = _controlModel?.ToString(Newtonsoft.Json.Formatting.None),
            DeclaredVariable = _declaredVariable,
            Status = Status,
            Errors = CloneErrors(Errors),
            Validation = CloneValidation(Validation),
            Freshness = ValidationStatus,
            WorkflowState = WorkflowState,
        };

        void SnapshotBeforeTransition()
        {
            // If a transition happens inside the debounce window, the transition snapshot already
            // contains those live edits. Do not later add a second, older per-drag history entry.
            _pendingEditSnapshot = null;
            PushSnapshot(CaptureSnapshot());
        }

        void PushSnapshot(Snapshot snapshot)
        {
            if (_history.Count == HistoryCapacity) _history.RemoveAt(0);
            _history.Add(snapshot);
        }

        void RestoreSnapshot(Snapshot snapshot)
        {
            _model = string.IsNullOrEmpty(snapshot.ModelJson) ? null : ResponseJson.Parse(snapshot.ModelJson);
            _controlModel = string.IsNullOrEmpty(snapshot.ControlJson) ? null : ResponseJson.Parse(snapshot.ControlJson);
            _declaredVariable = snapshot.DeclaredVariable;
            Status = snapshot.Status;
            Errors = CloneErrors(snapshot.Errors);
            Validation = CloneValidation(snapshot.Validation);
            ValidationStatus = snapshot.Freshness;
            WorkflowState = snapshot.WorkflowState;
            _pendingReqId = null;
            PreviewPending = true;
            SyncFieldValues();
        }

        void SyncFieldValues()
        {
            for (var i = 0; i < _fields.Count; i++)
            {
                var field = _fields[i];
                field.Value = ReadDouble(field.Path, field.Min);
                _fields[i] = field;
            }
        }

        static ValidationState CloneValidation(ValidationState state) => new ValidationState
        {
            Ok = state.Ok,
            ViolationCodes = state.ViolationCodes?.ToList() ?? new List<string>(),
            DiffPaths = state.DiffPaths?.ToList() ?? new List<string>(),
            Violations = state.Violations?.Select(e => new ErrorRow
                { Code = e.Code, Path = e.Path, Message = e.Message }).ToList() ?? new List<ErrorRow>(),
            Notes = state.Notes?.ToList() ?? new List<string>(),
            ActiveCondition = state.ActiveCondition,
        };

        static IReadOnlyList<ErrorRow> CloneErrors(IReadOnlyList<ErrorRow> errors) =>
            errors?.Select(e => new ErrorRow
                { Code = e.Code, Path = e.Path, Message = e.Message }).ToList() ?? new List<ErrorRow>();

        // ---- json helpers (kept inside; never in the public surface) ----

        double ReadDouble(string dottedPath, double fallback)
        {
            var tok = ReadDotted(_model, dottedPath);
            return tok != null && (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)
                ? tok.Value<double>()
                : fallback;
        }

        static JToken ReadDotted(JObject root, string dottedPath)
        {
            JToken node = root;
            foreach (var seg in dottedPath.Split('.'))
            {
                if (!(node is JObject o)) return null;
                node = o[seg];
                if (node == null) return null;
            }
            return node;
        }

        static void SetDotted(JObject root, string dottedPath, JToken value)
        {
            var segs = dottedPath.Split('.');
            var node = root;
            for (var i = 0; i < segs.Length - 1; i++)
            {
                if (!(node[segs[i]] is JObject child))
                {
                    child = new JObject();
                    node[segs[i]] = child;
                }
                node = child;
            }
            node[segs[segs.Length - 1]] = value;
        }

        static string Labelize(string dottedPath)
        {
            var last = dottedPath.Split('.').Last();
            return last.Replace('_', ' ');
        }

        string NextReqId(string prefix) => $"{prefix}-{++_reqCounter}";
    }
}
