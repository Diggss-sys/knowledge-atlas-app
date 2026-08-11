using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RoomGen.Contracts
{
    /// <summary>
    /// Content-addressing helper for contract JSON. Object keys are sorted recursively using ordinal
    /// order, arrays retain authored order, insignificant whitespace is removed, and integral floating
    /// tokens that fit Int64 normalize to integers (so 1 and 1.0 address the same payload). Remaining
    /// numbers use Newtonsoft's invariant JSON representation. This is the repository's canonicalization
    /// contract; external verifiers must reproduce these rules before comparing SHA-256 values.
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

            if (token is JValue value && token.Type == JTokenType.Float)
            {
                var number = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
                if (!double.IsNaN(number) && !double.IsInfinity(number) &&
                    number == Math.Truncate(number) && number >= long.MinValue && number <= long.MaxValue)
                    return new JValue((long)number);
            }

            return token.DeepClone();
        }
    }
}
