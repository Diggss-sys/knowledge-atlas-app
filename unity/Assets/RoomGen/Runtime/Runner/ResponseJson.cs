using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoomGen.Runner
{
    /// <summary>
    /// Parses response-log JSON WITHOUT Newtonsoft's default ISO-8601 date coercion. `timestamp_utc`
    /// is an ISO string in the contract; the default parser would turn it into a Date token, which
    /// then fails the schema's `type: string` check (and any deep-equality against a builder row).
    /// </summary>
    public static class ResponseJson
    {
        public static JObject Parse(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }
    }
}
