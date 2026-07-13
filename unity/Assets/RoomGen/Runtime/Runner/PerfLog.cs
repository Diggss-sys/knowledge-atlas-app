using System.Globalization;
using System.IO;
using System.Text;
using RoomGen.Metrics;

namespace RoomGen.Runner
{
    /// <summary>
    /// Per-trial frame-rate log — a SIDECAR to the response CSV (never part of it: response_log.schema
    /// is a frozen contract). One row per rated trial, keyed by session_id + trial_index so it joins
    /// straight onto the response rows, and carrying the condition so the per-condition fps delta (the
    /// confound check in docs/PERFORMANCE.md) is a one-line group-by. Written silently — the
    /// participant never sees a frame counter (that would be a distraction/confound); this is for the
    /// researcher and for Michael's Mac smoke test.
    /// </summary>
    public sealed class PerfLog
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        readonly string _path;
        public string Path => _path;
        public int WrittenCount { get; private set; }

        public PerfLog(string path) { _path = path; }

        public static string Header() =>
            "session_id,participant_id,study_id,trial_index,condition,avg_fps,min_fps,hitch_count,frame_count,duration_s";

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
                s.DurationSeconds.ToString("0.00", inv));
            File.AppendAllText(_path, line + "\n", Utf8NoBom);
            WrittenCount++;
        }

        // These fields are safe tokens (GUID, sanitized id, enum, study id), but quote defensively so a
        // stray comma can never shift a column.
        static string Cell(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0
                ? "\"" + v.Replace("\"", "\"\"") + "\""
                : v;
        }
    }
}
