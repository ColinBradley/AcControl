using System.Text.Json.Serialization;

namespace RingApi.Entities
{
    public class DeviceStatus
    {
        /// <summary>
        /// Contains the status of a Ring device
        /// </summary>
        public partial class Status
        {
            [JsonPropertyName("seconds_remaining")]
            public long? SecondsRemaining { get; set; }
        }
    }
}
