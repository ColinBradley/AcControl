namespace AcControl.Server.Data;

using AcControl.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

public class LuxPowerTekService : IDisposable
{
    public const string HTTP_CLIENT_NAME = "LuxPowerTek";

    private const string SESSION_COOKIE_PREFIX = "JSESSIONID";

    private readonly IConfiguration mConfig;
    private readonly IHttpClientFactory mClientFactory;
    private readonly IServiceScopeFactory mScopeFactory;

    private string? mSessionId = null;

    private readonly ConcurrentDictionary<string, Inverter> mInvertersBySerialNumber = new();

    private readonly Timer mUpdateTimer;
    private int mSubscriptionCount = 0;

    private bool mIsDisposed;
    private readonly CancellationTokenSource mDisposedCancelationSource = new();

    public delegate void ChangedEventHandler();
    public event ChangedEventHandler? Changed;

    public LuxPowerTekService(IConfiguration config, IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory)
    {
        mConfig = config;
        mClientFactory = httpClientFactory;
        mScopeFactory = scopeFactory;

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
        var isToday = date == LuxPowerTekService.TodaysDateString;
        if (!isToday && inverter.DaySummariesByDate.TryGetValue(date, out var existing))
        {
            var lastEntry = existing.Last();

            var isComplete = !isToday && IsDecentLastEntry(lastEntry);

            if (isComplete)
            {
                return existing;
            }
        }

        var asDateOnly = DateOnly.Parse(date);
        using var scope = mScopeFactory.CreateScope();
        await using var homeDbContext = scope.ServiceProvider.GetRequiredService<HomeDbContext>();
        var result = isToday ? null : (await homeDbContext.InverterDaySummaries.FirstOrDefaultAsync(s => s.Date == asDateOnly))?.Entries;

        if (result is null)
        {
            result = await this.GetInverterDaySummary(inverter.InverterData.SerialNum, date, cancellationToken);

            if (result?.Length is null or 0)
            {
                return Array.Empty<InverterDaySummaryPoint>();
            }
            else if (!isToday && IsDecentLastEntry(result.Last()))
            {
                homeDbContext.InverterDaySummaries.Add(
                    new InverterDaySummaryEntry()
                    {
                        Date = asDateOnly,
                        Entries = result,
                    }
                );

                await homeDbContext.SaveChangesAsync();
            }
        }

        inverter.DaySummariesByDate[date] = result;

        return result;
    }

    private static bool IsDecentLastEntry(InverterDaySummaryPoint lastEntry)
    {
        return lastEntry.Hour == 23 && lastEntry.Minute > 50;
    }

