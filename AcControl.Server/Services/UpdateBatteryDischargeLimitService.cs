namespace AcControl.Server.Services;

using AcControl.Server.Data;
using System.Text.Json;

internal class UpdateBatteryDischargeLimitService : IHostedService, IDisposable
{
    private readonly ILogger<UpdateBatteryDischargeLimitService> mLogger;
    private readonly LuxPowerTekService mLuxSerice;

    private Timer? mTimer = null;

    public UpdateBatteryDischargeLimitService(ILogger<UpdateBatteryDischargeLimitService> logger, LuxPowerTekService luxService)
    {
        mLogger = logger;
        mLuxSerice = luxService;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        mLogger.LogInformation("UpdateBatteryDischargeLimitService running.");

        mTimer = new Timer(
            async _ => await this.DoWork(), 
            null, 
            TimeSpan.Zero,
            TimeSpan.FromHours(1)
        );

        return Task.CompletedTask;
    }

    private async Task DoWork()
    {
        mLogger.LogInformation("UpdateBatteryDischargeLimitService is working");

        try
        {
            await mLuxSerice.UpdateBattery();
        } 
        catch (JsonException ex)
        {
            File.WriteAllText("./SomeOtherIssue.txt", ex.ToString());
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        mLogger.LogInformation("UpdateBatteryDischargeLimitService is stopping.");

        mTimer?.Change(Timeout.Infinite, 0);
        mTimer?.Dispose();
        mTimer = null;

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        mTimer?.Dispose();
    }
}