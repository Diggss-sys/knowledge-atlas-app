using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoomGen.Runner
{
    /// <summary>
    /// Non-response session events live in a separate JSONL sidecar. They never enter the response
    /// JSONL mirror, so an aborted or degraded trial cannot masquerade as participant data.
    /// </summary>
    public sealed class SessionEventLog
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        readonly string _path;
        readonly Func<string> _nowUtc;

        public string Path => _path;
        public int WrittenCount { get; private set; }

        public SessionEventLog(string path, Func<string> nowUtc)
        {
            _path = path;
            _nowUtc = nowUtc ?? throw new ArgumentNullException(nameof(nowUtc));
        }

        public void Write(string kind, int? trialIndex = null, string detail = null)
        {
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Event kind is required.", nameof(kind));

            var row = new JObject
            {
                ["ts"] = _nowUtc(),
                ["kind"] = kind,
            };
            if (trialIndex.HasValue) row["trial_index"] = trialIndex.Value;
            if (!string.IsNullOrEmpty(detail)) row["detail"] = detail;

            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(_path, row.ToString(Formatting.None) + "\n", Utf8NoBom);
            WrittenCount++;
        }
    }
}