    public async Task UpdateBattery()
    {
        await this.EnsureAuthenticated(default);

        var httpClient = mClientFactory.CreateClient(HTTP_CLIENT_NAME);

        var invertersRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "https://eu.luxpowertek.com/WManage/web/config/inverter/list")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("rows", "20"),
                new KeyValuePair<string, string>("plantId", string.Empty),
                new KeyValuePair<string, string>("searchText", string.Empty),
                new KeyValuePair<string, string>("targetSerialNum", string.Empty),
            ]),
        };

        invertersRequest.Headers.Add("cookie", mSessionId);

        var invertersResponse = await httpClient.SendAsync(invertersRequest);
        var invertersResult = await invertersResponse.Content.ReadFromJsonAsync<PagedResponse<InverterData>>();

        var getMaintenanceRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "https://eu.luxpowertek.com/WManage/web/maintain/remoteRead/read")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("inverterSn", invertersResult!.Rows.First().SerialNum),
                new KeyValuePair<string, string>("startRegister", "80"),
                new KeyValuePair<string, string>("pointNumber", "40"),
            ]),
        };

        getMaintenanceRequest.Headers.Add("cookie", mSessionId);

        var getMaintenanceResponse = await httpClient.SendAsync(getMaintenanceRequest);
        
        GetMaintenanceResult? getMaintenanceResult;
        try
        {
            getMaintenanceResult = await getMaintenanceResponse.Content.ReadFromJsonAsync<GetMaintenanceResult>();
        } 
        catch (JsonException)
        {
            File.WriteAllText("./ThatFileIssue.txt", await getMaintenanceResponse.Content.ReadAsStringAsync());

            return;
        }

        var currentBatteryCutOffPercent = getMaintenanceResult!.HOLD_DISCHG_CUT_OFF_SOC_EOD;

        var ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        var ukTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, ukTimeZone);

        var expectedBatteryCutOffPercent = ukTime.TimeOfDay.Hours switch
        {
            > 2 and < 16 => "50",
            _ => "10",
        };

        if (expectedBatteryCutOffPercent == currentBatteryCutOffPercent)
        {
            return;
        }

        var setMaintenanceRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "https://eu.luxpowertek.com/WManage/web/maintain/remoteSet/write")
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("inverterSn", invertersResult.Rows.First().SerialNum),
                new KeyValuePair<string, string>("holdParam", "HOLD_DISCHG_CUT_OFF_SOC_EOD"),
                new KeyValuePair<string, string>("valueText", expectedBatteryCutOffPercent),
                new KeyValuePair<string, string>("clientType", "WEB"),
                new KeyValuePair<string, string>("remoteSetType", "NORMAL"),
            ]),
        };

        setMaintenanceRequest.Headers.Add("cookie", mSessionId);

        var setMaintenanceResponse = await httpClient.SendAsync(setMaintenanceRequest);

        if (!setMaintenanceResponse.IsSuccessStatusCode)
        {
            throw new Exception("Http error: " + setMaintenanceResponse.StatusCode.ToString());
        }
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

        public required T[] Rows { get; set; }
    }

    private class DaySummaryResponse
    {
        public required string XAxis { get; set; }

        public InverterDaySummaryPoint[]? Data { get; set; }

        public bool Success { get; set; }
    }

    public class GetMaintenanceResult
    {
        public required string HOLD_FEED_IN_GRID_POWER_PERCENT { get; set; }
        public required string HOLD_LEAD_ACID_CHARGE_VOLT_REF { get; set; }
        public required string HOLD_FORCED_DISCHARGE_END_MINUTE_1 { get; set; }
        public bool FUNC_BATTERY_ECO_EN { get; set; }
        public required string HOLD_FORCED_DISCHARGE_END_MINUTE_2 { get; set; }
        public bool FUNC_BAT_SHARED { get; set; }
        public bool FUNC_BUZZER_EN { get; set; }
        public required string HOLD_P_TO_USER_START_DISCHG { get; set; }
        public required string HOLD_LEAD_ACID_DISCHARGE_CUT_OFF_VOLT { get; set; }
        public required string valueFrame { get; set; }
        public bool FUNC_TAKE_LOAD_TOGETHER { get; set; }
        public required string HOLD_FORCED_DISCHG_SOC_LIMIT { get; set; }
        public required string HOLD_FORCED_DISCHARGE_END_MINUTE { get; set; }
        public required string HOLD_LEAD_ACID_DISCHARGE_RATE { get; set; }
        public required string HOLD_FORCED_CHARGE_END_MINUTE_2 { get; set; }
        public required string HOLD_DISCHG_CUT_OFF_SOC_EOD { get; set; }
        public bool success { get; set; }
        public required string HOLD_VBAT_START_DERATING { get; set; }
        public required string HOLD_LEAD_ACID_TEMPR_LOWER_LIMIT_CHG { get; set; }
        public required string HOLD_FORCED_DISCHARGE_END_HOUR { get; set; }
        public required string HOLD_FORCED_CHARGE_END_HOUR_2 { get; set; }
        public required string HOLD_LEAD_ACID_CHARGE_RATE { get; set; }
        public required string HOLD_FORCED_CHARGE_START_HOUR_2 { get; set; }
        public bool FUNC_CHARGE_LAST { get; set; }
        public int BIT_PVCT_SAMPLE_TYPE { get; set; }
        public required string HOLD_FORCED_DISCHARGE_END_HOUR_1 { get; set; }
        public required string HOLD_LEAD_ACID_TEMPR_UPPER_LIMIT_DISCHG { get; set; }
        public int BIT_CT_SAMPLE_RATIO { get; set; }
        public required string HOLD_FORCED_DISCHARGE_END_HOUR_2 { get; set; }
        public required string HOLD_FORCED_DISCHARGE_START_HOUR_1 { get; set; }
        public required string HOLD_FORCED_DISCHARGE_START_HOUR_2 { get; set; }
        public int BIT_WORKING_MODE { get; set; }
        public required string HOLD_SET_MASTER_OR_SLAVE { get; set; }
        public required string HOLD_LEAD_ACID_TEMPR_UPPER_LIMIT_CHG { get; set; }
        public bool FUNC_GREEN_EN { get; set; }
        public int BIT_PVCT_SAMPLE_RATIO { get; set; }
        public required string HOLD_FORCED_CHARGE_START_MINUTE_2 { get; set; }
        public required string HOLD_EPS_FREQ_SET { get; set; }
        public required string HOLD_SET_COMPOSED_PHASE { get; set; }
        public required string HOLD_FORCED_DISCHARGE_START_MINUTE { get; set; }
        public required string HOLD_FORCED_DISCHG_POWER_CMD { get; set; }
        public required string HOLD_CT_POWER_OFFSET { get; set; }
        public required string HOLD_FORCED_DISCHARGE_START_HOUR { get; set; }
        public required string HOLD_EPS_VOLT_SET { get; set; }
        public bool FUNC_RUN_WITHOUT_GRID { get; set; }
        public required string HOLD_FORCED_DISCHARGE_START_MINUTE_1 { get; set; }
        public required string HOLD_FORCED_DISCHARGE_START_MINUTE_2 { get; set; }
        public required string HOLD_LEAD_ACID_TEMPR_LOWER_LIMIT_DISCHG { get; set; }
        public bool FUNC_MICRO_GRID_EN { get; set; }
        public bool FUNC_PV_GRID_OFF_EN { get; set; }
    }
}
