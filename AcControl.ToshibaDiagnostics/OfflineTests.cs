namespace AcControl.ToshibaDiagnostics;

using AcControl.Server.Data;
using AcControl.Server.Telemetry;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class OfflineTests
{
    private const string SUCCESS_BODY = """
        {"IsSuccess":true,"StatusCode":0,"ResObj":{"access_token":"fixture-token","token_type":"Bearer","expires_in":3600,"consumerId":"fixture-consumer"}}
        """;

    public static async Task<int> Run()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Login sends PascalCase credentials and stable Device-ID", LoginRequest),
            ("Successful login reads token_type and expires_in", SuccessfulLogin),
            ("429 preserves status and Retry-After without retrying or exposing bodies", RateLimitedLogin),
            ("Retry-After HTTP dates are supported", RetryAfterDate),
            ("HTML error responses do not reach the success JSON parser", HtmlFailure),
            ("API credential rejection is reported without echoing its message", RejectedCredentials),
            ("API rejection overrides even a populated result", RejectedResult),
            ("Missing token, consumer ID, result and malformed JSON fail safely", InvalidSuccessBodies),
            ("Missing configuration sends no HTTP request", MissingConfiguration),
            ("Cancellation sends no HTTP request", Cancellation),
            ("Mapping and registration also carry the stable device identity", AuthenticatedRequests),
            ("Legacy comparison reproduces the original request", LegacyRequest),
        };

        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.GetType().Name}");
                return 1;
            }
        }

        Console.WriteLine($"All {tests.Length} offline checks passed. No real credentials or network used.");
        return 0;
    }

    private static Task LoginRequest() => WithResponse(SUCCESS_BODY, async (service, factory) =>
    {
        factory.Inspect = async request =>
        {
            Assert(request.Method == HttpMethod.Post);
            Assert(request.RequestUri == new Uri("https://mobileapi.toshibahomeaccontrols.com/api/Consumer/Login"));
            Assert(request.Headers.GetValues("Device-ID").Single() == "fixture-device");
            Assert(request.Headers.Authorization is null);
            Assert(!request.Headers.UserAgent.Any());
            Assert(request.Content!.Headers.ContentType!.MediaType == "application/json");
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
            Assert(body.RootElement.GetProperty("Username").GetString() == "fixture-user");
            Assert(body.RootElement.GetProperty("Password").GetString() == "fixture-password");
            Assert(body.RootElement.EnumerateObject().Count() == 2);
        };
        await service.TryGetAccessToken(CancellationToken.None);
        Assert(factory.Calls == 1);
    });

    private static Task SuccessfulLogin() => WithResponse(SUCCESS_BODY, async (service, _) =>
    {
        var result = await service.TryGetAccessToken(CancellationToken.None);
        Assert(result.AccessToken == "fixture-token");
        Assert(result.ConsumerId == "fixture-consumer");
        Assert(result.TokenType == "Bearer");
        Assert(result.ExpiresIn == 3600);
    });

    private static async Task RateLimitedLogin()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AcControlServerTelemetry.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => Assert(!activity.TagObjects.Any(tag => tag.Key == "http.response.body")),
        };
        ActivitySource.AddActivityListener(listener);
        using var factory = new StubFactory(() =>
        {
            var response = Response("fixture-user fixture-password fixture-token", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(900));
            return response;
        });
        var exception = await Failure(Service(factory));
        Assert(exception.StatusCode == HttpStatusCode.TooManyRequests);
        Assert(exception.RetryAfter == TimeSpan.FromSeconds(900));
        Assert(factory.Calls == 1);
        Assert(!exception.ToString().Contains("fixture-", StringComparison.Ordinal));
    }

    private static async Task RetryAfterDate()
    {
        using var factory = new StubFactory(() =>
        {
            var response = Response("", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(10));
            return response;
        });
        var exception = await Failure(Service(factory));
        Assert(exception.RetryAfter?.TotalSeconds is > 590 and <= 600);
    }

    private static async Task HtmlFailure()
    {
        using var factory = new StubFactory(() => Response("<html>fixture-password</html>", HttpStatusCode.Forbidden, "text/html"));
        var exception = await Failure(Service(factory));
        Assert(exception.StatusCode == HttpStatusCode.Forbidden);
        Assert(factory.Calls == 1);
        Assert(!exception.ToString().Contains("fixture-password", StringComparison.Ordinal));
    }

    private static Task RejectedCredentials() => WithResponse("""
        {"IsSuccess":false,"StatusCode":"InvalidUserNameorPassword","Message":"fixture-user fixture-password"}
        """, async (service, factory) =>
    {
        var exception = await Failure(service);
        Assert(exception.Message.Contains("credentials", StringComparison.Ordinal));
        Assert(!exception.ToString().Contains("fixture-", StringComparison.Ordinal));
        Assert(factory.Calls == 1);
    });

    private static Task RejectedResult() => WithResponse(SUCCESS_BODY.Replace("true", "false"), async (service, _) =>
    {
        await Failure(service);
    });

    private static async Task InvalidSuccessBodies()
    {
        foreach (var body in new[]
        {
            "null", "{}", "{", "", "{\"IsSuccess\":true,\"ResObj\":null}",
            "{\"IsSuccess\":true,\"ResObj\":{\"access_token\":\"fixture-token\"}}",
            "{\"IsSuccess\":true,\"ResObj\":{\"consumerId\":\"fixture-consumer\"}}",
        })
        {
            await WithResponse(body, async (service, _) => { await Failure(service); });
        }
    }

    private static async Task MissingConfiguration()
    {
        using var factory = new StubFactory(() => Response(SUCCESS_BODY));
        var service = new ToshibaAcHttpService(factory, new ConfigurationBuilder().Build());
        try
        {
            await service.TryGetAccessToken(CancellationToken.None);
            Assert(false);
        }
        catch (InvalidOperationException)
        {
            Assert(factory.Calls == 0);
        }
    }

    private static async Task Cancellation()
    {
        using var factory = new StubFactory(() => Response(SUCCESS_BODY));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Service(factory).TryGetAccessToken(cancellation.Token);
            Assert(false);
        }
        catch (OperationCanceledException)
        {
            Assert(factory.Calls == 0);
        }
    }

    private static async Task AuthenticatedRequests()
    {
        using var factory = new StubFactory(() => Response(SUCCESS_BODY));
        factory.Inspect = request =>
        {
            Assert(request.Headers.GetValues("Device-ID").Single() == "fixture-device");
            if (request.RequestUri!.AbsolutePath != "/api/Consumer/Login")
            {
                Assert(request.Headers.Authorization?.ToString() == "Bearer fixture-token");
            }

            return Task.CompletedTask;
        };
        var service = Service(factory);
        await service.LoginToken.Value;
        factory.CreateResponse = () => Response("{\"IsSuccess\":true,\"ResObj\":[]}");
        await service.GetAirConditionerUnits(CancellationToken.None);
        factory.CreateResponse = () => Response("{\"IsSuccess\":true,\"ResObj\":{}}");
        await service.RegisterDevice(CancellationToken.None);
        Assert(factory.Calls == 3);
    }

    private static async Task LegacyRequest()
    {
        using var factory = new StubFactory(() => Response(SUCCESS_BODY));
        factory.Inspect = async request =>
        {
            Assert(!request.Headers.Contains("Device-ID"));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert(body.RootElement.GetProperty("username").GetString() == "fixture-user");
            Assert(body.RootElement.GetProperty("password").GetString() == "fixture-password");
        };
        factory.Legacy = true;
        await Service(factory).TryGetAccessToken(CancellationToken.None);
        Assert(factory.Calls == 1);
    }

    private static async Task WithResponse(string body, Func<ToshibaAcHttpService, StubFactory, Task> test)
    {
        using var factory = new StubFactory(() => Response(body));
        await test(Service(factory), factory);
    }

    private static ToshibaAcHttpService Service(StubFactory factory) => new(factory,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Toshiba:Username"] = "fixture-user",
            ["Toshiba:Password"] = "fixture-password",
            ["Toshiba:DeviceId"] = "fixture-device",
        }).Build());

    private static async Task<ToshibaAcLoginException> Failure(ToshibaAcHttpService service)
    {
        try
        {
            await service.TryGetAccessToken(CancellationToken.None);
        }
        catch (ToshibaAcLoginException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected a login failure.");
    }

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static void Assert(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Regression assertion failed.");
        }
    }

    private sealed class StubFactory(Func<HttpResponseMessage> createResponse) : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public Func<HttpRequestMessage, Task>? Inspect { get; set; }
        public Func<HttpResponseMessage> CreateResponse { get; set; } = createResponse;
        public bool Legacy { get; set; }

        public HttpClient CreateClient(string name)
        {
            Assert(name == ToshibaAcHttpService.HTTP_CLIENT_NAME);
            return this.Legacy
                ? new HttpClient(new LoginDiagnosticsHandler(true) { InnerHandler = this }, disposeHandler: false)
                : new HttpClient(this, disposeHandler: false);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Calls++;
            if (this.Inspect is not null)
            {
                await this.Inspect(request);
            }

            return this.CreateResponse();
        }
    }
}