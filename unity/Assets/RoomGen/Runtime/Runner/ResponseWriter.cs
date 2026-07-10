using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RoomGen.Runner
{
    /// <summary>
    /// Persists response rows immediately (RESPONSE_LOG.md rule 1): validate, then append to the
    /// local CSV (canonical order, header on first write) and a JSONL mirror. A row that fails
    /// validation is REFUSED (never written) and its errors returned — the sink is the last honesty
    /// gate before data hits disk. The queued POST path (R3) wraps this same validated row.
    /// </summary>
    public sealed class ResponseWriter
    {
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        readonly string _csvPath;
        readonly string _jsonlPath;
        readonly JObject _schema;

        public int WrittenCount { get; private set; }
        public string CsvPath => _csvPath;

        public ResponseWriter(string csvPath, string jsonlPath, JObject schema)
        {
            _csvPath = csvPath;
            _jsonlPath = jsonlPath;
            _schema = schema;
        }

        public static JObject LoadSchema()
        {
            var text = Resources.Load<TextAsset>("RoomGen/response_log.schema");
            return text != null ? ResponseJson.Parse(text.text) : null;
        }

        /// <summary>
        /// Writer over the bundled contract schema. Tests use this so they never name a JObject —
        /// the EditMode test assembly cannot reference Newtonsoft (see PairGateFixtureRunner).
        /// </summary>
        public static ResponseWriter CreateDefault(string csvPath, string jsonlPath)
            => new ResponseWriter(csvPath, jsonlPath, LoadSchema());

        /// <summary>Validates and appends. Returns validation errors; empty means the row was written.</summary>
        public IReadOnlyList<string> Write(ResponseRow row)
        {
            var json = row.ToJson();
            var errors = new List<string>(ResponseLogValidator.Validate(json, _schema));
            errors.AddRange(SemanticErrors(json));
            if (errors.Count > 0)
            {
                Debug.LogWarning($"ResponseWriter refused a row: {string.Join(" | ", errors)}");
                return errors;
            }

            if (!File.Exists(_csvPath) || new FileInfo(_csvPath).Length == 0)
                File.AppendAllText(_csvPath, ResponseCsv.Header() + "\n", Utf8NoBom);
            File.AppendAllText(_csvPath, ResponseCsv.ToCsvLine(row) + "\n", Utf8NoBom);
            File.AppendAllText(_jsonlPath, json.ToString(Formatting.None) + "\n", Utf8NoBom);
            WrittenCount++;
            return errors;
        }

        // Task-payload rules the JSON schema cannot express ("runner-enforced" in the contract).
        // The sink REFUSES violations rather than repairing them: silently altering a response
        // value would be data fabrication, and repair here would mask task-screen bugs.
        static IEnumerable<string> SemanticErrors(JObject row)
        {
            var taskType = (string)row["task_type"];
            var response = row["response"] as JObject;
            if (response == null) yield break;

            if (taskType == "rating")
            {
                var min = (int?)response["scale_min"];
                var max = (int?)response["scale_max"];
                var value = (int?)response["value"];
                if (min.HasValue && max.HasValue && max.Value < min.Value)
                    yield return $"$.response: scale_max {max} < scale_min {min}";
                else if (min.HasValue && max.HasValue && value.HasValue &&
                         (value.Value < min.Value || value.Value > max.Value))
                    yield return $"$.response.value: {value} outside [{min}, {max}] (runner-enforced range)";
            }
            else if (taskType == "choice")
            {
                var left = (string)response["left_condition"];
                var right = (string)response["right_condition"];
                if (left != null && left == right)
                    yield return $"$.response: left_condition == right_condition ('{left}') — counterbalancing broken";
            }
        }
    }
}
