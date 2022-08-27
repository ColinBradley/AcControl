namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;
using AcControl.Server.Utils;
using KoenZomers.Ring.Api.Entities;
using Microsoft.Graph;
using System.Collections.ObjectModel;
using System.Net;
using Session = KoenZomers.Ring.Api.Session;
using SessionInformation = KoenZomers.Ring.Api.Entities.Session;

public class RingDevicesService : IDisposable
{
    private readonly IConfiguration mConfig;

    private readonly Debouncer mUpdateDebouncer;

    private int mSubscriptionCount = 0;

    private bool mIsDisposed;

    public delegate void ChangedEventHandler();
    public event ChangedEventHandler? Changed;

    public RingDevicesService(IConfiguration configuration)
    {
        mConfig = configuration;

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
                matchingDevice.LatestSnapshotTime = latestTimestamp;
                
                try
                {
                    using var latestSnapshot = await this.Session.GetLatestSnapshot(doorBot.Id);
                    using var latestSnapshotMemoryStream = new MemoryStream();
                    await latestSnapshot.CopyToAsync(latestSnapshotMemoryStream);
                    matchingDevice.LatestSnapshot = latestSnapshotMemoryStream.ToArray();
                }
                catch (WebException)
                {
                    // Meh.. probably a 404 as there's no image atm
                }
            }
        }

        if (!this.Devices.SequenceEqual(newDeviceList))
        {
            this.Devices = newDeviceList.ToArray();
            this.Changed?.Invoke();
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
