using System.Globalization;
using System.IO;
using System.Text;
using RoomGen.Metrics;

namespace RoomGen.Runner
{
    /// <summary>Which machine a session ran on — stamped into every perf row so a multi-machine sweep
    /// (the whole point of the team run) is attributable. Model/GPU/OS only, never a user-set device
    /// NAME, to avoid carrying someone's "Paco's MacBook" into the data.</summary>
    public readonly struct MachineInfo
    {
        public readonly string Gpu, Os, Device;
        public MachineInfo(string gpu, string os, string device) { Gpu = gpu; Os = os; Device = device; }

        public static MachineInfo Current() => new MachineInfo(
            UnityEngine.SystemInfo.graphicsDeviceName,
            UnityEngine.SystemInfo.operatingSystem,
            UnityEngine.SystemInfo.deviceModel);

        public static readonly MachineInfo Unknown = new MachineInfo("unknown", "unknown", "unknown");
    }

    /// <summary>
    /// Per-trial frame-rate log — a SIDECAR to the response CSV (never part of it: response_log.schema
    /// is a frozen contract). One row per rated trial, keyed by session_id + trial_index so it joins
    /// straight onto the response rows, carrying the condition (for the per-condition delta check in
    /// docs/PERFORMANCE.md) and the machine (for the cross-machine sweep). Written silently — the
    /// participant never sees a frame counter (that would be a distraction/confound); this is for the
    /// researcher and for Michael's Mac smoke test.
    /// </summary>
    public sealed class PerfLog
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        readonly string _path;
        readonly MachineInfo _machine;
        public string Path => _path;
        public int WrittenCount { get; private set; }

        public PerfLog(string path, MachineInfo machine)
        {
            _path = path;
            _machine = machine;
        }

        public static string Header() =>
            "session_id,participant_id,study_id,trial_index,condition,avg_fps,min_fps,hitch_count,frame_count,duration_s,gpu,os,device";

        public void Write(string sessionId, string participantId, string studyId,
            int trialIndex, string condition, PerfSample s)
        {
            if (!File.Exists(_path) || new FileInfo(_path).Length == 0)
                File.AppendAllText(_path, Header() + "\n", Utf8NoBom);

            var inv = CultureInfo.InvariantCulture;
            var line = string.Join(",",
                Cell(sessionId), Cell(participantId), Cell(studyId),
                trialIndex.ToString(inv), Cell(condition),
                s.AvgFps.ToString("0.0", inv), s.MinFps.ToString("0.0", inv),
                s.HitchCount.ToString(inv), s.FrameCount.ToString(inv),
                s.DurationSeconds.ToString("0.00", inv),
                Cell(_machine.Gpu), Cell(_machine.Os), Cell(_machine.Device));
            File.AppendAllText(_path, line + "\n", Utf8NoBom);
            WrittenCount++;
        }

        // Quote defensively so a stray comma (a GPU string can contain them) never shifts a column.
        static string Cell(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0
                ? "\"" + v.Replace("\"", "\"\"") + "\""
                : v;
        }
    }
}
