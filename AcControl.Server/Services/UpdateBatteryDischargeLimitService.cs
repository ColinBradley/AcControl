namespace AcControl.Server.Services;

using AcControl.Server.Data;
using System.Text.Json;

internal class UpdateBatteryDischargeLimitService : IHostedService, IDisposable
{
    private readonly LuxPowerTekService mLuxService;

    private Timer? mTimer = null;

    public UpdateBatteryDischargeLimitService(
        LuxPowerTekService luxService
    )
    {
        mLuxService = luxService;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
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
        try
        {
            await mLuxService.UpdateBattery();
        } 
        catch (JsonException ex)
        {
            File.WriteAllText("./SomeOtherIssue.txt", ex.ToString());
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
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