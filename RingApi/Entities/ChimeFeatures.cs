using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class ChimeFeatures
    {
        [JsonPropertyName("ringtones_enabled")]
        public bool RingtonesEnabled { get; set; }
    }
}
