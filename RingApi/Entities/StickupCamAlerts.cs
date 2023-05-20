using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class DoorbotAlerts
    {
        [JsonPropertyName("connection")]
        public string Connection { get; set; }
    }
}
