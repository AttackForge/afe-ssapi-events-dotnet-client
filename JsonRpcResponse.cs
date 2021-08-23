using System.Text.Json;

namespace com.attackforge
{
    public class JsonRpcResponse
    {
        public JsonElement? Result { get; set; }

        public JsonElement? Error { get; set; }

        public string Id { get; set; }
    }
}
