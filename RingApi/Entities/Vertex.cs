using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class Vertex
    {
        [JsonPropertyName("x")]
        public double? X { get; set; }

        [JsonPropertyName("y")]
        public double? Y { get; set; }
    }
}
