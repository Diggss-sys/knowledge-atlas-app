using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoomGen.Contracts
{
    /// <summary>
    /// Content-addressing helper for contract JSON. Object keys are sorted recursively, arrays retain
    /// their authored order, and insignificant whitespace is removed before hashing.
    /// </summary>
    public static class CanonicalJson
    {
        public static string Normalize(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None
            };
            return SortToken(JToken.ReadFrom(reader)).ToString(Formatting.None);
        }

        public static string Sha256(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(Normalize(json));
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var text = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        /// <summary>Hash one object-valued property without exposing Newtonsoft types to callers.</summary>
        public static string Sha256Property(string json, string propertyName)
        {
            using var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None
            };
            if (!(JToken.ReadFrom(reader) is JObject root) || root[propertyName] == null)
                throw new InvalidDataException("JSON property is missing: " + propertyName);
            return Sha256(root[propertyName].ToString(Formatting.None));
        }

        static JToken SortToken(JToken token)
        {
            if (token is JObject obj)
            {
                var sorted = new JObject();
                foreach (var property in obj.Properties().OrderBy(p => p.Name, System.StringComparer.Ordinal))
                    sorted.Add(property.Name, SortToken(property.Value));
                return sorted;
            }

            if (token is JArray array)
                return new JArray(array.Select(SortToken));

            return token.DeepClone();
        }
    }
}
