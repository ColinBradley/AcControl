namespace AcControl.Server.Services;

using AcControl.Server.Data;
using AcControl.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Protocol;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

internal class AirGradientMqttRecorder : BackgroundService
{
    private readonly IServiceScopeFactory mScopeFactory;
    private readonly IConfiguration mConfig;
    private readonly IDbContextFactory<HomeDbContext> mDbContextFactory;

    public AirGradientMqttRecorder(IServiceScopeFactory scopeFactory, IConfiguration config, IDbContextFactory<HomeDbContext> dbContextFactory)
    {
        mScopeFactory = scopeFactory;
        mConfig = config;
        mDbContextFactory = dbContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new MqttClientFactory().CreateMqttClient();

        // Setup message handling before connecting so that queued messages
        // are also handled properly. When there is no event handler attached all
        // received messages get lost.
        client.ApplicationMessageReceivedAsync += async e =>
        {
            var update = JsonSerializer.Deserialize<AirGradientUpdate>(System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.Payload.ToArray()))!;

            Console.WriteLine("ApplicationMessageReceivedAsync: " + e.ApplicationMessage.Topic);

            if (update.Atmp < -50)
            {
                Console.WriteLine("ApplicationMessageReceivedAsync: Ignoring bad entry " + JsonSerializer.Serialize(update));
                return;
            }

            await using var homeDbContext = await mDbContextFactory.CreateDbContextAsync();

            homeDbContext.AirGradientSensorEntries.Add(
                new AirGradientSensorEntry()
                {
                    DateTime = DateTime.UtcNow,
                    SerialNumber = update.SerialNumber,
                    Atmp = update.Atmp,
                    AtmpCompensated = update.AtmpCompensated,
                    NoxIndex = update.NoxIndex,
                    NoxRaw = update.NoxRaw,
                    Pm003Count = update.Pm003Count,
                    Pm01 = update.Pm01,
                    Pm02 = update.Pm02,
                    Pm10 = update.Pm10,
                    RCo2 = update.RCo2,
                    Rhum = update.RHum,
                    RhumCompensated = update.RhumCompensated,
                    TvocIndex = update.TvocIndex,
                    TvocRaw = update.TvocRaw,
                    WiFiStrength = update.WiFiStrength,
                }
            );

            await homeDbContext.SaveChangesAsync();
        };

        try
        {
            await client.ConnectAsync(
                new MqttClientOptionsBuilder()
                    .WithTcpServer("localhost")
                    .WithCredentials("local", mConfig.GetValue<string>("Mqtt:Password"))
                    .Build(),
                stoppingToken
            );
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
        }

        await client.SubscribeAsync(
            "airgradient/readings/+",
            MqttQualityOfServiceLevel.AtLeastOnce,
            stoppingToken
        );

        var silly = new TaskCompletionSource();
        stoppingToken.Register(silly.SetResult);
        await silly.Task;
    }
}

file sealed record AirGradientUpdate
{
    [JsonPropertyName("wifi")]
    public required int WiFiStrength { get; init; }

    [JsonPropertyName("serialno")]
    public required string SerialNumber { get; init; }

    [JsonPropertyName("rco2")]
    public required int RCo2 { get; init; }

    [JsonPropertyName("pm01")]
    public required int Pm01 { get; init; }

    [JsonPropertyName("pm02")]
    public required int Pm02 { get; init; }

    [JsonPropertyName("pm10")]
    public required int Pm10 { get; init; }

    [JsonPropertyName("pm003Count")]
    public required int Pm003Count { get; init; }

    [JsonPropertyName("atmp")]
    public required double Atmp { get; init; }

    [JsonPropertyName("atmpCompensated")]
    public required double AtmpCompensated { get; init; }

    [JsonPropertyName("rhum")]
    public required int RHum { get; init; }

    [JsonPropertyName("rhumCompensated")]
    public required int RhumCompensated { get; init; }

    [JsonPropertyName("tvocIndex")]
    public required int TvocIndex { get; init; }

    [JsonPropertyName("tvocRaw")]
    public required int TvocRaw { get; init; }

    [JsonPropertyName("noxIndex")]
    public required int NoxIndex { get; init; }

    [JsonPropertyName("noxRaw")]
    public required int NoxRaw { get; init; }

    [JsonPropertyName("boot")]
    public required int Boot { get; init; }

    [JsonPropertyName("bootCount")]
    public required int BootCount { get; init; }

    [JsonPropertyName("ledMode")]
    public string? LedMode { get; init; }

    [JsonPropertyName("firmware")]
    public required string Firmware { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }
}