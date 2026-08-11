using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KnowledgeAtlas.Seam;

namespace RoomGen.UI
{
    /// <summary>
    /// A1's composition seam. The operator view-model edits only the preset's envelope
    /// (shell / surfaces / lighting), so the spec it emits is NOT a complete canonical RoomSpec, and
    /// it never authors the experiment block. room_spec.schema requires spec_version + room_type at
    /// the root (additionalProperties:false), and the single-variable gate requires an experiment
    /// block (condition / manipulated_variables / pair_id) on BOTH specs. The EditMode tests never
    /// noticed either gap because they drove a MockSpecChannel that echoes fixtures; wired to the REAL
    /// LocalChannel, the first apply would be refused schema_invalid and every SubmitPair would be
    /// refused missing_experiment.
    ///
    /// This decorator sits between the view-model and the real channel and does two things, purely in
    /// the composition layer (view-model, preset, and contracts stay untouched):
    ///   • completes every outgoing spec by deep-merging the edited fields onto a full, schema-valid
    ///     base template (the byte-identical KA dining example);
    ///   • on load_pair, stamps the experiment block: condition = control/treatment by argument order,
    ///     manipulated_variables = [the declared variable from the panel's dropdown], shared pair_id.
    ///
    /// Because both specs are completed from the SAME base, their openings, furniture and seed are
    /// identical by construction, so the gate's stimulus diff is exactly the field(s) the operator
    /// changed — and it is checked against exactly what they declared.
    /// </summary>
    public sealed class StudioSpecChannel : ISpecChannel
    {
        public const string PairId = "operator_studio_pair";
        const string FallbackVariable = "shell.ceiling_height_m";

        readonly ISpecChannel _inner;
        readonly JObject _baseTemplate;
        readonly Func<string> _declaredVariable;

        // Subscribing to the decorator IS subscribing to the inner channel: events originate in
        // LocalChannel and reach the view-model unchanged (we only rewrite the outgoing specs).
        public event Action<SeamEvent> OnEvent
        {
            add => _inner.OnEvent += value;
            remove => _inner.OnEvent -= value;
        }

        /// <param name="declaredVariable">Reads the panel's declared independent variable at send time
        /// (the studio passes () =&gt; viewModel.DeclaredVariable). Null/empty falls back to ceiling height.</param>
        public StudioSpecChannel(ISpecChannel inner, JObject baseTemplate, Func<string> declaredVariable)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _baseTemplate = baseTemplate ?? throw new ArgumentNullException(nameof(baseTemplate));
            _declaredVariable = declaredVariable ?? (() => FallbackVariable);
        }

        public void Apply(string specJson, string requestId) =>
            _inner.Apply(Complete(_baseTemplate, specJson), requestId);

        public void LoadPair(string controlJson, string treatmentJson, string requestId)
        {
            var declared = string.IsNullOrEmpty(_declaredVariable()) ? FallbackVariable : _declaredVariable();
            var control = CompleteAndStamp(_baseTemplate, controlJson, "control", declared);
            var treatment = CompleteAndStamp(_baseTemplate, treatmentJson, "treatment", declared);
            _inner.LoadPair(control, treatment, requestId);
        }

        public void SwitchCondition(string condition, string transition, string requestId) =>
            _inner.SwitchCondition(condition, transition, requestId);

        public void SetCameraMode(string mode, string requestId) =>
            _inner.SetCameraMode(mode, requestId);

        public void CaptureScreenshot(string requestId) => _inner.CaptureScreenshot(requestId);

        /// <summary>Parse the canonical example that supplies every field the panel never edits
        /// (spec_version, room_type, seed, openings, furniture) — the merge base for completion.
        /// The fixture's experiment block is dropped: a plain apply is a draft edit, not a member of
        /// the fixture's pair, and the session provenance log must not claim otherwise. LoadPair
        /// stamps a fresh block from the operator's actual declaration (Fable review, 2026-07-10).</summary>
        public static JObject BuildBase(string baseTemplateJson)
        {
            var b = JObject.Parse(baseTemplateJson);
            b.Remove("experiment");
            return b;
        }

        /// <summary>
        /// Deep-merge the edited fields over the base: nested objects merge recursively, arrays and
        /// scalars from the edit win, and the base supplies everything the editor never touches. The
        /// same method the studio uses to build the live-preview spec, so preview and seam agree.
        /// </summary>
        public static string Complete(JObject baseTemplate, string specJson)
        {
            if (string.IsNullOrEmpty(specJson)) return baseTemplate.ToString(Formatting.None);
            var merged = (JObject)baseTemplate.DeepClone();
            merged.Merge(JObject.Parse(specJson), new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace,
                MergeNullValueHandling = MergeNullValueHandling.Ignore,
            });
            return merged.ToString(Formatting.None);
        }

        /// <summary>Produce the exact canonical pair member used by both the runtime gate and study
        /// publishing: complete the editor's partial model, then stamp its experiment metadata.</summary>
        public static string CompleteAndStamp(
            JObject baseTemplate, string specJson, string condition, string declaredVariable)
        {
            var declared = string.IsNullOrEmpty(declaredVariable) ? FallbackVariable : declaredVariable;
            return WithExperiment(Complete(baseTemplate, specJson), condition, declared);
        }

        // Stamp the gate's required experiment block. condition distinguishes the two specs; the
        // declared variable is what the gate checks the actual stimulus diff against. experiment lives
        // under a non-stimulus prefix, so making condition differ never counts as a stimulus difference.
        static string WithExperiment(string specJson, string condition, string declaredVariable)
        {
            var o = JObject.Parse(specJson);
            o["experiment"] = new JObject
            {
                ["condition"] = condition,
                ["manipulated_variables"] = new JArray(declaredVariable),
                ["pair_id"] = PairId,
            };
            return o.ToString(Formatting.None);
        }
    }
}
