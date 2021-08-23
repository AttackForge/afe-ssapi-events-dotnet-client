using System.Text.Json;
using System.Text.Json.Serialization;

namespace com.attackforge
{
    public class SsApiEventParams
    {
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; }

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }
    }
}
