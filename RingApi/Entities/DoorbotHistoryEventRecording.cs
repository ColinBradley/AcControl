using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class DoorbotHistoryEventRecording
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}
