namespace AcControl.Server.Data;

using KoenZomers.Ring.Api.Entities;
using System.Net.Http;
using System.Net.Http.Headers;

public static class SneakyRingApi
{
    public static async Task<DeviceHistoryResponse?> GetDeviceHistory(IHttpClientFactory httpClientFactory, string deviceId, string csrfToken, string sessionToken)
    {
        using var httpClient = httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://account.ring.com/api/cgw/evm/v2/history/devices/{deviceId}?capabilities=offline_event,mobility,ringtercom&limit=50"));
        request.Headers.Add("csrf-token", csrfToken);
        request.Headers.Add("cookie", $"rs_session={sessionToken}");
        
        using var httpResult = await httpClient.SendAsync(request);
        
        return await httpResult.Content.ReadFromJsonAsync<DeviceHistoryResponse>();
    }
}

public class CloudMediaVisualization
{
    public string schema { get; set; }
    public List<Medium> media { get; set; }
}

public class CustomMetadata
{
    public string audio_encoding { get; set; }
    public string video_encoding { get; set; }
}

public class Cv
{
    public object person_detected { get; set; }
    public bool stream_broken { get; set; }
    public object detection_type { get; set; }
    public object cv_triggers { get; set; }
    public object detection_types { get; set; }
}

public class Device
{
    public int id { get; set; }
    public string description { get; set; }
    public string type { get; set; }
}

public class Item
{
    public string schema { get; set; }
    public string source_id { get; set; }
    public string event_id { get; set; }
    public DateTime start_time { get; set; }
    public DateTime end_time { get; set; }
    public string event_type { get; set; }
    public object source_type { get; set; }
    public int duration_ms { get; set; }
    public int session_duration { get; set; }
    public string state { get; set; }
    public bool had_subscription { get; set; }
    public bool is_favorite { get; set; }
    public string recording_status { get; set; }
    public Cv cv { get; set; }
    public Properties properties { get; set; }
    public object origin { get; set; }
    public object error_message { get; set; }
    public DateTime updated_at { get; set; }
    public Visualizations visualizations { get; set; }
    public Device device { get; set; }
    public string owner_id { get; set; }
}

public class LocalMediaVisualization
{
    public string schema { get; set; }
    public List<object> media { get; set; }
}

public class MapVisualization
{
    public string schema { get; set; }
    public object id { get; set; }
    public double zoom_level { get; set; }
    public string map_type { get; set; }
    public int bearing { get; set; }
    public double latitude { get; set; }
    public double longitude { get; set; }
}

public class Medium
{
    public string schema { get; set; }
    public string url { get; set; }
    public CustomMetadata custom_metadata { get; set; }
    public bool is_e2ee { get; set; }
    public object manifest_id { get; set; }
    public string file_type { get; set; }
    public string file_family { get; set; }
    public int preroll_duration_ms { get; set; }
    public int playback_duration { get; set; }
    public string source { get; set; }
}

public class Properties
{
    public bool is_alexa { get; set; }
    public bool is_sidewalk { get; set; }
    public bool is_autoreply { get; set; }
}

public class RadarVisualization
{
    public string schema { get; set; }
    public string custom_metadata { get; set; }
    public string url { get; set; }
    public string source { get; set; }
}

public class DeviceHistoryResponse
{
    public List<Item> items { get; set; }
    public string pagination_key { get; set; }
}

public class Visualizations
{
    public CloudMediaVisualization cloud_media_visualization { get; set; }
    public LocalMediaVisualization local_media_visualization { get; set; }
    public RadarVisualization radar_visualization { get; set; }
    public object single_coordinate_visualization { get; set; }
    public MapVisualization map_visualization { get; set; }
}
