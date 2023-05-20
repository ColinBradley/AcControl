using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class StickupCamAlerts
    {
        [JsonPropertyName("connection")]
        public string Connection { get; set; }
    }
}
