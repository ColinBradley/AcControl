using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class HistoryEventRecording
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}
