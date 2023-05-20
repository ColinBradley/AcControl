using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class Session
    {
        [JsonPropertyName("profile")]
        public Profile Profile { get; set; }
    }
}