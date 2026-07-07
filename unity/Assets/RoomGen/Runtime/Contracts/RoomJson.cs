using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace RoomGen.Contracts
{
    public static class RoomJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Error,
            ContractResolver = new DefaultContractResolver()
        };

        public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, Settings);
        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
        public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));
    }
}
