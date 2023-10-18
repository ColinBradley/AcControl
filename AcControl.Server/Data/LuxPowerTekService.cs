namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;

public class LuxPowerTekService : IDisposable
{
    public const string HTTP_CLIENT_NAME = "LuxPowerTek";

    private const string SESSION_COOKIE_PREFIX = "JSESSIONID";
    private readonly IConfiguration mConfig;
    private readonly IHttpClientFactory mClientFactory;

    private string? mSessionId = null;

    private readonly ConcurrentDictionary<string, Inverter> mInvertersBySerialNumber = new();

    private readonly Timer mUpdateTimer;
    private int mSubscriptionCount = 0;

    private bool mIsDisposed;
    private readonly CancellationTokenSource mDisposedCancelationSource = new();

    public delegate void ChangedEventHandler();
    public event ChangedEventHandler? Changed;

    public LuxPowerTekService(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        mConfig = config;
        mClientFactory = httpClientFactory;

        mUpdateTimer = new Timer(
            async (_) =>
            {
                if (mSubscriptionCount <= 0 || mIsDisposed)
                {
                    return;
                }

                await this.UpdateInverters(mDisposedCancelationSource.Token);
            });
    }

    public static string TodaysDateString => DateTime.UtcNow.ToString("yyyy-MM-dd");

    public Inverter[] Inverters => mInvertersBySerialNumber.Values.ToArray();

    public async Task<InverterDaySummaryPoint[]> GetDaySummary(Inverter inverter, string date, CancellationToken cancellationToken)
    {
        if (inverter.DaySummariesByDate.TryGetValue(date, out var existing))
        {
            return existing;
        }

        var result = await this.GetInverterDaySummary(inverter.InverterData.SerialNum, date, cancellationToken);
        if (result is null)
        {
            return Array.Empty<InverterDaySummaryPoint>();
        }

        _ = inverter.DaySummariesByDate.TryAdd(date, result);

        return result;
    }

    private async Task UpdateInverters(CancellationToken cancellationToken)
    {
        var inverters = await this.GetInverters(cancellationToken);
        if (inverters is null)
        {
            return;
        }

        var updated = false;
        await Parallel.ForEachAsync(inverters, cancellationToken, async (inverterData, inverterCancelationToken) =>
        {
            var isNewInverter = false;
            var inverter = mInvertersBySerialNumber.GetOrAdd(inverterData.SerialNum, (_) =>
            {
                isNewInverter = true;
                updated = true;
                return new Inverter(inverterData);
            });

            if (!isNewInverter)
            {
                var isNewData = !inverterData.Equals(inverter.InverterData);
                if (isNewData)
                {
                    inverter.InverterData = inverterData;
                    updated = true;
                }
            }

            await Task.WhenAll(
                new Func<Task>(async () =>
                {
                    var inverterEnergyData = await this.GetInverterEnergyData(inverterData.SerialNum, inverterCancelationToken);
                    if (inverterEnergyData != null && !inverterEnergyData.Equals(inverter.EnergyData))
                    {
                        inverter.EnergyData = inverterEnergyData;
                        updated = true;
                    }
                })(),
                new Func<Task>(async () =>
                {
                    var inverterRuntimeData = await this.GetInverterRuntimeData(inverterData.SerialNum, inverterCancelationToken);
                    if (inverterRuntimeData != null && !inverterRuntimeData.Equals(inverter.RuntimeData))
                    {
                        inverter.RuntimeData = inverterRuntimeData;
                        updated = true;
                    }
                })(),
                new Func<Task>(async () =>
                {
                    var todaysDate = LuxPowerTekService.TodaysDateString;
                    var todaysSummaryPoints = await this.GetInverterDaySummary(inverterData.SerialNum, todaysDate, inverterCancelationToken);
                    if (todaysSummaryPoints != null
                        && (!inverter.DaySummariesByDate.TryGetValue(todaysDate, out var currentSummaryPoints)
                        || !todaysSummaryPoints.SequenceEqual(currentSummaryPoints)))
                    {
                        _ = inverter.DaySummariesByDate.AddOrUpdate(todaysDate, todaysSummaryPoints, (_, _) => todaysSummaryPoints);
                        updated = true;
                    }
                })());
        });

        if (updated)
        {
            this.Changed?.Invoke();
        }
    }

