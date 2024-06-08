using System.Text.Json.Serialization;

namespace AcControl.Server.Data
{
    public class ToshibaAcHttpService
    {
        private const string BASE_ADDRESS = "https://mobileapi.toshibahomeaccontrols.com";
        private const string LOGIN_PATH = "/api/Consumer/Login";
        private const string MAPPING_PATH = "/api/AC/GetConsumerACMapping";
        private const string REGISTER_DEVICE_PATH = "/api/Consumer/RegisterMobileDevice";

        private readonly IHttpClientFactory mHttpClientFactory;

        public ToshibaAcHttpService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            mHttpClientFactory = httpClientFactory;

            this.Username = configuration.GetValue<string>("Toshiba:Username")!;
            this.Password = configuration.GetValue<string>("Toshiba:Password")!;
            this.DeviceId = configuration.GetValue<string>("Toshiba:DeviceId")!;

            this.LoginToken = new(() => this.TryGetAccessToken(CancellationToken.None));
        }

        public string Username { get; }

        public string Password { get; }

        public string DeviceId { get; }

        public Lazy<Task<LoginSuccessResponse>> LoginToken { get; }

        public async Task<LoginSuccessResponse> TryGetAccessToken(CancellationToken cancellationToken)
        {
            var httpClient = mHttpClientFactory.CreateClient();

            var result = await httpClient.PostAsJsonAsync(
                BASE_ADDRESS + LOGIN_PATH,
                new { this.Username, this.Password },
                cancellationToken
            );

            var body = await result.Content.ReadFromJsonAsync<MessageWrapper<LoginSuccessResponse>>(cancellationToken: cancellationToken);

            return body!.ResObj!;
        }

        public async Task<AirConditionerUnitDetails[]> GetAirConditionerUnits(CancellationToken cancellationToken)
        {
            var httpClient = mHttpClientFactory.CreateClient();
            var login = await this.LoginToken.Value;

            var request = new HttpRequestMessage(HttpMethod.Get, BASE_ADDRESS + MAPPING_PATH + "?consumerId=" + login.ConsumerId);
            request.Headers.Add("Authorization", $"Bearer {login.AccessToken}");

            var result = await httpClient.SendAsync(request, cancellationToken);

            var body = await result.Content.ReadFromJsonAsync<MessageWrapper<AirConditionerGroup[]>>(cancellationToken: cancellationToken);

            return body?.ResObj?[0]?.ACList ?? Array.Empty<AirConditionerUnitDetails>();
        }

        public async Task<DeviceRegistration?> RegisterDevice(CancellationToken cancellationToken)
        {
            var httpClient = mHttpClientFactory.CreateClient();
            var login = await this.LoginToken.Value;

            var request = new HttpRequestMessage(HttpMethod.Post, BASE_ADDRESS + REGISTER_DEVICE_PATH);
            request.Headers.Add("Authorization", $"Bearer {login.AccessToken}");
            request.Content = JsonContent.Create(new RegisterDeviceBody()
            {
                Username = this.Username,
                DeviceId = this.Username + "_" + this.DeviceId,
            });

            var result = await httpClient.SendAsync(request, cancellationToken);
            
            var body = await result.Content.ReadFromJsonAsync<MessageWrapper<DeviceRegistration>>(cancellationToken: cancellationToken);

            return body?.ResObj;
        }

        private class MessageWrapper<T>
        {
            public T? ResObj { get; set; }

            public bool IsSuccess { get; set; }

            public string? Message { get; set; }
        }

        private class LoginErrorResponse
        {
            public string Error { get; set; } = "";
        }

        private class AirConditionerGroup
        {
            public string GroupId { get; set; } = "";

            public string GroupName { get; set; } = "";

            public string ConsumerId { get; set; } = "";

            public string TimeZone { get; set; } = "";

            public AirConditionerUnitDetails[] ACList { get; set; } = Array.Empty<AirConditionerUnitDetails>();
        }

        private class RegisterDeviceBody
        {
            public string DeviceId { get; set; } = "";

            public string DeviceType { get; set; } = "1";

            public string Username { get; set; } = "";
        }
    }

    public class LoginSuccessResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        public string? TokenType { get; set; }

        public double ExpiresIn { get; set; }

        public string? ConsumerId { get; set; }

        public int CountryId { get; set; }

        public string? ConsumerMasterId { get; set; }
    }

    public class AirConditionerUnitDetails
    {
        public string Id { get; set; } = "";

        public string DeviceUniqueId { get; set; } = "";

        public string Name { get; set; } = "";

        public string ACModelId { get; set; } = "";

        public string Description { get; set; } = "";

        public string CreatedDate { get; set; } = "";

        public string ACStateData { get; set; } = "";

        public string FirmwareUpgradeStatus { get; set; } = "";

        public string URL { get; set; } = "";

        public string File { get; set; } = "";

        public string MeritFeature { get; set; } = "";

        public string AdapterType { get; set; } = "";

        public string FirmwareVersion { get; set; } = "";

        public string FirmwareCode { get; set; } = "";
    }

    public class DeviceRegistration
    {
        public string DeviceId { get; set; } = "";

        public string HostName { get; set; } = "";

        public string PrimaryKey { get; set; } = "";

        public string SecondaryKey { get; set; } = "";

        public string SasToken { get; set; } = "";

        public string RegisterDate { get; set; } = "";
    }
}