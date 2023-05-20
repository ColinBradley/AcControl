using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class DoNotDisturb
    {
        [JsonPropertyName("seconds_left")]
        public double? SecondsLeft { get; set; }
    }
}