    public async Task Subscribe()
    {
        if (Interlocked.Increment(ref mSubscriptionCount) != 1)
        {
            return;
        }

        await this.UpdateInverters(mDisposedCancelationSource.Token);

        mUpdateTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Unsubscribe()
    {
        if (Interlocked.Decrement(ref mSubscriptionCount) != 0)
        {
            return;
        }

        mUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async Task Authenticate(CancellationToken cancellationToken)
    {
        var client = mClientFactory.CreateClient(HTTP_CLIENT_NAME);
        var response = await client.PostAsync(
            "https://eu.luxpowertek.com/WManage/web/login",
            new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("account", mConfig.GetValue<string>("LuxPowerTek:Account")!),
                new KeyValuePair<string, string>("password", mConfig.GetValue<string>("LuxPowerTek:Password")!),
            }),
            cancellationToken);

        if (response.StatusCode != System.Net.HttpStatusCode.Found)
        {
            throw new UnauthorizedAccessException(response.ReasonPhrase);
        }

        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();

        mSessionId = cookies.FirstOrDefault(c => c.Contains(SESSION_COOKIE_PREFIX))!.Split(";").FirstOrDefault(c => c.StartsWith(SESSION_COOKIE_PREFIX));
    }

    private async Task<InverterData[]?> GetInverters(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await this.EnsureAuthenticated(cancellationToken);

            var client = mClientFactory.CreateClient(HTTP_CLIENT_NAME);
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://eu.luxpowertek.com/WManage/web/config/inverter/list")
            {
                Content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("page", "1"),
                    new KeyValuePair<string, string>("rows", "20"),
                    new KeyValuePair<string, string>("plantId", string.Empty),
                    new KeyValuePair<string, string>("searchText", string.Empty),
                    new KeyValuePair<string, string>("targetSerialNum", string.Empty),
                }),
            };

            request.Headers.Add("cookie", mSessionId);

            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                mSessionId = null;
                continue;
            }

            var result = await response.Content.ReadFromJsonAsync<PagedResponse<InverterData>>();

            return result?.Rows;
        }

        return null;
    }

    private async Task EnsureAuthenticated(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(mSessionId))
        {
            return;
        }

        await this.Authenticate(cancellationToken);
    }

    private async Task<InverterEnergyData?> GetInverterEnergyData(string inverterSerialNumber, CancellationToken cancellationToken)
    {
        await this.EnsureAuthenticated(cancellationToken);

        var client = mClientFactory.CreateClient(HTTP_CLIENT_NAME);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://eu.luxpowertek.com/WManage/api/inverter/getInverterEnergyInfo")
        {
            Content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("serialNum", inverterSerialNumber),
            }),
        };

        request.Headers.Add("cookie", mSessionId);

        var response = await client.SendAsync(request, cancellationToken);

        return await response.Content.ReadFromJsonAsync<InverterEnergyData>();
    }

    private async Task<InverterRuntimeData?> GetInverterRuntimeData(string inverterSerialNumber, CancellationToken cancellationToken)
    {
        await this.EnsureAuthenticated(cancellationToken);

        var client = mClientFactory.CreateClient(HTTP_CLIENT_NAME);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://eu.luxpowertek.com/WManage/api/inverter/getInverterRuntime")
        {
            Content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("serialNum", inverterSerialNumber),
            }),
        };

        request.Headers.Add("cookie", mSessionId);

        var response = await client.SendAsync(request, cancellationToken);

        return await response.Content.ReadFromJsonAsync<InverterRuntimeData>();
    }

    private async Task<InverterDaySummaryPoint[]?> GetInverterDaySummary(string inverterSerialNumber, string date, CancellationToken cancellationToken)
    {
        await this.EnsureAuthenticated(cancellationToken);

        var client = mClientFactory.CreateClient(HTTP_CLIENT_NAME);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://eu.luxpowertek.com/WManage/api/analyze/chart/dayMultiLine")
        {
            Content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("serialNum", inverterSerialNumber),
                new KeyValuePair<string, string>("dateText", date),
            }),
        };

        request.Headers.Add("cookie", mSessionId);

        var response = await client.SendAsync(request, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<DaySummaryResponse>();

        return result?.Success ?? false ? result.Data : null;
    }

    public void Dispose()
    {
        mIsDisposed = true;
        mDisposedCancelationSource.Cancel();
    }

    private class PagedResponse<T>
    {
        public int Total { get; set; }

        public T[] Rows { get; set; }
    }

    private class DaySummaryResponse
    {
        public string XAxis { get; set; }

        public InverterDaySummaryPoint[]? Data { get; set; }

        public bool Success { get; set; }
    }
}
