namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;
using AcControl.Server.Services;
using AcControl.Server.Utils;
using KoenZomers.Ring.Api.Entities;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Session = KoenZomers.Ring.Api.Session;

public class RingDevicesService : IDisposable
{
    private readonly IConfiguration mConfig;
    private readonly IHttpClientFactory mHttpClientFactory;
    private readonly ApplicationService mAppService;

    private readonly Debouncer mUpdateDebouncer;

    private int mSubscriptionCount = 0;

    private bool mIsDisposed;

    public delegate void ChangedEventHandler();

    /// <summary>
    /// Just a general anything changed at all event to know when to update the UI.
    /// Ideally, this'd just be scoped to collection changes, but ANGTFT.
    /// </summary>
    public event ChangedEventHandler? AnythingChanged;

    public RingDevicesService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ApplicationService appService)
    {
        mConfig = configuration;
        mHttpClientFactory = httpClientFactory;
        mAppService = appService;

        this.CsrfToken = mConfig.GetValue<string?>("Ring:CsrfToken");
        this.SessionToken = mConfig.GetValue<string?>("Ring:SessionToken");

        var session = new Session(
                mConfig.GetValue<string>("Ring:Username"),
                mConfig.GetValue<string>("Ring:Password"));

        var accessToken = mConfig.GetValue<string?>("Ring:AccessToken");
        var refreshToken = mConfig.GetValue<string?>("Ring:RefreshToken");

        if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
        {
            typeof(Session).GetProperty("OAuthToken")!.SetValue(session, new OAutToken()
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                Scope = "client",
                TokenType = "Bearer",
                ExpiresInSeconds = -1,
            });
        }

        this.Session = session;

        mUpdateDebouncer = new Debouncer(
                async () =>
                {
                    if (mSubscriptionCount <= 0 || mIsDisposed)
                    {
                        return;
                    }

                    try
                    {
                        await this.UpdateDevices();
                    }
                    catch
                    {
                        mUpdateDebouncer?.Pause();
                    }
                },
                5_000
            );
    }

    public string? AuthCode { get; set; }

    public string? CsrfToken { get; set; }

    public string? SessionToken { get; set; }

    public Session Session { get; private set; }

    public RingDeviceModel[] Devices { get; set; } = Array.Empty<RingDeviceModel>();

    public async Task TryAuth()
    {
        _ = this.AuthCode is null ? 
            await this.Session.Authenticate() : 
            await this.Session.Authenticate(twoFactorAuthCode: this.AuthCode);

        await mAppService.UpdateConfiguration(new Dictionary<string, string>() { 
            { "Ring:AccessToken", this.Session.OAuthToken.AccessToken },
            { "Ring:RefreshToken", this.Session.OAuthToken.RefreshToken } ,
        });
    }

    public async Task Subscribe()
    {
        if (Interlocked.Increment(ref mSubscriptionCount) != 1)
        {
            return;
        }

        await this.UpdateDevices();
    }

    public void Unsubscribe()
    {
        if (Interlocked.Decrement(ref mSubscriptionCount) != 0)
        {
            return;
        }

        mUpdateDebouncer.Pause();
    }

    public async Task UpdateDevices()
    {
        mUpdateDebouncer.Ping();

        var newDeviceList = new List<RingDeviceModel>();

        var devices = await this.Session.GetRingDevices();
        var doorbots = devices.Doorbots.Select(d => new { d.Id, d.Description, Type = "doorbot" });
        var cams = devices.StickupCams.Select(d => new { Id = Convert.ToInt32(d.Id!.Value), d.Description, Type = "cam" });
        foreach (var doorBot in doorbots.Concat(cams))
        {
            var matchingDevice = this.Devices.FirstOrDefault(device => device.Id == doorBot.Id);
            if (matchingDevice is null)
            {
                matchingDevice = new RingDeviceModel(doorBot.Id, doorBot.Description);
                
                // Note we don't have to worry about cleaning this up as we live longer
                matchingDevice.PropertyChanged += this.RingDeviceModel_Changed;
                
                await this.Session.UpdateSnapshot(doorBot.Id);
            }
            else
            {
                matchingDevice.Description = doorBot.Description;
            }

            newDeviceList.Add(matchingDevice);
            
            var timestamps = await this.Session.GetDoorbotSnapshotTimestamp(doorBot.Id);
            var latestTimestamp = timestamps.Timestamp.LastOrDefault()?.Timestamp;
            if (latestTimestamp is not null && matchingDevice.LatestSnapshotTime != latestTimestamp)
            {
                try
                {
                    using var latestSnapshot = await this.Session.GetLatestSnapshot(doorBot.Id);
                    using var latestSnapshotMemoryStream = new MemoryStream();
                    await latestSnapshot.CopyToAsync(latestSnapshotMemoryStream);
                    matchingDevice.LatestSnapshot = latestSnapshotMemoryStream.ToArray();
                    
                    // URLs are based on this, so update it last so that we don't fetch previous byte values
                    matchingDevice.LatestSnapshotTime = latestTimestamp;
                }
                catch (WebException)
                {
                    // Meh.. probably a 404 as there's no image atm
                }
            }

            if (string.IsNullOrEmpty(this.CsrfToken) || string.IsNullOrEmpty(this.SessionToken))
            {
                var lastEventTime = matchingDevice.Events.MaxBy(e => e.CreatedAtDateTime)?.CreatedAtDateTime;
                var historyEvents = lastEventTime is not null
                    ? await this.Session.GetDoorbotsHistory(startDate: lastEventTime.Value.AddSeconds(1), endDate: null, doorbotId: doorBot.Id)
                    : await this.Session.GetDoorbotsHistory(doorbotId: doorBot.Id);

                foreach (var historyEvent in historyEvents)
                {
                    matchingDevice.Events.Add(historyEvent);
                }
            }
            else
            {
                await this.TryEnrichDeviceHistory(matchingDevice);
            }
        }

        if (!this.Devices.SequenceEqual(newDeviceList))
        {
            this.Devices = newDeviceList.ToArray();
            this.AnythingChanged?.Invoke();
        }
    }

    /// <summary>
    /// Gets a very short lived link to a video of a ding event.
    /// </summary>
    public async Task<string?> GetEventVideoUrl(string dingId)
    {
        using var httpClient = mHttpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(this.Session.RingApiBaseUrl, "dings/" + dingId + "/recording?disable_redirect=true"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.Session.AuthenticationToken);
        var httpResult = await httpClient.SendAsync(request);
        var result = await httpResult.Content.ReadFromJsonAsync<DownloadRecording>();

        return result?.Url;
    }

    public async Task<LiveViewStartResult> StartLiveView(int deviceId, string sdp)
    {
        using var httpClient = mHttpClientFactory.CreateClient();
        var riid = "feb5f58f043b9b2c";
        var sessionId = Guid.NewGuid().ToString();

        // Ensure there is a call to fetch (this is okay to call multiple times - it de-dupes)
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://account.ring.com/api/cgw/integrations/v1/liveview/start"));
        request.Headers.Add("csrf-token", this.CsrfToken);
        request.Headers.Add("cookie", $"rs_session={this.SessionToken}");
        request.Headers.Add("riid", riid);
        request.Content = JsonContent.Create(new { 
            device_id = deviceId,
            protococl = "webrtc",
            riid,
            sdp,
            session_id = sessionId,
        });

        var httpResult = await httpClient.SendAsync(request);
        var content = await httpResult.Content.ReadFromJsonAsync<LiveViewStartResult>();

        if (content is null)
        {
            throw new Exception("Unexpceted failure.");
        }

        return content;
    }

    public async Task<DeviceVodDing> GetDeviceCallInformation(int deviceId)
    {
        using var httpClient = mHttpClientFactory.CreateClient();

        // Ensure there is a call to fetch (this is okay to call multiple times - it de-dupes)
        var vodRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(this.Session.RingApiBaseUrl, "doorbots/" + deviceId.ToString() + "/vod"));
        vodRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.Session.AuthenticationToken);
        var vodHttpResult = await httpClient.SendAsync(vodRequest);
        if (vodHttpResult.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception("Computer says no");
        }

        // Retrieve it from active "dings"
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var dingsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(this.Session.RingApiBaseUrl, "dings/active"));
            dingsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.Session.AuthenticationToken);
            var dingsHttpResult = await httpClient.SendAsync(dingsRequest);
            var activeDings = await dingsHttpResult.Content.ReadFromJsonAsync<DeviceVodDing[]>();

            var matchingDing = activeDings?.FirstOrDefault(ding => ding.DoorbotId == deviceId);

            if (matchingDing is not null)
            {
                return matchingDing;
            }

            await Task.Delay(TimeSpan.FromSeconds(0.5));
        }

        throw new Exception("Unable to find matching ding in time");
    }

    private void RingDeviceModel_Changed(object? sender, PropertyChangedEventArgs e)
    {
        this.AnythingChanged?.Invoke();
    }

    private async Task TryEnrichDeviceHistory(RingDeviceModel device)
    {
        if (string.IsNullOrWhiteSpace(this.CsrfToken) || string.IsNullOrWhiteSpace(this.SessionToken))
        {
            return;
        }

        var events = await this.Session.GetDoorbotsHistory(doorbotId: device.Id, limit: 100);
        

        //var result = await SneakyRingApi.GetDeviceHistory(mHttpClientFactory, device.Id.ToString(), this.CsrfToken, this.SessionToken);

        //if (result is null)
        //{
        //    return;
        //}

        var anythingChanged = false;

        var deviceEventsById = device.Events.ToDictionary(e => e.Id!.Value);
        foreach(var historyEvent in events)
        {
            if (!deviceEventsById.TryGetValue(historyEvent.Id!.Value, out var deviceEvent))
            {
                device.Events.Add(historyEvent);

                anythingChanged = true;
            }
        }

        if (anythingChanged)
        {
            this.AnythingChanged?.Invoke();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (mIsDisposed)
        {
            return;
        }

        if (disposing)
        {
            mUpdateDebouncer.Dispose();
        }

        mIsDisposed = true;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class LiveViewStartResult
{
    [JsonPropertyName("sdp")]
    public string? Sdp { get; set; }
}

public class DeviceVodDing
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("id_str")]
    public string? IdStr { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("doorbot_id")]
    public int DoorbotId { get; set; }

    [JsonPropertyName("doorbot_description")]
    public string? DoorbotDescription { get; set; }

    [JsonPropertyName("device_kind")]
    public string? DeviceKind { get; set; }

    [JsonPropertyName("motion")]
    public bool Motion { get; set; }

    [JsonPropertyName("snapshot_url")]
    public string? SnapshotUrl { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("sip_server_ip")]
    public string? SipServerIp { get; set; }

    [JsonPropertyName("sip_server_port")]
    public int SipServerPort { get; set; }

    [JsonPropertyName("sip_server_tls")]
    public bool SipServerTls { get; set; }

    [JsonPropertyName("sip_session_id")]
    public string? SipSessionId { get; set; }

    [JsonPropertyName("sip_from")]
    public string? SipFrom { get; set; }

    [JsonPropertyName("sip_to")]
    public string? SipTo { get; set; }

    [JsonPropertyName("audio_jitter_buffer_ms")]
    public int AudioJitterBufferMs { get; set; }

    [JsonPropertyName("video_jitter_buffer_ms")]
    public int VideoJitterBufferMs { get; set; }

    [JsonPropertyName("sip_endpoints")]
    public object? SipEndpoints { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("now")]
    public double Now { get; set; }

    [JsonPropertyName("optimization_level")]
    public int OptimizationLevel { get; set; }

    [JsonPropertyName("sip_token")]
    public string? SipToken { get; set; }

    [JsonPropertyName("sip_ding_id")]
    public string? SipDingId { get; set; }

    [JsonPropertyName("detection_type")]
    public object? DetectionType { get; set; }

    [JsonPropertyName("ding_encrypted")]
    public bool DingEncrypted { get; set; }
}
