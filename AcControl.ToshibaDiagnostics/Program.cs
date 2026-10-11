using AcControl.Server.Data;
using AcControl.ToshibaDiagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;

if (args.SequenceEqual(new[] { "--self-test" }))
{
    return await OfflineTests.Run();
}

var live = args.SequenceEqual(new[] { "--live" });
var legacy = args.SequenceEqual(new[] { "--live", "--legacy-request" });
var checkConfig = args.SequenceEqual(new[] { "--check-config" });
if (!live && !legacy && !checkConfig)
{
    Console.WriteLine("Toshiba sign-in diagnostics (does not start the website).");
    Console.WriteLine("  --self-test                 Run offline regression checks; no secrets or network.");
    Console.WriteLine("  --check-config              Check that server user secrets exist; print no values.");
    Console.WriteLine("  --live                      Make ONE login request using the production service.");
    Console.WriteLine("  --live --legacy-request     Make ONE request with the previous headers/body casing.");
    return args.Length == 0 || args.SequenceEqual(new[] { "--help" }) ? 0 : 2;
}

IConfiguration configuration;
try
{
    configuration = new ConfigurationBuilder().AddUserSecrets<DiagnosticsSecrets>(optional: true).Build();
}
catch
{
    Console.Error.WriteLine("Could not load the server's user secrets. No request was made.");
    return 2;
}

var missing = false;
foreach (var key in new[] { "Toshiba:Username", "Toshiba:Password", "Toshiba:DeviceId" })
{
    var configured = !string.IsNullOrWhiteSpace(configuration[key]);
    Console.WriteLine($"{key}: {(configured ? "configured" : "missing")}");
    missing |= !configured;
}

if (checkConfig || missing)
{
    return missing ? 2 : 0;
}

var stateDirectory = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "DiagnosticsStateDirectory").Value!;
Directory.CreateDirectory(stateDirectory);
FileStream attemptLock;
try
{
    attemptLock = new FileStream(Path.Combine(stateDirectory, "toshiba-login-cooldown.txt"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
catch (IOException)
{
    Console.Error.WriteLine("Another diagnostic run is active or its cooldown file cannot be opened. No request was made.");
    return 2;
}

using (attemptLock)
{
    using var reader = new StreamReader(attemptLock, leaveOpen: true);
    var state = await reader.ReadToEndAsync();
    var now = DateTimeOffset.UtcNow;
    if (state.Length > 0)
    {
        if (!DateTimeOffset.TryParseExact(state, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var nextAllowed))
        {
            Console.Error.WriteLine("The local cooldown file is unreadable. No request was made.");
            return 2;
        }

        if (nextAllowed > now)
        {
            Console.Error.WriteLine($"Local cooldown: wait at least {Math.Ceiling((nextAllowed - now).TotalSeconds)} seconds. No request was made.");
            return 2;
        }
    }

    async Task SaveCooldown(DateTimeOffset until)
    {
        attemptLock.Position = 0;
        attemptLock.SetLength(0);
        using var writer = new StreamWriter(attemptLock, leaveOpen: true);
        await writer.WriteAsync(until.ToString("O", CultureInfo.InvariantCulture));
        await writer.FlushAsync();
        attemptLock.Flush(flushToDisk: true);
    }

    // Reserve before sending, so cancellation, crashes and concurrent runs cannot retry immediately.
    await SaveCooldown(now.AddMinutes(5));
    Console.WriteLine($"Sending one {(legacy ? "legacy" : "updated")} login request; no automatic retries.");
    var services = new ServiceCollection();
    services.AddSingleton(configuration);
    services.AddSingleton<ToshibaAcHttpService>();
    services.AddHttpClient(ToshibaAcHttpService.HTTP_CLIENT_NAME, client => client.Timeout = TimeSpan.FromSeconds(30))
        .RemoveAllLoggers()
        .ConfigurePrimaryHttpMessageHandler(() => new LoginDiagnosticsHandler(legacy)
        {
            InnerHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false },
        });
    using var provider = services.BuildServiceProvider();
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    try
    {
        var result = await provider.GetRequiredService<ToshibaAcHttpService>().TryGetAccessToken(cancellation.Token);
        Console.WriteLine("Login succeeded: access token and consumer ID present (values withheld).");
        Console.WriteLine($"Token lifetime reported: {result.ExpiresIn.ToString(CultureInfo.InvariantCulture)} seconds.");
        return 0;
    }
    catch (ToshibaAcLoginException exception)
    {
        Console.Error.WriteLine(exception.Message);
        var cooldown = exception.StatusCode == HttpStatusCode.TooManyRequests && exception.RetryAfter is null
            ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5);
        if (exception.RetryAfter > cooldown)
        {
            cooldown = exception.RetryAfter.Value;
        }

        await SaveCooldown(DateTimeOffset.UtcNow.Add(cooldown));
        Console.Error.WriteLine($"Local cooldown set to {Math.Ceiling(cooldown.TotalSeconds)} seconds.");
        return 1;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Login cancelled or timed out. No automatic retry was made.");
        return 1;
    }
    catch (HttpRequestException exception)
    {
        Console.Error.WriteLine($"HTTP transport failed ({exception.HttpRequestError}). No automatic retry was made.");
        if (exception.InnerException is SocketException socketException)
        {
            Console.Error.WriteLine($"Socket error: {socketException.SocketErrorCode}.");
        }

        return 1;
    }
    catch
    {
        Console.Error.WriteLine("Diagnostics failed. Details withheld to protect secrets; no automatic retry was made.");
        return 1;
    }
}

internal sealed class DiagnosticsSecrets;

internal sealed class LoginDiagnosticsHandler(bool legacy) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (legacy)
        {
            request.Headers.Remove("Device-ID");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var previousContent = request.Content;
            request.Content = JsonContent.Create(new
            {
                Username = body.RootElement.GetProperty("Username").GetString(),
                Password = body.RootElement.GetProperty("Password").GetString(),
            });
            previousContent.Dispose();
        }

        var response = await base.SendAsync(request, cancellationToken);
        Console.WriteLine($"HTTP status: {(int)response.StatusCode}.");
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        Console.WriteLine($"Response format: {(mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ? "JSON" : mediaType == "text/html" ? "HTML" : "other/unspecified")}.");
        var retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
        if (retryAfter is not null)
        {
            Console.WriteLine($"Retry-After: {Math.Max(0, Math.Ceiling(retryAfter.Value.TotalSeconds))} seconds.");
        }

        return response;
    }
}