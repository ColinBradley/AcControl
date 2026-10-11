using AcControl.Server.Telemetry;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcControl.Server.Data
{
    public class ToshibaAcHttpService
    {
        public const string HTTP_CLIENT_NAME = "ToshibaAc";
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
            using var activity = AcControlServerTelemetry.Source.Start();

            if (string.IsNullOrWhiteSpace(this.Username) || string.IsNullOrWhiteSpace(this.Password)
                || string.IsNullOrWhiteSpace(this.DeviceId))
            {
                throw new InvalidOperationException("Configure Toshiba:Username, Toshiba:Password and Toshiba:DeviceId before logging in.");
            }

            using var httpClient = mHttpClientFactory.CreateClient(HTTP_CLIENT_NAME);

            using var request = this.CreateRequest(HttpMethod.Post, LOGIN_PATH);
            request.Content = JsonContent.Create(new LoginBody(this.Username, this.Password));

            using var result = await httpClient.SendAsync(request, cancellationToken);

            activity?.SetTag("http.response.status_code", (int)result.StatusCode);

            if (!result.IsSuccessStatusCode)
            {
                var retryAfter = result.Headers.RetryAfter?.Delta
                    ?? (result.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                if (retryAfter < TimeSpan.Zero)
                {
                    retryAfter = TimeSpan.Zero;
                }

                // Error responses can contain account details. Keep raw bodies out of logs and telemetry.
                throw new ToshibaAcLoginException(
                    $"Toshiba login returned HTTP {(int)result.StatusCode}. No automatic retry was made.",
                    result.StatusCode,
                    retryAfter
                );
            }

            MessageWrapper<LoginSuccessResponse>? body;
            try
            {
                body = await result.Content.ReadFromJsonAsync<MessageWrapper<LoginSuccessResponse>>(cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new ToshibaAcLoginException("Toshiba login returned an unreadable JSON response.", result.StatusCode);
            }

            if (body?.IsSuccess != true)
            {
                var credentialError = body?.StatusCode.ValueKind == JsonValueKind.String
                    && body.StatusCode.GetString() == "InvalidUserNameorPassword"
                    || body?.Message?.Contains("password", StringComparison.OrdinalIgnoreCase) == true
                    || body?.Message?.Contains("lock", StringComparison.OrdinalIgnoreCase) == true;
                throw new ToshibaAcLoginException(
                    credentialError
                        ? "Toshiba rejected the credentials or reported an account lock. No automatic retry was made."
                        : "Toshiba reported an unsuccessful login. No automatic retry was made.",
                    result.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(body.ResObj?.AccessToken) || string.IsNullOrWhiteSpace(body.ResObj.ConsumerId))
            {
                throw new ToshibaAcLoginException("Toshiba login response is missing an access token or consumer ID.", result.StatusCode);
            }

            return body.ResObj;
        }

        public async Task<AirConditionerUnitDetails[]> GetAirConditionerUnits(CancellationToken cancellationToken)
        {
            using var httpClient = mHttpClientFactory.CreateClient(HTTP_CLIENT_NAME);
            var login = await this.LoginToken.Value;

            using var request = this.CreateRequest(HttpMethod.Get, MAPPING_PATH + "?consumerId=" + Uri.EscapeDataString(login.ConsumerId!));
            request.Headers.Add("Authorization", $"Bearer {login.AccessToken}");

            using var result = await httpClient.SendAsync(request, cancellationToken);

            var body = await result.Content.ReadFromJsonAsync<MessageWrapper<AirConditionerGroup[]>>(cancellationToken: cancellationToken);

            return (body?.ResObj is null or { Length: 0 } ? null : body?.ResObj?[0].ACList) ?? Array.Empty<AirConditionerUnitDetails>();
        }

        public async Task<DeviceRegistration?> RegisterDevice(CancellationToken cancellationToken)
        {
            using var httpClient = mHttpClientFactory.CreateClient(HTTP_CLIENT_NAME);
            var login = await this.LoginToken.Value;

            using var request = this.CreateRequest(HttpMethod.Post, REGISTER_DEVICE_PATH);
            request.Headers.Add("Authorization", $"Bearer {login.AccessToken}");
            request.Content = JsonContent.Create(new RegisterDeviceBody()
            {
                Username = this.Username,
                DeviceId = this.Username + "_" + this.DeviceId,
            });

            using var result = await httpClient.SendAsync(request, cancellationToken);

            var body = await result.Content.ReadFromJsonAsync<MessageWrapper<DeviceRegistration>>(cancellationToken: cancellationToken);

            return body?.ResObj;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, BASE_ADDRESS + path);
            // Match the stable device identity used by the maintained community client.
            request.Headers.Add("Device-ID", this.DeviceId);
            return request;
        }

        private sealed record LoginBody(
            [property: JsonPropertyName("Username")] string Username,
            [property: JsonPropertyName("Password")] string Password);

        private class MessageWrapper<T>
        {
            public T? ResObj { get; set; }

            public bool IsSuccess { get; set; }

            public string? Message { get; set; }

            public JsonElement StatusCode { get; set; }
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

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public double ExpiresIn { get; set; }

        public string? ConsumerId { get; set; }

        public int CountryId { get; set; }

        public string? ConsumerMasterId { get; set; }
    }

    public sealed class ToshibaAcLoginException : HttpRequestException
    {
        public ToshibaAcLoginException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter = null)
            : base(message, null, statusCode)
        {
            this.RetryAfter = retryAfter;
        }

        public TimeSpan? RetryAfter { get; }
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