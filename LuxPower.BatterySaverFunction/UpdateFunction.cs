using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LuxPower.BatterySaverFunction
{
    public class UpdateFunction
    {
        private const string SESSION_COOKIE_PREFIX = "JSESSIONID";

        private readonly HttpClient mHttpClient;
        private readonly IConfiguration mConfig;

        public UpdateFunction(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            mHttpClient = httpClientFactory.CreateClient("LUX");
            mConfig = config;
        }

        [FunctionName("BatteryUpdate")]
        public async Task Run([TimerTrigger("0 5,16 * * *", RunOnStartup = true, UseMonitor = false)] TimerInfo myTimer, ILogger log, CancellationToken cancellationToken)
        {
            var ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            var ukTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, ukTimeZone);

            log.LogInformation($"Timer trigger function executing at: {ukTime} (UK time)");

            var account = Environment.GetEnvironmentVariable("LuxPowerTek_Account") ?? mConfig.GetValue<string>("LuxPowerTek:Account")!;
            var password = Environment.GetEnvironmentVariable("LuxPowerTek_Password") ?? mConfig.GetValue<string>("LuxPowerTek:Password")!;

            var authResponse = await mHttpClient.PostAsync(
                "https://eu.luxpowertek.com/WManage/web/login",
                new FormUrlEncodedContent(new[] {
                new KeyValuePair<string, string>("account", account),
                new KeyValuePair<string, string>("password", password),
                }),
                cancellationToken);

            if (authResponse.StatusCode != System.Net.HttpStatusCode.Found)
            {
                var content = await authResponse.Content.ReadAsStringAsync();
                throw new UnauthorizedAccessException(authResponse.ReasonPhrase);
            }

            var cookies = authResponse.Headers.GetValues("Set-Cookie").ToArray();

            var sessionId = cookies.FirstOrDefault(c => c.Contains(SESSION_COOKIE_PREFIX))!.Split(";").FirstOrDefault(c => c.StartsWith(SESSION_COOKIE_PREFIX));

            var invertersRequest = new HttpRequestMessage(
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

            invertersRequest.Headers.Add("cookie", sessionId);

            var invertersResponse = await mHttpClient.SendAsync(invertersRequest, cancellationToken);
            var invertersResult = await invertersResponse.Content.ReadFromJsonAsync<PagedResponse<InverterData>>();

            var getMaintenanceRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "https://eu.luxpowertek.com/WManage/web/maintain/remoteRead/read")
            {
                Content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("inverterSn", invertersResult.Rows.First().SerialNum),
                    new KeyValuePair<string, string>("startRegister", "80"),
                    new KeyValuePair<string, string>("pointNumber", "40"),
                }),
            };

            getMaintenanceRequest.Headers.Add("cookie", sessionId);

            var getMaintenanceResponse = await mHttpClient.SendAsync(getMaintenanceRequest, cancellationToken);
            var getMaintenanceResult = await getMaintenanceResponse.Content.ReadFromJsonAsync<GetMaintenanceResult>();
            var currentBatteryCutOffPercent = getMaintenanceResult.HOLD_DISCHG_CUT_OFF_SOC_EOD;

            var expectedBatteryCutOffPercent = ukTime.TimeOfDay.Hours switch
            {
                > 2 and < 16 => "70",
                _ => "10",
            };

            if (expectedBatteryCutOffPercent == currentBatteryCutOffPercent)
            {
                log.LogInformation("All good");
                return;
            }

            var setMaintenanceRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "https://eu.luxpowertek.com/WManage/web/maintain/remoteSet/write")
            {
                Content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("inverterSn", invertersResult.Rows.First().SerialNum),
                    new KeyValuePair<string, string>("holdParam", "HOLD_DISCHG_CUT_OFF_SOC_EOD"),
                    new KeyValuePair<string, string>("valueText", expectedBatteryCutOffPercent),
                    new KeyValuePair<string, string>("clientType", "WEB"),
                    new KeyValuePair<string, string>("remoteSetType", "NORMAL"),
                }),
            };

            setMaintenanceRequest.Headers.Add("cookie", sessionId);

            var setMaintenanceResponse = await mHttpClient.SendAsync(setMaintenanceRequest, cancellationToken);

            if (!setMaintenanceResponse.IsSuccessStatusCode)
            {
                throw new Exception("Http error: " + setMaintenanceResponse.StatusCode.ToString());
            }

            log.LogInformation($"Changed battery hold from {currentBatteryCutOffPercent} to {expectedBatteryCutOffPercent}");
        }

        private class PagedResponse<T>
        {
            public int Total { get; set; }

            public T[] Rows { get; set; }
        }

        private class GetMaintenanceDataPayload
        {
            public int InverterSn { get; set; }
            public int StartRegister { get; set; }
            public int PointNumber { get; set; }
        }

        public record InverterData
        {
            public bool WithBatteryData { get; set; }
            public string WarrantyExpireDate { get; set; }
            public string DeviceTypeText { get; set; }
            public int ServerId { get; set; }
            public int PowerRating { get; set; }
            public int Dtc { get; set; }
            public bool ParallelEnabled { get; set; }
            public bool Lost { get; set; }
            public string DatalogSn { get; set; }
            public int Model { get; set; }
            public string CommissionDate { get; set; }
            public int BatCapacity { get; set; }
            public int Phase { get; set; }
            public int DeviceType { get; set; }
            public string SerialNum { get; set; }
            public string FwCode { get; set; }
            public int PlantId { get; set; }
            public int SubDeviceType { get; set; }
            public string SohText { get; set; }
            public string EndUser { get; set; }
            public int BatParallelNum { get; set; }
            public string PowerRatingText { get; set; }
            public string PlantName { get; set; }
            public int Status { get; set; }
            public string LastUpdateTime { get; set; }
        }

        public class GetMaintenanceResult
        {
            public string HOLD_FEED_IN_GRID_POWER_PERCENT { get; set; }
            public string HOLD_LEAD_ACID_CHARGE_VOLT_REF { get; set; }
            public string HOLD_FORCED_DISCHARGE_END_MINUTE_1 { get; set; }
            public bool FUNC_BATTERY_ECO_EN { get; set; }
            public string HOLD_FORCED_DISCHARGE_END_MINUTE_2 { get; set; }
            public bool FUNC_BAT_SHARED { get; set; }
            public bool FUNC_BUZZER_EN { get; set; }
            public string HOLD_P_TO_USER_START_DISCHG { get; set; }
            public string HOLD_LEAD_ACID_DISCHARGE_CUT_OFF_VOLT { get; set; }
            public string valueFrame { get; set; }
            public bool FUNC_TAKE_LOAD_TOGETHER { get; set; }
            public string HOLD_FORCED_DISCHG_SOC_LIMIT { get; set; }
            public string HOLD_FORCED_DISCHARGE_END_MINUTE { get; set; }
            public string HOLD_LEAD_ACID_DISCHARGE_RATE { get; set; }
            public string HOLD_FORCED_CHARGE_END_MINUTE_2 { get; set; }
            public string HOLD_DISCHG_CUT_OFF_SOC_EOD { get; set; }
            public bool success { get; set; }
            public string HOLD_VBAT_START_DERATING { get; set; }
            public string HOLD_LEAD_ACID_TEMPR_LOWER_LIMIT_CHG { get; set; }
            public string HOLD_FORCED_DISCHARGE_END_HOUR { get; set; }
            public string HOLD_FORCED_CHARGE_END_HOUR_2 { get; set; }
            public string HOLD_LEAD_ACID_CHARGE_RATE { get; set; }
            public string HOLD_FORCED_CHARGE_START_HOUR_2 { get; set; }
            public bool FUNC_CHARGE_LAST { get; set; }
            public int BIT_PVCT_SAMPLE_TYPE { get; set; }
            public string HOLD_FORCED_DISCHARGE_END_HOUR_1 { get; set; }
            public string HOLD_LEAD_ACID_TEMPR_UPPER_LIMIT_DISCHG { get; set; }
            public int BIT_CT_SAMPLE_RATIO { get; set; }
            public string HOLD_FORCED_DISCHARGE_END_HOUR_2 { get; set; }
            public string HOLD_FORCED_DISCHARGE_START_HOUR_1 { get; set; }
            public string HOLD_FORCED_DISCHARGE_START_HOUR_2 { get; set; }
            public int BIT_WORKING_MODE { get; set; }
            public string HOLD_SET_MASTER_OR_SLAVE { get; set; }
            public string HOLD_LEAD_ACID_TEMPR_UPPER_LIMIT_CHG { get; set; }
            public bool FUNC_GREEN_EN { get; set; }
            public int BIT_PVCT_SAMPLE_RATIO { get; set; }
            public string HOLD_FORCED_CHARGE_START_MINUTE_2 { get; set; }
            public string HOLD_EPS_FREQ_SET { get; set; }
            public string HOLD_SET_COMPOSED_PHASE { get; set; }
            public string HOLD_FORCED_DISCHARGE_START_MINUTE { get; set; }
            public string HOLD_FORCED_DISCHG_POWER_CMD { get; set; }
            public string HOLD_CT_POWER_OFFSET { get; set; }
            public string HOLD_FORCED_DISCHARGE_START_HOUR { get; set; }
            public string HOLD_EPS_VOLT_SET { get; set; }
            public bool FUNC_RUN_WITHOUT_GRID { get; set; }
            public string HOLD_FORCED_DISCHARGE_START_MINUTE_1 { get; set; }
            public string HOLD_FORCED_DISCHARGE_START_MINUTE_2 { get; set; }
            public string HOLD_LEAD_ACID_TEMPR_LOWER_LIMIT_DISCHG { get; set; }
            public bool FUNC_MICRO_GRID_EN { get; set; }
            public bool FUNC_PV_GRID_OFF_EN { get; set; }
        }
    }
}
