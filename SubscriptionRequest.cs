using System.Text.Json.Serialization;

namespace com.attackforge
{
    public class SubscriptionRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRPC { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("params")]
        public SubscriptionParams Params { get; set; }

        [JsonPropertyName("id")]
        public string ID { get; set; }
    }
}
