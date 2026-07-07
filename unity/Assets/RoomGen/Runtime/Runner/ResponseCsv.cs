using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace RoomGen.Runner
{
    /// <summary>
    /// Canonical CSV serialisation of a response row (spec/RESPONSE_LOG.md "Canonical CSV column
    /// order"). RFC-4180 quoting, UTF-8, LF; manipulated_variables is ';'-joined; response_json is
    /// the response object as a compact JSON string; empty optionals are empty strings, never "null".
    /// </summary>
    public static class ResponseCsv
    {
        public static readonly string[] Columns =
        {
            "schema_version", "study_id", "pair_id", "participant_id", "session_id", "trial_index",
            "task_type", "condition", "manipulated_variables", "modality", "execution_path",
            "response_json", "rt_ms", "timestamp_utc", "presentation_order_seed", "runner_version", "spec_sha256"
        };

        public static string Header() => string.Join(",", Columns);

        public static string[] ToColumns(ResponseRow row)
        {
            return new[]
            {
                row.SchemaVersion ?? "",
                row.StudyId ?? "",
                row.PairId ?? "",
                row.ParticipantId ?? "",
                row.SessionId ?? "",
                row.TrialIndex.ToString(CultureInfo.InvariantCulture),
                row.TaskType ?? "",
                row.Condition ?? "",
                string.Join(";", row.ManipulatedVariables ?? new List<string>()),
                row.Modality ?? "",
                row.ExecutionPath ?? "",
                row.Response != null ? row.Response.ToString(Formatting.None) : "",
                row.RtMs.HasValue ? row.RtMs.Value.ToString(CultureInfo.InvariantCulture) : "",
                row.TimestampUtc ?? "",
                row.PresentationOrderSeed.ToString(CultureInfo.InvariantCulture),
                row.RunnerVersion ?? "",
                row.SpecSha256 ?? ""
            };
        }

        public static string ToCsvLine(ResponseRow row) => string.Join(",", ToColumns(row).Select(Escape));

        static string Escape(string field)
        {
            field ??= "";
            var needsQuote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            return needsQuote ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
        }
    }
}
