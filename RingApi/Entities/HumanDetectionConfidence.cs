using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class HumanDetectionConfidence
    {
        [JsonPropertyName("day")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]

        public double? Day { get; set; }

        [JsonPropertyName("night")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Night { get; set; }
    }
}
