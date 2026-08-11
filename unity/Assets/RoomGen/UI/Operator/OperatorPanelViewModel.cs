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
        public enum ValidationFreshness
        {
            None,
            Fresh,
            Stale,
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
            public string ActiveCondition;
        }

        readonly ISpecChannel _channel;
        readonly Func<double> _now;
        readonly double _debounce;

        JObject _model;                 // the live canonical RoomSpec being edited
        JObject _controlModel;          // snapshot frozen by SetAsControl()
        string _declaredVariable;
        readonly List<FieldSpec> _fields = new List<FieldSpec>();
        readonly Dictionary<string, (double Min, double Max)> _ranges = new Dictionary<string, (double, double)>();
        readonly List<string> _manipulable = new List<string>();

        bool _dirty;
        double _lastEditAt;
        int _reqCounter;

        // ---- observable state (plain types) ----

        public string Status { get; private set; } = "";
        public IReadOnlyList<ErrorRow> Errors { get; private set; } = new List<ErrorRow>();
        public ValidationState Validation { get; private set; } =
            new ValidationState { Ok = false, ViolationCodes = new List<string>(), DiffPaths = new List<string>(), ActiveCondition = null };
        public ValidationFreshness ValidationStatus { get; private set; } = ValidationFreshness.None;

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
            var preset = ResponseJson.Parse(presetJson);

            _model = (JObject)preset["defaults"].DeepClone();
            _controlModel = null;
            Validation = EmptyValidation();
            ValidationStatus = ValidationFreshness.None;

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

            _dirty = false;
            _channel.Apply(_model.ToString(Newtonsoft.Json.Formatting.None), NextReqId("apply"));
            return true;
        }

        // ---- pair workflow ----

        /// <summary>Freeze the current model as the control; editing continues on the treatment.</summary>
        public void SetAsControl()
        {
            _controlModel = (JObject)_model.DeepClone();
            MarkValidationStale();
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
                        ActiveCondition = ev.ActiveCondition,
                    };
                    ValidationStatus = ValidationFreshness.Fresh;
                    break;
            }
        }

        void MarkValidationStale()
        {
            if (ValidationStatus != ValidationFreshness.None)
                ValidationStatus = ValidationFreshness.Stale;
        }

        static ValidationState EmptyValidation() => new ValidationState
        {
            Ok = false,
            ViolationCodes = new List<string>(),
            DiffPaths = new List<string>(),
            ActiveCondition = null,
        };

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
