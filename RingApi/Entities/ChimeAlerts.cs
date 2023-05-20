using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class ChimeAlerts
    {
        [JsonPropertyName("connection")]
        public string Connection { get; set; }
    }
}
