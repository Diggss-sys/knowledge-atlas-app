using System;
using System.Collections.Generic;
using KnowledgeAtlas.Seam;

namespace RoomGen.Testing
{
    /// <summary>
    /// A contract-only <see cref="ISpecChannel"/> for tests and offline UI bring-up: it never touches
    /// the generator, so the operator panel (U1..U5) can be driven and asserted with zero engine
    /// dependency (UNITY_UI.md U0 — "your panel drives the mock; you never block on E1"). Behaviour is
    /// derived from the seam contract alone:
    ///
    /// - Every submission is RECORDED (kind, requestId, raw payload JSON) in <see cref="Calls"/> — a
    ///   plain-typed list, so the EditMode test assembly (which cannot reference Newtonsoft) reads it
    ///   without naming a JSON type.
    /// - <see cref="Apply"/> raises <c>spec_applied{ok:true}</c> by default; a per-call scripted queue
    ///   (<see cref="EnqueueApplyResult"/>) replays canned events FIFO instead (e.g. a schema_invalid
    ///   with paths) so a test can drive the error-surfacing path deterministically.
    /// - <see cref="LoadPair"/> raises <c>pair_loaded</c> (default ok, diff shell.ceiling_height_m,
    ///   active control), likewise scriptable via <see cref="EnqueuePairResult"/>.
    /// - SwitchCondition raises condition_switched; CaptureScreenshot raises screenshot_captured;
    ///   SetCameraMode is accepted with NO event (per SeamCodes — a camera-mode change has no contract
    ///   event), matching LocalChannel.
    /// - Events dispatch SYNCHRONOUSLY on the calling thread so tests observe them without pumping.
    ///
    /// Swapping this for the real <c>LocalChannel</c> is a single constructor argument; the view-model
    /// holds only <see cref="ISpecChannel"/>.
    /// </summary>
    public sealed class MockSpecChannel : ISpecChannel
    {
        /// <summary>One recorded submission. Plain types only (test-assembly readable).</summary>
        public struct Call
        {
            public string Kind;
            public string RequestId;
            public string PayloadJson;
        }

        readonly List<Call> _calls = new List<Call>();
        readonly Queue<SeamEvent> _scriptedApply = new Queue<SeamEvent>();
        readonly Queue<SeamEvent> _scriptedPair = new Queue<SeamEvent>();

        long _buildMs = 3;

        /// <summary>Every call the channel has received, in order.</summary>
        public IReadOnlyList<Call> Calls => _calls;

        public event Action<SeamEvent> OnEvent;

        // ---- scripted results (FIFO; consumed one per matching call) ----

        /// <summary>Queue a canned event to raise on the next <see cref="Apply"/> instead of the default ok.</summary>
        public void EnqueueApplyResult(SeamEvent ev) => _scriptedApply.Enqueue(ev);

        /// <summary>Queue a canned event to raise on the next <see cref="LoadPair"/> instead of the default.</summary>
        public void EnqueuePairResult(SeamEvent ev) => _scriptedPair.Enqueue(ev);

        /// <summary>Convenience: script a schema_invalid failure for the next Apply with the given paths.</summary>
        public void EnqueueApplyFailure(string requestId, params (string Code, string Path, string Message)[] errors)
        {
            var list = new List<SeamError>();
            foreach (var e in errors) list.Add(new SeamError(e.Code, e.Path, e.Message));
            _scriptedApply.Enqueue(SeamEvent.SpecAppliedFail(requestId, list, _buildMs));
        }

        /// <summary>Convenience: script the complete failed validation result promised by seam v1.</summary>
        public void EnqueuePairFailure(string requestId, IEnumerable<string> diffPaths,
            IEnumerable<SeamError> violations, IEnumerable<string> notes,
            string activeCondition = "control")
        {
            _scriptedPair.Enqueue(SeamEvent.PairResult(requestId, false, diffPaths,
                activeCondition, violations, notes));
        }

        // ---- ISpecChannel ----

        public void Apply(string specJson, string requestId)
        {
            Record(SeamCodes.ApplySpec, requestId, specJson);
            var ev = _scriptedApply.Count > 0
                ? _scriptedApply.Dequeue()
                : SeamEvent.SpecAppliedOk(requestId, _buildMs, Sha256Stub(specJson));
            Dispatch(ev);
        }

        public void LoadPair(string controlJson, string treatmentJson, string requestId)
        {
            // Record both sides so a test can prove SubmitPair sent control AND treatment.
            Record(SeamCodes.LoadPair, requestId, controlJson);
            Record(SeamCodes.LoadPair, requestId, treatmentJson);
            var ev = _scriptedPair.Count > 0
                ? _scriptedPair.Dequeue()
                : SeamEvent.PairResult(requestId, true, Array.Empty<string>(),
                    new[] { "shell.ceiling_height_m" }, "control");
            Dispatch(ev);
        }

        public void SwitchCondition(string condition, string transition, string requestId)
        {
            Record(SeamCodes.SwitchCondition, requestId, condition + "|" + transition);
            Dispatch(SeamEvent.ConditionDone(requestId, condition, transition, 0));
        }

        public void SetCameraMode(string mode, string requestId)
        {
            // Accepted, no event (matches SeamCodes / LocalChannel).
            Record(SeamCodes.SetCameraMode, requestId, mode);
        }

        public void CaptureScreenshot(string requestId)
        {
            Record(SeamCodes.CaptureScreenshot, requestId, null);
            Dispatch(SeamEvent.Screenshot(requestId));
        }

        // ---- helpers ----

        void Record(string kind, string requestId, string payloadJson) =>
            _calls.Add(new Call { Kind = kind, RequestId = requestId, PayloadJson = payloadJson });

        void Dispatch(SeamEvent ev) => OnEvent?.Invoke(ev);

        // A deterministic, cheap stand-in for spec_sha256 — the mock never builds a room, and tests
        // only assert it is present/non-empty, never that it equals the generator's real SHA.
        static string Sha256Stub(string s) =>
            (s == null ? 0 : s.Length).ToString("x").PadLeft(64, '0');
    }
}
