using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace RoomGen.Gate
{
    /// <summary>
    /// A compact, dependency-free JSON-Schema validator covering exactly the Draft 2020-12 keywords
    /// used by spec/room_spec.schema.json: type / const / enum / minimum / maximum / minItems /
    /// maxItems / pattern / required / additionalProperties(false) / properties / items / $ref /
    /// oneOf. It is NOT a general validator — it is the faithful subset the pair gate needs so the
    /// Unity side agrees with Diego's Python jsonschema on which specs are schema-valid. Unknown
    /// keywords are ignored (permissive), matching "validate against THIS schema".
    /// </summary>
    public static class JsonSchemaLite
    {
        public struct SchemaError
        {
            public string Path;
            public string Message;
            public SchemaError(string path, string message) { Path = path; Message = message; }
        }

        public static List<SchemaError> Validate(JToken instance, JObject schema)
        {
            var errors = new List<SchemaError>();
            ValidateNode(instance, schema, schema, "$", errors);
            return errors;
        }

        static void ValidateNode(JToken inst, JObject schema, JObject root, string path, List<SchemaError> errs)
        {
            // $ref — resolve within the root document and validate against the target (siblings ignored;
            // in this schema the only $ref siblings are "description").
            if (schema["$ref"] is JToken refTok)
            {
                var resolved = ResolveRef((string)refTok, root);
                if (resolved != null) ValidateNode(inst, resolved, root, path, errs);
                return;
            }

            var typeTok = schema["type"];
            if (typeTok != null && !TypeMatches(inst, (string)typeTok))
            {
                errs.Add(new SchemaError(path, $"expected type '{typeTok}', got '{inst.Type}'"));
                return; // downstream keywords assume the correct type
            }

            if (schema["const"] is JToken constTok && !JToken.DeepEquals(inst, constTok))
                errs.Add(new SchemaError(path, $"expected const {constTok}, got {inst}"));

            if (schema["enum"] is JArray enumArr && !enumArr.Any(e => JToken.DeepEquals(e, inst)))
                errs.Add(new SchemaError(path, $"{inst} is not one of the permitted values"));

            if (inst.Type == JTokenType.Integer || inst.Type == JTokenType.Float)
            {
                var value = inst.Value<double>();
                if (schema["minimum"] != null && value < schema["minimum"].Value<double>())
                    errs.Add(new SchemaError(path, $"{value} is below minimum {schema["minimum"]}"));
                if (schema["maximum"] != null && value > schema["maximum"].Value<double>())
                    errs.Add(new SchemaError(path, $"{value} is above maximum {schema["maximum"]}"));
            }

            if (inst.Type == JTokenType.String && schema["pattern"] is JToken patTok)
            {
                var text = (string)inst;
                if (!Regex.IsMatch(text, (string)patTok))
                    errs.Add(new SchemaError(path, $"'{text}' does not match pattern {patTok}"));
            }

            if (inst is JObject io && (typeTok == null || (string)typeTok == "object"))
            {
                var props = schema["properties"] as JObject;
                if (schema["required"] is JArray required)
                    foreach (var rq in required)
                        if (io[(string)rq] == null)
                            errs.Add(new SchemaError(path, $"missing required property '{rq}'"));

                var ap = schema["additionalProperties"];
                var allowAdditional = !(ap != null && ap.Type == JTokenType.Boolean && !ap.Value<bool>());
                foreach (var member in io.Properties())
                {
                    if (props?[member.Name] is JObject childSchema)
                        ValidateNode(member.Value, childSchema, root, path + "." + member.Name, errs);
                    else if (!allowAdditional)
                        errs.Add(new SchemaError(path, $"additional property '{member.Name}' is not allowed"));
                }
            }

            if (inst is JArray ia && (typeTok == null || (string)typeTok == "array"))
            {
                if (schema["minItems"] != null && ia.Count < schema["minItems"].Value<int>())
                    errs.Add(new SchemaError(path, $"array has {ia.Count} items, fewer than minItems"));
                if (schema["maxItems"] != null && ia.Count > schema["maxItems"].Value<int>())
                    errs.Add(new SchemaError(path, $"array has {ia.Count} items, more than maxItems"));
                if (schema["items"] is JObject itemSchema)
                    for (var i = 0; i < ia.Count; i++)
                        ValidateNode(ia[i], itemSchema, root, $"{path}[{i}]", errs);
            }

            if (schema["oneOf"] is JArray oneOf)
            {
                var matches = 0;
                foreach (var sub in oneOf.OfType<JObject>())
                {
                    var subErrs = new List<SchemaError>();
                    ValidateNode(inst, sub, root, path, subErrs);
                    if (subErrs.Count == 0) matches++;
                }
                if (matches != 1)
                    errs.Add(new SchemaError(path, $"matched {matches} of the oneOf branches (exactly 1 required)"));
            }
        }

        static bool TypeMatches(JToken t, string type)
        {
            switch (type)
            {
                case "object":  return t.Type == JTokenType.Object;
                case "array":   return t.Type == JTokenType.Array;
                case "string":  return t.Type == JTokenType.String;
                case "boolean": return t.Type == JTokenType.Boolean;
                case "null":    return t.Type == JTokenType.Null;
                case "number":  return t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
                case "integer":
                    if (t.Type == JTokenType.Integer) return true;
                    if (t.Type == JTokenType.Float)
                    {
                        var v = t.Value<double>();
                        return Math.Abs(v - Math.Truncate(v)) < double.Epsilon;
                    }
                    return false;
                default: return true; // unknown type name -> don't assert
            }
        }

        static JObject ResolveRef(string reference, JObject root)
        {
            if (string.IsNullOrEmpty(reference) || reference[0] != '#') return null;
            JToken node = root;
            foreach (var raw in reference.Substring(1).Split('/'))
            {
                if (raw.Length == 0) continue;
                var token = raw.Replace("~1", "/").Replace("~0", "~");
                node = node?[token];
            }
            return node as JObject;
        }
    }
}
