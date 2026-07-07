using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RoomGen.Gate;

namespace RoomGen.Runner
{
    /// <summary>
    /// Validates a response row against spec/response_log.schema.json using the same compact
    /// Draft-2020 subset validator (<see cref="JsonSchemaLite"/>) that gates room specs and seam
    /// messages. The runner validates BEFORE writing the local CSV (data-honesty rule 6).
    /// </summary>
    public static class ResponseLogValidator
    {
        public static IReadOnlyList<string> Validate(JObject row, JObject schema)
            => JsonSchemaLite.Validate(row, schema).Select(e => e.Path + ": " + e.Message).ToList();

        public static bool IsValid(JObject row, JObject schema) => Validate(row, schema).Count == 0;
    }
}
