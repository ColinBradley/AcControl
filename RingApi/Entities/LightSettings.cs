using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class LightSettings
    {
        [JsonPropertyName("brightness")]
        public long? Brightness { get; set; }
    }
}
