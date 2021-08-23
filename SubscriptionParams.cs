using System.Text.Json.Serialization;

namespace com.attackforge
{
    public class SubscriptionParams
    {
        [JsonPropertyName("events")]
        public string[] Events { get; set; }

        [JsonPropertyName("from")]
        public string From { get; set; }
    }
}
