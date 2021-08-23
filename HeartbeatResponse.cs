using System.Text.Json.Serialization;

namespace com.attackforge
{
    public class HeartbeatResponse
    {

        [JsonPropertyName("jsonrpc")]
        public string JsonRPC { get; set; }

        [JsonPropertyName("result")]
        public string Result { get; set; }

        [JsonPropertyName("id")]
        public string ID { get; set; }
    }
}
