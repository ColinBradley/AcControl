namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;
using AcControl.Server.Utils;
using KoenZomers.Ring.Api.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Session = KoenZomers.Ring.Api.Session;
using SessionInformation = KoenZomers.Ring.Api.Entities.Session;

public class RingDevicesService : IDisposable
{
    private readonly IConfiguration mConfig;
    private readonly IHttpClientFactory mHttpClientFactory;
    private readonly Debouncer mUpdateDebouncer;

    private int mSubscriptionCount = 0;

    private bool mIsDisposed;

    public delegate void ChangedEventHandler();

    /// <summary>
    /// Just a general anything changed at all event to know when to update the UI.
    /// Ideally, this'd just be scoped to collection changes, but ANGTFT.
    /// </summary>
    public event ChangedEventHandler? AnythingChanged;

    public RingDevicesService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        mConfig = configuration;
        mHttpClientFactory = httpClientFactory;

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

                    await this.UpdateDevices();
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
        _ = await this.Session.Authenticate(twoFactorAuthCode: this.AuthCode);
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
        foreach (var doorBot in devices.Doorbots)
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
                    matchingDevice.Events.Add(RingDeviceHistoryEvent.From(historyEvent));
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

        var result = await SneakyRingApi.GetDeviceHistory(mHttpClientFactory, device.Id.ToString(), this.CsrfToken, this.SessionToken);

        if (result is null)
        {
            return;
        }

        var anythingChanged = false;

        var deviceEventsById = device.Events.ToDictionary(e => e.Id);
        foreach(var historyEvent in result.items)
        {
            if (!deviceEventsById.TryGetValue(historyEvent.event_id, out var deviceEvent))
            {
                deviceEvent = new RingDeviceHistoryEvent(historyEvent.event_id, historyEvent.event_type, historyEvent.start_time);
                device.Events.Add(deviceEvent);

                anythingChanged = true;
            }

            var videoUrl = historyEvent.visualizations.cloud_media_visualization.media.FirstOrDefault(m => m.file_family == "VIDEO")?.url;
            var thumbnailUrl = historyEvent.visualizations.cloud_media_visualization.media.FirstOrDefault(m => m.file_family == "THUMBNAIL")?.url;

            if (videoUrl != deviceEvent.VideoUrl)
            {
                deviceEvent.VideoUrl = videoUrl;
                anythingChanged = true;
            }

            if (thumbnailUrl != deviceEvent.ThumbnailUrl)
            {
                deviceEvent.ThumbnailUrl = thumbnailUrl;
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
