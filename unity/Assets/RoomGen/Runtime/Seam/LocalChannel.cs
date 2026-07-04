using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KnowledgeAtlas.Seam
{
    /// <summary>
    /// The v1 in-process ISpecChannel: same-thread direct calls into an IRoomRuntime, events raised
    /// on OnEvent, zero latency. Each call is serialized to a wire envelope and routed through the
    /// shared SeamEnvelope (so LocalChannel and a future NetworkChannel behave identically), and — when
    /// a session log path is set — every call and event is appended to a JSONL provenance log: the
    /// complete, timestamp-orderable record of what an operator did and a subject saw (ENGINE_SEAM
    /// guarantee #5). UI/generator code holds only ISpecChannel and never learns which channel is live.
    /// </summary>
    public sealed class LocalChannel : ISpecChannel
    {
        readonly IRoomRuntime runtime;
        readonly string sessionLogPath;

        public event Action<SeamEvent> OnEvent;

        public LocalChannel(IRoomRuntime runtime, string sessionLogPath = null)
        {
            this.runtime = runtime;
            this.sessionLogPath = sessionLogPath;
        }

        public void Apply(string specJson, string requestId) =>
            Send(SeamCodes.ApplySpec, requestId, new JObject { ["spec"] = JToken.Parse(specJson) });

        public void LoadPair(string controlJson, string treatmentJson, string requestId) =>
            Send(SeamCodes.LoadPair, requestId,
                new JObject { ["control"] = JToken.Parse(controlJson), ["treatment"] = JToken.Parse(treatmentJson) });

        public void SwitchCondition(string condition, string transition, string requestId) =>
            Send(SeamCodes.SwitchCondition, requestId,
                new JObject { ["condition"] = condition, ["transition"] = transition });

        public void SetCameraMode(string mode, string requestId) =>
            Send(SeamCodes.SetCameraMode, requestId, new JObject { ["mode"] = mode });

        public void CaptureScreenshot(string requestId) =>
            Send(SeamCodes.CaptureScreenshot, requestId, new JObject { ["width"] = 1920, ["height"] = 1080 });

        /// <summary>Feed a raw wire envelope (NetworkChannel parity / fixture replay).</summary>
        public void Receive(string envelopeJson) => Route(envelopeJson);

        void Send(string kind, string requestId, JObject payload)
        {
            var envelope = new JObject
            {
                ["seam_version"] = SeamCodes.Version,
                ["kind"] = kind,
                ["request_id"] = requestId,
                ["payload"] = payload,
            }.ToString(Formatting.None);
            Route(envelope);
        }

        void Route(string envelopeJson)
        {
            Log(envelopeJson);                                  // the call
            var ev = SeamEnvelope.Route(envelopeJson, runtime);
            if (ev == null) return;                             // accepted, no event (e.g. set_camera_mode)
            Log(ev.ToJson());                                   // the resulting event
            OnEvent?.Invoke(ev);
        }

        void Log(string jsonLine)
        {
            if (string.IsNullOrEmpty(sessionLogPath)) return;
            File.AppendAllText(sessionLogPath, jsonLine + "\n");
        }
    }
}
